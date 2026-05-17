using Temporalio.Common;
using Temporalio.Workflows;
using TemporalEngine.Catalog.Contracts;
using TemporalEngine.Shared.Contracts;
using TemporalEngine.Sport.Contracts;

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

        // 2. Hand off to catalog: start the auction-side EventWorkflow as a child
        //    on the catalog task queue. Sport doesn't know anything about events,
        //    products, or bids — that's all catalog's problem.
        var eventInput = new CreateEventInput(
            ExternalFixtureId: input.ExternalId,
            Name: $"{input.Name} - Match",
            ScheduledStart: input.ScheduledKickoff,
            ScheduledEnd: input.ScheduledKickoff + input.EventDuration,
            // For the demo, seed one starting athlete so the auction has a roster.
            StartingAthletes: new[]
            {
                new AthleteStartingInput("athlete-001", "Demo Athlete", 10),
            },
            CorrelationId: input.CorrelationId);

        var eventResult = await Workflow.ExecuteChildWorkflowAsync<EventWorkflowResult>(
            WorkflowNames.EventWorkflow,
            new object[] { eventInput },
            new ChildWorkflowOptions
            {
                Id = $"event-{input.ExternalId}",
                TaskQueue = TaskQueues.Catalog,
            });

        // 3. Catalog auction completed → fixture is done from sport's POV.
        await Workflow.ExecuteActivityAsync(
            (SportActivities a) => a.MarkFixtureStatusAsync(fixtureId, "Completed"),
            DefaultActivityOptions);

        return new FixtureResult(fixtureId, eventResult.EventId);
    }
}
