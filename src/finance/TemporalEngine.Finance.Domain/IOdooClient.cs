namespace TemporalEngine.Finance.Domain;

public interface IOdooClient
{
    Task<string> CreateSalesOrderAsync(
        string productName,
        string customerId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken);

    Task<string> ShipSalesOrderAsync(
        string odooSalesOrderId,
        CancellationToken cancellationToken);
}
