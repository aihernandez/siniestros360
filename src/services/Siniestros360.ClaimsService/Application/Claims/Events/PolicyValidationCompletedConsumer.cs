using MassTransit;
using Siniestros360.ClaimsService.Infrastructure;
using Siniestros360.Contracts.Events;

namespace Siniestros360.ClaimsService.Application.Claims.Events;

// Policy validó la póliza: vigente o rechazada.
// El consumer outbox de MassTransit envuelve Consume: inbox (deduplicación), cambio y SaveChanges en una transacción.
public sealed class PolicyValidationCompletedConsumer(ClaimsDbContext db) : IConsumer<PolicyValidationCompleted>
{
    public async Task Consume(ConsumeContext<PolicyValidationCompleted> context)
    {
        var e = context.Message;
        (await db.GetForUpdateAsync(e.ClaimId, context.CancellationToken)).SetCoverage(e.CoverageStatus, e.Reason, e.ValidatedAt);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
