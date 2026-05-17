using Temporalio.Common;
using Temporalio.Workflows;
using TemporalEngine.Shared.Contracts;
using TemporalEngine.Sport.Contracts;
using CatalogContracts = TemporalEngine.Catalog.Contracts;

namespace TemporalEngine.Sport.Workflows;

internal static class SearchAttrs
{
    public static readonly SearchAttributeKey<string> CorrelationId =
        SearchAttributeKey.CreateKeyword(SearchAttributeNames.CorrelationId);
    public static readonly SearchAttributeKey<string> Stage =
        SearchAttributeKey.CreateKeyword(SearchAttributeNames.Stage);
}

[Workflow(WorkflowNames.FixtureWorkflow)]
public class FixtureWorkflow
{
    private static readonly ActivityOptions DefaultActivityOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(1),
        RetryPolicy = new RetryPolicy
        {
            InitialInterval = TimeSpan.FromSeconds(1),
            MaximumInterval = TimeSpan.FromSeconds(30),
            MaximumAttempts = 5,
        },
    };

    private static readonly TimeSpan SubInInterval = TimeSpan.FromSeconds(2);

    private ChildWorkflowHandle? _catalogEventHandle;

    [WorkflowRun]
    public async Task<FixtureResult> RunAsync(CreateFixtureInput input)
    {
        Workflow.UpsertTypedSearchAttributes(
            SearchAttrs.CorrelationId.ValueSet(input.CorrelationId),
            SearchAttrs.Stage.ValueSet("fixture-active"));

        // 1. Persist fixture in sport DB.
        var fixtureId = await Workflow.ExecuteActivityAsync(
            (SportActivities a) => a.CreateFixtureAsync(input),
            DefaultActivityOptions);

        await Workflow.ExecuteActivityAsync(
            (SportActivities a) => a.MarkFixtureStatusAsync(fixtureId, "Live"),
            DefaultActivityOptions);

        // 2. Hand off to catalog: start the auction-side EventWorkflow as a child.
        var scheduledEnd = input.ScheduledKickoff + input.EventDuration;
        var eventInput = new CatalogContracts.CreateEventInput(
            ExternalFixtureId: input.ExternalId,
            Name: $"{input.Name} - Match",
            ScheduledStart: input.ScheduledKickoff,
            ScheduledEnd: scheduledEnd,
            StartingAthletes: new[]
            {
                new CatalogContracts.AthleteStartingInput("athlete-001", "Demo Athlete", 10),
            },
            CorrelationId: input.CorrelationId);

        _catalogEventHandle = await Workflow.StartChildWorkflowAsync(
            WorkflowNames.EventWorkflow,
            new object[] { eventInput },
            new ChildWorkflowOptions
            {
                Id = $"event-{input.ExternalId}",
                TaskQueue = TaskQueues.Catalog,
            });

        // 3. While the event is live, run two concurrent tasks:
        //    - await the catalog auction's result (the real work)
        //    - simulate athlete sub-ins every 2 seconds (the demo flavor)
        var eventTask = _catalogEventHandle.GetResultAsync<CatalogContracts.EventWorkflowResult>();
        var simulatorTask = SimulateAthleteSubInsAsync(scheduledEnd);
        await Task.WhenAll(eventTask, simulatorTask);
        var eventResult = await eventTask;

        // 4. Catalog auction completed → fixture is done from sport's POV.
        await Workflow.ExecuteActivityAsync(
            (SportActivities a) => a.MarkFixtureStatusAsync(fixtureId, "Completed"),
            DefaultActivityOptions);

        return new FixtureResult(fixtureId, eventResult.EventId);
    }

    /// External signal from sport-data webhook (or test driver) — forwards into catalog's auction.
    [WorkflowSignal]
    public Task AthleteSubbingInAsync(AthleteSubbingInSignal signal) => ForwardSubInAsync(signal);

    /// Simulator: fires a fake sub-in every 2 seconds while the match is live.
    /// Stops when the scheduled end is reached. Athlete IDs are sim-prefixed so they
    /// don't collide with real sub-ins.
    private async Task SimulateAthleteSubInsAsync(DateTimeOffset scheduledEnd)
    {
        var counter = 100;
        while (Workflow.UtcNow + SubInInterval < scheduledEnd)
        {
            await Workflow.DelayAsync(SubInInterval);
            if (Workflow.UtcNow >= scheduledEnd) break;

            await ForwardSubInAsync(new AthleteSubbingInSignal(
                AthleteExternalId: $"sim-athlete-{counter}",
                AthleteName: $"Sub Athlete {counter}",
                ShirtNumber: counter,
                Idempotency: $"sim-{counter}"));
            counter++;
        }
    }

    private async Task ForwardSubInAsync(AthleteSubbingInSignal signal)
    {
        if (_catalogEventHandle is null)
        {
            return; // catalog child not yet started; ignore (extremely tight race window)
        }

        var catalogSignal = new CatalogContracts.AthleteSubbingInSignal(
            signal.AthleteExternalId,
            signal.AthleteName,
            signal.ShirtNumber,
            signal.Idempotency);

        await _catalogEventHandle.SignalAsync(
            "AthleteSubbingIn",
            new object[] { catalogSignal });
    }
}
