namespace TemporalEngine.Sport.Domain;

public class Fixture
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset ScheduledKickoff { get; set; }
    public string Status { get; set; } = "Scheduled";
    public DateTimeOffset CreatedAt { get; set; }
}
