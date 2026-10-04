using MassTransit;
using Microsoft.EntityFrameworkCore;
using Siniestros360.Contracts.Events;
using Siniestros360.DocumentsService.Domain;
using Siniestros360.DocumentsService.Infrastructure;

namespace Siniestros360.DocumentsService.Application;

// Proyección local de quién puede ver los documentos de cada siniestro (asegurado dueño y ajustador asignado).
public sealed class ClaimAccessConsumer(DocumentsDbContext db) :
    IConsumer<ClaimReported>,
    IConsumer<AdjusterAssigned>,
    IConsumer<ClaimReassigned>
{
    public async Task Consume(ConsumeContext<ClaimReported> context)
    {
        var e = context.Message;
        if (!await db.ClaimAccess.AnyAsync(x => x.ClaimId == e.ClaimId, context.CancellationToken))
            db.ClaimAccess.Add(new ClaimAccessProjection { ClaimId = e.ClaimId, InsuredId = e.InsuredId });
        await db.SaveChangesAsync(context.CancellationToken);
    }

    public Task Consume(ConsumeContext<AdjusterAssigned> context)
        => SetAdjuster(context.Message.ClaimId, context.Message.AdjusterId, context.CancellationToken);

    public Task Consume(ConsumeContext<ClaimReassigned> context)
        => SetAdjuster(context.Message.ClaimId, context.Message.NewAdjusterId, context.CancellationToken);

    private async Task SetAdjuster(Guid claimId, Guid adjusterId, CancellationToken ct)
    {
        var access = await db.ClaimAccess.SingleOrDefaultAsync(x => x.ClaimId == claimId, ct);
        if (access is not null) access.AdjusterId = adjusterId;
        await db.SaveChangesAsync(ct);
    }
}
