using System.Globalization;
using MergeDuo.Aggregates.Domain.Contracts;
using MergeDuo.Aggregates.Domain.Documents;
using MergeDuo.Aggregates.Domain.Rules;
using MergeDuo.Aggregates.Domain.Services;
using MergeDuo.Copilot.Domain.Abstractions;
using MergeDuo.Copilot.Domain.Contracts;
using MergeDuo.Copilot.Domain.Exceptions;
using MergeDuo.Copilot.Domain.Options;

namespace MergeDuo.Copilot.Domain.Services;

public sealed class CopilotFinanceService(
    ICopilotReadRepository repository,
    FixedRuleProjectionService projectionService,
    AggregateCalculator calculator,
    CopilotOptions options,
    TimeProvider clock) : ICopilotFinanceService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<CopilotMonthSummaryResponse> GetMonthSummaryAsync(
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var yearMonth = YearMonth.FromRoute(year, month);
        var owners = await ResolveOwnersAsync(cancellationToken);
        return await BuildMonthSummaryAsync(owners, yearMonth, includeAllRelevantMovements: false, cancellationToken);
    }

    public async Task<CopilotNextThreeMonthsResponse> GetNextThreeMonthsAsync(
        int? year,
        int? month,
        CancellationToken cancellationToken)
    {
        var businessDate = BusinessToday();
        var first = year is null || month is null
            ? YearMonth.FromDate(businessDate)
            : YearMonth.FromRoute(year.Value, month.Value);

        var owners = await ResolveOwnersAsync(cancellationToken);
        var months = new List<CopilotMonthSummaryResponse>(3);
        for (var offset = 0; offset < 3; offset++)
        {
            months.Add(await BuildMonthSummaryAsync(owners, first.AddMonths(offset), includeAllRelevantMovements: true, cancellationToken));
        }

        var totalEntradas = months.Sum(x => x.Totals.Entradas);
        var totalSaidas = months.Sum(x => x.Totals.Saidas);
        var totalAportes = months.Sum(x => x.Totals.Aportes);
        var last = months[^1].Totals;
        var freshness = CombineFreshness(months.Select(x => x.DataFreshness));
        var upcoming = months
            .SelectMany(x => x.RelevantMovements)
            .Where(x => x.Date >= businessDate || x.Projected)
            .OrderBy(x => x.Date)
            .ThenByDescending(x => x.Amount)
            .Take(12)
            .ToArray();

        var totals = new CopilotTotalsResponse(
            totalEntradas,
            totalSaidas,
            totalAportes,
            last.Saldo,
            last.Investido,
            last.Patrimonio,
            null,
            null,
            null);

        var response = new CopilotNextThreeMonthsResponse(
            owners.Scope,
            owners.Responses,
            months[0].Period.From,
            months[^1].Period.To,
            months,
            totals,
            upcoming,
            freshness,
            clock.GetUtcNow(),
            "");

        return response with { SummaryText = NextThreeMonthsText(response) };
    }

    public async Task<CopilotPurchaseSimulationResponse> SimulatePurchaseAsync(
        PurchaseSimulationRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new CopilotBadRequestException("invalid_request", "Request body is required.");
        }

        var description = ValidateDescription(request.Description);
        var amount = ValidateAmount(request.Amount);
        var purchaseDate = request.PurchaseDate
            ?? throw new CopilotBadRequestException("invalid_purchase_date", "purchaseDate is required.");
        var paymentType = ValidatePaymentType(request.PaymentType);
        var totalInstallments = ValidateInstallments(request.Installments);
        var owners = await ResolveOwnersAsync(cancellationToken);

        string? cardId = null;
        string impactOwnerUserId = owners.PrimaryUserId;
        CardDocument? card = null;
        if (paymentType == "credit_card")
        {
            cardId = string.IsNullOrWhiteSpace(request.CardId)
                ? throw new CopilotBadRequestException("missing_card_id", "cardId is required for credit card simulations.")
                : request.CardId.Trim();

            foreach (var owner in owners.Responses)
            {
                card = await repository.GetActiveAsync(owner.UserId, cardId, cancellationToken);
                if (card is not null)
                {
                    impactOwnerUserId = owner.UserId;
                    break;
                }
            }

            if (card is null)
            {
                throw new CopilotBadRequestException("card_not_found", "Card not found.");
            }
        }
        else if (totalInstallments != 1)
        {
            throw new CopilotBadRequestException("invalid_installments", "Cash simulations must use one installment.");
        }

        var split = SplitAmount(amount, totalInstallments);
        var installments = split.Select((installmentAmount, index) =>
        {
            var installmentIndex = index + 1;
            var impactDate = paymentType == "credit_card"
                ? DueDateForPurchase(card!, purchaseDate, installmentIndex)
                : purchaseDate;
            var yearMonth = YearMonth.FromDate(impactDate);
            return new PurchaseSimulationInstallmentResponse(
                installmentIndex,
                totalInstallments,
                impactOwnerUserId,
                impactDate,
                yearMonth.Value,
                installmentAmount);
        }).ToArray();

        var impactedMonths = installments
            .Select(x => YearMonth.Parse(x.YearMonth))
            .Distinct()
            .OrderBy(x => x.Year)
            .ThenBy(x => x.Month)
            .ToArray();

        var monthImpacts = new List<PurchaseSimulationMonthImpactResponse>(impactedMonths.Length);
        decimal cumulativeImpact = 0m;
        foreach (var impactedMonth in impactedMonths)
        {
            var baseline = await BuildMonthSummaryAsync(owners, impactedMonth, includeAllRelevantMovements: true, cancellationToken);
            var monthImpact = installments
                .Where(x => x.YearMonth == impactedMonth.Value)
                .Sum(x => x.Amount);
            cumulativeImpact += monthImpact;

            var projectedTotals = baseline.Totals with
            {
                Saidas = baseline.Totals.Saidas + monthImpact,
                Saldo = baseline.Totals.Saldo - cumulativeImpact,
                Patrimonio = baseline.Totals.Patrimonio - cumulativeImpact,
                SaldoHoje = null,
                InvestidoHoje = null,
                PatrimonioHoje = null
            };

            monthImpacts.Add(new PurchaseSimulationMonthImpactResponse(
                baseline.Period,
                monthImpact,
                cumulativeImpact,
                baseline.Totals,
                projectedTotals));
        }

        var response = new CopilotPurchaseSimulationResponse(
            owners.Scope,
            owners.Responses,
            description,
            paymentType,
            amount,
            purchaseDate,
            cardId,
            installments,
            monthImpacts,
            clock.GetUtcNow(),
            "");

        return response with { SummaryText = SimulationText(response) };
    }

    private async Task<CopilotMonthSummaryResponse> BuildMonthSummaryAsync(
        OwnerSet owners,
        YearMonth yearMonth,
        bool includeAllRelevantMovements,
        CancellationToken cancellationToken)
    {
        var ownerMonths = new List<OwnerMonthData>(owners.Responses.Count);
        foreach (var owner in owners.Responses)
        {
            ownerMonths.Add(await LoadOwnerMonthAsync(owner.UserId, yearMonth, cancellationToken));
        }

        var totals = SumTotals(ownerMonths);
        var byCategory = SumDictionaries(ownerMonths.Select(x => x.Response.ByCategory));
        var byCard = SumDictionaries(ownerMonths.Select(x => x.Response.ByCard));
        var movements = ownerMonths
            .SelectMany(x => x.Response.DailyMovements.Select(m => ToMovement(yearMonth, m)))
            .OrderByDescending(x => includeAllRelevantMovements ? 0 : x.Amount)
            .ThenBy(x => x.Date)
            .ThenBy(x => x.OwnerUserId, StringComparer.Ordinal)
            .Take(includeAllRelevantMovements ? 40 : 8)
            .ToArray();

        if (!includeAllRelevantMovements)
        {
            movements = movements
                .OrderByDescending(x => x.Amount)
                .ThenBy(x => x.Date)
                .Take(8)
                .ToArray();
        }

        var freshness = CombineFreshness(ownerMonths.Select(x => x.Freshness));
        var response = new CopilotMonthSummaryResponse(
            owners.Scope,
            owners.Responses,
            Period(yearMonth),
            totals,
            byCategory,
            byCard,
            movements,
            ownerMonths.Any(x => x.Response.Projection.IncludesProjected),
            ownerMonths.Sum(x => x.Response.Projection.ProjectedCount),
            ownerMonths.Sum(x => x.Response.TransactionsCount),
            freshness,
            clock.GetUtcNow(),
            "");

        return response with { SummaryText = MonthSummaryText(response) };
    }

    private async Task<OwnerMonthData> LoadOwnerMonthAsync(
        string userId,
        YearMonth yearMonth,
        CancellationToken cancellationToken)
    {
        var aggregate = await repository.GetMonthAggregateAsync(userId, yearMonth, cancellationToken);
        if (aggregate is not null)
        {
            var freshness = await ResolveFreshnessAsync(aggregate, cancellationToken);
            return new OwnerMonthData(
                AggregateMapping.ToResponse(aggregate, "stored", freshness.State != "fresh", freshness.Reason),
                freshness);
        }

        var computed = await ComputeTransientMonthAsync(userId, yearMonth, cancellationToken);
        return new OwnerMonthData(
            AggregateMapping.ToResponse(computed, "computed_transient", false),
            new CopilotFreshnessResponse("fresh", "computed_transient", null));
    }

    private async Task<MonthlyAggregateDocument> ComputeTransientMonthAsync(
        string userId,
        YearMonth yearMonth,
        CancellationToken cancellationToken)
    {
        var ownerTransactions = await repository.ListActiveMonthAsync(userId, yearMonth, cancellationToken);
        var sourceWatermark = await repository.GetMonthWatermarkAsync(userId, yearMonth, cancellationToken);
        var businessDate = BusinessToday();
        var startingBalance = await StartingBalanceAsync(userId, cancellationToken);
        var accumulatedActual = await repository.SumTotalsThroughAsync(userId, yearMonth.LastDay, cancellationToken);

        IReadOnlyList<TransactionProjection> projectedThrough = [];
        if (businessDate < yearMonth.LastDay)
        {
            var projectionRangeStart = new DateOnly(businessDate.Year, businessDate.Month, 1);
            var actualRange = await repository.ListActiveRangeAsync(userId, projectionRangeStart, yearMonth.LastDay, cancellationToken);
            projectedThrough = await projectionService.ProjectAsync(
                userId,
                businessDate,
                yearMonth.LastDay,
                actualRange,
                cancellationToken);
        }

        var projectedMonth = projectedThrough
            .Where(x => x.YearMonth == yearMonth.Value)
            .ToArray();
        var ownerTransactionsWithProjections = ownerTransactions.Concat(projectedMonth).ToArray();
        var projectedTotalsThrough = AggregateCalculator.CalculateTotals(projectedThrough);
        var projectedSaldoDelta = projectedTotalsThrough.Entradas - projectedTotalsThrough.Saidas - projectedTotalsThrough.Aportes;

        var saldo = startingBalance + accumulatedActual.SaldoDelta + projectedSaldoDelta;
        var investido = accumulatedActual.Aportes + projectedTotalsThrough.Aportes;

        decimal saldoHoje = 0m;
        decimal investidoHoje = 0m;
        if (yearMonth == YearMonth.FromDate(businessDate))
        {
            var todayActual = await repository.SumTotalsThroughAsync(userId, businessDate, cancellationToken);
            saldoHoje = startingBalance + todayActual.SaldoDelta;
            investidoHoje = todayActual.Aportes;
        }

        var aggregate = calculator.Compute(
            userId,
            yearMonth,
            ownerTransactionsWithProjections,
            saldo,
            investido,
            saldoHoje,
            investidoHoje,
            businessDate,
            projectedThrough.Count > 0,
            projectedThrough.Count,
            null,
            [],
            clock.GetUtcNow());
        aggregate.SourceWatermark = sourceWatermark;
        return aggregate;
    }

    private async Task<CopilotFreshnessResponse> ResolveFreshnessAsync(
        MonthlyAggregateDocument aggregate,
        CancellationToken cancellationToken)
    {
        if (aggregate.SourceVersion < options.SourceVersion)
        {
            return new CopilotFreshnessResponse("stale", "stored", "source_version");
        }

        var watermark = await repository.GetMonthWatermarkAsync(
            aggregate.UserId,
            new YearMonth(aggregate.Year, aggregate.MonthIdx + 1),
            cancellationToken);

        var aggregateWatermark = aggregate.SourceWatermark ?? new SourceWatermarkDocument();
        if (watermark.MaxTransactionUpdatedAt > aggregateWatermark.MaxTransactionUpdatedAt ||
            watermark.ActiveTransactionsCount != aggregateWatermark.ActiveTransactionsCount)
        {
            return new CopilotFreshnessResponse("stale", "stored", "source_behind");
        }

        return new CopilotFreshnessResponse("fresh", "stored", null);
    }

    private async Task<decimal> StartingBalanceAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await repository.GetUserAsync(userId, cancellationToken);
        return user?.DeletedAt is null ? user?.Financial.StartingBalance ?? 0m : 0m;
    }

    private async Task<OwnerSet> ResolveOwnersAsync(CancellationToken cancellationToken)
    {
        var configuredUserId = options.UserId.Trim();
        if (!UserIdRules.IsValid(configuredUserId))
        {
            throw new CopilotConfigurationException("missing_copilot_user_id", "Copilot:UserId is missing or invalid.");
        }

        var user = await repository.GetUserAsync(configuredUserId, cancellationToken);
        if (user is null || user.DeletedAt is not null)
        {
            throw new CopilotConfigurationException("copilot_user_not_found", "Configured Copilot user was not found.");
        }

        var owners = new List<CopilotOwnerResponse>
        {
            new(configuredUserId, "primary")
        };

        var partnership = await repository.GetActivePartnerAsync(configuredUserId, cancellationToken);
        if (partnership is { Status: "active" } && UserIdRules.IsValid(partnership.PartnerUserId))
        {
            owners.Add(new CopilotOwnerResponse(partnership.PartnerUserId, "partner"));
        }

        return new OwnerSet(
            owners.Count > 1 ? "merged" : "single",
            configuredUserId,
            owners);
    }

    private static CopilotTotalsResponse SumTotals(IReadOnlyList<OwnerMonthData> months)
    {
        var entradas = months.Sum(x => x.Response.Totals.Entradas);
        var saidas = months.Sum(x => x.Response.Totals.Saidas);
        var aportes = months.Sum(x => x.Response.Totals.Aportes);
        var saldo = months.Sum(x => x.Response.Totals.Saldo);
        var investido = months.Sum(x => x.Response.Totals.Investido);

        decimal? saldoHoje = null;
        decimal? investidoHoje = null;
        decimal? patrimonioHoje = null;
        var snapshots = months
            .Select(x => x.Response.SnapshotToday)
            .Where(x => x is not null)
            .ToArray();
        if (snapshots.Length > 0)
        {
            saldoHoje = snapshots.Sum(x => x!.SaldoHoje);
            investidoHoje = snapshots.Sum(x => x!.InvestidoHoje);
            patrimonioHoje = snapshots.Sum(x => x!.PatrimonioHoje);
        }

        return new CopilotTotalsResponse(
            entradas,
            saidas,
            aportes,
            saldo,
            investido,
            saldo + investido,
            saldoHoje,
            investidoHoje,
            patrimonioHoje);
    }

    private static IReadOnlyDictionary<string, decimal> SumDictionaries(
        IEnumerable<IReadOnlyDictionary<string, decimal>> dictionaries)
    {
        var totals = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var dictionary in dictionaries)
        {
            foreach (var (key, value) in dictionary)
            {
                totals[key] = totals.GetValueOrDefault(key) + value;
            }
        }

        return totals
            .OrderByDescending(x => x.Value)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
    }

    private static CopilotMovementResponse ToMovement(YearMonth yearMonth, DailyMovementResponse movement) =>
        new(
            new DateOnly(yearMonth.Year, yearMonth.Month, movement.Day),
            movement.Day,
            movement.UserId,
            movement.Category,
            movement.Kind,
            movement.Description,
            movement.Amount,
            movement.CardId,
            movement.FixedRuleId,
            movement.Projected);

    private static CopilotFreshnessResponse CombineFreshness(IEnumerable<CopilotFreshnessResponse> values)
    {
        var items = values.ToArray();
        if (items.Any(x => x.State != "fresh"))
        {
            return new CopilotFreshnessResponse(
                "stale",
                string.Join("+", items.Select(x => x.Source).Distinct(StringComparer.Ordinal)),
                string.Join("+", items.Select(x => x.Reason).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal)));
        }

        return new CopilotFreshnessResponse(
            "fresh",
            string.Join("+", items.Select(x => x.Source).Distinct(StringComparer.Ordinal)),
            null);
    }

    private static CopilotPeriodResponse Period(YearMonth yearMonth) =>
        new(yearMonth.Year, yearMonth.Month, yearMonth.Value, yearMonth.FirstDay, yearMonth.LastDay);

    private DateOnly BusinessToday() => BusinessClock.Today(clock, options.BusinessTimeZone);

    private static string ValidateDescription(string? value)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.Length is 0 or > 200)
        {
            throw new CopilotBadRequestException("invalid_description", "Invalid description.");
        }

        return normalized;
    }

    private static decimal ValidateAmount(decimal? value)
    {
        if (value is null || value <= 0 || decimal.GetBits(value.Value)[3] >> 16 > 2)
        {
            throw new CopilotBadRequestException("invalid_amount", "Invalid amount.");
        }

        return value.Value;
    }

    private static string ValidatePaymentType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "cash" or "credit_card"
            ? normalized
            : throw new CopilotBadRequestException("invalid_payment_type", "paymentType must be cash or credit_card.");
    }

    private int ValidateInstallments(int? value)
    {
        var normalized = value ?? 1;
        if (normalized < 1 || normalized > options.MaxSimulationInstallments)
        {
            throw new CopilotBadRequestException("invalid_installments", "Invalid installments.");
        }

        return normalized;
    }

    private static decimal[] SplitAmount(decimal amount, int total)
    {
        var totalCents = checked((long)(amount * 100m));
        var baseCents = totalCents / total;
        var values = new decimal[total];
        for (var i = 0; i < total - 1; i++)
        {
            values[i] = baseCents / 100m;
        }

        values[^1] = (totalCents - (baseCents * (total - 1))) / 100m;
        return values;
    }

    private static DateOnly DueDateForPurchase(CardDocument card, DateOnly purchaseDate, int installmentIndex)
    {
        var closingDate = DateWithMonthFallback(purchaseDate.Year, purchaseDate.Month, card.ClosingDay);
        var invoiceMonth = purchaseDate <= closingDate
            ? new DateOnly(purchaseDate.Year, purchaseDate.Month, 1)
            : new DateOnly(purchaseDate.Year, purchaseDate.Month, 1).AddMonths(1);
        var dueMonth = card.DueDay > card.ClosingDay ? invoiceMonth : invoiceMonth.AddMonths(1);
        dueMonth = dueMonth.AddMonths(installmentIndex - 1);
        return DateWithMonthFallback(dueMonth.Year, dueMonth.Month, card.DueDay);
    }

    private static DateOnly DateWithMonthFallback(int year, int month, int requestedDay) =>
        new(year, month, Math.Min(requestedDay, DateTime.DaysInMonth(year, month)));

    private static string Money(decimal value) => value.ToString("C", PtBr);

    private static string MonthSummaryText(CopilotMonthSummaryResponse response)
    {
        var mergeText = response.Scope == "merged" ? " considerando o merge ativo" : "";
        return $"Resumo de {response.Period.YearMonth}{mergeText}: entradas {Money(response.Totals.Entradas)}, saidas {Money(response.Totals.Saidas)}, aportes {Money(response.Totals.Aportes)}, saldo final {Money(response.Totals.Saldo)} e patrimonio {Money(response.Totals.Patrimonio)}.";
    }

    private static string NextThreeMonthsText(CopilotNextThreeMonthsResponse response)
    {
        var mergeText = response.Scope == "merged" ? " considerando o merge ativo" : "";
        return $"Levantamento de {response.From:yyyy-MM-dd} a {response.To:yyyy-MM-dd}{mergeText}: entradas previstas {Money(response.Totals.Entradas)}, saidas previstas {Money(response.Totals.Saidas)}, aportes {Money(response.Totals.Aportes)} e patrimonio projetado ao fim do periodo de {Money(response.Totals.Patrimonio)}.";
    }

    private static string SimulationText(CopilotPurchaseSimulationResponse response)
    {
        var last = response.MonthImpacts.LastOrDefault();
        var impactText = last is null
            ? "sem impacto mensal calculado"
            : $"saldo projetado de {Money(last.ProjectedTotals.Saldo)} em {last.Period.YearMonth}";
        var payment = response.PaymentType == "credit_card"
            ? $"{response.Installments.Count}x no cartao"
            : "a vista";
        return $"Simulacao da compra \"{response.Description}\" de {Money(response.Amount)} {payment}: {impactText}. Nenhuma transacao foi criada.";
    }

    private sealed record OwnerSet(
        string Scope,
        string PrimaryUserId,
        IReadOnlyList<CopilotOwnerResponse> Responses);

    private sealed record OwnerMonthData(
        MonthlyAggregateResponse Response,
        CopilotFreshnessResponse Freshness);
}
