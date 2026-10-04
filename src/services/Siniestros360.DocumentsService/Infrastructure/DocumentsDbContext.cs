using Microsoft.EntityFrameworkCore;
using Siniestros360.DocumentsService.Domain;
using Siniestros360.Messaging.Persistence;

namespace Siniestros360.DocumentsService.Infrastructure;

public sealed class DocumentsDbContext(DbContextOptions<DocumentsDbContext> options) : DbContext(options), IReliableMessagingDbContext
{
    public DbSet<ClaimDocument> Documents => Set<ClaimDocument>();
    public DbSet<ClaimAccessProjection> ClaimAccess => Set<ClaimAccessProjection>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("documents");
        builder.AddReliableMessaging();
        builder.Entity<ClaimDocument>().HasIndex(x => new { x.ClaimId, x.UploadedAt });
        builder.Entity<ClaimAccessProjection>().HasKey(x => x.ClaimId);
        builder.Entity<ClaimAccessProjection>().HasIndex(x => x.InsuredId);
        builder.Entity<ClaimAccessProjection>().HasIndex(x => x.AdjusterId);
    }
}
