using MassTransit;
using Microsoft.EntityFrameworkCore;
using Siniestros360.AdjustersService.Domain;
using Siniestros360.AdjustersService.Infrastructure;
using Siniestros360.Contracts.Events;

namespace Siniestros360.AdjustersService.Application;

// Estado operativo del ajustador derivado de la asignación y del avance del siniestro.
// El consumer outbox de MassTransit envuelve cada Consume: inbox, cambio y AdjusterStatusChanged en una transacción.
public sealed class AdjusterEventsConsumer(AdjustersDbContext db) :
    IConsumer<AdjusterAssigned>,
    IConsumer<ClaimReassigned>,
    IConsumer<AdjusterArrived>,
    IConsumer<AdjusterServiceStarted>,
    IConsumer<AdjusterServiceCompleted>,
    IConsumer<ClaimCancelled>
{
    public async Task Consume(ConsumeContext<AdjusterAssigned> context)
    {
        await Change(context, context.Message.AdjusterId, AdjusterStatus.Assigned, context.Message.ClaimId, context.Message.AssignedAt);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ClaimReassigned> context)
    {
        var e = context.Message;
        await Release(context, e.PreviousAdjusterId, e.ClaimId, e.ReassignedAt);
        await Change(context, e.NewAdjusterId, AdjusterStatus.Assigned, e.ClaimId, e.ReassignedAt);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<AdjusterArrived> context)
    {
        await Change(context, context.Message.AdjusterId, AdjusterStatus.Arrived, context.Message.ClaimId, context.Message.ArrivedAt);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<AdjusterServiceStarted> context)
    {
        await Change(context, context.Message.AdjusterId, AdjusterStatus.InService, context.Message.ClaimId, context.Message.StartedAt);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<AdjusterServiceCompleted> context)
    {
        await Release(context, context.Message.AdjusterId, context.Message.ClaimId, context.Message.CompletedAt);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ClaimCancelled> context)
    {
        var e = context.Message;
        var adjuster = await db.Adjusters.SingleOrDefaultAsync(x => x.ActiveClaimId == e.ClaimId, context.CancellationToken);
        if (adjuster is not null) await Release(context, adjuster.Id, e.ClaimId, e.CancelledAt);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    private async Task Change(ConsumeContext context, Guid adjusterId, AdjusterStatus status, Guid claimId, DateTimeOffset at)
    {
        var adjuster = await db.Adjusters.SingleOrDefaultAsync(x => x.Id == adjusterId, context.CancellationToken);
        if (adjuster is null) return;
        var previous = adjuster.ChangeStatus(status, claimId, at);
        if (previous is not null) await context.Publish(new AdjusterStatusChanged(adjusterId, previous.Value.ToString(), status.ToString(), at), context.CancellationToken);
    }

    // Sólo libera al ajustador si sigue asignado a ese siniestro; un evento tardío no pisa una asignación más nueva.
    private async Task Release(ConsumeContext context, Guid adjusterId, Guid claimId, DateTimeOffset at)
    {
        var adjuster = await db.Adjusters.SingleOrDefaultAsync(x => x.Id == adjusterId, context.CancellationToken);
        if (adjuster is null || adjuster.ActiveClaimId != claimId) return;
        var previous = adjuster.ChangeStatus(AdjusterStatus.Available, null, at);
        if (previous is not null) await context.Publish(new AdjusterStatusChanged(adjusterId, previous.Value.ToString(), AdjusterStatus.Available.ToString(), at), context.CancellationToken);
    }
}
