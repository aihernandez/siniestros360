using MassTransit;
using Siniestros360.ClaimsService.Infrastructure;
using Siniestros360.Contracts.Events;

namespace Siniestros360.ClaimsService.Application.Claims.Events;

// El proveedor respondió con un error que no se resuelve reintentando.
// El consumer outbox de MassTransit envuelve Consume: inbox (deduplicación), cambio y SaveChanges en una transacción.
public sealed class PolicyValidationFailedConsumer(ClaimsDbContext db) : IConsumer<PolicyValidationFailed>
{
    public async Task Consume(ConsumeContext<PolicyValidationFailed> context)
    {
        var e = context.Message;
        (await db.GetForUpdateAsync(e.ClaimId, context.CancellationToken)).SetCoverage("Failed", e.ErrorMessage, e.FailedAt);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
