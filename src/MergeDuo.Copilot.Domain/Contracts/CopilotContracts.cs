using System.Text.Json.Serialization;

namespace MergeDuo.Copilot.Domain.Contracts;

public sealed record CopilotOwnerResponse(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("role")] string Role);

public sealed record CopilotPeriodResponse(
    [property: JsonPropertyName("year")] int Year,
    [property: JsonPropertyName("month")] int Month,
    [property: JsonPropertyName("yearMonth")] string YearMonth,
    [property: JsonPropertyName("from")] DateOnly From,
    [property: JsonPropertyName("to")] DateOnly To);

public sealed record CopilotTotalsResponse(
    [property: JsonPropertyName("entradas")] decimal Entradas,
    [property: JsonPropertyName("saidas")] decimal Saidas,
    [property: JsonPropertyName("aportes")] decimal Aportes,
    [property: JsonPropertyName("saldo")] decimal Saldo,
    [property: JsonPropertyName("investido")] decimal Investido,
    [property: JsonPropertyName("patrimonio")] decimal Patrimonio,
    [property: JsonPropertyName("saldoHoje")] decimal? SaldoHoje,
    [property: JsonPropertyName("investidoHoje")] decimal? InvestidoHoje,
    [property: JsonPropertyName("patrimonioHoje")] decimal? PatrimonioHoje);

public sealed record CopilotFreshnessResponse(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record CopilotMovementResponse(
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("day")] int Day,
    [property: JsonPropertyName("ownerUserId")] string OwnerUserId,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("cardId")] string? CardId,
    [property: JsonPropertyName("fixedRuleId")] string? FixedRuleId,
    [property: JsonPropertyName("projected")] bool Projected);

public sealed record CopilotMonthSummaryResponse(
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("owners")] IReadOnlyList<CopilotOwnerResponse> Owners,
    [property: JsonPropertyName("period")] CopilotPeriodResponse Period,
    [property: JsonPropertyName("totals")] CopilotTotalsResponse Totals,
    [property: JsonPropertyName("byCategory")] IReadOnlyDictionary<string, decimal> ByCategory,
    [property: JsonPropertyName("byCard")] IReadOnlyDictionary<string, decimal> ByCard,
    [property: JsonPropertyName("relevantMovements")] IReadOnlyList<CopilotMovementResponse> RelevantMovements,
    [property: JsonPropertyName("includesProjected")] bool IncludesProjected,
    [property: JsonPropertyName("projectedCount")] int ProjectedCount,
    [property: JsonPropertyName("transactionsCount")] int TransactionsCount,
    [property: JsonPropertyName("dataFreshness")] CopilotFreshnessResponse DataFreshness,
    [property: JsonPropertyName("computedAt")] DateTimeOffset ComputedAt,
    [property: JsonPropertyName("summaryText")] string SummaryText);

public sealed record CopilotNextThreeMonthsResponse(
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("owners")] IReadOnlyList<CopilotOwnerResponse> Owners,
    [property: JsonPropertyName("from")] DateOnly From,
    [property: JsonPropertyName("to")] DateOnly To,
    [property: JsonPropertyName("months")] IReadOnlyList<CopilotMonthSummaryResponse> Months,
    [property: JsonPropertyName("totals")] CopilotTotalsResponse Totals,
    [property: JsonPropertyName("upcomingMovements")] IReadOnlyList<CopilotMovementResponse> UpcomingMovements,
    [property: JsonPropertyName("dataFreshness")] CopilotFreshnessResponse DataFreshness,
    [property: JsonPropertyName("computedAt")] DateTimeOffset ComputedAt,
    [property: JsonPropertyName("summaryText")] string SummaryText);

public sealed class PurchaseSimulationRequest
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("amount")]
    public decimal? Amount { get; set; }

    [JsonPropertyName("purchaseDate")]
    public DateOnly? PurchaseDate { get; set; }

    [JsonPropertyName("paymentType")]
    public string? PaymentType { get; set; }

    [JsonPropertyName("cardId")]
    public string? CardId { get; set; }

    [JsonPropertyName("installments")]
    public int? Installments { get; set; }
}

public sealed record PurchaseSimulationInstallmentResponse(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("ownerUserId")] string OwnerUserId,
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("yearMonth")] string YearMonth,
    [property: JsonPropertyName("amount")] decimal Amount);

public sealed record PurchaseSimulationMonthImpactResponse(
    [property: JsonPropertyName("period")] CopilotPeriodResponse Period,
    [property: JsonPropertyName("impactAmount")] decimal ImpactAmount,
    [property: JsonPropertyName("cumulativeImpact")] decimal CumulativeImpact,
    [property: JsonPropertyName("baselineTotals")] CopilotTotalsResponse BaselineTotals,
    [property: JsonPropertyName("projectedTotals")] CopilotTotalsResponse ProjectedTotals);

public sealed record CopilotPurchaseSimulationResponse(
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("owners")] IReadOnlyList<CopilotOwnerResponse> Owners,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("paymentType")] string PaymentType,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("purchaseDate")] DateOnly PurchaseDate,
    [property: JsonPropertyName("cardId")] string? CardId,
    [property: JsonPropertyName("installments")] IReadOnlyList<PurchaseSimulationInstallmentResponse> Installments,
    [property: JsonPropertyName("monthImpacts")] IReadOnlyList<PurchaseSimulationMonthImpactResponse> MonthImpacts,
    [property: JsonPropertyName("computedAt")] DateTimeOffset ComputedAt,
    [property: JsonPropertyName("summaryText")] string SummaryText);
