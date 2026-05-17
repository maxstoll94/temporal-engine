namespace TemporalEngine.Catalog.Domain;

public class Bid
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ExternalBidId { get; set; } = "";
    public string BidderId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public DateTimeOffset PlacedAt { get; set; }
}
