using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using MergeDuo.Copilot.Api;
using MergeDuo.Copilot.Domain.Abstractions;
using MergeDuo.Copilot.Domain.Contracts;
using MergeDuo.Copilot.Domain.Exceptions;
using MergeDuo.Copilot.Domain.Options;
using MergeDuo.Copilot.Domain.Services;
using MergeDuo.Copilot.Infra.Cosmos;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 16 * 1024;
});

builder.Services.AddSingleton(TimeProvider.System);

var copilotOptions = Bind<CopilotOptions>("Copilot");
var cosmosOptions = Bind<MergeDuo.Copilot.Domain.Options.CosmosOptions>("Cosmos");
var corsOptions = Bind<MergeDuo.Copilot.Domain.Options.CorsOptions>("Cors");
var rateLimitOptions = Bind<MergeDuo.Copilot.Domain.Options.RateLimitOptions>("RateLimit");

builder.Services.AddSingleton(copilotOptions);
builder.Services.AddSingleton(cosmosOptions);
builder.Services.AddSingleton(corsOptions);
builder.Services.AddSingleton(rateLimitOptions);

builder.Services.AddSingleton<CosmosClient>(sp => CosmosClientFactory.Create(sp.GetRequiredService<MergeDuo.Copilot.Domain.Options.CosmosOptions>()));
builder.Services.AddSingleton<CopilotCosmosRepository>();
builder.Services.AddSingleton<ICopilotReadRepository>(sp => sp.GetRequiredService<CopilotCosmosRepository>());
builder.Services.AddSingleton<ICopilotReadinessProbe>(sp => sp.GetRequiredService<CopilotCosmosRepository>());
builder.Services.AddSingleton<IFixedRulesProjectionRepository>(sp => sp.GetRequiredService<CopilotCosmosRepository>());
builder.Services.AddSingleton<ICardsProjectionRepository>(sp => sp.GetRequiredService<CopilotCosmosRepository>());
builder.Services.AddSingleton<AggregateCalculator>();
builder.Services.AddSingleton<FixedRuleProjectionService>();
builder.Services.AddSingleton<ICopilotFinanceService, CopilotFinanceService>();

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = corsOptions.AllowedOrigins.Length == 0
            ? ["http://localhost:5173"]
            : corsOptions.AllowedOrigins;

        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
        await ProblemHttp.WriteAsync(
            context.HttpContext,
            StatusCodes.Status429TooManyRequests,
            "rate_limited",
            "Rate limit exceeded.",
            cancellationToken);

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        FixedWindow(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", rateLimitOptions.GlobalPermitLimit));
    options.AddPolicy("copilot-read", context => FixedWindow(RateLimitKey(context), rateLimitOptions.ReadPermitLimit));
    options.AddPolicy("copilot-simulate", context => FixedWindow(RateLimitKey(context), rateLimitOptions.SimulationPermitLimit));
});

var applicationInsightsConnectionString =
    builder.Configuration["ApplicationInsights:ConnectionString"]
    ?? builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

if (!builder.Environment.IsEnvironment("Testing") && !string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(options => options.ConnectionString = applicationInsightsConnectionString);
}

var app = builder.Build();

app.UseForwardedHeaders();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var (status, code, detail) = exception switch
        {
            CopilotBadRequestException ex => (StatusCodes.Status400BadRequest, ex.Code, ex.Message),
            CopilotConfigurationException ex => (StatusCodes.Status503ServiceUnavailable, ex.Code, ex.Message),
            CopilotDependencyException ex => (StatusCodes.Status503ServiceUnavailable, ex.Code, ex.Message),
            InvalidTransactionProjectionException ex => (StatusCodes.Status422UnprocessableEntity, ex.Code, ex.Message),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "invalid_request", "Invalid request."),
            ArgumentException => (StatusCodes.Status400BadRequest, "invalid_request", "Invalid request."),
            _ => (StatusCodes.Status500InternalServerError, "copilot_unhandled_error", "Unhandled Copilot error.")
        };

        await ProblemHttp.WriteAsync(context, status, code, detail, context.RequestAborted);
    });
});

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseRateLimiter();

app.MapGet("/", () => Results.Ok(new
{
    service = "mergeduo-copilot",
    status = "ok"
}))
.ExcludeFromDescription();

app.MapGet("/startupz", () => Results.Ok(new { status = "started" }))
    .ExcludeFromDescription();

app.MapHealthChecks("/healthz");
app.MapGet("/readyz", async (ICopilotReadinessProbe readiness, CancellationToken cancellationToken) =>
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
    var result = await readiness.CheckAsync(linked.Token);
    return result.Ready
        ? Results.Ok(new { status = "ready" })
        : ProblemHttp.Problem(StatusCodes.Status503ServiceUnavailable, result.Code ?? "copilot_not_ready", result.Detail ?? "Copilot is not ready.");
});

var copilot = app.MapGroup("/copilot").WithTags("Copilot");

copilot.MapGet("/month-summary/{year:int}/{month:int}", async (
    int year,
    int month,
    ICopilotFinanceService service,
    CancellationToken cancellationToken) =>
{
    var response = await service.GetMonthSummaryAsync(year, month, cancellationToken);
    return Results.Ok(response);
})
.WithName("GetMonthSummary")
.WithSummary("Returns a structured financial summary for the configured Copilot user and active merge partner.")
.RequireRateLimiting("copilot-read");

copilot.MapGet("/next-three-months", async (
    int? year,
    int? month,
    ICopilotFinanceService service,
    CancellationToken cancellationToken) =>
{
    var response = await service.GetNextThreeMonthsAsync(year, month, cancellationToken);
    return Results.Ok(response);
})
.WithName("GetNextThreeMonths")
.WithSummary("Returns the current requested month plus the next two months for the configured Copilot user and active merge partner.")
.RequireRateLimiting("copilot-read");

copilot.MapGet("/cards", async (
    ICopilotFinanceService service,
    CancellationToken cancellationToken) =>
{
    var response = await service.ListCardsAsync(cancellationToken);
    return Results.Ok(response);
})
.WithName("ListAvailableCards")
.WithSummary("Lists active credit cards available to the configured Copilot user and active merge partner.")
.RequireRateLimiting("copilot-read");

copilot.MapPost("/purchase-simulation", async (
    PurchaseSimulationRequest? request,
    ICopilotFinanceService service,
    CancellationToken cancellationToken) =>
{
    var response = await service.SimulatePurchaseAsync(request, cancellationToken);
    return Results.Ok(response);
})
.WithName("SimulatePurchase")
.WithSummary("Simulates a cash or credit card purchase without persisting any transaction.")
.RequireRateLimiting("copilot-simulate");

app.Run();

T Bind<T>(string sectionName) where T : new() =>
    builder.Configuration.GetSection(sectionName).Get<T>() ?? new T();

static RateLimitPartition<string> FixedWindow(string key, int permitLimit) =>
    RateLimitPartition.GetFixedWindowLimiter(
        key,
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permitLimit),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });

static string RateLimitKey(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

public partial class Program;
