using Microsoft.EntityFrameworkCore;
using Siniestros360.Messaging.Persistence;
using Siniestros360.SlaService.Domain;

namespace Siniestros360.SlaService.Infrastructure;

public sealed class SlaDbContext(DbContextOptions<SlaDbContext> options) : DbContext(options), IReliableMessagingDbContext
{
    public DbSet<SlaTracking> Trackings => Set<SlaTracking>();
    public DbSet<SlaAlert> Alerts => Set<SlaAlert>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("sla");
        builder.AddReliableMessaging();
        builder.Entity<SlaTracking>().HasKey(x => x.ClaimId);
        builder.Entity<SlaTracking>().HasIndex(x => x.Status);
        builder.Entity<SlaTracking>().HasIndex(x => x.AdjusterId);
        builder.Entity<SlaTracking>().Property(x => x.Status).HasConversion<string>();
        builder.Entity<SlaTracking>().Property(x => x.Version).IsConcurrencyToken();
        builder.Entity<SlaAlert>().HasIndex(x => new { x.ClaimId, x.AlertType, x.StageStartedAt }).IsUnique();
    }
}
