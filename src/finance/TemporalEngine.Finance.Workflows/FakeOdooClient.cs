using Microsoft.Extensions.Logging;
using TemporalEngine.Finance.Domain;

namespace TemporalEngine.Finance.Workflows;

public class FakeOdooClient : IOdooClient
{
    private readonly ILogger<FakeOdooClient> _logger;

    public FakeOdooClient(ILogger<FakeOdooClient> logger) => _logger = logger;

    public async Task<string> CreateSalesOrderAsync(
        string productName, string customerId, decimal amount, string currency, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        var id = $"SO-{Guid.NewGuid():N}".Substring(0, 16);
        _logger.LogInformation(
            "[Odoo MOCK] Created sales order {Id} for {Customer}: {Amount} {Currency} ({Product})",
            id, customerId, amount, currency, productName);
        return id;
    }

    public async Task<string> ShipSalesOrderAsync(string odooSalesOrderId, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        var tracking = $"TRK-{Guid.NewGuid():N}".Substring(0, 16);
        _logger.LogInformation(
            "[Odoo MOCK] Shipped sales order {Id} with tracking {Tracking}",
            odooSalesOrderId, tracking);
        return tracking;
    }
}
