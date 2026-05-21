using MergeDuo.Copilot.Domain.Abstractions;
using MergeDuo.Copilot.Domain.Contracts;
using MergeDuo.Copilot.Domain.Documents;
using MergeDuo.Copilot.Domain.Exceptions;
using MergeDuo.Copilot.Domain.Options;
using MergeDuo.Copilot.Domain.Rules;

namespace MergeDuo.Copilot.Domain.Services;

public sealed class FixedRuleProjectionService(
    IFixedRulesProjectionRepository fixedRules,
    ICardsProjectionRepository cards)
{
    public async Task<IReadOnlyList<TransactionProjection>> ProjectAsync(
        string userId,
        DateOnly businessDate,
        DateOnly throughDate,
        IReadOnlyList<TransactionProjection> actualTransactions,
        CancellationToken cancellationToken)
    {
        if (throughDate <= businessDate)
        {
            return [];
        }

        var fromDate = businessDate.AddDays(1);
        var candidateFrom = fromDate.AddMonths(-2);
        var rules = await fixedRules.ListActiveCandidatesAsync(userId, candidateFrom, throughDate, cancellationToken);
        if (rules.Count == 0)
        {
            return [];
        }

        var materialized = actualTransactions
            .Where(x => !string.IsNullOrWhiteSpace(x.FixedRuleId))
            .Select(x => OccurrenceKey(x.FixedRuleId!, x.PurchaseDate ?? x.Date))
            .ToHashSet(StringComparer.Ordinal);
        var businessMonth = YearMonth.FromDate(businessDate);

        var projected = new List<TransactionProjection>();
        var cardCache = new Dictionary<string, CardDocument?>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (!IsProjectableRule(rule) ||
                !TryParseDate(rule.StartsAt, out var startsAt))
            {
                continue;
            }

            var endsAt = TryParseDate(rule.EndsAt, out var parsedEndsAt)
                ? parsedEndsAt
                : (DateOnly?)null;

            var cursor = new DateOnly(candidateFrom.Year, candidateFrom.Month, 1);
            var endMonth = new DateOnly(throughDate.Year, throughDate.Month, 1);
            while (cursor <= endMonth)
            {
                var occurrence = ResolveOccurrenceDate(rule.Schedule, cursor.Year, cursor.Month);
                cursor = cursor.AddMonths(1);

                if (occurrence < startsAt || (endsAt is not null && occurrence > endsAt.Value))
                {
                    continue;
                }

                var cashDate = occurrence;
                string? cardId = null;
                if (rule.Category == AggregateCategories.CreditCard)
                {
                    if (string.IsNullOrWhiteSpace(rule.CardId))
                    {
                        continue;
                    }

                    if (!cardCache.TryGetValue(rule.CardId, out var card))
                    {
                        card = await cards.GetActiveAsync(userId, rule.CardId, cancellationToken);
                        cardCache[rule.CardId] = card;
                    }

                    if (card is null)
                    {
                        continue;
                    }

                    cardId = card.Id;
                    cashDate = DueDateForPurchase(card, occurrence);
                }

                if (cashDate <= businessDate || cashDate > throughDate)
                {
                    var cashYearMonth = YearMonth.FromDate(cashDate);
                    if (cashDate > throughDate || cashYearMonth != businessMonth)
                    {
                        continue;
                    }
                }

                var yearMonth = YearMonth.FromDate(cashDate);
                if (materialized.Contains(OccurrenceKey(rule.Id, occurrence)))
                {
                    continue;
                }

                projected.Add(new TransactionProjection
                {
                    Id = $"projected_{rule.Id}_{occurrence:yyyyMMdd}",
                    DocType = "transaction",
                    UserId = userId,
                    YearMonth = yearMonth.Value,
                    Date = cashDate,
                    PurchaseDate = rule.Category == AggregateCategories.CreditCard ? occurrence : null,
                    Category = rule.Category,
                    Description = rule.Description,
                    Kind = KindFor(rule.Category),
                    Amount = rule.Amount,
                    Currency = "BRL",
                    CardId = cardId,
                    FixedRuleId = rule.Id,
                    Projected = true
                });
            }
        }

        return projected;
    }

    public static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out date);

    private static bool IsProjectableRule(FixedRuleDocument rule) =>
        string.Equals(rule.DocType, "fixedRule", StringComparison.Ordinal) &&
        UserIdRules.IsValid(rule.UserId) &&
        rule.Active &&
        rule.DeletedAt is null &&
        rule.Amount > 0 &&
        AggregateCategories.All.Contains(rule.Category);

    private static DateOnly ResolveOccurrenceDate(FixedRuleScheduleDocument schedule, int year, int month)
    {
        var day = schedule.Type switch
        {
            "calendar_day" when schedule.Day is not null =>
                Math.Min(schedule.Day.Value, DateTime.DaysInMonth(year, month)),
            "business_day" when schedule.Ordinal is not null =>
                NthBusinessDay(year, month, schedule.Ordinal.Value).Day,
            "period" when schedule.Period == "start" => 1,
            "period" when schedule.Period == "middle" => Math.Min(15, DateTime.DaysInMonth(year, month)),
            "period" when schedule.Period == "end" => DateTime.DaysInMonth(year, month),
            _ => 1
        };

        return new DateOnly(year, month, day);
    }

    private static DateOnly NthBusinessDay(int year, int month, int ordinal)
    {
        var lastBusinessDay = new DateOnly(year, month, 1);
        var count = 0;
        for (var day = 1; day <= DateTime.DaysInMonth(year, month); day++)
        {
            var candidate = new DateOnly(year, month, day);
            if (candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            count++;
            lastBusinessDay = candidate;
            if (count == ordinal)
            {
                return candidate;
            }
        }

        return lastBusinessDay;
    }

    private static DateOnly DueDateForPurchase(CardDocument card, DateOnly purchaseDate)
    {
        var closingDate = DateWithMonthFallback(purchaseDate.Year, purchaseDate.Month, card.ClosingDay);
        var invoiceMonth = purchaseDate <= closingDate
            ? new DateOnly(purchaseDate.Year, purchaseDate.Month, 1)
            : new DateOnly(purchaseDate.Year, purchaseDate.Month, 1).AddMonths(1);

        var dueMonth = card.DueDay > card.ClosingDay ? invoiceMonth : invoiceMonth.AddMonths(1);
        return DateWithMonthFallback(dueMonth.Year, dueMonth.Month, card.DueDay);
    }

    private static DateOnly DateWithMonthFallback(int year, int month, int requestedDay) =>
        new(year, month, Math.Min(requestedDay, DateTime.DaysInMonth(year, month)));

    private static string OccurrenceKey(string fixedRuleId, DateOnly occurrenceDate) =>
        $"{fixedRuleId}|{occurrenceDate:yyyy-MM-dd}";

    private static string KindFor(string category) => category switch
    {
        AggregateCategories.Income => AggregateKinds.In,
        AggregateCategories.Investment => AggregateKinds.Invest,
        _ => AggregateKinds.Out
    };
}

public sealed class AggregateCalculator(CopilotOptions options)
{
    public MonthlyAggregateDocument Compute(
        string userId,
        YearMonth yearMonth,
        IReadOnlyList<TransactionProjection> ownerTransactions,
        decimal saldo,
        decimal investido,
        decimal saldoHoje,
        decimal investidoHoje,
        DateOnly businessDate,
        bool includesProjected,
        int projectedCount,
        string? partnerUserId,
        IReadOnlyList<TransactionProjection> partnerTransactions,
        DateTimeOffset computedAt)
    {
        ValidateTransactions(userId, yearMonth, ownerTransactions);
        if (partnerUserId is not null)
        {
            ValidateTransactions(partnerUserId, yearMonth, partnerTransactions);
        }

        var ownerTotals = CalculateOwnerTotals(ownerTransactions);
        var totals = new MonthlyTotalsDocument
        {
            Entradas = ownerTotals.Entradas,
            Saidas = ownerTotals.Saidas,
            Aportes = ownerTotals.Aportes,
            Saldo = saldo,
            Investido = investido
        };

        var byOwner = new Dictionary<string, OwnerTotalsDocument>(StringComparer.Ordinal);
        if (ownerTransactions.Count > 0)
        {
            byOwner[userId] = ownerTotals;
        }

        if (partnerUserId is not null && partnerTransactions.Count > 0)
        {
            byOwner[partnerUserId] = CalculateOwnerTotals(partnerTransactions);
        }

        return new MonthlyAggregateDocument
        {
            Id = AggregateDocumentId.For(userId, yearMonth),
            DocType = "monthlyAggregate",
            SchemaVersion = 1,
            UserId = userId,
            Year = yearMonth.Year,
            MonthIdx = yearMonth.MonthIdx,
            YearMonth = yearMonth.Value,
            Totals = totals,
            SnapshotToday = BuildSnapshotToday(yearMonth, saldoHoje, investidoHoje, businessDate),
            DailyBalances = BuildDailyBalances(yearMonth, ownerTransactions, ownerTotals, saldo),
            DailyMovements = BuildDailyMovements(ownerTransactions),
            Projection = new ProjectionDocument
            {
                IncludesProjected = includesProjected,
                ProjectedCount = projectedCount,
                AsOfDate = businessDate
            },
            ByCategory = ownerTransactions
                .GroupBy(x => x.Category, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Sum(t => t.Amount), StringComparer.Ordinal),
            ByCard = ownerTransactions
                .Where(x => x.Category == AggregateCategories.CreditCard && !string.IsNullOrWhiteSpace(x.CardId))
                .GroupBy(x => x.CardId!, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Sum(t => t.Amount), StringComparer.Ordinal),
            ByOwner = byOwner,
            TransactionsCount = ownerTransactions.Count(x => !x.Projected),
            ComputedAt = computedAt,
            SourceVersion = options.SourceVersion
        };
    }

    private static List<DailyBalanceDocument> BuildDailyBalances(
        YearMonth yearMonth,
        IReadOnlyList<TransactionProjection> ownerTransactions,
        OwnerTotalsDocument ownerTotals,
        decimal monthEndSaldo)
    {
        var monthDelta = ownerTotals.Entradas - ownerTotals.Saidas - ownerTotals.Aportes;
        var runningSaldo = monthEndSaldo - monthDelta;
        var deltaByDay = ownerTransactions
            .GroupBy(x => x.Date.Day)
            .ToDictionary(
                x => x.Key,
                x => x.Sum(DailySaldoDelta),
                EqualityComparer<int>.Default);

        var balances = new List<DailyBalanceDocument>(yearMonth.LastDay.Day);
        for (var day = 1; day <= yearMonth.LastDay.Day; day++)
        {
            runningSaldo += deltaByDay.GetValueOrDefault(day);
            balances.Add(new DailyBalanceDocument
            {
                Day = day,
                Saldo = runningSaldo
            });
        }

        return balances;
    }

    private static List<DailyMovementDocument> BuildDailyMovements(
        IReadOnlyList<TransactionProjection> ownerTransactions) =>
        ownerTransactions
            .OrderBy(x => x.Date.Day)
            .ThenBy(x => x.Projected)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .Select(x => new DailyMovementDocument
            {
                Day = x.Date.Day,
                Id = x.Id,
                UserId = x.UserId,
                Category = x.Category,
                Kind = x.Kind,
                Description = x.Description,
                Amount = x.Amount,
                CardId = x.CardId,
                FixedRuleId = x.FixedRuleId,
                Projected = x.Projected,
                PurchaseDate = x.PurchaseDate
            })
            .ToList();

    private static decimal DailySaldoDelta(TransactionProjection projection) => projection.Kind switch
    {
        AggregateKinds.In => projection.Amount,
        AggregateKinds.Invest => -projection.Amount,
        _ => -projection.Amount
    };

    private static SnapshotTodayDocument? BuildSnapshotToday(
        YearMonth yearMonth,
        decimal saldoHoje,
        decimal investidoHoje,
        DateOnly businessDate)
    {
        if (yearMonth != YearMonth.FromDate(businessDate))
        {
            return null;
        }

        return new SnapshotTodayDocument
        {
            SaldoHoje = saldoHoje,
            InvestidoHoje = investidoHoje,
            PatrimonioHoje = saldoHoje + investidoHoje,
            AsOfDate = businessDate
        };
    }

    public static OwnerTotalsDocument CalculateTotals(IEnumerable<TransactionProjection> projections) =>
        CalculateOwnerTotals(projections);

    private static OwnerTotalsDocument CalculateOwnerTotals(IEnumerable<TransactionProjection> projections)
    {
        var totals = new OwnerTotalsDocument();
        foreach (var projection in projections)
        {
            switch (projection.Kind)
            {
                case AggregateKinds.In:
                    totals.Entradas += projection.Amount;
                    break;
                case AggregateKinds.Out:
                    totals.Saidas += projection.Amount;
                    break;
                case AggregateKinds.Invest:
                    totals.Aportes += projection.Amount;
                    break;
            }
        }

        return totals;
    }

    private static void ValidateTransactions(string userId, YearMonth yearMonth, IEnumerable<TransactionProjection> projections)
    {
        foreach (var projection in projections)
        {
            if (!string.Equals(projection.DocType, "transaction", StringComparison.Ordinal) ||
                !string.Equals(projection.UserId, userId, StringComparison.Ordinal) ||
                !string.Equals(projection.YearMonth, yearMonth.Value, StringComparison.Ordinal) ||
                projection.DeletedAt is not null ||
                projection.Amount < 0 ||
                !AggregateCategories.All.Contains(projection.Category) ||
                !AggregateKinds.All.Contains(projection.Kind))
            {
                throw new InvalidTransactionProjectionException("Invalid transaction projection.");
            }

            if (projection.Category == AggregateCategories.CreditCard && string.IsNullOrWhiteSpace(projection.CardId))
            {
                throw new InvalidTransactionProjectionException("Credit card transaction without cardId.");
            }
        }
    }
}

public static class AggregateMapping
{
    public static MonthlyAggregateResponse ToResponse(
        MonthlyAggregateDocument document,
        string source,
        bool isStale,
        string? staleReason = null)
    {
        var sourceWatermark = document.SourceWatermark ?? new SourceWatermarkDocument();
        return new MonthlyAggregateResponse(
            document.Id,
            document.UserId,
            document.Year,
            document.MonthIdx + 1,
            document.MonthIdx,
            document.YearMonth,
            new MonthlyTotalsResponse(
                document.Totals.Entradas,
                document.Totals.Saidas,
                document.Totals.Aportes,
                document.Totals.Saldo,
                document.Totals.Investido),
            document.SnapshotToday is null
                ? null
                : new SnapshotTodayResponse(
                    document.SnapshotToday.SaldoHoje,
                    document.SnapshotToday.InvestidoHoje,
                    document.SnapshotToday.PatrimonioHoje,
                    document.SnapshotToday.AsOfDate),
            document.DailyBalances
                .OrderBy(x => x.Day)
                .Select(x => new DailyBalanceResponse(x.Day, x.Saldo))
                .ToArray(),
            document.DailyMovements
                .OrderBy(x => x.Day)
                .ThenBy(x => x.Projected)
                .ThenBy(x => x.Id, StringComparer.Ordinal)
                .Select(x => new DailyMovementResponse(
                    x.Day,
                    x.Id,
                    x.UserId,
                    x.Category,
                    x.Kind,
                    x.Description,
                    x.Amount,
                    x.CardId,
                    x.FixedRuleId,
                    x.Projected,
                    x.PurchaseDate))
                .ToArray(),
            new ProjectionResponse(
                document.Projection.IncludesProjected,
                document.Projection.ProjectedCount,
                document.Projection.AsOfDate),
            document.ByCategory,
            document.ByCard,
            document.ByOwner.ToDictionary(
                x => x.Key,
                x => new OwnerTotalsResponse(x.Value.Entradas, x.Value.Saidas, x.Value.Aportes),
                StringComparer.Ordinal),
            document.TransactionsCount,
            document.ComputedAt,
            document.SourceVersion,
            isStale,
            source,
            new FreshnessResponse(isStale ? "stale" : "fresh", isStale ? staleReason ?? "stale" : null),
            new SourceWatermarkResponse(
                sourceWatermark.MaxTransactionUpdatedAt,
                sourceWatermark.ActiveTransactionsCount));
    }
}

public static class BusinessClock
{
    public static DateOnly Today(TimeProvider clock, string timeZoneId)
    {
        var timeZone = ResolveTimeZone(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), timeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException) when (timeZoneId == "America/Sao_Paulo")
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
        catch (InvalidTimeZoneException) when (timeZoneId == "America/Sao_Paulo")
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
    }
}
