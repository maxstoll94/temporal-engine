namespace TemporalEngine.Finance.Contracts;

public record PaymentReceivedSignal(
    string PaymentId,
    decimal Amount,
    string Currency,
    DateTimeOffset ReceivedAt);

public record ShipmentUpdatedSignal(
    string TrackingNumber,
    string Status,
    DateTimeOffset UpdatedAt);
