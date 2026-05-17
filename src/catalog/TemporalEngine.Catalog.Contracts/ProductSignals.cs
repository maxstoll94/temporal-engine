namespace TemporalEngine.Catalog.Contracts;

public record BidPlacedSignal(
    string BidId,
    string BidderId,
    decimal Amount,
    string Currency,
    DateTimeOffset PlacedAt);

public record EventEndedSignal(DateTimeOffset EndedAt);
