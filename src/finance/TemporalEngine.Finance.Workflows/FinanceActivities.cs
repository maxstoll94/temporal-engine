using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using TemporalEngine.Finance.Contracts;
using TemporalEngine.Finance.Data;
using TemporalEngine.Finance.Domain;

namespace TemporalEngine.Finance.Workflows;

public class FinanceActivities
{
    private readonly FinanceDbContext _db;
    private readonly IOdooClient _odoo;
    private readonly ILogger<FinanceActivities> _logger;

    public FinanceActivities(FinanceDbContext db, IOdooClient odoo, ILogger<FinanceActivities> logger)
    {
        _db = db;
        _odoo = odoo;
        _logger = logger;
    }

    [Activity]
    public async Task<Guid> CreateOrderAsync(CreateOrderInput input)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;

        var existing = await _db.Orders.FirstOrDefaultAsync(o => o.ProductId == input.ProductId, ct);
        if (existing is not null)
        {
            return existing.Id;
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            ProductId = input.ProductId,
            ProductName = input.ProductName,
            WinnerBidderId = input.WinnerBidderId,
            WinningBidId = input.WinningBidId,
            Amount = input.Amount,
            Currency = input.Currency,
            Status = "AwaitingPayment",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created order {OrderId} for product {ProductId}", order.Id, input.ProductId);
        return order.Id;
    }

    [Activity]
    public async Task RecordPaymentAsync(Guid orderId, PaymentReceivedSignal signal)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;

        var dup = await _db.Payments.AnyAsync(p => p.ExternalPaymentId == signal.PaymentId, ct);
        if (dup)
        {
            return;
        }

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ExternalPaymentId = signal.PaymentId,
            Amount = signal.Amount,
            Currency = signal.Currency,
            ReceivedAt = signal.ReceivedAt,
        };
        _db.Payments.Add(payment);

        var order = await _db.Orders.FirstAsync(o => o.Id == orderId, ct);
        order.Status = "Paid";
        order.PaidAt = signal.ReceivedAt;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Recorded payment {PaymentId} on order {OrderId}", signal.PaymentId, orderId);
    }

    [Activity]
    public async Task<string> CreateOdooSalesOrderAsync(Guid orderId)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;
        var order = await _db.Orders.FirstAsync(o => o.Id == orderId, ct);

        var odooId = await _odoo.CreateSalesOrderAsync(
            order.ProductName, order.WinnerBidderId, order.Amount, order.Currency, ct);

        order.OdooSalesOrderId = odooId;
        order.Status = "SalesOrderCreated";
        await _db.SaveChangesAsync(ct);

        return odooId;
    }

    [Activity]
    public async Task<string> ShipOdooSalesOrderAsync(Guid orderId)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;
        var order = await _db.Orders.FirstAsync(o => o.Id == orderId, ct);

        if (string.IsNullOrEmpty(order.OdooSalesOrderId))
        {
            throw new InvalidOperationException($"Order {orderId} has no Odoo sales order id; cannot ship.");
        }

        var tracking = await _odoo.ShipSalesOrderAsync(order.OdooSalesOrderId, ct);

        order.TrackingNumber = tracking;
        order.Status = "Shipped";
        order.ShippedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return tracking;
    }

    [Activity]
    public async Task MarkOrderStatusAsync(Guid orderId, string status)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;
        var order = await _db.Orders.FirstAsync(o => o.Id == orderId, ct);
        order.Status = status;
        await _db.SaveChangesAsync(ct);
    }
}
