using Microsoft.Extensions.Logging;
using Temporalio.Api.Enums.V1;
using Temporalio.Api.OperatorService.V1;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace TemporalEngine.Shared.Contracts;

/// Idempotently registers the engine's custom search attributes on the given namespace.
/// Safe to call from every worker process at startup; AlreadyExists is swallowed.
public static class SearchAttributeRegistration
{
    public static async Task EnsureAsync(ITemporalClient client, string @namespace, ILogger? logger = null)
    {
        foreach (var name in new[] { SearchAttributeNames.CorrelationId, SearchAttributeNames.Stage })
        {
            try
            {
                await client.Connection.OperatorService.AddSearchAttributesAsync(
                    new AddSearchAttributesRequest
                    {
                        Namespace = @namespace,
                        SearchAttributes = { [name] = IndexedValueType.Keyword },
                    });
                logger?.LogInformation("Registered search attribute {Name}", name);
            }
            catch (RpcException ex) when (ex.Code == RpcException.StatusCode.AlreadyExists)
            {
                // already registered — fine
            }
        }
    }
}
