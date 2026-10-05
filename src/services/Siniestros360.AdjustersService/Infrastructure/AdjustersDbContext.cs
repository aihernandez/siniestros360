using Microsoft.EntityFrameworkCore;
using Siniestros360.AdjustersService.Domain;
using Siniestros360.Messaging.Persistence;

namespace Siniestros360.AdjustersService.Infrastructure;

public sealed class AdjustersDbContext(DbContextOptions<AdjustersDbContext> options) : DbContext(options), IReliableMessagingDbContext
{
    public DbSet<Adjuster> Adjusters => Set<Adjuster>();
    public DbSet<ClaimRecord> ClaimRecords => Set<ClaimRecord>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("adjusters");
        builder.AddReliableMessaging();
        builder.Entity<Adjuster>().HasIndex(x => x.IsAvailable);
        builder.Entity<Adjuster>().HasIndex(x => x.ActiveClaimId);
        builder.Entity<Adjuster>().Property(x => x.Status).HasConversion<string>();
        builder.Entity<Adjuster>().Property(x => x.Version).IsConcurrencyToken();
        builder.Entity<ClaimRecord>().HasKey(x => x.ClaimId);
        builder.Entity<ClaimRecord>().Property(x => x.ClaimId).ValueGeneratedNever();
        builder.Entity<ClaimRecord>().Property(x => x.RowVersion).IsRowVersion();
    }
}
