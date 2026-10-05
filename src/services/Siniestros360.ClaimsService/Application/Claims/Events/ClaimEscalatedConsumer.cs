using MassTransit;
using Siniestros360.ClaimsService.Infrastructure;
using Siniestros360.Contracts.Events;

namespace Siniestros360.ClaimsService.Application.Claims.Events;

// Venció un SLA: el expediente queda marcado como escalado sin bloquear el servicio.
// El consumer outbox de MassTransit envuelve Consume: inbox (deduplicación), cambio y SaveChanges en una transacción.
public sealed class ClaimEscalatedConsumer(ClaimsDbContext db) : IConsumer<ClaimEscalated>
{
    public async Task Consume(ConsumeContext<ClaimEscalated> context)
    {
        var e = context.Message;
        (await db.GetForUpdateAsync(e.ClaimId, context.CancellationToken)).Escalate(e.Reason, e.EscalatedAt);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
