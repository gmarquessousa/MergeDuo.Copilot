using System.Text.Json.Serialization;

namespace MergeDuo.Copilot.Domain.Contracts;

public sealed record MonthlyAggregateResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("year")] int Year,
    [property: JsonPropertyName("month")] int Month,
    [property: JsonPropertyName("monthIdx")] int MonthIdx,
    [property: JsonPropertyName("yearMonth")] string YearMonth,
    [property: JsonPropertyName("totals")] MonthlyTotalsResponse Totals,
    [property: JsonPropertyName("snapshotToday")] SnapshotTodayResponse? SnapshotToday,
    [property: JsonPropertyName("dailyBalances")] IReadOnlyList<DailyBalanceResponse> DailyBalances,
    [property: JsonPropertyName("dailyMovements")] IReadOnlyList<DailyMovementResponse> DailyMovements,
    [property: JsonPropertyName("projection")] ProjectionResponse Projection,
    [property: JsonPropertyName("byCategory")] IReadOnlyDictionary<string, decimal> ByCategory,
    [property: JsonPropertyName("byCard")] IReadOnlyDictionary<string, decimal> ByCard,
    [property: JsonPropertyName("byOwner")] IReadOnlyDictionary<string, OwnerTotalsResponse> ByOwner,
    [property: JsonPropertyName("transactionsCount")] int TransactionsCount,
    [property: JsonPropertyName("computedAt")] DateTimeOffset? ComputedAt,
    [property: JsonPropertyName("sourceVersion")] int SourceVersion,
    [property: JsonPropertyName("isStale")] bool IsStale,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("freshness")] FreshnessResponse Freshness,
    [property: JsonPropertyName("sourceWatermark")] SourceWatermarkResponse SourceWatermark);

public sealed record MonthlyTotalsResponse(
    [property: JsonPropertyName("entradas")] decimal Entradas,
    [property: JsonPropertyName("saidas")] decimal Saidas,
    [property: JsonPropertyName("aportes")] decimal Aportes,
    [property: JsonPropertyName("saldo")] decimal Saldo,
    [property: JsonPropertyName("investido")] decimal Investido);

public sealed record OwnerTotalsResponse(
    [property: JsonPropertyName("entradas")] decimal Entradas,
    [property: JsonPropertyName("saidas")] decimal Saidas,
    [property: JsonPropertyName("aportes")] decimal Aportes);

public sealed record SnapshotTodayResponse(
    [property: JsonPropertyName("saldoHoje")] decimal SaldoHoje,
    [property: JsonPropertyName("investidoHoje")] decimal InvestidoHoje,
    [property: JsonPropertyName("patrimonioHoje")] decimal PatrimonioHoje,
    [property: JsonPropertyName("asOfDate")] DateOnly AsOfDate);

public sealed record DailyBalanceResponse(
    [property: JsonPropertyName("day")] int Day,
    [property: JsonPropertyName("saldo")] decimal Saldo);

public sealed record DailyMovementResponse(
    [property: JsonPropertyName("day")] int Day,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("cardId")] string? CardId,
    [property: JsonPropertyName("fixedRuleId")] string? FixedRuleId,
    [property: JsonPropertyName("projected")] bool Projected,
    [property: JsonPropertyName("purchaseDate")] DateOnly? PurchaseDate);

public sealed record ProjectionResponse(
    [property: JsonPropertyName("includesProjected")] bool IncludesProjected,
    [property: JsonPropertyName("projectedCount")] int ProjectedCount,
    [property: JsonPropertyName("asOfDate")] DateOnly AsOfDate);

public sealed record FreshnessResponse(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record SourceWatermarkResponse(
    [property: JsonPropertyName("maxTransactionUpdatedAt")] DateTimeOffset? MaxTransactionUpdatedAt,
    [property: JsonPropertyName("activeTransactionsCount")] int ActiveTransactionsCount);
