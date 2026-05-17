using Temporalio.Common;
using Temporalio.Workflows;
using TemporalEngine.Catalog.Contracts;
using TemporalEngine.Finance.Contracts;
using TemporalEngine.Shared.Contracts;

namespace TemporalEngine.Catalog.Workflows;

internal static class SearchAttrs
{
    public static readonly SearchAttributeKey<string> CorrelationId =
        SearchAttributeKey.CreateKeyword(SearchAttributeNames.CorrelationId);
    public static readonly SearchAttributeKey<string> Stage =
        SearchAttributeKey.CreateKeyword(SearchAttributeNames.Stage);
}

[Workflow(WorkflowNames.ProductWorkflow)]
public class ProductWorkflow
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

    private readonly List<BidPlacedSignal> _bids = new();
    private readonly HashSet<string> _seenBidIds = new();
    private Guid _productId;
    private CreateProductInput _input = default!;
    private bool _eventEnded;

    [WorkflowRun]
    public async Task<ProductResult> RunAsync(CreateProductInput input)
    {
        _input = input;

        Workflow.UpsertTypedSearchAttributes(
            SearchAttrs.CorrelationId.ValueSet(input.CorrelationId),
            SearchAttrs.Stage.ValueSet("auction-open"));

        // 1. Persist product
        _productId = await Workflow.ExecuteActivityAsync(
            (CatalogActivities a) => a.CreateProductAsync(input),
            DefaultActivityOptions);

        // 2. Wait for the event-ended signal (sent by parent EventWorkflow).
        await Workflow.WaitConditionAsync(() => _eventEnded);

        // 3. Determine winner from received bids (highest amount wins; first one to that amount in case of ties).
        BidPlacedSignal? winner = null;
        foreach (var bid in _bids)
        {
            if (winner is null || bid.Amount > winner.Amount)
            {
                winner = bid;
            }
        }

        // 4. Mark product closed
        var winningBidId = winner?.BidId;
        var winningAmount = winner?.Amount ?? 0m;
        await Workflow.ExecuteActivityAsync(
            (CatalogActivities a) => a.CloseProductAsync(_productId, winningBidId, winningAmount),
            DefaultActivityOptions);

        // 5. If there's a winner, start the Finance OrderWorkflow as a child on the finance task queue.
        Guid? orderId = null;
        if (winner is not null)
        {
            var orderInput = new CreateOrderInput(
                ProductId: _productId,
                ExternalFixtureId: input.ExternalFixtureId,
                AthleteExternalId: input.AthleteExternalId,
                ProductName: $"Shirt #{input.ShirtNumber} - {input.AthleteName}",
                WinnerBidderId: winner.BidderId,
                WinningBidId: winner.BidId,
                Amount: winner.Amount,
                Currency: winner.Currency,
                CorrelationId: input.CorrelationId);

            var orderResult = await Workflow.ExecuteChildWorkflowAsync<OrderResult>(
                WorkflowNames.OrderWorkflow,
                new object[] { orderInput },
                new ChildWorkflowOptions
                {
                    Id = $"order-{input.ExternalFixtureId}-{input.AthleteExternalId}",
                    TaskQueue = TaskQueues.Finance,
                });
            orderId = orderResult.OrderId;
        }

        return new ProductResult(
            ProductId: _productId,
            HadWinner: winner is not null,
            WinnerBidId: winner?.BidId,
            WinningAmount: winner?.Amount ?? 0m,
            OrderId: orderId);
    }

    [WorkflowSignal]
    public async Task BidPlacedAsync(BidPlacedSignal signal)
    {
        if (_eventEnded)
        {
            // Late bids are rejected; the workflow has already moved on.
            return;
        }
        if (!_seenBidIds.Add(signal.BidId))
        {
            return; // idempotent
        }

        _bids.Add(signal);

        // Best-effort persistence of the bid (audit trail). Workflow continues even if it fails after retries — Temporal will surface.
        await Workflow.ExecuteActivityAsync(
            (CatalogActivities a) => a.RecordBidAsync(_productId, signal),
            DefaultActivityOptions);
    }

    [WorkflowSignal]
    public Task EventEndedAsync(EventEndedSignal signal)
    {
        _eventEnded = true;
        return Task.CompletedTask;
    }

    [WorkflowQuery]
    public int CurrentBidCount() => _bids.Count;

    [WorkflowQuery]
    public decimal CurrentTopBid() => _bids.Count == 0 ? 0m : _bids.Max(b => b.Amount);
}
