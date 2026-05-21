using System.Text.Json.Serialization;

namespace MergeDuo.Copilot.Domain.Documents;

public sealed class MonthlyAggregateDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("docType")]
    public string DocType { get; set; } = "monthlyAggregate";

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("monthIdx")]
    public int MonthIdx { get; set; }

    [JsonPropertyName("yearMonth")]
    public string YearMonth { get; set; } = "";

    [JsonPropertyName("totals")]
    public MonthlyTotalsDocument Totals { get; set; } = new();

    [JsonPropertyName("snapshotToday")]
    public SnapshotTodayDocument? SnapshotToday { get; set; }

    [JsonPropertyName("dailyBalances")]
    public List<DailyBalanceDocument> DailyBalances { get; set; } = [];

    [JsonPropertyName("dailyMovements")]
    public List<DailyMovementDocument> DailyMovements { get; set; } = [];

    [JsonPropertyName("projection")]
    public ProjectionDocument Projection { get; set; } = new();

    [JsonPropertyName("byCategory")]
    public Dictionary<string, decimal> ByCategory { get; set; } = [];

    [JsonPropertyName("byCard")]
    public Dictionary<string, decimal> ByCard { get; set; } = [];

    [JsonPropertyName("byOwner")]
    public Dictionary<string, OwnerTotalsDocument> ByOwner { get; set; } = [];

    [JsonPropertyName("transactionsCount")]
    public int TransactionsCount { get; set; }

    [JsonPropertyName("computedAt")]
    public DateTimeOffset ComputedAt { get; set; }

    [JsonPropertyName("sourceVersion")]
    public int SourceVersion { get; set; }

    [JsonPropertyName("sourceWatermark")]
    public SourceWatermarkDocument SourceWatermark { get; set; } = new();

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

public sealed class SourceWatermarkDocument
{
    [JsonPropertyName("maxTransactionUpdatedAt")]
    public DateTimeOffset? MaxTransactionUpdatedAt { get; set; }

    [JsonPropertyName("activeTransactionsCount")]
    public int ActiveTransactionsCount { get; set; }
}

public sealed class MonthlyTotalsDocument
{
    [JsonPropertyName("entradas")]
    public decimal Entradas { get; set; }

    [JsonPropertyName("saidas")]
    public decimal Saidas { get; set; }

    [JsonPropertyName("aportes")]
    public decimal Aportes { get; set; }

    [JsonPropertyName("saldo")]
    public decimal Saldo { get; set; }

    [JsonPropertyName("investido")]
    public decimal Investido { get; set; }
}

public sealed class SnapshotTodayDocument
{
    [JsonPropertyName("saldoHoje")]
    public decimal SaldoHoje { get; set; }

    [JsonPropertyName("investidoHoje")]
    public decimal InvestidoHoje { get; set; }

    [JsonPropertyName("patrimonioHoje")]
    public decimal PatrimonioHoje { get; set; }

    [JsonPropertyName("asOfDate")]
    public DateOnly AsOfDate { get; set; }
}

public sealed class DailyBalanceDocument
{
    [JsonPropertyName("day")]
    public int Day { get; set; }

    [JsonPropertyName("saldo")]
    public decimal Saldo { get; set; }
}

public sealed class DailyMovementDocument
{
    [JsonPropertyName("day")]
    public int Day { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("cardId")]
    public string? CardId { get; set; }

    [JsonPropertyName("fixedRuleId")]
    public string? FixedRuleId { get; set; }

    [JsonPropertyName("projected")]
    public bool Projected { get; set; }

    [JsonPropertyName("purchaseDate")]
    public DateOnly? PurchaseDate { get; set; }
}

public sealed class ProjectionDocument
{
    [JsonPropertyName("includesProjected")]
    public bool IncludesProjected { get; set; }

    [JsonPropertyName("projectedCount")]
    public int ProjectedCount { get; set; }

    [JsonPropertyName("asOfDate")]
    public DateOnly AsOfDate { get; set; }
}

public sealed class OwnerTotalsDocument
{
    [JsonPropertyName("entradas")]
    public decimal Entradas { get; set; }

    [JsonPropertyName("saidas")]
    public decimal Saidas { get; set; }

    [JsonPropertyName("aportes")]
    public decimal Aportes { get; set; }
}

public sealed class TransactionProjection
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("docType")]
    public string DocType { get; set; } = "transaction";

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("yearMonth")]
    public string YearMonth { get; set; } = "";

    [JsonPropertyName("date")]
    public DateOnly Date { get; set; }

    [JsonPropertyName("purchaseDate")]
    public DateOnly? PurchaseDate { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "BRL";

    [JsonPropertyName("cardId")]
    public string? CardId { get; set; }

    [JsonPropertyName("fixedRuleId")]
    public string? FixedRuleId { get; set; }

    [JsonPropertyName("projected")]
    public bool Projected { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("deletedAt")]
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class UserDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("docType")]
    public string DocType { get; set; } = "user";

    [JsonPropertyName("financial")]
    public UserFinancialDocument Financial { get; set; } = new();

    [JsonPropertyName("deletedAt")]
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class UserFinancialDocument
{
    [JsonPropertyName("startingBalance")]
    public decimal StartingBalance { get; set; }
}

public sealed class FixedRuleDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("docType")]
    public string DocType { get; set; } = "fixedRule";

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("cardId")]
    public string? CardId { get; set; }

    [JsonPropertyName("schedule")]
    public FixedRuleScheduleDocument Schedule { get; set; } = new();

    [JsonPropertyName("startsAt")]
    public string StartsAt { get; set; } = "";

    [JsonPropertyName("endsAt")]
    public string? EndsAt { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("deletedAt")]
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class FixedRuleScheduleDocument
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("day")]
    public int? Day { get; set; }

    [JsonPropertyName("ordinal")]
    public int? Ordinal { get; set; }

    [JsonPropertyName("period")]
    public string? Period { get; set; }
}

public sealed class CardDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("docType")]
    public string DocType { get; set; } = "card";

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("closingDay")]
    public int ClosingDay { get; set; }

    [JsonPropertyName("dueDay")]
    public int DueDay { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "BRL";

    [JsonPropertyName("deletedAt")]
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class PartnershipDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("docType")]
    public string DocType { get; set; } = "partnership";

    [JsonPropertyName("partnershipId")]
    public string PartnershipId { get; set; } = "";

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("partnerUserId")]
    public string PartnerUserId { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("mergedSince")]
    public DateOnly MergedSince { get; set; }
}
