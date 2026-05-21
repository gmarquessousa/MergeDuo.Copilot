using System.Globalization;
using System.Net;
using MergeDuo.Aggregates.Domain.Abstractions;
using MergeDuo.Aggregates.Domain.Documents;
using MergeDuo.Aggregates.Domain.Rules;
using MergeDuo.Copilot.Domain.Abstractions;
using MergeDuo.Copilot.Domain.Exceptions;
using MergeDuo.Copilot.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Copilot.Infra.Cosmos;

public sealed class CopilotCosmosRepository : ICopilotReadRepository, ICopilotReadinessProbe
{
    private readonly Container _users;
    private readonly Container _partnerships;
    private readonly Container _monthlyAggregates;
    private readonly Container _transactions;
    private readonly Container _fixedRules;
    private readonly Container _cards;
    private readonly CopilotOptions _copilotOptions;

    public CopilotCosmosRepository(CosmosClient client, CosmosOptions options, CopilotOptions copilotOptions)
    {
        _users = client.GetContainer(options.Database, options.UsersContainer);
        _partnerships = client.GetContainer(options.Database, options.PartnershipsContainer);
        _monthlyAggregates = client.GetContainer(options.Database, options.MonthlyAggregatesContainer);
        _transactions = client.GetContainer(options.Database, options.TransactionsContainer);
        _fixedRules = client.GetContainer(options.Database, options.FixedRulesContainer);
        _cards = client.GetContainer(options.Database, options.CardsContainer);
        _copilotOptions = copilotOptions;
    }

    public async Task<CopilotReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        if (!UserIdRules.IsValid(_copilotOptions.UserId))
        {
            return new CopilotReadinessResult(false, "missing_copilot_user_id", "Copilot:UserId is missing or invalid.");
        }

        try
        {
            await _users.ReadContainerAsync(cancellationToken: cancellationToken);
            await _partnerships.ReadContainerAsync(cancellationToken: cancellationToken);
            await _monthlyAggregates.ReadContainerAsync(cancellationToken: cancellationToken);
            await _transactions.ReadContainerAsync(cancellationToken: cancellationToken);
            await _fixedRules.ReadContainerAsync(cancellationToken: cancellationToken);
            await _cards.ReadContainerAsync(cancellationToken: cancellationToken);

            var user = await GetUserAsync(_copilotOptions.UserId, cancellationToken);
            return user is { DeletedAt: null }
                ? new CopilotReadinessResult(true)
                : new CopilotReadinessResult(false, "copilot_user_not_found", "Configured Copilot user was not found.");
        }
        catch (CosmosException ex)
        {
            return new CopilotReadinessResult(false, "copilot_dependency_unavailable", $"Cosmos unavailable: {(int)ex.StatusCode}.");
        }
        catch (OperationCanceledException)
        {
            return new CopilotReadinessResult(false, "copilot_readiness_timeout", "Cosmos readiness check timed out.");
        }
    }

    public async Task<UserDocument?> GetUserAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _users.ReadItemAsync<UserDocument>(
                userId,
                new PartitionKey(userId),
                cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            throw Dependency(ex);
        }
    }

    public async Task<PartnershipDocument?> GetActivePartnerAsync(string userId, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT * FROM c
                WHERE c.docType = "partnership"
                  AND c.userId = @userId
                  AND c.status = "active"
                """)
            .WithParameter("@userId", userId);

        try
        {
            using var iterator = _partnerships.GetItemQueryIterator<PartnershipDocument>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(userId),
                    MaxItemCount = 1
                });

            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                return page.Resource.FirstOrDefault();
            }

            return null;
        }
        catch (CosmosException ex)
        {
            throw Dependency(ex);
        }
    }

    public async Task<MonthlyAggregateDocument?> GetMonthAggregateAsync(
        string userId,
        YearMonth yearMonth,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _monthlyAggregates.ReadItemAsync<MonthlyAggregateDocument>(
                AggregateDocumentId.For(userId, yearMonth),
                new PartitionKey(userId),
                cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            throw Dependency(ex);
        }
    }

    public async Task<MonthlyAggregateDocument?> GetLatestAggregateBeforeAsync(
        string userId,
        YearMonth yearMonth,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT TOP 1 *
                FROM c
                WHERE c.userId = @userId
                  AND c.yearMonth < @yearMonth
                ORDER BY c.yearMonth DESC
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@yearMonth", yearMonth.Value);

        try
        {
            using var iterator = _monthlyAggregates.GetItemQueryIterator<MonthlyAggregateDocument>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(userId),
                    MaxItemCount = 1
                });

            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                return page.Resource.FirstOrDefault();
            }

            return null;
        }
        catch (CosmosException ex)
        {
            throw Dependency(ex);
        }
    }

    public async Task<IReadOnlyList<TransactionProjection>> ListActiveMonthAsync(
        string userId,
        YearMonth yearMonth,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT c.id, c.docType, c.userId, c.yearMonth, c.date, c.purchaseDate, c.category, c.description,
                       c.kind, c.amount, c.currency, c.cardId, c.fixedRuleId, c.updatedAt, c.deletedAt
                FROM c
                WHERE c.userId = @userId
                  AND c.yearMonth = @yearMonth
                  AND IS_NULL(c.deletedAt)
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@yearMonth", yearMonth.Value);

        try
        {
            using var iterator = _transactions.GetItemQueryIterator<TransactionProjection>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKeyBuilder().Add(userId).Add(yearMonth.Value).Build(),
                    MaxItemCount = 100
                });

            var results = new List<TransactionProjection>();
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(page.Resource);
            }

            return results;
        }
        catch (CosmosException ex)
        {
            throw Dependency(ex);
        }
    }

    public async Task<IReadOnlyList<TransactionProjection>> ListActiveRangeAsync(
        string userId,
        DateOnly fromDate,
        DateOnly throughDate,
        CancellationToken cancellationToken)
    {
        if (fromDate > throughDate)
        {
            return [];
        }

        var query = new QueryDefinition(
                """
                SELECT c.id, c.docType, c.userId, c.yearMonth, c.date, c.purchaseDate, c.category, c.description,
                       c.kind, c.amount, c.currency, c.cardId, c.fixedRuleId, c.updatedAt, c.deletedAt
                FROM c
                WHERE c.userId = @userId
                  AND c.date >= @fromDate
                  AND c.date <= @throughDate
                  AND IS_NULL(c.deletedAt)
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@fromDate", fromDate.ToString("yyyy-MM-dd"))
            .WithParameter("@throughDate", throughDate.ToString("yyyy-MM-dd"));

        try
        {
            using var iterator = _transactions.GetItemQueryIterator<TransactionProjection>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKeyBuilder().Add(userId).Build(),
                    MaxItemCount = 100
                });

            var results = new List<TransactionProjection>();
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(page.Resource);
            }

            return results;
        }
        catch (CosmosException ex)
        {
            throw Dependency(ex);
        }
    }

    public async Task<SourceWatermarkDocument> GetMonthWatermarkAsync(
        string userId,
        YearMonth yearMonth,
        CancellationToken cancellationToken)
    {
        var partitionKey = new PartitionKeyBuilder().Add(userId).Add(yearMonth.Value).Build();
        try
        {
            return new SourceWatermarkDocument
            {
                MaxTransactionUpdatedAt = await GetMonthMaxUpdatedAtAsync(userId, yearMonth, partitionKey, cancellationToken),
                ActiveTransactionsCount = await GetActiveMonthCountAsync(userId, yearMonth, partitionKey, cancellationToken)
            };
        }
        catch (CosmosException ex)
        {
            throw Dependency(ex);
        }
    }

    public async Task<IReadOnlyDictionary<YearMonth, SourceWatermarkDocument>> GetYearWatermarksAsync(
        string userId,
        int year,
        CancellationToken cancellationToken)
    {
        var partitionKey = new PartitionKeyBuilder().Add(userId).Build();
        var results = new Dictionary<YearMonth, SourceWatermarkDocument>();

        try
        {
            await LoadYearMaxUpdatedAtAsync(userId, year, partitionKey, results, cancellationToken);
            await LoadYearActiveCountsAsync(userId, year, partitionKey, results, cancellationToken);
            return results;
        }
        catch (CosmosException ex)
        {
            throw Dependency(ex);
        }
    }

    public async Task<MovementTotals> SumTotalsThroughAsync(
        string userId,
        DateOnly throughDate,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT c.kind, SUM(c.amount) AS amount
                FROM c
                WHERE c.userId = @userId
                  AND c.date <= @throughDate
                  AND IS_NULL(c.deletedAt)
                GROUP BY c.kind
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@throughDate", throughDate.ToString("yyyy-MM-dd"));

        try
        {
            using var iterator = _transactions.GetItemQueryIterator<TotalsByKindProjection>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKeyBuilder().Add(userId).Build(),
                    MaxItemCount = 3
                });

            decimal entradas = 0m;
            decimal saidas = 0m;
            decimal aportes = 0m;
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                foreach (var item in page.Resource)
                {
                    switch (item.Kind)
                    {
                        case AggregateKinds.In:
                            entradas += item.Amount;
                            break;
                        case AggregateKinds.Out:
                            saidas += item.Amount;
                            break;
                        case AggregateKinds.Invest:
                            aportes += item.Amount;
                            break;
                    }
                }
            }

            return new MovementTotals(entradas, saidas, aportes);
        }
        catch (CosmosException ex)
        {
            throw Dependency(ex);
        }
    }

    public async Task<decimal> SumInvestmentsThroughAsync(
        string userId,
        DateOnly throughDate,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT VALUE SUM(c.amount)
                FROM c
                WHERE c.userId = @userId
                  AND c.kind = "invest"
                  AND c.date <= @throughDate
                  AND IS_NULL(c.deletedAt)
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@throughDate", throughDate.ToString("yyyy-MM-dd"));

        try
        {
            using var iterator = _transactions.GetItemQueryIterator<decimal?>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKeyBuilder().Add(userId).Build(),
                    MaxItemCount = 1
                });

            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                return page.Resource.FirstOrDefault() ?? 0m;
            }

            return 0m;
        }
        catch (CosmosException ex)
        {
            throw Dependency(ex);
        }
    }

    public async Task<IReadOnlyList<FixedRuleDocument>> ListActiveCandidatesAsync(
        string userId,
        DateOnly fromDate,
        DateOnly throughDate,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT *
                FROM c
                WHERE c.docType = "fixedRule"
                  AND c.userId = @userId
                  AND c.active = true
                  AND (NOT IS_DEFINED(c.deletedAt) OR IS_NULL(c.deletedAt))
                  AND c.startsAt <= @throughDate
                  AND (NOT IS_DEFINED(c.endsAt) OR IS_NULL(c.endsAt) OR c.endsAt >= @fromDate)
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@fromDate", fromDate.ToString("yyyy-MM-dd"))
            .WithParameter("@throughDate", throughDate.ToString("yyyy-MM-dd"));

        try
        {
            using var iterator = _fixedRules.GetItemQueryIterator<FixedRuleDocument>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(userId),
                    MaxItemCount = 100
                });

            var results = new List<FixedRuleDocument>();
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(page.Resource);
            }

            return results;
        }
        catch (CosmosException ex)
        {
            throw Dependency(ex);
        }
    }

    public async Task<CardDocument?> GetActiveAsync(string userId, string cardId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _cards.ReadItemAsync<CardDocument>(
                cardId,
                new PartitionKey(userId),
                cancellationToken: cancellationToken);
            var card = response.Resource;
            return card.DeletedAt is null && card.UserId == userId ? card : null;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            throw Dependency(ex);
        }
    }

    private async Task<DateTimeOffset?> GetMonthMaxUpdatedAtAsync(
        string userId,
        YearMonth yearMonth,
        PartitionKey partitionKey,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT VALUE MAX(c.updatedAt)
                FROM c
                WHERE c.userId = @userId
                  AND c.yearMonth = @yearMonth
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@yearMonth", yearMonth.Value);

        using var iterator = _transactions.GetItemQueryIterator<string?>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = partitionKey,
                MaxItemCount = 1
            });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            return ParseDateTimeOffset(page.Resource.FirstOrDefault());
        }

        return null;
    }

    private async Task<int> GetActiveMonthCountAsync(
        string userId,
        YearMonth yearMonth,
        PartitionKey partitionKey,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT VALUE COUNT(1)
                FROM c
                WHERE c.userId = @userId
                  AND c.yearMonth = @yearMonth
                  AND IS_NULL(c.deletedAt)
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@yearMonth", yearMonth.Value);

        using var iterator = _transactions.GetItemQueryIterator<int>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = partitionKey,
                MaxItemCount = 1
            });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            return page.Resource.FirstOrDefault();
        }

        return 0;
    }

    private async Task LoadYearMaxUpdatedAtAsync(
        string userId,
        int year,
        PartitionKey partitionKey,
        Dictionary<YearMonth, SourceWatermarkDocument> results,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT c.yearMonth, MAX(c.updatedAt) AS maxTransactionUpdatedAt
                FROM c
                WHERE c.userId = @userId
                  AND STARTSWITH(c.yearMonth, @yearPrefix)
                GROUP BY c.yearMonth
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@yearPrefix", $"{year}-");

        using var iterator = _transactions.GetItemQueryIterator<YearMaxUpdatedAtProjection>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = partitionKey,
                MaxItemCount = 100
            });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var item in page.Resource)
            {
                if (!YearMonth.TryParse(item.YearMonth, out var yearMonth)) continue;
                var watermark = GetOrCreateWatermark(results, yearMonth);
                watermark.MaxTransactionUpdatedAt = ParseDateTimeOffset(item.MaxTransactionUpdatedAt);
            }
        }
    }

    private async Task LoadYearActiveCountsAsync(
        string userId,
        int year,
        PartitionKey partitionKey,
        Dictionary<YearMonth, SourceWatermarkDocument> results,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT c.yearMonth, COUNT(1) AS activeTransactionsCount
                FROM c
                WHERE c.userId = @userId
                  AND STARTSWITH(c.yearMonth, @yearPrefix)
                  AND IS_NULL(c.deletedAt)
                GROUP BY c.yearMonth
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@yearPrefix", $"{year}-");

        using var iterator = _transactions.GetItemQueryIterator<YearActiveCountProjection>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = partitionKey,
                MaxItemCount = 100
            });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var item in page.Resource)
            {
                if (!YearMonth.TryParse(item.YearMonth, out var yearMonth)) continue;
                var watermark = GetOrCreateWatermark(results, yearMonth);
                watermark.ActiveTransactionsCount = item.ActiveTransactionsCount;
            }
        }
    }

    private static SourceWatermarkDocument GetOrCreateWatermark(
        Dictionary<YearMonth, SourceWatermarkDocument> watermarks,
        YearMonth yearMonth)
    {
        if (!watermarks.TryGetValue(yearMonth, out var watermark))
        {
            watermark = new SourceWatermarkDocument();
            watermarks[yearMonth] = watermark;
        }

        return watermark;
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;

    private static CopilotDependencyException Dependency(Exception ex) =>
        new("copilot_dependency_unavailable", "Copilot dependency unavailable.", ex);

    private sealed class TotalsByKindProjection
    {
        public string Kind { get; set; } = "";
        public decimal Amount { get; set; }
    }

    private sealed class YearMaxUpdatedAtProjection
    {
        public string YearMonth { get; set; } = "";
        public string? MaxTransactionUpdatedAt { get; set; }
    }

    private sealed class YearActiveCountProjection
    {
        public string YearMonth { get; set; } = "";
        public int ActiveTransactionsCount { get; set; }
    }
}
