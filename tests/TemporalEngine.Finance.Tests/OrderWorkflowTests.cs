using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Temporalio.Client;
using Temporalio.Testing;
using Temporalio.Worker;
using TemporalEngine.Finance.Contracts;
using TemporalEngine.Finance.Data;
using TemporalEngine.Finance.Workflows;
using TemporalEngine.Shared.Contracts;

namespace TemporalEngine.Finance.Tests;

public class OrderWorkflowTests
{
    [Fact]
    public async Task OrderWorkflow_Receives_Payment_Then_Ships()
    {
        // === Arrange ===
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();

        var dbOptions = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"finance-test-{Guid.NewGuid():N}")
            .Options;
        await using var db = new FinanceDbContext(dbOptions);

        var odoo = new FakeOdooClient(NullLogger<FakeOdooClient>.Instance);
        var activities = new FinanceActivities(db, odoo, NullLogger<FinanceActivities>.Instance);

        using var worker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions(TaskQueues.Finance)
                .AddAllActivities(activities)
                .AddWorkflow<OrderWorkflow>());

        var input = new CreateOrderInput(
            ProductId: Guid.NewGuid(),
            ExternalFixtureId: "fix-test",
            AthleteExternalId: "athlete-test",
            ProductName: "Test Shirt",
            WinnerBidderId: "alice",
            WinningBidId: "bid-1",
            Amount: 250m,
            Currency: "EUR",
            CorrelationId: "test-corr");

        // === Act ===
        var result = await worker.ExecuteAsync(async () =>
        {
            var handle = await env.Client.StartWorkflowAsync<OrderWorkflow, OrderResult>(
                wf => wf.RunAsync(input),
                new WorkflowOptions(id: "order-test", taskQueue: TaskQueues.Finance));

            // Send payment signal before deadline.
            await handle.SignalAsync(wf => wf.PaymentReceivedAsync(new PaymentReceivedSignal(
                PaymentId: "pay-1",
                Amount: 250m,
                Currency: "EUR",
                ReceivedAt: DateTimeOffset.UtcNow)));

            return await handle.GetResultAsync();
        });

        // === Assert ===
        Assert.Equal("Shipped", result.FinalStatus);
        Assert.NotNull(result.OdooSalesOrderId);
        Assert.NotNull(result.TrackingNumber);

        var stored = await db.Orders.FirstAsync();
        Assert.Equal("Shipped", stored.Status);
        Assert.NotNull(stored.OdooSalesOrderId);
        Assert.NotNull(stored.PaidAt);

        Assert.Single(await db.Payments.ToListAsync());
    }

    [Fact]
    public async Task OrderWorkflow_TimesOut_When_No_Payment_Received()
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();

        var dbOptions = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"finance-test-{Guid.NewGuid():N}")
            .Options;
        await using var db = new FinanceDbContext(dbOptions);

        var odoo = new FakeOdooClient(NullLogger<FakeOdooClient>.Instance);
        var activities = new FinanceActivities(db, odoo, NullLogger<FinanceActivities>.Instance);

        using var worker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions(TaskQueues.Finance)
                .AddAllActivities(activities)
                .AddWorkflow<OrderWorkflow>());

        var input = new CreateOrderInput(
            ProductId: Guid.NewGuid(),
            ExternalFixtureId: "fix-test",
            AthleteExternalId: "athlete-test",
            ProductName: "Test Shirt",
            WinnerBidderId: "alice",
            WinningBidId: "bid-1",
            Amount: 250m,
            Currency: "EUR",
            CorrelationId: "test-corr");

        var result = await worker.ExecuteAsync(async () =>
        {
            // Time skipping makes the 2-minute payment-deadline timer fire instantly.
            var handle = await env.Client.StartWorkflowAsync<OrderWorkflow, OrderResult>(
                wf => wf.RunAsync(input),
                new WorkflowOptions(id: "order-test-timeout", taskQueue: TaskQueues.Finance));
            return await handle.GetResultAsync();
        });

        Assert.Equal("PaymentTimedOut", result.FinalStatus);
        Assert.Null(result.OdooSalesOrderId);

        var stored = await db.Orders.FirstAsync();
        Assert.Equal("PaymentTimedOut", stored.Status);
    }
}
