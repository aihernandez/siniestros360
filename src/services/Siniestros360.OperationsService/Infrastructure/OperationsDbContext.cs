using Microsoft.EntityFrameworkCore;
using Siniestros360.Messaging.Persistence;
using Siniestros360.OperationsService.Domain;

namespace Siniestros360.OperationsService.Infrastructure;

public sealed class OperationsDbContext(DbContextOptions<OperationsDbContext> options) : DbContext(options), IReliableMessagingDbContext
{
    public DbSet<ClaimReadModel> Claims => Set<ClaimReadModel>();
    public DbSet<AdjusterReadModel> Adjusters => Set<AdjusterReadModel>();
    public DbSet<OperationalAlert> Alerts => Set<OperationalAlert>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("operations");
        builder.AddReliableMessaging();
        builder.Entity<ClaimReadModel>().HasKey(x => x.ClaimId);
        builder.Entity<ClaimReadModel>().Ignore(x => x.IsFinished);
        builder.Entity<ClaimReadModel>().HasIndex(x => x.Status);
        builder.Entity<ClaimReadModel>().HasIndex(x => x.ReportedAt);
        builder.Entity<AdjusterReadModel>().HasKey(x => x.AdjusterId);
        builder.Entity<AdjusterReadModel>().Property(x => x.RowVersion).IsRowVersion();
        builder.Entity<OperationalAlert>().HasIndex(x => x.RaisedAt);
        builder.Entity<OperationalAlert>().HasIndex(x => x.AcknowledgedAt);
    }
}
