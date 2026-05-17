namespace TemporalEngine.Finance.Domain;

public class Order
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string WinnerBidderId { get; set; } = "";
    public string WinningBidId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Status { get; set; } = "AwaitingPayment";
    public string? OdooSalesOrderId { get; set; }
    public string? TrackingNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
}
