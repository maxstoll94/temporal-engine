namespace TemporalEngine.Sport.Contracts;

public record AthleteSubbingInSignal(
    string AthleteExternalId,
    string AthleteName,
    int ShirtNumber,
    string Idempotency);
