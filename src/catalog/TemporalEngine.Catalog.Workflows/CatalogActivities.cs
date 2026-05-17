using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using TemporalEngine.Catalog.Contracts;
using TemporalEngine.Catalog.Data;
using TemporalEngine.Catalog.Domain;

namespace TemporalEngine.Catalog.Workflows;

public class CatalogActivities
{
    private readonly CatalogDbContext _db;
    private readonly ILogger<CatalogActivities> _logger;

    public CatalogActivities(CatalogDbContext db, ILogger<CatalogActivities> logger)
    {
        _db = db;
        _logger = logger;
    }

    [Activity]
    public async Task<Guid> CreateProductAsync(CreateProductInput input)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;

        var existing = await _db.Products
            .FirstOrDefaultAsync(p => p.EventId == input.EventId && p.AthleteExternalId == input.AthleteExternalId, ct);
        if (existing is not null)
        {
            return existing.Id;
        }

        var product = new Product
        {
            Id = Guid.NewGuid(),
            EventId = input.EventId,
            AthleteExternalId = input.AthleteExternalId,
            AthleteName = input.AthleteName,
            ShirtNumber = input.ShirtNumber,
            Status = "Open",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created product {Id} for athlete {Athlete}", product.Id, input.AthleteName);
        return product.Id;
    }

    [Activity]
    public async Task RecordBidAsync(Guid productId, BidPlacedSignal signal)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;

        var dup = await _db.Bids.AnyAsync(b => b.ExternalBidId == signal.BidId, ct);
        if (dup)
        {
            return;
        }

        var bid = new Bid
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ExternalBidId = signal.BidId,
            BidderId = signal.BidderId,
            Amount = signal.Amount,
            Currency = signal.Currency,
            PlacedAt = signal.PlacedAt,
        };
        _db.Bids.Add(bid);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Recorded bid {BidId} on product {ProductId}: {Amount} {Currency}",
            signal.BidId, productId, signal.Amount, signal.Currency);
    }

    [Activity]
    public async Task CloseProductAsync(Guid productId, string? winningBidId, decimal winningAmount)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;
        var product = await _db.Products.FirstAsync(p => p.Id == productId, ct);
        product.Status = winningBidId is null ? "ClosedNoWinner" : "ClosedWithWinner";
        product.WinningBidId = winningBidId;
        product.WinningAmount = winningAmount;
        product.ClosedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Closed product {ProductId} with winner {WinnerBidId} amount {Amount}",
            productId, winningBidId ?? "(none)", winningAmount);
    }
}
