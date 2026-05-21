using MergeDuo.Aggregates.Domain.Abstractions;
using MergeDuo.Aggregates.Domain.Documents;
using MergeDuo.Aggregates.Domain.Rules;
using MergeDuo.Aggregates.Domain.Services;
using MergeDuo.Copilot.Domain.Abstractions;

namespace MergeDuo.Copilot.Tests.Fakes;

public sealed class InMemoryCopilotRepository(string configuredUserId) : ICopilotReadRepository, ICopilotReadinessProbe
{
    private readonly object _gate = new();
    private readonly Dictionary<string, UserDocument> _users = [];
    private readonly Dictionary<(string UserId, string YearMonth), MonthlyAggregateDocument> _aggregates = [];
    private readonly List<TransactionProjection> _transactions = [];
    private readonly List<PartnershipDocument> _partnerships = [];
    private readonly List<FixedRuleDocument> _fixedRules = [];
    private readonly Dictionary<(string UserId, string CardId), CardDocument> _cards = [];

    public int MutationCount { get; private set; }

    public void SeedUser(string userId, decimal startingBalance = 0m)
    {
        lock (_gate)
        {
            _users[userId] = new UserDocument
            {
                Id = userId,
                DocType = "user",
                Financial = new UserFinancialDocument { StartingBalance = startingBalance }
            };
        }
    }

    public void SeedAggregate(MonthlyAggregateDocument aggregate)
    {
        lock (_gate)
        {
            _aggregates[(aggregate.UserId, aggregate.YearMonth)] = Clone(aggregate);
        }
    }

    public void SeedTransaction(TransactionProjection transaction)
    {
        lock (_gate)
        {
            _transactions.Add(Clone(transaction));
        }
    }

    public void SeedPartnership(PartnershipDocument partnership)
    {
        lock (_gate)
        {
            _partnerships.Add(Clone(partnership));
        }
    }

    public void SeedFixedRule(FixedRuleDocument rule)
    {
        lock (_gate)
        {
            _fixedRules.Add(Clone(rule));
        }
    }

    public void SeedCard(CardDocument card)
    {
        lock (_gate)
        {
            _cards[(card.UserId, card.Id)] = Clone(card);
        }
    }

    public Task<CopilotReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!UserIdRules.IsValid(configuredUserId))
            {
                return Task.FromResult(new CopilotReadinessResult(false, "missing_copilot_user_id", "Copilot:UserId is missing or invalid."));
            }

            var ready = _users.TryGetValue(configuredUserId, out var user) && user.DeletedAt is null;
            return Task.FromResult(ready
                ? new CopilotReadinessResult(true)
                : new CopilotReadinessResult(false, "copilot_user_not_found", "Configured Copilot user was not found."));
        }
    }

    public Task<UserDocument?> GetUserAsync(string userId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_users.TryGetValue(userId, out var user) ? Clone(user) : null);
        }
    }

    public Task<PartnershipDocument?> GetActivePartnerAsync(string userId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var partnership = _partnerships
                .FirstOrDefault(x => x.UserId == userId && x.Status == "active");
            return Task.FromResult(partnership is null ? null : Clone(partnership));
        }
    }

    public Task<MonthlyAggregateDocument?> GetMonthAggregateAsync(
        string userId,
        YearMonth yearMonth,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_aggregates.TryGetValue((userId, yearMonth.Value), out var aggregate)
                ? Clone(aggregate)
                : null);
        }
    }

    public Task<MonthlyAggregateDocument?> GetLatestAggregateBeforeAsync(
        string userId,
        YearMonth yearMonth,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var aggregate = _aggregates.Values
                .Where(x => x.UserId == userId && string.CompareOrdinal(x.YearMonth, yearMonth.Value) < 0)
                .OrderBy(x => x.YearMonth, StringComparer.Ordinal)
                .LastOrDefault();
            return Task.FromResult(aggregate is null ? null : Clone(aggregate));
        }
    }

    public Task<IReadOnlyList<TransactionProjection>> ListActiveMonthAsync(
        string userId,
        YearMonth yearMonth,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<TransactionProjection> result = _transactions
                .Where(x => x.UserId == userId && x.YearMonth == yearMonth.Value && x.DeletedAt is null)
                .Select(Clone)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<TransactionProjection>> ListActiveRangeAsync(
        string userId,
        DateOnly fromDate,
        DateOnly throughDate,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<TransactionProjection> result = _transactions
                .Where(x => x.UserId == userId && x.Date >= fromDate && x.Date <= throughDate && x.DeletedAt is null)
                .Select(Clone)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<SourceWatermarkDocument> GetMonthWatermarkAsync(
        string userId,
        YearMonth yearMonth,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(Watermark(_transactions.Where(x => x.UserId == userId && x.YearMonth == yearMonth.Value)));
        }
    }

    public Task<IReadOnlyDictionary<YearMonth, SourceWatermarkDocument>> GetYearWatermarksAsync(
        string userId,
        int year,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyDictionary<YearMonth, SourceWatermarkDocument> result = _transactions
                .Where(x => x.UserId == userId && x.YearMonth.StartsWith($"{year:D4}-", StringComparison.Ordinal))
                .GroupBy(x => x.YearMonth)
                .Where(x => YearMonth.TryParse(x.Key, out _))
                .ToDictionary(x => YearMonth.Parse(x.Key), Watermark);
            return Task.FromResult(result);
        }
    }

    public Task<MovementTotals> SumTotalsThroughAsync(
        string userId,
        DateOnly throughDate,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var totals = _transactions
                .Where(x => x.UserId == userId && x.Date <= throughDate && x.DeletedAt is null)
                .Aggregate(
                    new MovementTotals(0, 0, 0),
                    (current, item) => item.Kind switch
                    {
                        AggregateKinds.In => current with { Entradas = current.Entradas + item.Amount },
                        AggregateKinds.Out => current with { Saidas = current.Saidas + item.Amount },
                        AggregateKinds.Invest => current with { Aportes = current.Aportes + item.Amount },
                        _ => current
                    });
            return Task.FromResult(totals);
        }
    }

    public Task<decimal> SumInvestmentsThroughAsync(
        string userId,
        DateOnly throughDate,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_transactions
                .Where(x => x.UserId == userId && x.Kind == AggregateKinds.Invest && x.Date <= throughDate && x.DeletedAt is null)
                .Sum(x => x.Amount));
        }
    }

    public Task<IReadOnlyList<FixedRuleDocument>> ListActiveCandidatesAsync(
        string userId,
        DateOnly fromDate,
        DateOnly throughDate,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<FixedRuleDocument> result = _fixedRules
                .Where(x => x.UserId == userId &&
                            x.Active &&
                            x.DeletedAt is null &&
                            FixedRuleProjectionService.TryParseDate(x.StartsAt, out var startsAt) &&
                            startsAt <= throughDate &&
                            (!FixedRuleProjectionService.TryParseDate(x.EndsAt, out var endsAt) || endsAt >= fromDate))
                .Select(Clone)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<CardDocument?> GetActiveAsync(string userId, string cardId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_cards.TryGetValue((userId, cardId), out var card) && card.DeletedAt is null
                ? Clone(card)
                : null);
        }
    }

    private static SourceWatermarkDocument Watermark(IEnumerable<TransactionProjection> transactions)
    {
        var items = transactions.ToArray();
        return new SourceWatermarkDocument
        {
            MaxTransactionUpdatedAt = items
                .Select(x => x.UpdatedAt)
                .Where(x => x is not null)
                .DefaultIfEmpty()
                .Max(),
            ActiveTransactionsCount = items.Count(x => x.DeletedAt is null)
        };
    }

    private static UserDocument Clone(UserDocument user) =>
        new()
        {
            Id = user.Id,
            DocType = user.DocType,
            Financial = new UserFinancialDocument { StartingBalance = user.Financial.StartingBalance },
            DeletedAt = user.DeletedAt
        };

    private static PartnershipDocument Clone(PartnershipDocument partnership) =>
        new()
        {
            Id = partnership.Id,
            DocType = partnership.DocType,
            PartnershipId = partnership.PartnershipId,
            UserId = partnership.UserId,
            PartnerUserId = partnership.PartnerUserId,
            Status = partnership.Status,
            MergedSince = partnership.MergedSince
        };

    private static TransactionProjection Clone(TransactionProjection transaction) =>
        new()
        {
            Id = transaction.Id,
            DocType = transaction.DocType,
            UserId = transaction.UserId,
            YearMonth = transaction.YearMonth,
            Date = transaction.Date,
            PurchaseDate = transaction.PurchaseDate,
            Category = transaction.Category,
            Description = transaction.Description,
            Kind = transaction.Kind,
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            CardId = transaction.CardId,
            FixedRuleId = transaction.FixedRuleId,
            Projected = transaction.Projected,
            UpdatedAt = transaction.UpdatedAt,
            DeletedAt = transaction.DeletedAt
        };

    private static FixedRuleDocument Clone(FixedRuleDocument rule) =>
        new()
        {
            Id = rule.Id,
            DocType = rule.DocType,
            UserId = rule.UserId,
            Category = rule.Category,
            Description = rule.Description,
            Amount = rule.Amount,
            CardId = rule.CardId,
            Schedule = new FixedRuleScheduleDocument
            {
                Type = rule.Schedule.Type,
                Day = rule.Schedule.Day,
                Ordinal = rule.Schedule.Ordinal,
                Period = rule.Schedule.Period
            },
            StartsAt = rule.StartsAt,
            EndsAt = rule.EndsAt,
            Active = rule.Active,
            DeletedAt = rule.DeletedAt
        };

    private static CardDocument Clone(CardDocument card) =>
        new()
        {
            Id = card.Id,
            DocType = card.DocType,
            UserId = card.UserId,
            ClosingDay = card.ClosingDay,
            DueDay = card.DueDay,
            DeletedAt = card.DeletedAt
        };

    private static MonthlyAggregateDocument Clone(MonthlyAggregateDocument document) =>
        new()
        {
            Id = document.Id,
            DocType = document.DocType,
            SchemaVersion = document.SchemaVersion,
            UserId = document.UserId,
            Year = document.Year,
            MonthIdx = document.MonthIdx,
            YearMonth = document.YearMonth,
            Totals = new MonthlyTotalsDocument
            {
                Entradas = document.Totals.Entradas,
                Saidas = document.Totals.Saidas,
                Aportes = document.Totals.Aportes,
                Saldo = document.Totals.Saldo,
                Investido = document.Totals.Investido
            },
            SnapshotToday = document.SnapshotToday is null
                ? null
                : new SnapshotTodayDocument
                {
                    SaldoHoje = document.SnapshotToday.SaldoHoje,
                    InvestidoHoje = document.SnapshotToday.InvestidoHoje,
                    PatrimonioHoje = document.SnapshotToday.PatrimonioHoje,
                    AsOfDate = document.SnapshotToday.AsOfDate
                },
            DailyBalances = document.DailyBalances
                .Select(x => new DailyBalanceDocument { Day = x.Day, Saldo = x.Saldo })
                .ToList(),
            DailyMovements = document.DailyMovements
                .Select(x => new DailyMovementDocument
                {
                    Day = x.Day,
                    Id = x.Id,
                    UserId = x.UserId,
                    Category = x.Category,
                    Description = x.Description,
                    Kind = x.Kind,
                    Amount = x.Amount,
                    CardId = x.CardId,
                    FixedRuleId = x.FixedRuleId,
                    Projected = x.Projected,
                    PurchaseDate = x.PurchaseDate
                })
                .ToList(),
            Projection = new ProjectionDocument
            {
                IncludesProjected = document.Projection.IncludesProjected,
                ProjectedCount = document.Projection.ProjectedCount,
                AsOfDate = document.Projection.AsOfDate
            },
            ByCategory = new Dictionary<string, decimal>(document.ByCategory, StringComparer.Ordinal),
            ByCard = new Dictionary<string, decimal>(document.ByCard, StringComparer.Ordinal),
            ByOwner = document.ByOwner.ToDictionary(
                x => x.Key,
                x => new OwnerTotalsDocument
                {
                    Entradas = x.Value.Entradas,
                    Saidas = x.Value.Saidas,
                    Aportes = x.Value.Aportes
                },
                StringComparer.Ordinal),
            TransactionsCount = document.TransactionsCount,
            ComputedAt = document.ComputedAt,
            SourceVersion = document.SourceVersion,
            SourceWatermark = new SourceWatermarkDocument
            {
                MaxTransactionUpdatedAt = document.SourceWatermark.MaxTransactionUpdatedAt,
                ActiveTransactionsCount = document.SourceWatermark.ActiveTransactionsCount
            },
            ETag = document.ETag
        };
}
