using MassTransit;
using Siniestros360.ClaimsService.Infrastructure;
using Siniestros360.Contracts.Events;

namespace Siniestros360.ClaimsService.Application.Claims.Events;

// El proveedor de pólizas no respondió tras los reintentos; la atención sigue.
// El consumer outbox de MassTransit envuelve Consume: inbox (deduplicación), cambio y SaveChanges en una transacción.
public sealed class PolicyValidationUnavailableConsumer(ClaimsDbContext db) : IConsumer<PolicyValidationUnavailable>
{
    public async Task Consume(ConsumeContext<PolicyValidationUnavailable> context)
    {
        var e = context.Message;
        (await db.GetForUpdateAsync(e.ClaimId, context.CancellationToken)).SetCoverage("Unavailable", e.Reason, e.OccurredAt);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
