using MassTransit;
using Siniestros360.ClaimsService.Infrastructure;
using Siniestros360.Contracts.Events;

namespace Siniestros360.ClaimsService.Application.Claims.Events;

// Dispatch reasignó el siniestro (SLA de llegada vencido o decisión de la torre).
// El consumer outbox de MassTransit envuelve Consume: inbox (deduplicación), cambio y SaveChanges en una transacción.
public sealed class ClaimReassignedConsumer(ClaimsDbContext db) : IConsumer<ClaimReassigned>
{
    public async Task Consume(ConsumeContext<ClaimReassigned> context)
    {
        var e = context.Message;
        (await db.GetForUpdateAsync(e.ClaimId, context.CancellationToken)).TryAssign(e.NewAdjusterId, e.ReassignedAt, isReassignment: true);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
