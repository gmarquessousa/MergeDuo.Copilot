using System.Text.Json.Serialization;

namespace MergeDuo.Copilot.Domain.Contracts;

public sealed record CopilotOwnerResponse(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("name")] string Name);

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
    [property: JsonPropertyName("ownerName")] string OwnerName,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("categoryLabel")] string CategoryLabel,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("kindLabel")] string KindLabel,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("cardId")] string? CardId,
    [property: JsonPropertyName("cardTitle")] string? CardTitle,
    [property: JsonPropertyName("fixedRuleId")] string? FixedRuleId,
    [property: JsonPropertyName("projected")] bool Projected);

public sealed record CopilotMovementHighlightResponse(
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("day")] int Day,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("ownerUserId")] string OwnerUserId,
    [property: JsonPropertyName("ownerName")] string OwnerName,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("categoryLabel")] string? CategoryLabel,
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("kindLabel")] string? KindLabel,
    [property: JsonPropertyName("cardId")] string? CardId,
    [property: JsonPropertyName("cardTitle")] string? CardTitle,
    [property: JsonPropertyName("projected")] bool Projected);

public sealed record CopilotCardSummaryResponse(
    [property: JsonPropertyName("cardId")] string CardId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("ownerUserId")] string OwnerUserId,
    [property: JsonPropertyName("ownerName")] string OwnerName,
    [property: JsonPropertyName("ownerRole")] string OwnerRole,
    [property: JsonPropertyName("closingDay")] int ClosingDay,
    [property: JsonPropertyName("dueDay")] int DueDay,
    [property: JsonPropertyName("nextDueDate")] DateOnly NextDueDate,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("invoiceAmount")] decimal InvoiceAmount,
    [property: JsonPropertyName("percentageOfIncome")] decimal? PercentageOfIncome,
    [property: JsonPropertyName("percentageOfTotalExpenses")] decimal? PercentageOfTotalExpenses,
    [property: JsonPropertyName("topMovements")] IReadOnlyList<CopilotMovementHighlightResponse> TopMovements);

public sealed record CopilotCategorySummaryResponse(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("percentageOfIncome")] decimal? PercentageOfIncome,
    [property: JsonPropertyName("percentageOfTotalExpenses")] decimal? PercentageOfTotalExpenses,
    [property: JsonPropertyName("confirmedAmount")] decimal ConfirmedAmount,
    [property: JsonPropertyName("projectedAmount")] decimal ProjectedAmount,
    [property: JsonPropertyName("topMovements")] IReadOnlyList<CopilotMovementHighlightResponse> TopMovements);

public sealed record CopilotOwnerSummaryResponse(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("income")] decimal Income,
    [property: JsonPropertyName("expenses")] decimal Expenses,
    [property: JsonPropertyName("balance")] decimal Balance,
    [property: JsonPropertyName("creditCardExpenses")] decimal CreditCardExpenses,
    [property: JsonPropertyName("fixedExpenses")] decimal FixedExpenses,
    [property: JsonPropertyName("variableExpenses")] decimal VariableExpenses,
    [property: JsonPropertyName("loanExpenses")] decimal LoanExpenses,
    [property: JsonPropertyName("investmentAmount")] decimal InvestmentAmount,
    [property: JsonPropertyName("percentageOfTotalIncome")] decimal? PercentageOfTotalIncome,
    [property: JsonPropertyName("percentageOfTotalExpenses")] decimal? PercentageOfTotalExpenses);

public sealed record CopilotFinancialRatiosResponse(
    [property: JsonPropertyName("expenseToIncomeRatio")] decimal? ExpenseToIncomeRatio,
    [property: JsonPropertyName("creditCardToIncomeRatio")] decimal? CreditCardToIncomeRatio,
    [property: JsonPropertyName("fixedExpenseToIncomeRatio")] decimal? FixedExpenseToIncomeRatio,
    [property: JsonPropertyName("variableExpenseToIncomeRatio")] decimal? VariableExpenseToIncomeRatio,
    [property: JsonPropertyName("loanToIncomeRatio")] decimal? LoanToIncomeRatio,
    [property: JsonPropertyName("finalBalanceToIncomeRatio")] decimal? FinalBalanceToIncomeRatio,
    [property: JsonPropertyName("patrimonyToIncomeRatio")] decimal? PatrimonyToIncomeRatio);

public sealed record CopilotConfirmedProjectedBucketResponse(
    [property: JsonPropertyName("confirmed")] decimal Confirmed,
    [property: JsonPropertyName("projected")] decimal Projected,
    [property: JsonPropertyName("total")] decimal Total);

public sealed record CopilotConfirmedVsProjectedResponse(
    [property: JsonPropertyName("income")] CopilotConfirmedProjectedBucketResponse Income,
    [property: JsonPropertyName("expenses")] CopilotConfirmedProjectedBucketResponse Expenses,
    [property: JsonPropertyName("projectedIncomePercentage")] decimal? ProjectedIncomePercentage,
    [property: JsonPropertyName("projectedExpensePercentage")] decimal? ProjectedExpensePercentage);

public sealed record CopilotDailyEventResponse(
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("categoryLabel")] string CategoryLabel,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("kindLabel")] string KindLabel,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("ownerUserId")] string OwnerUserId,
    [property: JsonPropertyName("ownerName")] string OwnerName,
    [property: JsonPropertyName("cardId")] string? CardId,
    [property: JsonPropertyName("cardTitle")] string? CardTitle,
    [property: JsonPropertyName("projected")] bool Projected);

public sealed record CopilotDailyCashflowResponse(
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("day")] int Day,
    [property: JsonPropertyName("income")] decimal Income,
    [property: JsonPropertyName("expenses")] decimal Expenses,
    [property: JsonPropertyName("net")] decimal Net,
    [property: JsonPropertyName("balanceAfterDay")] decimal BalanceAfterDay,
    [property: JsonPropertyName("confirmedIncome")] decimal ConfirmedIncome,
    [property: JsonPropertyName("projectedIncome")] decimal ProjectedIncome,
    [property: JsonPropertyName("confirmedExpenses")] decimal ConfirmedExpenses,
    [property: JsonPropertyName("projectedExpenses")] decimal ProjectedExpenses,
    [property: JsonPropertyName("mainEvents")] IReadOnlyList<CopilotDailyEventResponse> MainEvents);

public sealed record CopilotCashflowMetricsResponse(
    [property: JsonPropertyName("openingBalance")] decimal OpeningBalance,
    [property: JsonPropertyName("finalBalance")] decimal FinalBalance,
    [property: JsonPropertyName("minimumBalance")] decimal MinimumBalance,
    [property: JsonPropertyName("minimumBalanceDate")] DateOnly? MinimumBalanceDate,
    [property: JsonPropertyName("daysWithNegativeBalance")] int DaysWithNegativeBalance,
    [property: JsonPropertyName("daysBelowSafetyMargin")] int DaysBelowSafetyMargin,
    [property: JsonPropertyName("safetyMarginValue")] decimal SafetyMarginValue,
    [property: JsonPropertyName("nextIncomeDate")] DateOnly? NextIncomeDate,
    [property: JsonPropertyName("balanceBeforeNextIncome")] decimal? BalanceBeforeNextIncome);

public sealed record CopilotCommitmentResponse(
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("day")] int Day,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("ownerUserId")] string OwnerUserId,
    [property: JsonPropertyName("ownerName")] string OwnerName,
    [property: JsonPropertyName("cardId")] string? CardId,
    [property: JsonPropertyName("cardTitle")] string? CardTitle,
    [property: JsonPropertyName("projected")] bool Projected,
    [property: JsonPropertyName("priority")] string Priority);

public sealed record CopilotCardInvoiceHighlightResponse(
    [property: JsonPropertyName("cardId")] string CardId,
    [property: JsonPropertyName("cardTitle")] string CardTitle,
    [property: JsonPropertyName("ownerName")] string OwnerName,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("dueDay")] int DueDay,
    [property: JsonPropertyName("nextDueDate")] DateOnly NextDueDate);

public sealed record CopilotHighlightsResponse(
    [property: JsonPropertyName("largestOutflows")] IReadOnlyList<CopilotMovementHighlightResponse> LargestOutflows,
    [property: JsonPropertyName("largestInflows")] IReadOnlyList<CopilotMovementHighlightResponse> LargestInflows,
    [property: JsonPropertyName("largestCardInvoices")] IReadOnlyList<CopilotCardInvoiceHighlightResponse> LargestCardInvoices);

public sealed record CopilotComparisonMonthResponse(
    [property: JsonPropertyName("yearMonth")] string YearMonth,
    [property: JsonPropertyName("income")] decimal Income,
    [property: JsonPropertyName("expenses")] decimal Expenses,
    [property: JsonPropertyName("balance")] decimal Balance,
    [property: JsonPropertyName("patrimony")] decimal Patrimony);

public sealed record CopilotComparisonDeltaResponse(
    [property: JsonPropertyName("income")] decimal? Income,
    [property: JsonPropertyName("expenses")] decimal? Expenses,
    [property: JsonPropertyName("balance")] decimal? Balance,
    [property: JsonPropertyName("patrimony")] decimal? Patrimony);

public sealed record CopilotComparisonResponse(
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("previousMonth")] CopilotComparisonMonthResponse? PreviousMonth,
    [property: JsonPropertyName("currentMonth")] CopilotComparisonMonthResponse? CurrentMonth,
    [property: JsonPropertyName("delta")] CopilotComparisonDeltaResponse? Delta,
    [property: JsonPropertyName("percentDelta")] CopilotComparisonDeltaResponse? PercentDelta);

public sealed record CopilotThreeMonthAverageResponse(
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("income")] decimal? Income,
    [property: JsonPropertyName("expenses")] decimal? Expenses,
    [property: JsonPropertyName("balance")] decimal? Balance,
    [property: JsonPropertyName("creditCardExpenses")] decimal? CreditCardExpenses,
    [property: JsonPropertyName("variableExpenses")] decimal? VariableExpenses,
    [property: JsonPropertyName("fixedExpenses")] decimal? FixedExpenses,
    [property: JsonPropertyName("loanExpenses")] decimal? LoanExpenses);

public sealed record CopilotAgainstThreeMonthAverageResponse(
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("incomeDelta")] decimal? IncomeDelta,
    [property: JsonPropertyName("expensesDelta")] decimal? ExpensesDelta,
    [property: JsonPropertyName("creditCardExpensesDelta")] decimal? CreditCardExpensesDelta,
    [property: JsonPropertyName("variableExpensesDelta")] decimal? VariableExpensesDelta,
    [property: JsonPropertyName("fixedExpensesDelta")] decimal? FixedExpensesDelta,
    [property: JsonPropertyName("loanExpensesDelta")] decimal? LoanExpensesDelta);

public sealed record CopilotMonthSummaryResponse(
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("owners")] IReadOnlyList<CopilotOwnerResponse> Owners,
    [property: JsonPropertyName("period")] CopilotPeriodResponse Period,
    [property: JsonPropertyName("totals")] CopilotTotalsResponse Totals,
    [property: JsonPropertyName("byCategory")] IReadOnlyDictionary<string, decimal> ByCategory,
    [property: JsonPropertyName("byCard")] IReadOnlyDictionary<string, decimal> ByCard,
    [property: JsonPropertyName("cardsSummary")] IReadOnlyList<CopilotCardSummaryResponse> CardsSummary,
    [property: JsonPropertyName("categoriesSummary")] IReadOnlyList<CopilotCategorySummaryResponse> CategoriesSummary,
    [property: JsonPropertyName("ownersSummary")] IReadOnlyList<CopilotOwnerSummaryResponse> OwnersSummary,
    [property: JsonPropertyName("financialRatios")] CopilotFinancialRatiosResponse FinancialRatios,
    [property: JsonPropertyName("confirmedVsProjected")] CopilotConfirmedVsProjectedResponse ConfirmedVsProjected,
    [property: JsonPropertyName("dailyCashflow")] IReadOnlyList<CopilotDailyCashflowResponse> DailyCashflow,
    [property: JsonPropertyName("cashflowMetrics")] CopilotCashflowMetricsResponse CashflowMetrics,
    [property: JsonPropertyName("commitmentCalendar")] IReadOnlyList<CopilotCommitmentResponse> CommitmentCalendar,
    [property: JsonPropertyName("highlights")] CopilotHighlightsResponse Highlights,
    [property: JsonPropertyName("comparison")] CopilotComparisonResponse Comparison,
    [property: JsonPropertyName("threeMonthAverage")] CopilotThreeMonthAverageResponse ThreeMonthAverage,
    [property: JsonPropertyName("againstThreeMonthAverage")] CopilotAgainstThreeMonthAverageResponse AgainstThreeMonthAverage,
    [property: JsonPropertyName("relevantMovements")] IReadOnlyList<CopilotMovementResponse> RelevantMovements,
    [property: JsonPropertyName("includesProjected")] bool IncludesProjected,
    [property: JsonPropertyName("projectedCount")] int ProjectedCount,
    [property: JsonPropertyName("transactionsCount")] int TransactionsCount,
    [property: JsonPropertyName("dataFreshness")] CopilotFreshnessResponse DataFreshness,
    [property: JsonPropertyName("computedAt")] DateTimeOffset ComputedAt,
    [property: JsonPropertyName("summaryText")] string SummaryText,
    [property: JsonPropertyName("aiContextText")] string AiContextText);

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

public sealed record CopilotCardResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("ownerUserId")] string OwnerUserId,
    [property: JsonPropertyName("ownerRole")] string OwnerRole,
    [property: JsonPropertyName("ownerName")] string OwnerName,
    [property: JsonPropertyName("closingDay")] int ClosingDay,
    [property: JsonPropertyName("dueDay")] int DueDay,
    [property: JsonPropertyName("nextDueDate")] DateOnly NextDueDate,
    [property: JsonPropertyName("currency")] string Currency);

public sealed record CopilotCardsResponse(
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("owners")] IReadOnlyList<CopilotOwnerResponse> Owners,
    [property: JsonPropertyName("cards")] IReadOnlyList<CopilotCardResponse> Cards,
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
