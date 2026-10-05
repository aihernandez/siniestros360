using MassTransit;
using Siniestros360.ClaimsService.Infrastructure;
using Siniestros360.Contracts.Events;

namespace Siniestros360.ClaimsService.Application.Claims.Events;

// Dispatch asignó un ajustador. Se ignora si el ajustador ya llegó o el siniestro terminó (TryAssign).
// El consumer outbox de MassTransit envuelve Consume: inbox (deduplicación), cambio y SaveChanges en una transacción.
public sealed class AdjusterAssignedConsumer(ClaimsDbContext db) : IConsumer<AdjusterAssigned>
{
    public async Task Consume(ConsumeContext<AdjusterAssigned> context)
    {
        var e = context.Message;
        var claim = await db.GetForUpdateAsync(e.ClaimId, context.CancellationToken);
        if (claim.TryAssign(e.AdjusterId, e.AssignedAt, isReassignment: false))
            ClaimsTelemetry.AssignmentDuration.Record((e.AssignedAt - claim.ReportedAt).TotalSeconds);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
