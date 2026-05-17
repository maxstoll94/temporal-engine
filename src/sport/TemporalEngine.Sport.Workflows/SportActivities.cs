using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using TemporalEngine.Sport.Contracts;
using TemporalEngine.Sport.Data;
using TemporalEngine.Sport.Domain;

namespace TemporalEngine.Sport.Workflows;

public class SportActivities
{
    private readonly SportDbContext _db;
    private readonly ILogger<SportActivities> _logger;

    public SportActivities(SportDbContext db, ILogger<SportActivities> logger)
    {
        _db = db;
        _logger = logger;
    }

    [Activity]
    public async Task<Guid> CreateFixtureAsync(CreateFixtureInput input)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;

        var existing = await _db.Fixtures
            .FirstOrDefaultAsync(f => f.ExternalId == input.ExternalId, ct);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Fixture {ExternalId} already exists with id {Id}",
                input.ExternalId, existing.Id);
            return existing.Id;
        }

        var fixture = new Fixture
        {
            Id = Guid.NewGuid(),
            ExternalId = input.ExternalId,
            Name = input.Name,
            ScheduledKickoff = input.ScheduledKickoff,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Fixtures.Add(fixture);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created fixture {Id} ({Name})", fixture.Id, fixture.Name);
        return fixture.Id;
    }

    [Activity]
    public async Task<Guid> CreateEventAsync(CreateEventInput input)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;

        var existing = await _db.Events
            .FirstOrDefaultAsync(e => e.FixtureId == input.FixtureId, ct);
        if (existing is not null)
        {
            return existing.Id;
        }

        var evt = new Event
        {
            Id = Guid.NewGuid(),
            FixtureId = input.FixtureId,
            Name = input.Name,
            ScheduledStart = input.ScheduledStart,
            ScheduledEnd = input.ScheduledEnd,
            Status = "Scheduled",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Events.Add(evt);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created event {Id} for fixture {FixtureId}", evt.Id, evt.FixtureId);
        return evt.Id;
    }

    [Activity]
    public async Task MarkEventStatusAsync(Guid eventId, string status)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;
        var evt = await _db.Events.FirstAsync(e => e.Id == eventId, ct);
        evt.Status = status;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Event {Id} -> {Status}", eventId, status);
    }
}
