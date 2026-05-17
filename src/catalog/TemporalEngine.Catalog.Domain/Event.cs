namespace TemporalEngine.Catalog.Domain;

public class Event
{
    public Guid Id { get; set; }
    // Natural key of the upstream fixture (sport service). No FK — soft reference only.
    public string ExternalFixtureId { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset ScheduledStart { get; set; }
    public DateTimeOffset ScheduledEnd { get; set; }
    public string Status { get; set; } = "Scheduled";
    public DateTimeOffset CreatedAt { get; set; }
}
