using Microsoft.EntityFrameworkCore;
using Siniestros360.LocationService.Domain;
using Siniestros360.Messaging.Persistence;

namespace Siniestros360.LocationService.Infrastructure;

public sealed class LocationDbContext(DbContextOptions<LocationDbContext> options) : DbContext(options), IReliableMessagingDbContext
{
    public DbSet<LatestAdjusterLocation> LatestLocations => Set<LatestAdjusterLocation>();
    public DbSet<LocationHistory> LocationHistory => Set<LocationHistory>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("locations");
        builder.AddReliableMessaging();
        builder.Entity<LatestAdjusterLocation>().HasKey(x => x.AdjusterId);
        builder.Entity<LatestAdjusterLocation>().HasIndex(x => x.Position).HasMethod("gist");
        builder.Entity<LocationHistory>().HasIndex(x => new { x.AdjusterId, x.CapturedAt });
        builder.Entity<LocationHistory>().HasIndex(x => x.Position).HasMethod("gist");
    }
}
