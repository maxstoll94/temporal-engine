namespace TemporalEngine.Sport.Contracts;

// Sport-facing payload for the "athlete subbed in" event (from sport-data feeds / webhooks).
// Sport.FixtureWorkflow handles it and forwards to the catalog auction workflow.
public record AthleteSubbingInSignal(
    string AthleteExternalId,
    string AthleteName,
    int ShirtNumber,
    string Idempotency);
