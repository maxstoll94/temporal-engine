using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Temporalio.Client;
using Temporalio.Testing;
using Temporalio.Worker;
using Temporalio.Workflows;
using TemporalEngine.Catalog.Contracts;
using TemporalEngine.Catalog.Data;
using TemporalEngine.Catalog.Workflows;
using TemporalEngine.Finance.Contracts;
using TemporalEngine.Shared.Contracts;

namespace TemporalEngine.Catalog.Tests;

// Stub of OrderWorkflow registered on the Finance task queue so that ProductWorkflow's
// cross-service child workflow call resolves without spinning up the real Finance worker.
[Workflow(WorkflowNames.OrderWorkflow)]
public class StubOrderWorkflow
{
    [WorkflowRun]
    public Task<OrderResult> RunAsync(CreateOrderInput input) =>
        Task.FromResult(new OrderResult(
            OrderId: Guid.NewGuid(),
            FinalStatus: "Shipped",
            OdooSalesOrderId: "stub-odoo",
            TrackingNumber: "stub-trk"));
}

public class ProductWorkflowTests
{
    [Fact]
    public async Task ProductWorkflow_Receives_Bids_Closes_OnEventEnded_Starts_Order()
    {
        // === Arrange ===
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();

        var dbOptions = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"catalog-test-{Guid.NewGuid():N}")
            .Options;
        await using var db = new CatalogDbContext(dbOptions);

        var activities = new CatalogActivities(db, NullLogger<CatalogActivities>.Instance);

        // Real worker for catalog queue.
        using var catalogWorker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions(TaskQueues.Catalog)
                .AddAllActivities(activities)
                .AddWorkflow<ProductWorkflow>());

        // Stub worker for finance queue — only knows OrderWorkflow stub.
        using var financeStubWorker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions(TaskQueues.Finance)
                .AddWorkflow<StubOrderWorkflow>());

        var input = new CreateProductInput(
            EventId: Guid.NewGuid(),
            ExternalFixtureId: "fix-test",
            AthleteExternalId: "athlete-1",
            AthleteName: "Test Athlete",
            ShirtNumber: 9,
            CorrelationId: "test-corr");

        // === Act ===
        // Run both workers in parallel; cancel when test workflow finishes.
        using var cts = new CancellationTokenSource();
        var catalogTask = Task.Run(() => catalogWorker.ExecuteAsync(cts.Token));
        var financeStubTask = Task.Run(() => financeStubWorker.ExecuteAsync(cts.Token));

        var handle = await env.Client.StartWorkflowAsync(
            WorkflowNames.ProductWorkflow,
            new object[] { input },
            new WorkflowOptions(id: "product-test", taskQueue: TaskQueues.Catalog));

        await handle.SignalAsync("BidPlaced", new object[] { new BidPlacedSignal(
            BidId: "bid-1", BidderId: "alice", Amount: 100m, Currency: "EUR",
            PlacedAt: DateTimeOffset.UtcNow) });

        await handle.SignalAsync("BidPlaced", new object[] { new BidPlacedSignal(
            BidId: "bid-2", BidderId: "bob",   Amount: 250m, Currency: "EUR",
            PlacedAt: DateTimeOffset.UtcNow) });

        await handle.SignalAsync("EventEnded", new object[] { new EventEndedSignal(DateTimeOffset.UtcNow) });

        var result = await handle.GetResultAsync<ProductResult>();

        cts.Cancel();
        try { await Task.WhenAll(catalogTask, financeStubTask); }
        catch (OperationCanceledException) { /* expected */ }

        // === Assert ===
        Assert.True(result.HadWinner);
        Assert.Equal("bid-2", result.WinnerBidId);
        Assert.Equal(250m, result.WinningAmount);
        Assert.NotNull(result.OrderId);

        var product = await db.Products.FirstAsync();
        Assert.Equal("ClosedWithWinner", product.Status);
        Assert.Equal("bid-2", product.WinningBidId);
        Assert.Equal(250m, product.WinningAmount);

        Assert.Equal(2, await db.Bids.CountAsync());
    }
}
