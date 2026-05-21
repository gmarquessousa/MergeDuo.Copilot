using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MergeDuo.Copilot.Domain.Contracts;
using MergeDuo.Copilot.Domain.Documents;
using MergeDuo.Copilot.Domain.Rules;

namespace MergeDuo.Copilot.Tests;

public sealed class CopilotApiTests
{
    [Fact]
    public async Task Month_summary_returns_stored_single_user_without_authorization()
    {
        using var factory = new TestCopilotFactory();
        using var client = factory.CreateHttpsClient();
        factory.Repository.SeedUser("usr_primary", 1000);
        factory.Repository.SeedAggregate(Aggregate("usr_primary", 2026, 5, entradas: 5000, saidas: 1200, aportes: 500, saldo: 4300, investido: 2000));

        var response = await client.GetAsync("/copilot/month-summary/2026/5");
        await EnsureSuccessAsync(response);
        var summary = (await response.Content.ReadFromJsonAsync<CopilotMonthSummaryResponse>())!;

        Assert.Equal("single", summary.Scope);
        Assert.Single(summary.Owners);
        Assert.Equal("usr_primary", summary.Owners[0].UserId);
        Assert.Equal("2026-05", summary.Period.YearMonth);
        Assert.Equal(5000, summary.Totals.Entradas);
        Assert.Equal(1200, summary.Totals.Saidas);
        Assert.Equal(4300, summary.Totals.Saldo);
        Assert.Equal(6300, summary.Totals.Patrimonio);
        Assert.Equal("fresh", summary.DataFreshness.State);
        Assert.Contains("Resumo de 2026-05", summary.SummaryText);
    }

    [Fact]
    public async Task Month_summary_with_active_merge_sums_partner()
    {
        using var factory = new TestCopilotFactory();
        using var client = factory.CreateHttpsClient();
        factory.Repository.SeedUser("usr_primary");
        factory.Repository.SeedUser("usr_partner");
        factory.Repository.SeedPartnership(new PartnershipDocument
        {
            Id = "pair_usr_primary_usr_partner",
            PartnershipId = "pair_001",
            UserId = "usr_primary",
            PartnerUserId = "usr_partner",
            Status = "active",
            MergedSince = new DateOnly(2026, 1, 1)
        });
        factory.Repository.SeedAggregate(Aggregate("usr_primary", 2026, 5, entradas: 1000, saidas: 200, aportes: 100, saldo: 700, investido: 1000));
        factory.Repository.SeedAggregate(Aggregate("usr_partner", 2026, 5, entradas: 500, saidas: 100, aportes: 50, saldo: 350, investido: 500));

        var response = await client.GetAsync("/copilot/month-summary/2026/5");
        await EnsureSuccessAsync(response);
        var summary = (await response.Content.ReadFromJsonAsync<CopilotMonthSummaryResponse>())!;

        Assert.Equal("merged", summary.Scope);
        Assert.Equal(["primary", "partner"], summary.Owners.Select(x => x.Role).ToArray());
        Assert.Equal(1500, summary.Totals.Entradas);
        Assert.Equal(300, summary.Totals.Saidas);
        Assert.Equal(150, summary.Totals.Aportes);
        Assert.Equal(1050, summary.Totals.Saldo);
        Assert.Equal(1500, summary.Totals.Investido);
        Assert.Equal(2550, summary.Totals.Patrimonio);
        Assert.Contains("merge ativo", summary.SummaryText);
    }

    [Fact]
    public async Task Month_summary_falls_back_to_transient_computation_without_persisting()
    {
        using var factory = new TestCopilotFactory();
        using var client = factory.CreateHttpsClient();
        factory.Repository.SeedUser("usr_primary", 1000);
        factory.Repository.SeedTransaction(Tx("tx_salary", "usr_primary", "2026-05-05", AggregateCategories.Income, AggregateKinds.In, 2000));
        factory.Repository.SeedTransaction(Tx("tx_market", "usr_primary", "2026-05-10", AggregateCategories.VariableExpense, AggregateKinds.Out, 300));
        factory.Repository.SeedTransaction(Tx("tx_invest", "usr_primary", "2026-05-15", AggregateCategories.Investment, AggregateKinds.Invest, 400));

        var response = await client.GetAsync("/copilot/month-summary/2026/5");
        await EnsureSuccessAsync(response);
        var summary = (await response.Content.ReadFromJsonAsync<CopilotMonthSummaryResponse>())!;

        Assert.Equal(2000, summary.Totals.Entradas);
        Assert.Equal(300, summary.Totals.Saidas);
        Assert.Equal(400, summary.Totals.Aportes);
        Assert.Equal(2300, summary.Totals.Saldo);
        Assert.Equal(400, summary.Totals.Investido);
        Assert.Equal(2700, summary.Totals.Patrimonio);
        Assert.Equal(3, summary.TransactionsCount);
        Assert.Equal("computed_transient", summary.DataFreshness.Source);
        Assert.Equal(0, factory.Repository.MutationCount);
    }

    [Fact]
    public async Task Next_three_months_returns_requested_month_plus_two_following_months()
    {
        using var factory = new TestCopilotFactory();
        using var client = factory.CreateHttpsClient();
        factory.Repository.SeedUser("usr_primary");
        factory.Repository.SeedAggregate(Aggregate("usr_primary", 2026, 5, entradas: 1000, saidas: 200, aportes: 100, saldo: 700, investido: 100));
        factory.Repository.SeedAggregate(Aggregate("usr_primary", 2026, 6, entradas: 1100, saidas: 250, aportes: 150, saldo: 1400, investido: 250));
        factory.Repository.SeedAggregate(Aggregate("usr_primary", 2026, 7, entradas: 1200, saidas: 300, aportes: 200, saldo: 2100, investido: 450));

        var response = await client.GetAsync("/copilot/next-three-months?year=2026&month=5");
        await EnsureSuccessAsync(response);
        var projection = (await response.Content.ReadFromJsonAsync<CopilotNextThreeMonthsResponse>())!;

        Assert.Equal(new DateOnly(2026, 5, 1), projection.From);
        Assert.Equal(new DateOnly(2026, 7, 31), projection.To);
        Assert.Equal(["2026-05", "2026-06", "2026-07"], projection.Months.Select(x => x.Period.YearMonth).ToArray());
        Assert.Equal(3300, projection.Totals.Entradas);
        Assert.Equal(750, projection.Totals.Saidas);
        Assert.Equal(450, projection.Totals.Aportes);
        Assert.Equal(2100, projection.Totals.Saldo);
        Assert.Equal(2550, projection.Totals.Patrimonio);
        Assert.Contains("Levantamento", projection.SummaryText);
    }

    [Fact]
    public async Task Purchase_simulation_cash_does_not_persist()
    {
        using var factory = new TestCopilotFactory();
        using var client = factory.CreateHttpsClient();
        factory.Repository.SeedUser("usr_primary", 1000);

        var response = await client.PostAsJsonAsync("/copilot/purchase-simulation", new
        {
            description = "Mesa",
            amount = 250m,
            purchaseDate = "2026-05-22",
            paymentType = "cash"
        });
        await EnsureSuccessAsync(response);
        var simulation = (await response.Content.ReadFromJsonAsync<CopilotPurchaseSimulationResponse>())!;

        Assert.Equal("cash", simulation.PaymentType);
        Assert.Single(simulation.Installments);
        Assert.Equal(new DateOnly(2026, 5, 22), simulation.Installments[0].Date);
        Assert.Equal("2026-05", simulation.Installments[0].YearMonth);
        Assert.Equal(250, simulation.MonthImpacts[0].ImpactAmount);
        Assert.Equal(750, simulation.MonthImpacts[0].ProjectedTotals.Saldo);
        Assert.Contains("Nenhuma transacao foi criada", simulation.SummaryText);
        Assert.Equal(0, factory.Repository.MutationCount);
    }

    [Fact]
    public async Task Cards_endpoint_lists_active_cards_for_primary_and_partner()
    {
        using var factory = new TestCopilotFactory();
        using var client = factory.CreateHttpsClient();
        factory.Repository.SeedUser("usr_primary");
        factory.Repository.SeedUser("usr_partner");
        factory.Repository.SeedPartnership(new PartnershipDocument
        {
            Id = "pair_usr_primary_usr_partner",
            PartnershipId = "pair_001",
            UserId = "usr_primary",
            PartnerUserId = "usr_partner",
            Status = "active",
            MergedSince = new DateOnly(2026, 1, 1)
        });
        factory.Repository.SeedCard(new CardDocument
        {
            Id = "card_primary",
            UserId = "usr_primary",
            Title = "Nubank",
            ClosingDay = 28,
            DueDay = 10,
            Currency = "BRL"
        });
        factory.Repository.SeedCard(new CardDocument
        {
            Id = "card_partner",
            UserId = "usr_partner",
            Title = "Itau",
            ClosingDay = 20,
            DueDay = 25,
            Currency = "BRL"
        });

        var response = await client.GetAsync("/copilot/cards");
        await EnsureSuccessAsync(response);
        var cards = (await response.Content.ReadFromJsonAsync<CopilotCardsResponse>())!;

        Assert.Equal("merged", cards.Scope);
        Assert.Equal(2, cards.Cards.Count);
        Assert.Equal("card_primary", cards.Cards[0].Id);
        Assert.Equal("Nubank", cards.Cards[0].Title);
        Assert.Equal("usr_primary", cards.Cards[0].OwnerUserId);
        Assert.Equal("primary", cards.Cards[0].OwnerRole);
        Assert.Equal(10, cards.Cards[0].DueDay);
        Assert.Equal(new DateOnly(2026, 6, 10), cards.Cards[0].NextDueDate);
        Assert.Equal("card_partner", cards.Cards[1].Id);
        Assert.Equal("usr_partner", cards.Cards[1].OwnerUserId);
        Assert.Equal("partner", cards.Cards[1].OwnerRole);
        Assert.Equal(new DateOnly(2026, 5, 25), cards.Cards[1].NextDueDate);
        Assert.Contains("2 cartao", cards.SummaryText);
    }

    [Fact]
    public async Task Purchase_simulation_credit_card_uses_closing_and_due_dates()
    {
        using var factory = new TestCopilotFactory();
        using var client = factory.CreateHttpsClient();
        factory.Repository.SeedUser("usr_primary", 1000);
        factory.Repository.SeedCard(new CardDocument
        {
            Id = "card_main",
            UserId = "usr_primary",
            ClosingDay = 28,
            DueDay = 5
        });

        var response = await client.PostAsJsonAsync("/copilot/purchase-simulation", new
        {
            description = "Notebook",
            amount = 1000m,
            purchaseDate = "2026-05-29",
            paymentType = "credit_card",
            cardId = "card_main",
            installments = 2
        });
        await EnsureSuccessAsync(response);
        var simulation = (await response.Content.ReadFromJsonAsync<CopilotPurchaseSimulationResponse>())!;

        Assert.Equal("credit_card", simulation.PaymentType);
        Assert.Equal("card_main", simulation.CardId);
        Assert.Equal([new DateOnly(2026, 7, 5), new DateOnly(2026, 8, 5)], simulation.Installments.Select(x => x.Date).ToArray());
        Assert.Equal(["2026-07", "2026-08"], simulation.MonthImpacts.Select(x => x.Period.YearMonth).ToArray());
        Assert.All(simulation.Installments, installment => Assert.Equal(500, installment.Amount));
        Assert.Equal(0, factory.Repository.MutationCount);
    }

    [Fact]
    public async Task Readyz_fails_when_configured_user_does_not_exist()
    {
        using var factory = new TestCopilotFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("copilot_user_not_found", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Swagger_contains_copilot_operation_ids()
    {
        using var factory = new TestCopilotFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        await EnsureSuccessAsync(response);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"operationId\": \"GetMonthSummary\"", json);
        Assert.Contains("\"operationId\": \"GetNextThreeMonths\"", json);
        Assert.Contains("\"operationId\": \"ListAvailableCards\"", json);
        Assert.Contains("\"operationId\": \"SimulatePurchase\"", json);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.StatusCode}). Body: {body}");
        }
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static MonthlyAggregateDocument Aggregate(
        string userId,
        int year,
        int month,
        decimal entradas,
        decimal saidas,
        decimal aportes,
        decimal saldo,
        decimal investido)
    {
        var yearMonth = $"{year:D4}-{month:D2}";
        return new MonthlyAggregateDocument
        {
            Id = $"agg_{userId}_{yearMonth}",
            UserId = userId,
            Year = year,
            MonthIdx = month - 1,
            YearMonth = yearMonth,
            Totals = new MonthlyTotalsDocument
            {
                Entradas = entradas,
                Saidas = saidas,
                Aportes = aportes,
                Saldo = saldo,
                Investido = investido
            },
            SnapshotToday = new SnapshotTodayDocument
            {
                SaldoHoje = saldo,
                InvestidoHoje = investido,
                PatrimonioHoje = saldo + investido,
                AsOfDate = new DateOnly(year, month, Math.Min(21, DateTime.DaysInMonth(year, month)))
            },
            DailyBalances = Enumerable.Range(1, DateTime.DaysInMonth(year, month))
                .Select(day => new DailyBalanceDocument { Day = day, Saldo = saldo })
                .ToList(),
            DailyMovements =
            [
                new DailyMovementDocument
                {
                    Day = 5,
                    Id = $"tx_{userId}_{yearMonth}",
                    UserId = userId,
                    Category = AggregateCategories.Income,
                    Description = "Receita",
                    Kind = AggregateKinds.In,
                    Amount = entradas,
                    Projected = false
                }
            ],
            Projection = new ProjectionDocument
            {
                IncludesProjected = false,
                ProjectedCount = 0,
                AsOfDate = new DateOnly(year, month, Math.Min(21, DateTime.DaysInMonth(year, month)))
            },
            ByCategory = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                [AggregateCategories.Income] = entradas,
                [AggregateCategories.VariableExpense] = saidas,
                [AggregateCategories.Investment] = aportes
            },
            ByOwner = new Dictionary<string, OwnerTotalsDocument>(StringComparer.Ordinal)
            {
                [userId] = new() { Entradas = entradas, Saidas = saidas, Aportes = aportes }
            },
            TransactionsCount = 3,
            ComputedAt = DateTimeOffset.Parse("2026-05-21T15:00:00Z"),
            SourceVersion = 4
        };
    }

    private static TransactionProjection Tx(
        string id,
        string userId,
        string date,
        string category,
        string kind,
        decimal amount)
    {
        var parsed = DateOnly.Parse(date);
        return new TransactionProjection
        {
            Id = id,
            UserId = userId,
            YearMonth = $"{parsed.Year:D4}-{parsed.Month:D2}",
            Date = parsed,
            Category = category,
            Description = id,
            Kind = kind,
            Amount = amount,
            Currency = "BRL",
            UpdatedAt = DateTimeOffset.Parse("2026-05-21T15:00:00Z")
        };
    }
}
