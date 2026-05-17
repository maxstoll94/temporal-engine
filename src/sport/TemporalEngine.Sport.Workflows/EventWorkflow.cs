using Temporalio.Common;
using Temporalio.Workflows;
using TemporalEngine.Catalog.Contracts;
using TemporalEngine.Shared.Contracts;
using TemporalEngine.Sport.Contracts;

namespace TemporalEngine.Sport.Workflows;

public record EventWorkflowResult(Guid EventId, IReadOnlyList<ProductResult> ProductResults);

[Workflow(WorkflowNames.EventWorkflow)]
public class EventWorkflow
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

    private readonly List<ChildWorkflowHandle> _productHandles = new();
    private readonly List<AthleteStartingInput> _additionalAthletes = new();
    private Guid _eventId;
    private string _correlationId = "";
    private string _externalFixtureId = "";
    private bool _eventEnded;

    [WorkflowRun]
    public async Task<EventWorkflowResult> RunAsync(CreateEventInput input)
    {
        _correlationId = input.CorrelationId;
        _externalFixtureId = input.ExternalFixtureId;

        Workflow.UpsertTypedSearchAttributes(
            SearchAttrs.CorrelationId.ValueSet(input.CorrelationId),
            SearchAttrs.Stage.ValueSet("event-live"));

        // 1. Persist event
        _eventId = await Workflow.ExecuteActivityAsync(
            (SportActivities a) => a.CreateEventAsync(input),
            DefaultActivityOptions);

        await Workflow.ExecuteActivityAsync(
            (SportActivities a) => a.MarkEventStatusAsync(_eventId, "Live"),
            DefaultActivityOptions);

        // 2. Start a ProductWorkflow (on the catalog queue) for each starting athlete.
        foreach (var athlete in input.StartingAthletes)
        {
            await StartProductForAthleteAsync(athlete);
        }

        // 3. Wait until scheduled end OR until an external EventEnded signal flips the flag.
        var timeUntilEnd = input.ScheduledEnd - Workflow.UtcNow;
        if (timeUntilEnd > TimeSpan.Zero)
        {
            await Workflow.WaitConditionAsync(() => _eventEnded, timeUntilEnd);
        }

        // 4. Notify all product workflows that the event has ended.
        var endedAt = Workflow.UtcNow;
        foreach (var handle in _productHandles)
        {
            await handle.SignalAsync(
                "EventEnded",
                new object[] { new EventEndedSignal(endedAt) });
        }

        // 5. Wait for all products to finish (winner determined + order kicked off).
        var productResults = new List<ProductResult>();
        foreach (var handle in _productHandles)
        {
            productResults.Add(await handle.GetResultAsync<ProductResult>());
        }

        // 6. Mark event completed.
        await Workflow.ExecuteActivityAsync(
            (SportActivities a) => a.MarkEventStatusAsync(_eventId, "Completed"),
            DefaultActivityOptions);

        return new EventWorkflowResult(_eventId, productResults);
    }

    [WorkflowSignal]
    public async Task AthleteSubbingInAsync(AthleteSubbingInSignal signal)
    {
        if (_additionalAthletes.Any(a => a.AthleteExternalId == signal.AthleteExternalId))
        {
            return; // idempotent
        }

        var athlete = new AthleteStartingInput(
            signal.AthleteExternalId, signal.AthleteName, signal.ShirtNumber);
        _additionalAthletes.Add(athlete);
        await StartProductForAthleteAsync(athlete);
    }

    [WorkflowSignal]
    public Task EndEventAsync()
    {
        _eventEnded = true;
        return Task.CompletedTask;
    }

    private async Task StartProductForAthleteAsync(AthleteStartingInput athlete)
    {
        var productInput = new CreateProductInput(
            EventId: _eventId,
            ExternalFixtureId: _externalFixtureId,
            AthleteExternalId: athlete.AthleteExternalId,
            AthleteName: athlete.AthleteName,
            ShirtNumber: athlete.ShirtNumber,
            CorrelationId: _correlationId);

        var handle = await Workflow.StartChildWorkflowAsync(
            WorkflowNames.ProductWorkflow,
            new object[] { productInput },
            new ChildWorkflowOptions
            {
                Id = $"product-{_externalFixtureId}-{athlete.AthleteExternalId}",
                TaskQueue = TaskQueues.Catalog,
            });

        _productHandles.Add(handle);
    }
}
