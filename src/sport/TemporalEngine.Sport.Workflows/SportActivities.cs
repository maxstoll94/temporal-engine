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
            Status = "Scheduled",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Fixtures.Add(fixture);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created fixture {Id} ({Name})", fixture.Id, fixture.Name);
        return fixture.Id;
    }

    [Activity]
    public async Task MarkFixtureStatusAsync(Guid fixtureId, string status)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;
        var fixture = await _db.Fixtures.FirstAsync(f => f.Id == fixtureId, ct);
        fixture.Status = status;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Fixture {Id} -> {Status}", fixtureId, status);
    }
}
