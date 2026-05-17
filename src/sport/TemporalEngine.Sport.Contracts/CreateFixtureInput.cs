namespace TemporalEngine.Sport.Contracts;

public record CreateFixtureInput(
    string ExternalId,
    string Name,
    DateTimeOffset ScheduledKickoff,
    TimeSpan EventDuration,
    string CorrelationId);
