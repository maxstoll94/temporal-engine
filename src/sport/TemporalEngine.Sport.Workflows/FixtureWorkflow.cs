using Temporalio.Common;
using Temporalio.Workflows;
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
        // Set search attributes for cross-service correlation.
        Workflow.UpsertTypedSearchAttributes(
            SearchAttrs.CorrelationId.ValueSet(input.CorrelationId),
            SearchAttrs.Stage.ValueSet("fixture-active"));

        // 1. Persist fixture
        var fixtureId = await Workflow.ExecuteActivityAsync(
            (SportActivities a) => a.CreateFixtureAsync(input),
            DefaultActivityOptions);

        // 2. Start child EventWorkflow on the same (sport) task queue
        var eventInput = new CreateEventInput(
            FixtureId: fixtureId,
            ExternalFixtureId: input.ExternalId,
            Name: $"{input.Name} - Match",
            ScheduledStart: input.ScheduledKickoff,
            ScheduledEnd: input.ScheduledKickoff + input.EventDuration,
            // For the demo, seed one starting athlete so the event has a roster.
            StartingAthletes: new[]
            {
                new AthleteStartingInput("athlete-001", "Demo Athlete", 10),
            },
            CorrelationId: input.CorrelationId);

        var eventResult = await Workflow.ExecuteChildWorkflowAsync<EventWorkflow, EventWorkflowResult>(
            wf => wf.RunAsync(eventInput),
            new ChildWorkflowOptions
            {
                Id = $"event-{input.ExternalId}",
                TaskQueue = TaskQueues.Sport,
            });

        return new FixtureResult(fixtureId, eventResult.EventId);
    }
}
