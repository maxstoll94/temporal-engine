using Microsoft.EntityFrameworkCore;
using TemporalEngine.Sport.Domain;

namespace TemporalEngine.Sport.Data;

public class SportDbContext : DbContext
{
    public SportDbContext(DbContextOptions<SportDbContext> options) : base(options) { }

    public DbSet<Fixture> Fixtures => Set<Fixture>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Fixture>(b =>
        {
            b.HasKey(f => f.Id);
            b.HasIndex(f => f.ExternalId).IsUnique();
            b.Property(f => f.Name).HasMaxLength(200);
            b.Property(f => f.ExternalId).HasMaxLength(100);
            b.Property(f => f.Status).HasMaxLength(40);
        });
    }
}
