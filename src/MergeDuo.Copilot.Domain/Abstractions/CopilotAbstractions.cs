using MergeDuo.Aggregates.Domain.Abstractions;
using MergeDuo.Aggregates.Domain.Documents;
using MergeDuo.Aggregates.Domain.Rules;
using MergeDuo.Copilot.Domain.Contracts;

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

public sealed record CopilotReadinessResult(bool Ready, string? Code = null, string? Detail = null);

public interface ICopilotReadinessProbe
{
    Task<CopilotReadinessResult> CheckAsync(CancellationToken cancellationToken);
}
