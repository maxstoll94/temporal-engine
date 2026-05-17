namespace TemporalEngine.Catalog.Domain;

public class Product
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string AthleteExternalId { get; set; } = "";
    public string AthleteName { get; set; } = "";
    public int ShirtNumber { get; set; }
    public string Status { get; set; } = "Open";
    public string? WinningBidId { get; set; }
    public decimal WinningAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}
