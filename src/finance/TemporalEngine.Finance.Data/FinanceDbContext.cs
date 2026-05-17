using Microsoft.EntityFrameworkCore;
using TemporalEngine.Finance.Domain;

namespace TemporalEngine.Finance.Data;

public class FinanceDbContext : DbContext
{
    public FinanceDbContext(DbContextOptions<FinanceDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Order>(b =>
        {
            b.HasKey(o => o.Id);
            b.HasIndex(o => o.ProductId).IsUnique();
            b.Property(o => o.ProductName).HasMaxLength(200);
            b.Property(o => o.WinnerBidderId).HasMaxLength(100);
            b.Property(o => o.WinningBidId).HasMaxLength(100);
            b.Property(o => o.Status).HasMaxLength(40);
            b.Property(o => o.Currency).HasMaxLength(8);
            b.Property(o => o.OdooSalesOrderId).HasMaxLength(100);
            b.Property(o => o.TrackingNumber).HasMaxLength(100);
            b.Property(o => o.Amount).HasPrecision(18, 2);
        });

        mb.Entity<Payment>(b =>
        {
            b.HasKey(p => p.Id);
            b.HasIndex(p => p.OrderId);
            b.HasIndex(p => p.ExternalPaymentId).IsUnique();
            b.Property(p => p.ExternalPaymentId).HasMaxLength(100);
            b.Property(p => p.Currency).HasMaxLength(8);
            b.Property(p => p.Amount).HasPrecision(18, 2);
        });
    }
}
