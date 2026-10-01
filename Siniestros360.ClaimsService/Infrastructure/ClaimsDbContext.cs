using Microsoft.EntityFrameworkCore;
using Siniestros360.ClaimsService.Domain;
using Siniestros360.Messaging.Persistence;

namespace Siniestros360.ClaimsService.Infrastructure;

public sealed class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : DbContext(options), IReliableMessagingDbContext
{
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimTimelineEntry> Timeline => Set<ClaimTimelineEntry>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("claims");
        builder.AddReliableMessaging();
        builder.Entity<Claim>().HasIndex(x => x.Folio).IsUnique();
        builder.Entity<Claim>().HasIndex(x => x.Status);
        builder.Entity<Claim>().HasIndex(x => x.InsuredId);
        builder.Entity<Claim>().HasIndex(x => x.AssignedAdjusterId);
        builder.Entity<Claim>().HasIndex(x => x.ReportedAt);
        builder.Entity<Claim>().Property(x => x.Status).HasConversion<string>();
        builder.Entity<Claim>().Property(x => x.Version).IsConcurrencyToken();
        builder.Entity<Claim>().HasMany(x => x.Timeline).WithOne().HasForeignKey(x => x.ClaimId);
        // El Id de la bitácora lo asigna el dominio; sin esto EF trata una entrada nueva agregada a un siniestro existente como UPDATE.
        builder.Entity<ClaimTimelineEntry>().Property(x => x.Id).ValueGeneratedNever();
    }
}
