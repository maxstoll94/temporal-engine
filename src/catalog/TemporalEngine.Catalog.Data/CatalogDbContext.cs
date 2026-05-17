using Microsoft.EntityFrameworkCore;
using TemporalEngine.Catalog.Domain;

namespace TemporalEngine.Catalog.Data;

public class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Bid> Bids => Set<Bid>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Product>(b =>
        {
            b.HasKey(p => p.Id);
            b.HasIndex(p => p.EventId);
            b.Property(p => p.AthleteExternalId).HasMaxLength(100);
            b.Property(p => p.AthleteName).HasMaxLength(200);
            b.Property(p => p.Status).HasMaxLength(40);
            b.Property(p => p.WinningBidId).HasMaxLength(100);
            b.Property(p => p.WinningAmount).HasPrecision(18, 2);
        });

        mb.Entity<Bid>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.ProductId);
            b.HasIndex(x => x.ExternalBidId).IsUnique();
            b.Property(x => x.BidderId).HasMaxLength(100);
            b.Property(x => x.ExternalBidId).HasMaxLength(100);
            b.Property(x => x.Currency).HasMaxLength(8);
            b.Property(x => x.Amount).HasPrecision(18, 2);
        });
    }
}
