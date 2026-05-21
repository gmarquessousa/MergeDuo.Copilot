using Azure.Identity;
using MergeDuo.Copilot.Domain.Exceptions;
using MergeDuo.Copilot.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Copilot.Infra.Cosmos;

public static class CosmosClientFactory
{
    public static CosmosClient Create(CosmosOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString) && string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new CopilotConfigurationException(
                "missing_cosmos_endpoint",
                "Cosmos:Endpoint or Cosmos:ConnectionString is required.");
        }

        var clientOptions = new CosmosClientOptions
        {
            ApplicationName = "mergeduo-copilot",
            ConsistencyLevel = ConsistencyLevel.Session,
            ConnectionMode = ConnectionMode.Direct,
            EnableContentResponseOnWrite = false,
            MaxRetryAttemptsOnRateLimitedRequests = 9,
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            }
        };

        return !string.IsNullOrWhiteSpace(options.ConnectionString)
            ? new CosmosClient(options.ConnectionString, clientOptions)
            : new CosmosClient(options.Endpoint, new DefaultAzureCredential(), clientOptions);
    }
}
