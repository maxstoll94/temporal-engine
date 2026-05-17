namespace TemporalEngine.Sport.Domain;

public class Event
{
    public Guid Id { get; set; }
    public Guid FixtureId { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset ScheduledStart { get; set; }
    public DateTimeOffset ScheduledEnd { get; set; }
    public string Status { get; set; } = "Scheduled";
    public DateTimeOffset CreatedAt { get; set; }
}
