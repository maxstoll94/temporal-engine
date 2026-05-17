namespace TemporalEngine.Catalog.Contracts;

public record CreateEventInput(
    string ExternalFixtureId,
    string Name,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    IReadOnlyList<AthleteStartingInput> StartingAthletes,
    string CorrelationId);

public record AthleteStartingInput(
    string AthleteExternalId,
    string AthleteName,
    int ShirtNumber);
