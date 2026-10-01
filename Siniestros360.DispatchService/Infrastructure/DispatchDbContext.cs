using Microsoft.EntityFrameworkCore;
using Siniestros360.DispatchService.Domain;
using Siniestros360.Messaging.Persistence;

namespace Siniestros360.DispatchService.Infrastructure;

public sealed class DispatchDbContext(DbContextOptions<DispatchDbContext> options) : DbContext(options), IReliableMessagingDbContext
{
    public DbSet<AssignmentState> AssignmentSagas => Set<AssignmentState>();
    public DbSet<AdjusterDispatchProjection> Adjusters => Set<AdjusterDispatchProjection>();
    public DbSet<DispatchAttempt> Attempts => Set<DispatchAttempt>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("dispatch");
        builder.AddReliableMessaging();

        // La saga conserva la tabla y columnas previas (ClaimId, Status) para que la migración no pierda datos.
        var saga = builder.Entity<AssignmentState>();
        saga.ToTable("AssignmentSagas");
        saga.HasKey(x => x.CorrelationId);
        saga.Property(x => x.CorrelationId).HasColumnName("ClaimId").ValueGeneratedNever();
        saga.Property(x => x.CurrentState).HasColumnName("Status").HasMaxLength(32);
        saga.Property(x => x.RowVersion).IsRowVersion();
        saga.HasIndex(x => x.CurrentState);

        builder.Entity<AdjusterDispatchProjection>().HasKey(x => x.AdjusterId);
        builder.Entity<AdjusterDispatchProjection>().HasIndex(x => x.IsAvailable);
        builder.Entity<AdjusterDispatchProjection>().Property(x => x.Version).IsConcurrencyToken();
        builder.Entity<DispatchAttempt>().HasIndex(x => new { x.ClaimId, x.OccurredAt });
    }
}
