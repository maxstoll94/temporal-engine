namespace TemporalEngine.Catalog.Contracts;

public record ProductResult(
    Guid ProductId,
    bool HadWinner,
    string? WinnerBidId,
    decimal WinningAmount,
    Guid? OrderId);
