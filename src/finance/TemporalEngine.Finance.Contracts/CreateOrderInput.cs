namespace TemporalEngine.Finance.Contracts;

public record CreateOrderInput(
    Guid ProductId,
    string ExternalFixtureId,
    string AthleteExternalId,
    string ProductName,
    string WinnerBidderId,
    string WinningBidId,
    decimal Amount,
    string Currency,
    string CorrelationId);
