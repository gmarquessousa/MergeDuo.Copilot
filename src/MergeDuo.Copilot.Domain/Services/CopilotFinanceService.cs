using System.Globalization;
using MergeDuo.Copilot.Domain.Abstractions;
using MergeDuo.Copilot.Domain.Contracts;
using MergeDuo.Copilot.Domain.Documents;
using MergeDuo.Copilot.Domain.Exceptions;
using MergeDuo.Copilot.Domain.Options;
using MergeDuo.Copilot.Domain.Rules;

namespace MergeDuo.Copilot.Domain.Services;

public sealed class CopilotFinanceService(
    ICopilotReadRepository repository,
    FixedRuleProjectionService projectionService,
    AggregateCalculator calculator,
    CopilotOptions options,
    TimeProvider clock) : ICopilotFinanceService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private const string SignalProjectedLowestBalanceBelowZero = "PROJECTED_LOWEST_BALANCE_BELOW_ZERO";
    private const string SignalProjectedEndBalanceBelowZero = "PROJECTED_END_BALANCE_BELOW_ZERO";
    private const string SignalProjectedEndBalanceLow = "PROJECTED_END_BALANCE_LOW";
    private const string SignalSafetyMarginCompromised = "SAFETY_MARGIN_COMPROMISED";
    private const string SignalAdditionalNegativeDaysCreated = "ADDITIONAL_NEGATIVE_DAYS_CREATED";
    private const string SignalAdditionalLowBalanceDaysCreated = "ADDITIONAL_LOW_BALANCE_DAYS_CREATED";
    private const string SignalHighMonthlyImpact = "HIGH_MONTHLY_IMPACT";
    private const string SignalLongInstallmentCommitment = "LONG_INSTALLMENT_COMMITMENT";
    private const string SignalMultipleMonthsImpacted = "MULTIPLE_MONTHS_IMPACTED";
    private const string SignalNoCriticalRiskDetected = "NO_CRITICAL_RISK_DETECTED";

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

        var analysisWindow = BuildAnalysisWindow(paymentType, purchaseDate, installments);
        var analysisMonths = MonthsBetween(
            YearMonth.FromDate(analysisWindow.From),
            YearMonth.FromDate(analysisWindow.To));
        var paymentScheduleAnalysis = BuildPaymentScheduleAnalysis(
            paymentType,
            amount,
            cardId ?? "",
            card,
            impactOwnerUserId,
            owners,
            installments);
        var safetyMarginAmount = ConfiguredSafetyMarginAmount();

        var monthImpacts = new List<PurchaseSimulationMonthImpactResponse>(analysisMonths.Count);
        decimal previousCumulativeImpact = 0m;
        foreach (var analysisMonth in analysisMonths)
        {
            var baseline = await BuildMonthSummaryAsync(owners, analysisMonth, includeAllRelevantMovements: true, cancellationToken);
            var monthInstallments = installments
                .Where(x => x.YearMonth == analysisMonth.Value)
                .ToArray();
            var monthImpact = MoneyValue(monthInstallments.Sum(x => x.Amount));
            var cumulativeImpact = MoneyValue(previousCumulativeImpact + monthImpact);
            var dailyImpact = BuildDailyImpact(
                baseline,
                monthInstallments,
                previousCumulativeImpact,
                description,
                paymentType);

            var baselineTotals = SimulationTotals(baseline.Totals);
            var projectedTotals = SimulationTotals(baseline.Totals with
            {
                Saidas = MoneyValue(baseline.Totals.Saidas + monthImpact),
                Saldo = MoneyValue(baseline.Totals.Saldo - cumulativeImpact),
                Patrimonio = MoneyValue(baseline.Totals.Patrimonio - cumulativeImpact),
                SaldoHoje = 0,
                InvestidoHoje = 0,
                PatrimonioHoje = 0
            });

            var monthRiskMetrics = BuildMonthRiskMetrics(
                baseline.Period,
                dailyImpact,
                safetyMarginAmount,
                monthImpact);

            monthImpacts.Add(new PurchaseSimulationMonthImpactResponse(
                baseline.Period,
                monthImpact,
                cumulativeImpact,
                baselineTotals,
                projectedTotals,
                dailyImpact,
                monthRiskMetrics));

            previousCumulativeImpact = cumulativeImpact;
        }

        var overallRiskMetrics = BuildOverallRiskMetrics(
            monthImpacts,
            safetyMarginAmount,
            MoneyValue(installments.Sum(x => x.Amount)),
            impactedMonths,
            totalInstallments);
        var aiAnalysisData = BuildAiAnalysisData(
            description,
            amount,
            purchaseDate,
            paymentType,
            analysisWindow,
            paymentScheduleAnalysis,
            monthImpacts,
            overallRiskMetrics);
        var aiContextText = SimulationAiContextText(
            description,
            amount,
            purchaseDate,
            paymentType,
            analysisWindow,
            aiAnalysisData,
            overallRiskMetrics);
        var response = new CopilotPurchaseSimulationResponse(
            owners.Scope,
            owners.Responses,
            description,
            paymentType,
            amount,
            purchaseDate,
            cardId ?? "",
            installments,
            analysisWindow,
            monthImpacts,
            overallRiskMetrics,
            paymentScheduleAnalysis,
            aiAnalysisData,
            aiContextText,
            clock.GetUtcNow(),
            "");

        return response with { SummaryText = SimulationText(response) };
    }

    public async Task<CopilotCardsResponse> ListCardsAsync(CancellationToken cancellationToken)
    {
        var owners = await ResolveOwnersAsync(cancellationToken);
        var businessDate = BusinessToday();
        var cards = new List<CopilotCardResponse>();

        foreach (var owner in owners.Responses)
        {
            var ownerCards = await repository.ListActiveCardsAsync(owner.UserId, cancellationToken);
            cards.AddRange(ownerCards.Select(card => new CopilotCardResponse(
                card.Id,
                string.IsNullOrWhiteSpace(card.Title) ? card.Id : card.Title,
                owner.UserId,
                owner.Role,
                OwnerName(owners, owner.UserId),
                card.ClosingDay,
                card.DueDay,
                NextDueDate(card.DueDay, businessDate),
                string.IsNullOrWhiteSpace(card.Currency) ? "BRL" : card.Currency)));
        }

        var ordered = cards
            .OrderBy(x => x.OwnerRole == "primary" ? 0 : 1)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

        var response = new CopilotCardsResponse(
            owners.Scope,
            owners.Responses,
            ordered,
            clock.GetUtcNow(),
            "");

        return response with { SummaryText = CardsText(response) };
    }

    private async Task<CopilotMonthSummaryResponse> BuildMonthSummaryAsync(
        OwnerSet owners,
        YearMonth yearMonth,
        bool includeAllRelevantMovements,
        CancellationToken cancellationToken)
    {
        var snapshot = await LoadMonthSnapshotAsync(owners, yearMonth, cancellationToken);
        var cardIndex = await LoadCardIndexAsync(owners, yearMonth, cancellationToken);
        var allMovements = snapshot.OwnerMonths
            .SelectMany(x => x.Response.DailyMovements.Select(m => ToMovement(yearMonth, m, owners, cardIndex)))
            .ToArray();
        var movements = allMovements
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

        var categoriesSummary = BuildCategoriesSummary(snapshot.ByCategory, allMovements, snapshot.Totals);
        var cardsSummary = BuildCardsSummary(owners, yearMonth, snapshot.ByCard, allMovements, cardIndex, snapshot.Totals);
        var ownersSummary = BuildOwnersSummary(owners, snapshot);
        var financialRatios = BuildFinancialRatios(snapshot.Totals, snapshot.ByCategory);
        var confirmedVsProjected = BuildConfirmedVsProjected(allMovements);
        var dailyCashflow = BuildDailyCashflow(yearMonth, snapshot.OwnerMonths, allMovements);
        var cashflowMetrics = BuildCashflowMetrics(yearMonth, dailyCashflow);
        var commitmentCalendar = BuildCommitmentCalendar(allMovements);
        var highlights = BuildHighlights(allMovements, cardsSummary);
        var comparison = await BuildComparisonAsync(owners, yearMonth, snapshot, cancellationToken);
        var (threeMonthAverage, againstThreeMonthAverage) = await BuildThreeMonthAverageAsync(owners, yearMonth, snapshot, cancellationToken);
        var freshness = CombineFreshness(snapshot.OwnerMonths.Select(x => x.Freshness));
        var response = new CopilotMonthSummaryResponse(
            owners.Scope,
            owners.Responses,
            Period(yearMonth),
            snapshot.Totals,
            snapshot.ByCategory,
            snapshot.ByCard,
            cardsSummary,
            categoriesSummary,
            ownersSummary,
            financialRatios,
            confirmedVsProjected,
            dailyCashflow,
            cashflowMetrics,
            commitmentCalendar,
            highlights,
            comparison,
            threeMonthAverage,
            againstThreeMonthAverage,
            movements,
            snapshot.OwnerMonths.Any(x => x.Response.Projection.IncludesProjected),
            snapshot.OwnerMonths.Sum(x => x.Response.Projection.ProjectedCount),
            snapshot.OwnerMonths.Sum(x => x.Response.TransactionsCount),
            freshness,
            clock.GetUtcNow(),
            "",
            "");

        return response with
        {
            SummaryText = MonthSummaryText(response),
            AiContextText = AiContextText(response)
        };
    }

    private async Task<MonthSnapshot> LoadMonthSnapshotAsync(
        OwnerSet owners,
        YearMonth yearMonth,
        CancellationToken cancellationToken)
    {
        var ownerMonths = new List<OwnerMonthData>(owners.Responses.Count);
        foreach (var owner in owners.Responses)
        {
            ownerMonths.Add(await LoadOwnerMonthAsync(owner.UserId, yearMonth, cancellationToken));
        }

        return new MonthSnapshot(
            yearMonth,
            ownerMonths,
            SumTotals(ownerMonths),
            SumDictionaries(ownerMonths.Select(x => x.Response.ByCategory)),
            SumDictionaries(ownerMonths.Select(x => x.Response.ByCard)));
    }

    private async Task<IReadOnlyDictionary<string, CardInfo>> LoadCardIndexAsync(
        OwnerSet owners,
        YearMonth yearMonth,
        CancellationToken cancellationToken)
    {
        var cards = new Dictionary<string, CardInfo>(StringComparer.Ordinal);
        foreach (var owner in owners.Responses)
        {
            var ownerCards = await repository.ListActiveCardsAsync(owner.UserId, cancellationToken);
            foreach (var card in ownerCards)
            {
                cards.TryAdd(card.Id, new CardInfo(
                    card.Id,
                    string.IsNullOrWhiteSpace(card.Title) ? card.Id : card.Title,
                    owner.UserId,
                    owner.Name,
                    owner.Role,
                    card.ClosingDay,
                    card.DueDay,
                    DateWithMonthFallback(yearMonth.Year, yearMonth.Month, Math.Max(1, card.DueDay)),
                    string.IsNullOrWhiteSpace(card.Currency) ? "BRL" : card.Currency));
            }
        }

        return cards;
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
            new(configuredUserId, "primary", DisplayName(user, configuredUserId))
        };
        var names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [configuredUserId] = DisplayName(user, configuredUserId)
        };

        var partnership = await repository.GetActivePartnerAsync(configuredUserId, cancellationToken);
        if (partnership is { Status: "active" } && UserIdRules.IsValid(partnership.PartnerUserId))
        {
            var partner = await repository.GetUserAsync(partnership.PartnerUserId, cancellationToken);
            owners.Add(new CopilotOwnerResponse(partnership.PartnerUserId, "partner", DisplayName(partner, partnership.PartnerUserId)));
            names[partnership.PartnerUserId] = DisplayName(partner, partnership.PartnerUserId);
        }

        return new OwnerSet(
            owners.Count > 1 ? "merged" : "single",
            configuredUserId,
            owners,
            names);
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

    private static IReadOnlyList<CopilotCategorySummaryResponse> BuildCategoriesSummary(
        IReadOnlyDictionary<string, decimal> byCategory,
        IReadOnlyList<CopilotMovementResponse> movements,
        CopilotTotalsResponse totals)
    {
        var categories = byCategory.Keys
            .Union(movements.Select(x => x.Category), StringComparer.Ordinal)
            .OrderBy(x => CategoryOrder(x))
            .ThenBy(x => x, StringComparer.Ordinal)
            .ToArray();

        return categories
            .Select(category =>
            {
                var categoryMovements = movements.Where(x => x.Category == category).ToArray();
                var amount = MoneyValue(byCategory.GetValueOrDefault(category, categoryMovements.Sum(x => x.Amount)));
                var confirmed = MoneyValue(categoryMovements.Where(x => !x.Projected).Sum(x => x.Amount));
                var projected = MoneyValue(categoryMovements.Where(x => x.Projected).Sum(x => x.Amount));
                return new CopilotCategorySummaryResponse(
                    category,
                    CategoryLabel(category),
                    amount,
                    Percent(amount, totals.Entradas),
                    Percent(amount, totals.Saidas),
                    confirmed,
                    projected,
                    TopMovements(categoryMovements, 5));
            })
            .ToArray();
    }

    private static IReadOnlyList<CopilotCardSummaryResponse> BuildCardsSummary(
        OwnerSet owners,
        YearMonth yearMonth,
        IReadOnlyDictionary<string, decimal> byCard,
        IReadOnlyList<CopilotMovementResponse> movements,
        IReadOnlyDictionary<string, CardInfo> cards,
        CopilotTotalsResponse totals)
    {
        var cardIds = byCard.Keys
            .Union(movements.Select(x => x.CardId).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!), StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        return cardIds
            .Select(cardId =>
            {
                var cardMovements = movements.Where(x => x.CardId == cardId).ToArray();
                var firstMovement = cardMovements.FirstOrDefault();
                var amount = MoneyValue(byCard.GetValueOrDefault(cardId, cardMovements.Sum(x => x.Amount)));
                var card = cards.GetValueOrDefault(cardId);
                var ownerUserId = card?.OwnerUserId ?? firstMovement?.OwnerUserId ?? owners.PrimaryUserId;
                var ownerName = card?.OwnerName ?? firstMovement?.OwnerName ?? OwnerName(owners, ownerUserId);
                var ownerRole = card?.OwnerRole ?? owners.Responses.FirstOrDefault(x => x.UserId == ownerUserId)?.Role ?? "primary";
                return new CopilotCardSummaryResponse(
                    cardId,
                    card?.Title ?? firstMovement?.CardTitle ?? cardId,
                    ownerUserId,
                    ownerName,
                    ownerRole,
                    card?.ClosingDay ?? 0,
                    card?.DueDay ?? 0,
                    card?.NextDueDate ?? firstMovement?.Date ?? yearMonth.LastDay,
                    card?.Currency ?? "BRL",
                    amount,
                    Percent(amount, totals.Entradas),
                    Percent(amount, totals.Saidas),
                    TopMovements(cardMovements, 5));
            })
            .OrderByDescending(x => x.InvoiceAmount)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<CopilotOwnerSummaryResponse> BuildOwnersSummary(
        OwnerSet owners,
        MonthSnapshot snapshot)
    {
        return owners.Responses
            .Select(owner =>
            {
                var month = snapshot.OwnerMonths.FirstOrDefault(x => x.Response.UserId == owner.UserId)?.Response;
                var categories = month?.ByCategory ?? new Dictionary<string, decimal>(StringComparer.Ordinal);
                var income = MoneyValue(month?.Totals.Entradas ?? 0m);
                var expenses = MoneyValue(month?.Totals.Saidas ?? 0m);
                return new CopilotOwnerSummaryResponse(
                    owner.UserId,
                    owner.Name,
                    owner.Role,
                    income,
                    expenses,
                    MoneyValue(income - expenses),
                    MoneyValue(categories.GetValueOrDefault(AggregateCategories.CreditCard)),
                    MoneyValue(categories.GetValueOrDefault(AggregateCategories.FixedExpense)),
                    MoneyValue(categories.GetValueOrDefault(AggregateCategories.VariableExpense)),
                    MoneyValue(categories.GetValueOrDefault(AggregateCategories.Loan)),
                    MoneyValue(month?.Totals.Aportes ?? categories.GetValueOrDefault(AggregateCategories.Investment)),
                    Percent(income, snapshot.Totals.Entradas),
                    Percent(expenses, snapshot.Totals.Saidas));
            })
            .ToArray();
    }

    private static CopilotFinancialRatiosResponse BuildFinancialRatios(
        CopilotTotalsResponse totals,
        IReadOnlyDictionary<string, decimal> byCategory) =>
        new(
            Percent(totals.Saidas, totals.Entradas),
            Percent(byCategory.GetValueOrDefault(AggregateCategories.CreditCard), totals.Entradas),
            Percent(byCategory.GetValueOrDefault(AggregateCategories.FixedExpense), totals.Entradas),
            Percent(byCategory.GetValueOrDefault(AggregateCategories.VariableExpense), totals.Entradas),
            Percent(byCategory.GetValueOrDefault(AggregateCategories.Loan), totals.Entradas),
            Percent(totals.Saldo, totals.Entradas),
            Percent(totals.Patrimonio, totals.Entradas));

    private static CopilotConfirmedVsProjectedResponse BuildConfirmedVsProjected(
        IReadOnlyList<CopilotMovementResponse> movements)
    {
        var confirmedIncome = MoneyValue(movements.Where(x => x.Kind == AggregateKinds.In && !x.Projected).Sum(x => x.Amount));
        var projectedIncome = MoneyValue(movements.Where(x => x.Kind == AggregateKinds.In && x.Projected).Sum(x => x.Amount));
        var confirmedExpenses = MoneyValue(movements.Where(x => x.Kind == AggregateKinds.Out && !x.Projected).Sum(x => x.Amount));
        var projectedExpenses = MoneyValue(movements.Where(x => x.Kind == AggregateKinds.Out && x.Projected).Sum(x => x.Amount));
        var income = new CopilotConfirmedProjectedBucketResponse(confirmedIncome, projectedIncome, MoneyValue(confirmedIncome + projectedIncome));
        var expenses = new CopilotConfirmedProjectedBucketResponse(confirmedExpenses, projectedExpenses, MoneyValue(confirmedExpenses + projectedExpenses));
        return new CopilotConfirmedVsProjectedResponse(
            income,
            expenses,
            Percent(projectedIncome, income.Total),
            Percent(projectedExpenses, expenses.Total));
    }

    private static IReadOnlyList<CopilotDailyCashflowResponse> BuildDailyCashflow(
        YearMonth yearMonth,
        IReadOnlyList<OwnerMonthData> ownerMonths,
        IReadOnlyList<CopilotMovementResponse> movements)
    {
        var daysInMonth = yearMonth.LastDay.Day;
        var balanceByDay = ownerMonths
            .SelectMany(x => x.Response.DailyBalances)
            .GroupBy(x => x.Day)
            .ToDictionary(x => x.Key, x => MoneyValue(x.Sum(day => day.Saldo)));
        var hasBalances = Enumerable.Range(1, daysInMonth).All(balanceByDay.ContainsKey);
        var daily = new List<CopilotDailyCashflowResponse>(daysInMonth);
        decimal runningBalance = 0m;

        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(yearMonth.Year, yearMonth.Month, day);
            var dayMovements = movements.Where(x => x.Day == day).ToArray();
            var income = MoneyValue(dayMovements.Where(x => x.Kind == AggregateKinds.In).Sum(x => x.Amount));
            var expenses = MoneyValue(dayMovements.Where(IsCashOutMovement).Sum(x => x.Amount));
            var net = MoneyValue(income - expenses);
            runningBalance = MoneyValue(runningBalance + net);
            var balanceAfterDay = hasBalances ? balanceByDay[day] : runningBalance;

            daily.Add(new CopilotDailyCashflowResponse(
                date,
                day,
                income,
                expenses,
                net,
                balanceAfterDay,
                MoneyValue(dayMovements.Where(x => x.Kind == AggregateKinds.In && !x.Projected).Sum(x => x.Amount)),
                MoneyValue(dayMovements.Where(x => x.Kind == AggregateKinds.In && x.Projected).Sum(x => x.Amount)),
                MoneyValue(dayMovements.Where(x => IsCashOutMovement(x) && !x.Projected).Sum(x => x.Amount)),
                MoneyValue(dayMovements.Where(x => IsCashOutMovement(x) && x.Projected).Sum(x => x.Amount)),
                dayMovements
                    .OrderByDescending(x => x.Amount)
                    .ThenBy(x => x.Description, StringComparer.OrdinalIgnoreCase)
                    .Take(4)
                    .Select(ToDailyEvent)
                    .ToArray()));
        }

        return daily;
    }

    private CopilotCashflowMetricsResponse BuildCashflowMetrics(
        YearMonth yearMonth,
        IReadOnlyList<CopilotDailyCashflowResponse> dailyCashflow)
    {
        if (dailyCashflow.Count == 0)
        {
            return new CopilotCashflowMetricsResponse(0, 0, 0, null, 0, 0, ConfiguredSafetyMarginAmount(), null, null);
        }

        var first = dailyCashflow[0];
        var openingBalance = MoneyValue(first.BalanceAfterDay - first.Net);
        var minimum = dailyCashflow.OrderBy(x => x.BalanceAfterDay).ThenBy(x => x.Date).First();
        var businessDate = BusinessToday();
        var nextIncomeStart = businessDate >= yearMonth.FirstDay && businessDate <= yearMonth.LastDay
            ? businessDate
            : yearMonth.FirstDay;
        var nextIncome = dailyCashflow.FirstOrDefault(x => x.Date >= nextIncomeStart && x.Income > 0);
        decimal? balanceBeforeNextIncome = null;
        if (nextIncome is not null)
        {
            balanceBeforeNextIncome = nextIncome.Day == 1
                ? openingBalance
                : dailyCashflow[nextIncome.Day - 2].BalanceAfterDay;
        }

        var safetyMargin = ConfiguredSafetyMarginAmount();
        return new CopilotCashflowMetricsResponse(
            openingBalance,
            dailyCashflow[^1].BalanceAfterDay,
            minimum.BalanceAfterDay,
            minimum.Date,
            dailyCashflow.Count(x => x.BalanceAfterDay < 0),
            dailyCashflow.Count(x => x.BalanceAfterDay < safetyMargin),
            safetyMargin,
            nextIncome?.Date,
            balanceBeforeNextIncome);
    }

    private PurchaseSimulationAnalysisWindowResponse BuildAnalysisWindow(
        string paymentType,
        DateOnly purchaseDate,
        IReadOnlyList<PurchaseSimulationInstallmentResponse> installments)
    {
        var firstMonth = paymentType == "cash"
            ? YearMonth.FromDate(purchaseDate)
            : YearMonth.FromDate(installments[0].Date);
        var lastImpactedMonth = paymentType == "cash"
            ? firstMonth
            : YearMonth.FromDate(installments[^1].Date);
        var lastMonth = lastImpactedMonth.AddMonths(3);
        var reason = paymentType == "cash"
            ? "Compra a vista analisada no mes da compra e nos 3 meses seguintes."
            : "Compra parcelada analisada durante todos os meses com parcelas e mais 3 meses apos a ultima parcela.";

        return new PurchaseSimulationAnalysisWindowResponse(
            firstMonth.FirstDay,
            lastMonth.LastDay,
            MonthsBetween(firstMonth, lastMonth).Count,
            reason);
    }

    private static IReadOnlyList<YearMonth> MonthsBetween(YearMonth first, YearMonth last)
    {
        var months = new List<YearMonth>();
        var current = first;
        while (current.IsBeforeOrEqual(last))
        {
            months.Add(current);
            current = current.AddMonths(1);
        }

        return months;
    }

    private static PurchaseSimulationPaymentScheduleAnalysisResponse BuildPaymentScheduleAnalysis(
        string paymentType,
        decimal amount,
        string cardId,
        CardDocument? card,
        string impactOwnerUserId,
        OwnerSet owners,
        IReadOnlyList<PurchaseSimulationInstallmentResponse> installments)
    {
        var first = installments[0];
        var last = installments[^1];
        var cardTitle = card is null
            ? ""
            : string.IsNullOrWhiteSpace(card.Title) ? card.Id : card.Title.Trim();

        return new PurchaseSimulationPaymentScheduleAnalysisResponse(
            paymentType,
            cardId,
            cardTitle,
            card is null ? "" : OwnerName(owners, impactOwnerUserId),
            card?.ClosingDay ?? 0,
            card?.DueDay ?? 0,
            installments.Count,
            first.Date,
            last.Date,
            first.YearMonth,
            last.YearMonth,
            MoneyValue(first.Amount),
            MoneyValue(amount));
    }

    private static IReadOnlyList<PurchaseSimulationDailyImpactResponse> BuildDailyImpact(
        CopilotMonthSummaryResponse baseline,
        IReadOnlyList<PurchaseSimulationInstallmentResponse> monthInstallments,
        decimal previousCumulativeImpact,
        string description,
        string paymentType)
    {
        var impactByDay = monthInstallments
            .GroupBy(x => x.Date.Day)
            .ToDictionary(x => x.Key, x => MoneyValue(x.Sum(i => i.Amount)));
        var daily = new List<PurchaseSimulationDailyImpactResponse>(baseline.DailyCashflow.Count);
        var cumulativeImpact = MoneyValue(previousCumulativeImpact);

        foreach (var day in baseline.DailyCashflow)
        {
            var purchaseImpactOnDay = impactByDay.GetValueOrDefault(day.Day);
            cumulativeImpact = MoneyValue(cumulativeImpact + purchaseImpactOnDay);
            var projectedNet = MoneyValue(day.Net - purchaseImpactOnDay);
            var projectedBalanceAfterDay = MoneyValue(day.BalanceAfterDay - cumulativeImpact);
            var mainEvents = day.MainEvents
                .Select(ToSimulationDailyEvent)
                .ToList();

            if (purchaseImpactOnDay > 0)
            {
                mainEvents.Add(SimulationPurchaseEvent(description, purchaseImpactOnDay, paymentType));
            }

            daily.Add(new PurchaseSimulationDailyImpactResponse(
                day.Date,
                day.Day,
                MoneyValue(day.BalanceAfterDay),
                projectedBalanceAfterDay,
                purchaseImpactOnDay,
                cumulativeImpact,
                MoneyValue(day.Net),
                projectedNet,
                mainEvents));
        }

        return daily;
    }

    private static PurchaseSimulationDailyEventResponse ToSimulationDailyEvent(CopilotDailyEventResponse movement) =>
        new(
            movement.Description,
            MoneyValue(movement.Amount),
            movement.Kind,
            movement.Category,
            movement.Projected);

    private static PurchaseSimulationDailyEventResponse SimulationPurchaseEvent(
        string description,
        decimal amount,
        string paymentType) =>
        new(
            $"Simulacao: {description}",
            MoneyValue(amount),
            AggregateKinds.Out,
            paymentType == "credit_card" ? AggregateCategories.CreditCard : AggregateCategories.VariableExpense,
            true);

    private static PurchaseSimulationMonthRiskMetricsResponse BuildMonthRiskMetrics(
        CopilotPeriodResponse period,
        IReadOnlyList<PurchaseSimulationDailyImpactResponse> dailyImpact,
        decimal safetyMarginAmount,
        decimal monthImpact)
    {
        if (dailyImpact.Count == 0)
        {
            return new PurchaseSimulationMonthRiskMetricsResponse(
                0,
                period.From,
                0,
                period.From,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                safetyMarginAmount,
                false,
                0,
                0,
                0,
                monthImpact > 0,
                [SignalNoCriticalRiskDetected]);
        }

        var baselineLowest = dailyImpact
            .OrderBy(x => x.BaselineBalanceAfterDay)
            .ThenBy(x => x.Date)
            .First();
        var projectedLowest = dailyImpact
            .OrderBy(x => x.ProjectedBalanceAfterDay)
            .ThenBy(x => x.Date)
            .First();
        var baselineNegativeBalanceDays = dailyImpact.Count(x => x.BaselineBalanceAfterDay < 0);
        var projectedNegativeBalanceDays = dailyImpact.Count(x => x.ProjectedBalanceAfterDay < 0);
        var baselineDaysBelowSafetyMargin = dailyImpact.Count(x => x.BaselineBalanceAfterDay < safetyMarginAmount);
        var projectedDaysBelowSafetyMargin = dailyImpact.Count(x => x.ProjectedBalanceAfterDay < safetyMarginAmount);
        var baselineEndBalance = dailyImpact[^1].BaselineBalanceAfterDay;
        var projectedEndBalance = dailyImpact[^1].ProjectedBalanceAfterDay;
        var additionalNegativeBalanceDays = Math.Max(0, projectedNegativeBalanceDays - baselineNegativeBalanceDays);
        var additionalDaysBelowSafetyMargin = Math.Max(0, projectedDaysBelowSafetyMargin - baselineDaysBelowSafetyMargin);

        var metrics = new PurchaseSimulationMonthRiskMetricsResponse(
            MoneyValue(baselineLowest.BaselineBalanceAfterDay),
            baselineLowest.Date,
            MoneyValue(projectedLowest.ProjectedBalanceAfterDay),
            projectedLowest.Date,
            MoneyValue(projectedLowest.ProjectedBalanceAfterDay - baselineLowest.BaselineBalanceAfterDay),
            baselineNegativeBalanceDays,
            projectedNegativeBalanceDays,
            additionalNegativeBalanceDays,
            baselineDaysBelowSafetyMargin,
            projectedDaysBelowSafetyMargin,
            additionalDaysBelowSafetyMargin,
            safetyMarginAmount,
            projectedEndBalance < 0,
            MoneyValue(projectedEndBalance),
            MoneyValue(baselineEndBalance),
            MoneyValue(projectedEndBalance - baselineEndBalance),
            monthImpact > 0,
            []);

        return metrics with { RiskSignals = BuildMonthRiskSignals(metrics, monthImpact) };
    }

    private static IReadOnlyList<string> BuildMonthRiskSignals(
        PurchaseSimulationMonthRiskMetricsResponse metrics,
        decimal monthImpact)
    {
        var signals = new List<string>();
        AddSignalIf(signals, metrics.ProjectedLowestBalance < 0, SignalProjectedLowestBalanceBelowZero);
        AddSignalIf(signals, metrics.ProjectedEndBalance < 0, SignalProjectedEndBalanceBelowZero);
        AddSignalIf(signals, metrics.ProjectedEndBalance >= 0 && metrics.ProjectedEndBalance < metrics.SafetyMarginAmount, SignalProjectedEndBalanceLow);
        AddSignalIf(signals, metrics.AdditionalNegativeBalanceDays > 0, SignalAdditionalNegativeDaysCreated);
        AddSignalIf(signals, metrics.AdditionalDaysBelowSafetyMargin > 0, SignalAdditionalLowBalanceDaysCreated);
        AddSignalIf(
            signals,
            metrics.AdditionalDaysBelowSafetyMargin > 0 || metrics.ProjectedEndBalance < metrics.SafetyMarginAmount,
            SignalSafetyMarginCompromised);
        AddSignalIf(signals, monthImpact >= metrics.SafetyMarginAmount, SignalHighMonthlyImpact);

        if (signals.Count == 0)
        {
            signals.Add(SignalNoCriticalRiskDetected);
        }

        return signals;
    }

    private static PurchaseSimulationOverallRiskMetricsResponse BuildOverallRiskMetrics(
        IReadOnlyList<PurchaseSimulationMonthImpactResponse> monthImpacts,
        decimal safetyMarginAmount,
        decimal totalPurchaseImpact,
        IReadOnlyList<YearMonth> impactedMonths,
        int totalInstallments)
    {
        var allDailyImpact = monthImpacts.SelectMany(x => x.DailyImpact).ToArray();
        var baselineLowest = allDailyImpact
            .OrderBy(x => x.BaselineBalanceAfterDay)
            .ThenBy(x => x.Date)
            .First();
        var projectedLowest = allDailyImpact
            .OrderBy(x => x.ProjectedBalanceAfterDay)
            .ThenBy(x => x.Date)
            .First();
        var worstMonth = monthImpacts
            .OrderBy(x => x.MonthRiskMetrics.ProjectedLowestBalance)
            .ThenBy(x => x.Period.YearMonth, StringComparer.Ordinal)
            .First();
        var impactMonths = monthImpacts.Where(x => x.ImpactAmount > 0).ToArray();
        var baselineNegativeBalanceDays = monthImpacts.Sum(x => x.MonthRiskMetrics.BaselineNegativeBalanceDays);
        var projectedNegativeBalanceDays = monthImpacts.Sum(x => x.MonthRiskMetrics.ProjectedNegativeBalanceDays);
        var baselineDaysBelowSafetyMargin = monthImpacts.Sum(x => x.MonthRiskMetrics.BaselineDaysBelowSafetyMargin);
        var projectedDaysBelowSafetyMargin = monthImpacts.Sum(x => x.MonthRiskMetrics.ProjectedDaysBelowSafetyMargin);
        var additionalNegativeBalanceDays = Math.Max(0, projectedNegativeBalanceDays - baselineNegativeBalanceDays);
        var additionalDaysBelowSafetyMargin = Math.Max(0, projectedDaysBelowSafetyMargin - baselineDaysBelowSafetyMargin);
        var maxMonthlyImpact = impactMonths.Length == 0 ? 0 : MoneyValue(impactMonths.Max(x => x.ImpactAmount));
        var averageMonthlyImpact = impactMonths.Length == 0 ? 0 : MoneyValue(totalPurchaseImpact / impactMonths.Length);

        var metrics = new PurchaseSimulationOverallRiskMetricsResponse(
            safetyMarginAmount,
            MoneyValue(baselineLowest.BaselineBalanceAfterDay),
            baselineLowest.Date,
            MoneyValue(projectedLowest.ProjectedBalanceAfterDay),
            projectedLowest.Date,
            MoneyValue(projectedLowest.ProjectedBalanceAfterDay - baselineLowest.BaselineBalanceAfterDay),
            baselineNegativeBalanceDays,
            projectedNegativeBalanceDays,
            additionalNegativeBalanceDays,
            baselineDaysBelowSafetyMargin,
            projectedDaysBelowSafetyMargin,
            additionalDaysBelowSafetyMargin,
            monthImpacts
                .Where(x => x.MonthRiskMetrics.ProjectedNegativeBalanceDays > 0)
                .Select(x => x.Period.YearMonth)
                .ToArray(),
            monthImpacts
                .Where(x => x.MonthRiskMetrics.ProjectedDaysBelowSafetyMargin > 0)
                .Select(x => x.Period.YearMonth)
                .ToArray(),
            worstMonth.Period.YearMonth,
            MoneyValue(worstMonth.MonthRiskMetrics.ProjectedEndBalance),
            totalPurchaseImpact,
            averageMonthlyImpact,
            maxMonthlyImpact,
            []);

        return metrics with
        {
            RiskSignals = BuildOverallRiskSignals(metrics, monthImpacts, impactedMonths.Count, totalInstallments)
        };
    }

    private static IReadOnlyList<string> BuildOverallRiskSignals(
        PurchaseSimulationOverallRiskMetricsResponse metrics,
        IReadOnlyList<PurchaseSimulationMonthImpactResponse> monthImpacts,
        int impactedMonthsCount,
        int totalInstallments)
    {
        var signals = monthImpacts
            .SelectMany(x => x.MonthRiskMetrics.RiskSignals)
            .Where(x => x != SignalNoCriticalRiskDetected)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        AddSignalIf(signals, metrics.MaxMonthlyImpact >= metrics.SafetyMarginAmount, SignalHighMonthlyImpact);
        AddSignalIf(signals, totalInstallments >= 6, SignalLongInstallmentCommitment);
        AddSignalIf(signals, impactedMonthsCount > 1, SignalMultipleMonthsImpacted);

        if (signals.Count == 0)
        {
            signals.Add(SignalNoCriticalRiskDetected);
        }

        return signals;
    }

    private static void AddSignalIf(List<string> signals, bool condition, string signal)
    {
        if (condition && !signals.Contains(signal, StringComparer.Ordinal))
        {
            signals.Add(signal);
        }
    }

    private PurchaseSimulationAiAnalysisDataResponse BuildAiAnalysisData(
        string description,
        decimal amount,
        DateOnly purchaseDate,
        string paymentType,
        PurchaseSimulationAnalysisWindowResponse analysisWindow,
        PurchaseSimulationPaymentScheduleAnalysisResponse paymentScheduleAnalysis,
        IReadOnlyList<PurchaseSimulationMonthImpactResponse> monthImpacts,
        PurchaseSimulationOverallRiskMetricsResponse overallRiskMetrics)
    {
        var firstImpactMonth = monthImpacts.FirstOrDefault(x => x.ImpactAmount > 0) ?? monthImpacts[0];
        var riskFacts = new List<string>();
        var positiveFacts = new List<string>
        {
            "A compra nao cria transacao real, e apenas simulacao."
        };
        var attentionPoints = new List<string>();

        if (overallRiskMetrics.ProjectedLowestBalance < 0)
        {
            riskFacts.Add($"O menor saldo projetado fica abaixo de zero em {overallRiskMetrics.ProjectedLowestBalanceDate:yyyy-MM-dd}.");
            attentionPoints.Add("Existe saldo diario projetado abaixo de zero na janela analisada.");
        }

        if (overallRiskMetrics.AdditionalNegativeBalanceDays > 0)
        {
            riskFacts.Add($"A compra cria {overallRiskMetrics.AdditionalNegativeBalanceDays} dia(s) adicional(is) com saldo negativo.");
        }

        if (overallRiskMetrics.AdditionalDaysBelowSafetyMargin > 0)
        {
            riskFacts.Add("A compra aumenta a quantidade de dias abaixo da margem de seguranca.");
            attentionPoints.Add("A margem de seguranca fica mais pressionada apos a compra.");
        }

        if (overallRiskMetrics.MonthsWithNegativeBalance.Count > 0)
        {
            riskFacts.Add($"Ha saldo projetado abaixo de zero em {string.Join(", ", overallRiskMetrics.MonthsWithNegativeBalance)}.");
        }

        if (overallRiskMetrics.RiskSignals.Contains(SignalLongInstallmentCommitment, StringComparer.Ordinal))
        {
            attentionPoints.Add($"O parcelamento gera compromisso por {paymentScheduleAnalysis.TotalInstallments} meses.");
        }

        if (overallRiskMetrics.WorstProjectedEndBalance >= 0)
        {
            positiveFacts.Add($"O pior mes da janela termina com saldo projetado de {Money(overallRiskMetrics.WorstProjectedEndBalance)}.");
        }

        if (overallRiskMetrics.AdditionalNegativeBalanceDays == 0)
        {
            positiveFacts.Add("A compra nao cria dias adicionais com saldo negativo.");
        }

        if (overallRiskMetrics.RiskSignals.Contains(SignalNoCriticalRiskDetected, StringComparer.Ordinal))
        {
            positiveFacts.Add("Nao foram detectados sinais criticos de risco na janela analisada.");
        }

        if (overallRiskMetrics.WorstProjectedEndBalance < overallRiskMetrics.SafetyMarginAmount)
        {
            attentionPoints.Add($"O pior mes termina abaixo da margem de seguranca de {Money(overallRiskMetrics.SafetyMarginAmount)}.");
        }

        var safetyMarginSummary = overallRiskMetrics.ProjectedDaysBelowSafetyMargin > overallRiskMetrics.BaselineDaysBelowSafetyMargin
            ? $"A quantidade de dias abaixo da margem de seguranca de {Money(overallRiskMetrics.SafetyMarginAmount)} aumenta de {overallRiskMetrics.BaselineDaysBelowSafetyMargin} para {overallRiskMetrics.ProjectedDaysBelowSafetyMargin} dias."
            : $"A quantidade de dias abaixo da margem de seguranca de {Money(overallRiskMetrics.SafetyMarginAmount)} permanece em {overallRiskMetrics.ProjectedDaysBelowSafetyMargin} dias.";

        return new PurchaseSimulationAiAnalysisDataResponse(
            PurchaseSummary(description, amount, purchaseDate, paymentType, paymentScheduleAnalysis),
            $"A analise considera {YearMonth.FromDate(analysisWindow.From).Value} ate {YearMonth.FromDate(analysisWindow.To).Value}.",
            $"A compra reduz o saldo final de {firstImpactMonth.Period.YearMonth} de {Money(firstImpactMonth.MonthRiskMetrics.BaselineEndBalance)} para {Money(firstImpactMonth.MonthRiskMetrics.ProjectedEndBalance)}.",
            $"O menor saldo projetado na janela fica em {Money(overallRiskMetrics.ProjectedLowestBalance)} no dia {overallRiskMetrics.ProjectedLowestBalanceDate:yyyy-MM-dd}.",
            safetyMarginSummary,
            riskFacts,
            positiveFacts,
            attentionPoints.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static string PurchaseSummary(
        string description,
        decimal amount,
        DateOnly purchaseDate,
        string paymentType,
        PurchaseSimulationPaymentScheduleAnalysisResponse paymentScheduleAnalysis)
    {
        if (paymentType == "credit_card")
        {
            return $"Compra \"{description}\" de {Money(amount)} no cartao em {paymentScheduleAnalysis.TotalInstallments} parcela(s), realizada em {purchaseDate:yyyy-MM-dd}.";
        }

        return $"Compra \"{description}\" de {Money(amount)} a vista em {purchaseDate:yyyy-MM-dd}.";
    }

    private static string SimulationAiContextText(
        string description,
        decimal amount,
        DateOnly purchaseDate,
        string paymentType,
        PurchaseSimulationAnalysisWindowResponse analysisWindow,
        PurchaseSimulationAiAnalysisDataResponse aiAnalysisData,
        PurchaseSimulationOverallRiskMetricsResponse overallRiskMetrics)
    {
        var payment = paymentType == "credit_card" ? "cartao de credito" : "pagamento a vista";
        return string.Join(" ", new[]
        {
            $"Simulacao de compra: {description}, {Money(amount)}, {payment} em {purchaseDate:yyyy-MM-dd}.",
            $"Janela analisada: {YearMonth.FromDate(analysisWindow.From).Value} ate {YearMonth.FromDate(analysisWindow.To).Value}.",
            aiAnalysisData.FinancialImpactSummary,
            aiAnalysisData.LowestBalanceSummary,
            aiAnalysisData.SafetyMarginSummary,
            $"Sinais de risco: {string.Join(", ", overallRiskMetrics.RiskSignals)}.",
            "A compra nao persiste transacao.",
            "Use estes dados para avaliar se a compra pode comprometer a organizacao financeira."
        });
    }

    private decimal ConfiguredSafetyMarginAmount()
    {
        var configured = options.SafetyMarginAmount > 0 ? options.SafetyMarginAmount : 1000m;
        return MoneyValue(configured);
    }

    private static IReadOnlyList<CopilotCommitmentResponse> BuildCommitmentCalendar(
        IReadOnlyList<CopilotMovementResponse> movements) =>
        movements
            .OrderBy(x => x.Date)
            .ThenByDescending(x => x.Amount)
            .Take(50)
            .Select(x => new CopilotCommitmentResponse(
                x.Date,
                x.Day,
                CommitmentType(x),
                x.Description,
                MoneyValue(x.Amount),
                x.OwnerUserId,
                x.OwnerName,
                x.CardId,
                x.CardTitle,
                x.Projected,
                Priority(x.Amount)))
            .ToArray();

    private static CopilotHighlightsResponse BuildHighlights(
        IReadOnlyList<CopilotMovementResponse> movements,
        IReadOnlyList<CopilotCardSummaryResponse> cardsSummary) =>
        new(
            TopMovements(movements.Where(IsCashOutMovement), 5),
            TopMovements(movements.Where(x => x.Kind == AggregateKinds.In), 5),
            cardsSummary
                .OrderByDescending(x => x.InvoiceAmount)
                .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .Select(x => new CopilotCardInvoiceHighlightResponse(
                    x.CardId,
                    x.Title,
                    x.OwnerName,
                    x.InvoiceAmount,
                    x.DueDay,
                    x.NextDueDate))
                .ToArray());

    private async Task<CopilotComparisonResponse> BuildComparisonAsync(
        OwnerSet owners,
        YearMonth yearMonth,
        MonthSnapshot current,
        CancellationToken cancellationToken)
    {
        var previous = await LoadMonthSnapshotAsync(owners, yearMonth.AddMonths(-1), cancellationToken);
        var currentMonth = ComparisonMonth(current);
        if (!HasMonthData(previous))
        {
            return new CopilotComparisonResponse(false, null, currentMonth, null, null);
        }

        var previousMonth = ComparisonMonth(previous);
        var delta = new CopilotComparisonDeltaResponse(
            MoneyValue(currentMonth.Income - previousMonth.Income),
            MoneyValue(currentMonth.Expenses - previousMonth.Expenses),
            MoneyValue(currentMonth.Balance - previousMonth.Balance),
            MoneyValue(currentMonth.Patrimony - previousMonth.Patrimony));
        var percentDelta = new CopilotComparisonDeltaResponse(
            PercentDelta(currentMonth.Income, previousMonth.Income),
            PercentDelta(currentMonth.Expenses, previousMonth.Expenses),
            PercentDelta(currentMonth.Balance, previousMonth.Balance),
            PercentDelta(currentMonth.Patrimony, previousMonth.Patrimony));
        return new CopilotComparisonResponse(true, previousMonth, currentMonth, delta, percentDelta);
    }

    private async Task<(CopilotThreeMonthAverageResponse Average, CopilotAgainstThreeMonthAverageResponse Against)> BuildThreeMonthAverageAsync(
        OwnerSet owners,
        YearMonth yearMonth,
        MonthSnapshot current,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<MonthSnapshot>(3);
        for (var offset = -3; offset <= -1; offset++)
        {
            snapshots.Add(await LoadMonthSnapshotAsync(owners, yearMonth.AddMonths(offset), cancellationToken));
        }

        if (snapshots.Count(HasMonthData) != 3)
        {
            return (
                new CopilotThreeMonthAverageResponse(false, null, null, null, null, null, null, null),
                new CopilotAgainstThreeMonthAverageResponse(false, null, null, null, null, null, null));
        }

        var average = new CopilotThreeMonthAverageResponse(
            true,
            Average(snapshots.Select(x => x.Totals.Entradas)),
            Average(snapshots.Select(x => x.Totals.Saidas)),
            Average(snapshots.Select(x => x.Totals.Saldo)),
            Average(snapshots.Select(x => x.ByCategory.GetValueOrDefault(AggregateCategories.CreditCard))),
            Average(snapshots.Select(x => x.ByCategory.GetValueOrDefault(AggregateCategories.VariableExpense))),
            Average(snapshots.Select(x => x.ByCategory.GetValueOrDefault(AggregateCategories.FixedExpense))),
            Average(snapshots.Select(x => x.ByCategory.GetValueOrDefault(AggregateCategories.Loan))));

        var against = new CopilotAgainstThreeMonthAverageResponse(
            true,
            MoneyValue(current.Totals.Entradas - average.Income.GetValueOrDefault()),
            MoneyValue(current.Totals.Saidas - average.Expenses.GetValueOrDefault()),
            MoneyValue(current.ByCategory.GetValueOrDefault(AggregateCategories.CreditCard) - average.CreditCardExpenses.GetValueOrDefault()),
            MoneyValue(current.ByCategory.GetValueOrDefault(AggregateCategories.VariableExpense) - average.VariableExpenses.GetValueOrDefault()),
            MoneyValue(current.ByCategory.GetValueOrDefault(AggregateCategories.FixedExpense) - average.FixedExpenses.GetValueOrDefault()),
            MoneyValue(current.ByCategory.GetValueOrDefault(AggregateCategories.Loan) - average.LoanExpenses.GetValueOrDefault()));

        return (average, against);
    }

    private static CopilotMovementResponse ToMovement(
        YearMonth yearMonth,
        DailyMovementResponse movement,
        OwnerSet owners,
        IReadOnlyDictionary<string, CardInfo> cards) =>
        new(
            new DateOnly(yearMonth.Year, yearMonth.Month, movement.Day),
            movement.Day,
            movement.UserId,
            OwnerName(owners, movement.UserId),
            movement.Category,
            CategoryLabel(movement.Category),
            movement.Kind,
            KindLabel(movement.Kind),
            movement.Description,
            MoneyValue(movement.Amount),
            movement.CardId,
            movement.CardId is not null && cards.TryGetValue(movement.CardId, out var card) ? card.Title : null,
            movement.FixedRuleId,
            movement.Projected);

    private static CopilotMovementHighlightResponse ToHighlight(CopilotMovementResponse movement) =>
        new(
            movement.Date,
            movement.Day,
            movement.Description,
            MoneyValue(movement.Amount),
            movement.OwnerUserId,
            movement.OwnerName,
            movement.Category,
            movement.CategoryLabel,
            movement.Kind,
            movement.KindLabel,
            movement.CardId,
            movement.CardTitle,
            movement.Projected);

    private static CopilotDailyEventResponse ToDailyEvent(CopilotMovementResponse movement) =>
        new(
            movement.Description,
            movement.Category,
            movement.CategoryLabel,
            movement.Kind,
            movement.KindLabel,
            MoneyValue(movement.Amount),
            movement.OwnerUserId,
            movement.OwnerName,
            movement.CardId,
            movement.CardTitle,
            movement.Projected);

    private static IReadOnlyList<CopilotMovementHighlightResponse> TopMovements(
        IEnumerable<CopilotMovementResponse> movements,
        int take) =>
        movements
            .OrderByDescending(x => x.Amount)
            .ThenBy(x => x.Date)
            .ThenBy(x => x.Description, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(ToHighlight)
            .ToArray();

    private static CopilotComparisonMonthResponse ComparisonMonth(MonthSnapshot snapshot) =>
        new(
            snapshot.YearMonth.Value,
            MoneyValue(snapshot.Totals.Entradas),
            MoneyValue(snapshot.Totals.Saidas),
            MoneyValue(snapshot.Totals.Saldo),
            MoneyValue(snapshot.Totals.Patrimonio));

    private static bool HasMonthData(MonthSnapshot snapshot) =>
        snapshot.OwnerMonths.Any(x =>
            x.Response.TransactionsCount > 0 ||
            x.Response.DailyMovements.Count > 0 ||
            x.Response.ByCategory.Count > 0);

    private static bool IsCashOutMovement(CopilotMovementResponse movement) =>
        movement.Kind is AggregateKinds.Out or AggregateKinds.Invest;

    private static decimal? Percent(decimal numerator, decimal denominator) =>
        denominator == 0m ? null : MoneyValue(numerator / denominator * 100m);

    private static decimal? PercentDelta(decimal current, decimal previous) =>
        previous == 0m ? null : MoneyValue((current - previous) / Math.Abs(previous) * 100m);

    private static decimal Average(IEnumerable<decimal> values)
    {
        var items = values.ToArray();
        return items.Length == 0 ? 0m : MoneyValue(items.Average());
    }

    private static decimal MoneyValue(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static CopilotTotalsResponse SimulationTotals(CopilotTotalsResponse totals) =>
        new(
            MoneyValue(totals.Entradas),
            MoneyValue(totals.Saidas),
            MoneyValue(totals.Aportes),
            MoneyValue(totals.Saldo),
            MoneyValue(totals.Investido),
            MoneyValue(totals.Patrimonio),
            MoneyValue(totals.SaldoHoje ?? 0),
            MoneyValue(totals.InvestidoHoje ?? 0),
            MoneyValue(totals.PatrimonioHoje ?? 0));

    private static int CategoryOrder(string category) => category switch
    {
        AggregateCategories.Income => 0,
        AggregateCategories.CreditCard => 1,
        AggregateCategories.FixedExpense => 2,
        AggregateCategories.VariableExpense => 3,
        AggregateCategories.Loan => 4,
        AggregateCategories.Investment => 5,
        _ => 99
    };

    private static string CategoryLabel(string category) => category switch
    {
        AggregateCategories.Income => "Entradas",
        AggregateCategories.CreditCard => "Cartão de crédito",
        AggregateCategories.VariableExpense => "Despesas variáveis",
        AggregateCategories.FixedExpense => "Despesas fixas",
        AggregateCategories.Loan => "Empréstimos",
        AggregateCategories.Investment => "Investimentos",
        "aporte" => "Aportes",
        _ => category
    };

    private static string KindLabel(string kind) => kind switch
    {
        AggregateKinds.In => "Entrada",
        AggregateKinds.Out => "Saída",
        AggregateKinds.Invest => "Investimento",
        _ => kind
    };

    private static string CommitmentType(CopilotMovementResponse movement) => movement.Category switch
    {
        AggregateCategories.Income => "income",
        AggregateCategories.CreditCard => "credit_card_invoice",
        AggregateCategories.FixedExpense => "fixed_expense",
        AggregateCategories.VariableExpense => "variable_expense",
        AggregateCategories.Loan => "loan",
        AggregateCategories.Investment => "investment",
        _ => "other"
    };

    private static string Priority(decimal amount) => amount switch
    {
        >= 1000m => "high",
        >= 300m => "medium",
        _ => "low"
    };

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

    private static DateOnly NextDueDate(int dueDay, DateOnly businessDate)
    {
        var currentMonthDueDate = DateWithMonthFallback(businessDate.Year, businessDate.Month, dueDay);
        if (currentMonthDueDate >= businessDate)
        {
            return currentMonthDueDate;
        }

        var nextMonth = businessDate.AddMonths(1);
        return DateWithMonthFallback(nextMonth.Year, nextMonth.Month, dueDay);
    }

    private static string OwnerName(OwnerSet owners, string userId) =>
        owners.Names.TryGetValue(userId, out var name) ? name : userId;

    private static string DisplayName(UserDocument? user, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(user?.Name))
        {
            return user.Name.Trim();
        }

        return !string.IsNullOrWhiteSpace(user?.Handle)
            ? user.Handle.Trim()
            : fallback;
    }

    private static string Money(decimal value) => value.ToString("C", PtBr);

    private static string MonthSummaryText(CopilotMonthSummaryResponse response)
    {
        var mergeText = response.Scope == "merged" ? " considerando o merge ativo" : "";
        var cardTotal = response.ByCategory.GetValueOrDefault(AggregateCategories.CreditCard);
        return $"Resumo de {response.Period.YearMonth}{mergeText}: entradas {Money(response.Totals.Entradas)}, saidas {Money(response.Totals.Saidas)}, saldo final {Money(response.Totals.Saldo)}, patrimonio {Money(response.Totals.Patrimonio)}, cartao de credito {Money(cardTotal)} e {response.ProjectedCount} movimentos projetados.";
    }

    private static string AiContextText(CopilotMonthSummaryResponse response)
    {
        var owners = string.Join(" e ", response.Owners.Select(x => $"{x.Name} ({x.Role})"));
        var scope = response.Scope == "merged" ? "merge ativo" : "individual";
        var cardTotal = response.ByCategory.GetValueOrDefault(AggregateCategories.CreditCard);
        var variable = response.ByCategory.GetValueOrDefault(AggregateCategories.VariableExpense);
        var fixedExpense = response.ByCategory.GetValueOrDefault(AggregateCategories.FixedExpense);
        var loan = response.ByCategory.GetValueOrDefault(AggregateCategories.Loan);
        var largestOutflow = response.Highlights.LargestOutflows.FirstOrDefault();
        var largestCardInvoice = response.Highlights.LargestCardInvoices.FirstOrDefault();

        var parts = new List<string>
        {
            $"Período analisado: {response.Period.YearMonth}.",
            $"Escopo: {scope}.",
            $"Responsáveis: {owners}.",
            $"Entradas totais: {Money(response.Totals.Entradas)}.",
            $"Saídas totais: {Money(response.Totals.Saidas)}.",
            $"Saldo final: {Money(response.Totals.Saldo)}.",
            $"Patrimônio: {Money(response.Totals.Patrimonio)}.",
            $"Cartão de crédito total: {Money(cardTotal)}, equivalente a {PercentText(response.FinancialRatios.CreditCardToIncomeRatio)} das entradas e {PercentText(Percent(cardTotal, response.Totals.Saidas))} das saídas.",
            $"Despesas variáveis: {Money(variable)}.",
            $"Despesas fixas: {Money(fixedExpense)}.",
            $"Empréstimos: {Money(loan)}.",
            $"O mês contém {response.TransactionsCount} transações confirmadas e {response.ProjectedCount} movimentos projetados."
        };

        if (largestOutflow is not null)
        {
            parts.Add($"A maior saída identificada é {largestOutflow.Description} em {largestOutflow.Date:yyyy-MM-dd} no valor de {Money(largestOutflow.Amount)}.");
        }

        if (largestCardInvoice is not null)
        {
            parts.Add($"A maior fatura de cartão é {largestCardInvoice.CardTitle}, responsável {largestCardInvoice.OwnerName}, com {Money(largestCardInvoice.Amount)}.");
        }

        var minimumDate = response.CashflowMetrics.MinimumBalanceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "indisponível";
        parts.Add($"O menor saldo diário calculado é {Money(response.CashflowMetrics.MinimumBalance)} em {minimumDate}.");
        parts.Add($"Existem {response.CashflowMetrics.DaysBelowSafetyMargin} dias abaixo da margem de segurança de {Money(response.CashflowMetrics.SafetyMarginValue)}.");

        return string.Join(" ", parts);
    }

    private static string PercentText(decimal? value) =>
        value is null ? "indisponível" : $"{value.Value.ToString("N2", PtBr)}%";

    private static string NextThreeMonthsText(CopilotNextThreeMonthsResponse response)
    {
        var mergeText = response.Scope == "merged" ? " considerando o merge ativo" : "";
        return $"Levantamento de {response.From:yyyy-MM-dd} a {response.To:yyyy-MM-dd}{mergeText}: entradas previstas {Money(response.Totals.Entradas)}, saidas previstas {Money(response.Totals.Saidas)}, aportes {Money(response.Totals.Aportes)} e patrimonio projetado ao fim do periodo de {Money(response.Totals.Patrimonio)}.";
    }

    private static string SimulationText(CopilotPurchaseSimulationResponse response)
    {
        var payment = response.PaymentType == "credit_card"
            ? $"{response.Installments.Count}x no cartao"
            : "a vista";
        return $"Simulacao da compra \"{response.Description}\" de {Money(response.Amount)} {payment}: janela {YearMonth.FromDate(response.AnalysisWindow.From).Value} ate {YearMonth.FromDate(response.AnalysisWindow.To).Value}, menor saldo projetado {Money(response.OverallRiskMetrics.ProjectedLowestBalance)} em {response.OverallRiskMetrics.ProjectedLowestBalanceDate:yyyy-MM-dd}, pior mes {response.OverallRiskMetrics.WorstMonth} com saldo final projetado de {Money(response.OverallRiskMetrics.WorstProjectedEndBalance)}. Nenhuma transacao foi criada.";
    }

    private static string CardsText(CopilotCardsResponse response)
    {
        var mergeText = response.Scope == "merged" ? " considerando o merge ativo" : "";
        return response.Cards.Count == 0
            ? $"Nenhum cartao ativo encontrado{mergeText}."
            : $"{response.Cards.Count} cartao(oes) ativo(s) encontrado(s){mergeText}.";
    }

    private sealed record OwnerSet(
        string Scope,
        string PrimaryUserId,
        IReadOnlyList<CopilotOwnerResponse> Responses,
        IReadOnlyDictionary<string, string> Names);

    private sealed record MonthSnapshot(
        YearMonth YearMonth,
        IReadOnlyList<OwnerMonthData> OwnerMonths,
        CopilotTotalsResponse Totals,
        IReadOnlyDictionary<string, decimal> ByCategory,
        IReadOnlyDictionary<string, decimal> ByCard);

    private sealed record CardInfo(
        string Id,
        string Title,
        string OwnerUserId,
        string OwnerName,
        string OwnerRole,
        int ClosingDay,
        int DueDay,
        DateOnly NextDueDate,
        string Currency);

    private sealed record OwnerMonthData(
        MonthlyAggregateResponse Response,
        CopilotFreshnessResponse Freshness);
}
