using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Temporalio.Client;
using Temporalio.Testing;
using Temporalio.Worker;
using Temporalio.Workflows;
using TemporalEngine.Catalog.Contracts;
using TemporalEngine.Shared.Contracts;
using TemporalEngine.Sport.Contracts;
using TemporalEngine.Sport.Data;
using TemporalEngine.Sport.Workflows;

namespace TemporalEngine.Sport.Tests;

// Stub ProductWorkflow registered on the Catalog task queue so that EventWorkflow's
// child workflow call resolves without bringing the real Catalog worker.
[Workflow(WorkflowNames.ProductWorkflow)]
public class StubProductWorkflow
{
    [WorkflowRun]
    public Task<ProductResult> RunAsync(CreateProductInput input) =>
        Task.FromResult(new ProductResult(
            ProductId: Guid.NewGuid(),
            HadWinner: false,
            WinnerBidId: null,
            WinningAmount: 0m,
            OrderId: null));
}

public class EventWorkflowTests
{
    [Fact]
    public async Task EventWorkflow_Creates_Event_And_Spawns_Product_Per_Starting_Athlete()
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();

        var dbOptions = new DbContextOptionsBuilder<SportDbContext>()
            .UseInMemoryDatabase($"sport-test-{Guid.NewGuid():N}")
            .Options;
        await using var db = new SportDbContext(dbOptions);

        // Seed the fixture row the event activity expects to chain off of.
        db.Fixtures.Add(new Domain.Fixture
        {
            Id = Guid.NewGuid(),
            ExternalId = "fix-test",
            Name = "Test Fixture",
            ScheduledKickoff = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var fixtureId = (await db.Fixtures.FirstAsync()).Id;

        var sportActivities = new SportActivities(db, NullLogger<SportActivities>.Instance);

        using var sportWorker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions(TaskQueues.Sport)
                .AddAllActivities(sportActivities)
                .AddWorkflow<EventWorkflow>());

        using var catalogStubWorker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions(TaskQueues.Catalog)
                .AddWorkflow<StubProductWorkflow>());

        using var cts = new CancellationTokenSource();
        var sportTask = Task.Run(() => sportWorker.ExecuteAsync(cts.Token));
        var catalogStubTask = Task.Run(() => catalogStubWorker.ExecuteAsync(cts.Token));

        var input = new CreateEventInput(
            FixtureId: fixtureId,
            ExternalFixtureId: "fix-test",
            Name: "Test Match",
            ScheduledStart: DateTimeOffset.UtcNow,
            ScheduledEnd: DateTimeOffset.UtcNow + TimeSpan.FromMinutes(90),
            StartingAthletes: new[]
            {
                new AthleteStartingInput("athlete-001", "Athlete One", 10),
                new AthleteStartingInput("athlete-002", "Athlete Two", 7),
            },
            CorrelationId: "test-corr");

        var handle = await env.Client.StartWorkflowAsync(
            WorkflowNames.EventWorkflow,
            new object[] { input },
            new WorkflowOptions(id: "event-test", taskQueue: TaskQueues.Sport));

        // End the event immediately so the timer doesn't gate the test.
        await handle.SignalAsync("EndEvent", Array.Empty<object>());

        var result = await handle.GetResultAsync<EventWorkflowResult>();

        cts.Cancel();
        try { await Task.WhenAll(sportTask, catalogStubTask); }
        catch (OperationCanceledException) { /* expected */ }

        Assert.NotEqual(Guid.Empty, result.EventId);
        Assert.Equal(2, result.ProductResults.Count);

        var storedEvent = await db.Events.FirstAsync();
        Assert.Equal("Completed", storedEvent.Status);
        Assert.Equal(fixtureId, storedEvent.FixtureId);
    }
}
