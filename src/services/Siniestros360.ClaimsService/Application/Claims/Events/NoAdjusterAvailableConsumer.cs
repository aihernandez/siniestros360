using MassTransit;
using Siniestros360.ClaimsService.Infrastructure;
using Siniestros360.Contracts.Events;

namespace Siniestros360.ClaimsService.Application.Claims.Events;

// Sin ajustador disponible: queda en la bitácora del expediente.
// El consumer outbox de MassTransit envuelve Consume: inbox (deduplicación), cambio y SaveChanges en una transacción.
public sealed class NoAdjusterAvailableConsumer(ClaimsDbContext db) : IConsumer<NoAdjusterAvailable>
{
    public async Task Consume(ConsumeContext<NoAdjusterAvailable> context)
    {
        var e = context.Message;
        (await db.GetForUpdateAsync(e.ClaimId, context.CancellationToken)).RecordDispatchIssue(e.Reason, e.OccurredAt);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
