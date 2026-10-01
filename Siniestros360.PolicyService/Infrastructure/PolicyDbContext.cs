using Microsoft.EntityFrameworkCore;
using Siniestros360.Messaging.Persistence;
using Siniestros360.PolicyService.Domain;

namespace Siniestros360.PolicyService.Infrastructure;

public sealed class PolicyDbContext(DbContextOptions<PolicyDbContext> options) : DbContext(options), IReliableMessagingDbContext
{
    public DbSet<PolicyValidation> Validations => Set<PolicyValidation>();
    public DbSet<ExternalValidationAttempt> Attempts => Set<ExternalValidationAttempt>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("policy");
        builder.AddReliableMessaging();
        builder.Entity<PolicyValidation>().HasKey(x => x.ClaimId);
        builder.Entity<PolicyValidation>().HasIndex(x => x.PolicyNumber);
        builder.Entity<ExternalValidationAttempt>().HasIndex(x => new { x.ClaimId, x.OccurredAt });
    }
}
