namespace TemporalEngine.Finance.Contracts;

public record OrderResult(
    Guid OrderId,
    string FinalStatus,
    string? OdooSalesOrderId,
    string? TrackingNumber);
