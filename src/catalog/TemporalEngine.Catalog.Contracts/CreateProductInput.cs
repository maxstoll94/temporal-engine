namespace TemporalEngine.Catalog.Contracts;

public record CreateProductInput(
    Guid EventId,
    string ExternalFixtureId,
    string AthleteExternalId,
    string AthleteName,
    int ShirtNumber,
    string CorrelationId);
