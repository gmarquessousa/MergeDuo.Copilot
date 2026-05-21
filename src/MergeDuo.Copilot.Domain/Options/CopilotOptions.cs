namespace MergeDuo.Copilot.Domain.Options;

public sealed class CopilotOptions
{
    public string UserId { get; set; } = "";
    public string BusinessTimeZone { get; set; } = "America/Sao_Paulo";
    public int SourceVersion { get; set; } = 4;
    public int ProjectionMonths { get; set; } = 3;
    public int MaxSimulationInstallments { get; set; } = 48;
    public decimal SafetyMarginAmount { get; set; } = 1000m;
}

public sealed class CosmosOptions
{
    public string Endpoint { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string Database { get; set; } = "mergeduo";
    public string UsersContainer { get; set; } = "users";
    public string PartnershipsContainer { get; set; } = "partnerships";
    public string MonthlyAggregatesContainer { get; set; } = "monthlyAggregates";
    public string TransactionsContainer { get; set; } = "transactions";
    public string FixedRulesContainer { get; set; } = "fixedRules";
    public string CardsContainer { get; set; } = "cards";
}

public sealed class CorsOptions
{
    public string[] AllowedOrigins { get; set; } = [];
}

public sealed class RateLimitOptions
{
    public int GlobalPermitLimit { get; set; } = 120;
    public int ReadPermitLimit { get; set; } = 60;
    public int SimulationPermitLimit { get; set; } = 30;
}
