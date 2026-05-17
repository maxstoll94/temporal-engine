using Microsoft.EntityFrameworkCore;
using TemporalEngine.Sport.Domain;

namespace TemporalEngine.Sport.Data;

public class SportDbContext : DbContext
{
    public SportDbContext(DbContextOptions<SportDbContext> options) : base(options) { }

    public DbSet<Fixture> Fixtures => Set<Fixture>();
    public DbSet<Event> Events => Set<Event>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Fixture>(b =>
        {
            b.HasKey(f => f.Id);
            b.HasIndex(f => f.ExternalId).IsUnique();
            b.Property(f => f.Name).HasMaxLength(200);
            b.Property(f => f.ExternalId).HasMaxLength(100);
        });

        mb.Entity<Event>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => e.FixtureId);
            b.Property(e => e.Name).HasMaxLength(200);
            b.Property(e => e.Status).HasMaxLength(40);
        });
    }
}
