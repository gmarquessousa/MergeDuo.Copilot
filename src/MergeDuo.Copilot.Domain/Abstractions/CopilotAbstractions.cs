using MergeDuo.Copilot.Domain.Contracts;
using MergeDuo.Copilot.Domain.Documents;
using MergeDuo.Copilot.Domain.Rules;

namespace MergeDuo.Copilot.Domain.Abstractions;

public interface ICopilotFinanceService
{
    Task<CopilotMonthSummaryResponse> GetMonthSummaryAsync(int year, int month, CancellationToken cancellationToken);
    Task<CopilotNextThreeMonthsResponse> GetNextThreeMonthsAsync(int? year, int? month, CancellationToken cancellationToken);
    Task<CopilotPurchaseSimulationResponse> SimulatePurchaseAsync(PurchaseSimulationRequest? request, CancellationToken cancellationToken);
}

public interface ICopilotReadRepository :
    ITransactionsProjectionRepository,
    IFixedRulesProjectionRepository,
    ICardsProjectionRepository
{
    Task<UserDocument?> GetUserAsync(string userId, CancellationToken cancellationToken);
    Task<PartnershipDocument?> GetActivePartnerAsync(string userId, CancellationToken cancellationToken);
    Task<MonthlyAggregateDocument?> GetMonthAggregateAsync(string userId, YearMonth yearMonth, CancellationToken cancellationToken);
    Task<MonthlyAggregateDocument?> GetLatestAggregateBeforeAsync(string userId, YearMonth yearMonth, CancellationToken cancellationToken);
}

public interface ITransactionsProjectionRepository
{
    Task<IReadOnlyList<TransactionProjection>> ListActiveMonthAsync(string userId, YearMonth yearMonth, CancellationToken cancellationToken);
    Task<IReadOnlyList<TransactionProjection>> ListActiveRangeAsync(string userId, DateOnly fromDate, DateOnly throughDate, CancellationToken cancellationToken);
    Task<SourceWatermarkDocument> GetMonthWatermarkAsync(string userId, YearMonth yearMonth, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<YearMonth, SourceWatermarkDocument>> GetYearWatermarksAsync(string userId, int year, CancellationToken cancellationToken);
    Task<MovementTotals> SumTotalsThroughAsync(string userId, DateOnly throughDate, CancellationToken cancellationToken);
    Task<decimal> SumInvestmentsThroughAsync(string userId, DateOnly throughDate, CancellationToken cancellationToken);
}

public sealed record MovementTotals(decimal Entradas, decimal Saidas, decimal Aportes)
{
    public decimal SaldoDelta => Entradas - Saidas - Aportes;
}

public interface IFixedRulesProjectionRepository
{
    Task<IReadOnlyList<FixedRuleDocument>> ListActiveCandidatesAsync(
        string userId,
        DateOnly fromDate,
        DateOnly throughDate,
        CancellationToken cancellationToken);
}

public interface ICardsProjectionRepository
{
    Task<CardDocument?> GetActiveAsync(string userId, string cardId, CancellationToken cancellationToken);
}

public sealed record CopilotReadinessResult(bool Ready, string? Code = null, string? Detail = null);

public interface ICopilotReadinessProbe
{
    Task<CopilotReadinessResult> CheckAsync(CancellationToken cancellationToken);
}
