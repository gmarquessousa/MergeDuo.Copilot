using MergeDuo.Copilot.Domain.Abstractions;
using MergeDuo.Copilot.Infra.Cosmos;
using MergeDuo.Copilot.Tests.Fakes;
using MergeDuo.Copilot.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MergeDuo.Copilot.Tests;

public sealed class TestCopilotFactory : WebApplicationFactory<Program>
{
    private readonly string _configuredUserId;

    public InMemoryCopilotRepository Repository { get; }
    public TestClock Clock { get; } = new(DateTimeOffset.Parse("2026-05-21T15:00:00Z"));

    public TestCopilotFactory(string configuredUserId = "usr_primary")
    {
        _configuredUserId = configuredUserId;
        Repository = new InMemoryCopilotRepository(configuredUserId);
        Environment.SetEnvironmentVariable("Copilot__UserId", configuredUserId);
        Environment.SetEnvironmentVariable("Copilot__BusinessTimeZone", "America/Sao_Paulo");
        Environment.SetEnvironmentVariable("Copilot__SourceVersion", "4");
        Environment.SetEnvironmentVariable("Copilot__ProjectionMonths", "3");
        Environment.SetEnvironmentVariable("Copilot__MaxSimulationInstallments", "24");
        Environment.SetEnvironmentVariable("Copilot__SafetyMarginValue", "500");
        Environment.SetEnvironmentVariable("Cosmos__Endpoint", "https://cosmos.test/");
        Environment.SetEnvironmentVariable("Cosmos__Database", "mergeduo");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Copilot:UserId"] = _configuredUserId,
                ["Copilot:BusinessTimeZone"] = "America/Sao_Paulo",
                ["Copilot:SourceVersion"] = "4",
                ["Copilot:ProjectionMonths"] = "3",
                ["Copilot:MaxSimulationInstallments"] = "24",
                ["Copilot:SafetyMarginValue"] = "500",
                ["Cosmos:Endpoint"] = "https://cosmos.test/",
                ["Cosmos:Database"] = "mergeduo",
                ["Cosmos:UsersContainer"] = "users",
                ["Cosmos:PartnershipsContainer"] = "partnerships",
                ["Cosmos:MonthlyAggregatesContainer"] = "monthlyAggregates",
                ["Cosmos:TransactionsContainer"] = "transactions",
                ["Cosmos:FixedRulesContainer"] = "fixedRules",
                ["Cosmos:CardsContainer"] = "cards",
                ["Cors:AllowedOrigins:0"] = "https://localhost",
                ["RateLimit:GlobalPermitLimit"] = "1000",
                ["RateLimit:ReadPermitLimit"] = "1000",
                ["RateLimit:SimulationPermitLimit"] = "1000"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<CosmosClient>();
            services.RemoveAll<CopilotCosmosRepository>();
            services.RemoveAll<ICopilotReadRepository>();
            services.RemoveAll<ICopilotReadinessProbe>();
            services.RemoveAll<IFixedRulesProjectionRepository>();
            services.RemoveAll<ICardsProjectionRepository>();
            services.RemoveAll<TimeProvider>();

            services.AddSingleton<ICopilotReadRepository>(Repository);
            services.AddSingleton<ICopilotReadinessProbe>(Repository);
            services.AddSingleton<IFixedRulesProjectionRepository>(Repository);
            services.AddSingleton<ICardsProjectionRepository>(Repository);
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });
}
