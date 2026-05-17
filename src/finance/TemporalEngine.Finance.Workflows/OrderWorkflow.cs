using Temporalio.Common;
using Temporalio.Workflows;
using TemporalEngine.Finance.Contracts;
using TemporalEngine.Shared.Contracts;

namespace TemporalEngine.Finance.Workflows;

internal static class SearchAttrs
{
    public static readonly SearchAttributeKey<string> CorrelationId =
        SearchAttributeKey.CreateKeyword(SearchAttributeNames.CorrelationId);
    public static readonly SearchAttributeKey<string> Stage =
        SearchAttributeKey.CreateKeyword(SearchAttributeNames.Stage);
}

[Workflow(WorkflowNames.OrderWorkflow)]
public class OrderWorkflow
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

    // Demo-friendly defaults; production would pull these from input/config.
    private static readonly TimeSpan PaymentDeadline = TimeSpan.FromMinutes(2);

    private Guid _orderId;
    private PaymentReceivedSignal? _payment;
    private ShipmentUpdatedSignal? _shipmentUpdate;

    [WorkflowRun]
    public async Task<OrderResult> RunAsync(CreateOrderInput input)
    {
        Workflow.UpsertTypedSearchAttributes(
            SearchAttrs.CorrelationId.ValueSet(input.CorrelationId),
            SearchAttrs.Stage.ValueSet("awaiting-payment"));

        // 1. Persist order
        _orderId = await Workflow.ExecuteActivityAsync(
            (FinanceActivities a) => a.CreateOrderAsync(input),
            DefaultActivityOptions);

        // 2. Wait for payment, with deadline.
        var paid = await Workflow.WaitConditionAsync(() => _payment is not null, PaymentDeadline);
        if (!paid)
        {
            await Workflow.ExecuteActivityAsync(
                (FinanceActivities a) => a.MarkOrderStatusAsync(_orderId, "PaymentTimedOut"),
                DefaultActivityOptions);
            return new OrderResult(_orderId, "PaymentTimedOut", null, null);
        }

        await Workflow.ExecuteActivityAsync(
            (FinanceActivities a) => a.RecordPaymentAsync(_orderId, _payment!),
            DefaultActivityOptions);

        // 3. Create Odoo sales order (mocked)
        var odooId = await Workflow.ExecuteActivityAsync(
            (FinanceActivities a) => a.CreateOdooSalesOrderAsync(_orderId),
            DefaultActivityOptions);

        // 4. Trigger shipment (mocked). In production this would await an external
        //    shipment-updated webhook signal; here we just call the activity directly.
        var tracking = await Workflow.ExecuteActivityAsync(
            (FinanceActivities a) => a.ShipOdooSalesOrderAsync(_orderId),
            DefaultActivityOptions);

        return new OrderResult(_orderId, "Shipped", odooId, tracking);
    }

    [WorkflowSignal]
    public Task PaymentReceivedAsync(PaymentReceivedSignal signal)
    {
        _payment ??= signal; // first one wins; idempotent on PaymentId is enforced in the activity
        return Task.CompletedTask;
    }

    [WorkflowSignal]
    public Task ShipmentUpdatedAsync(ShipmentUpdatedSignal signal)
    {
        _shipmentUpdate = signal;
        return Task.CompletedTask;
    }

    [WorkflowQuery]
    public string CurrentStatus() =>
        _shipmentUpdate is not null ? $"shipment:{_shipmentUpdate.Status}"
        : _payment is not null ? "paid"
        : "awaiting-payment";
}
