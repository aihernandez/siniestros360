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
        var e = context.Message;
        if (await TryAssign(e.ClaimId, e.AdjusterId, e.AssignedAt, context.CancellationToken))
            await Change(context, e.AdjusterId, AdjusterStatus.Assigned, e.ClaimId, e.AssignedAt);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ClaimReassigned> context)
    {
        var e = context.Message;
        await Release(context, e.PreviousAdjusterId, e.ClaimId, e.ReassignedAt);
        if (await TryAssign(e.ClaimId, e.NewAdjusterId, e.ReassignedAt, context.CancellationToken))
            await Change(context, e.NewAdjusterId, AdjusterStatus.Assigned, e.ClaimId, e.ReassignedAt);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<AdjusterArrived> context)
    {
        await Advance(context, context.Message.AdjusterId, AdjusterStatus.Arrived, context.Message.ClaimId, context.Message.ArrivedAt);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<AdjusterServiceStarted> context)
    {
        await Advance(context, context.Message.AdjusterId, AdjusterStatus.InService, context.Message.ClaimId, context.Message.StartedAt);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<AdjusterServiceCompleted> context)
    {
        await Finish(context.Message.ClaimId, context.Message.CompletedAt, context.CancellationToken);
        await Release(context, context.Message.AdjusterId, context.Message.ClaimId, context.Message.CompletedAt);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ClaimCancelled> context)
    {
        var e = context.Message;
        await Finish(e.ClaimId, e.CancelledAt, context.CancellationToken);
        var adjuster = await db.Adjusters.SingleOrDefaultAsync(x => x.ActiveClaimId == e.ClaimId, context.CancellationToken);
        if (adjuster is not null) await Release(context, adjuster.Id, e.ClaimId, e.CancelledAt);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    // Registra la asignación en la fila del siniestro; false si el siniestro ya terminó.
    private async Task<bool> TryAssign(Guid claimId, Guid adjusterId, DateTimeOffset at, CancellationToken ct)
    {
        var record = await Record(claimId, ct);
        record.UpdatedAt = at > record.UpdatedAt ? at : record.UpdatedAt.AddTicks(1);
        if (record.FinishedAt is not null) return false;
        record.AdjusterId = adjusterId;
        return true;
    }

    private async Task Finish(Guid claimId, DateTimeOffset at, CancellationToken ct)
    {
        var record = await Record(claimId, ct);
        record.FinishedAt ??= at;
        record.UpdatedAt = at > record.UpdatedAt ? at : record.UpdatedAt.AddTicks(1);
    }

    // UpdatedAt siempre cambia: así cada escritura es un UPDATE real que choca por RowVersion con otra concurrente.
    private async Task<ClaimRecord> Record(Guid claimId, CancellationToken ct)
    {
        var record = await db.ClaimRecords.SingleOrDefaultAsync(x => x.ClaimId == claimId, ct);
        if (record is not null) return record;
        record = new ClaimRecord { ClaimId = claimId };
        db.ClaimRecords.Add(record);
        return record;
    }

    private async Task Change(ConsumeContext context, Guid adjusterId, AdjusterStatus status, Guid claimId, DateTimeOffset at)
    {
        var adjuster = await db.Adjusters.SingleOrDefaultAsync(x => x.Id == adjusterId, context.CancellationToken);
        if (adjuster is null) return;
        var previous = adjuster.ChangeStatus(status, claimId, at);
        if (previous is not null) await context.Publish(new AdjusterStatusChanged(adjusterId, previous.Value.ToString(), status.ToString(), at), context.CancellationToken);
    }

    // Llegada e inicio sólo avanzan al ajustador dentro de su siniestro activo. Los eventos pueden llegar fuera de orden:
    // un AdjusterServiceStarted procesado después del cierre lo dejaba InService, atado a un siniestro ya cerrado.
    private async Task Advance(ConsumeContext context, Guid adjusterId, AdjusterStatus status, Guid claimId, DateTimeOffset at)
    {
        var adjuster = await db.Adjusters.SingleOrDefaultAsync(x => x.Id == adjusterId, context.CancellationToken);
        if (adjuster is null || adjuster.ActiveClaimId != claimId || Stage(adjuster.Status) >= Stage(status)) return;
        var previous = adjuster.ChangeStatus(status, claimId, at);
        if (previous is not null) await context.Publish(new AdjusterStatusChanged(adjusterId, previous.Value.ToString(), status.ToString(), at), context.CancellationToken);
    }

    private static int Stage(AdjusterStatus status) => status switch
    {
        AdjusterStatus.Assigned or AdjusterStatus.EnRoute => 1,
        AdjusterStatus.Arrived => 2,
        AdjusterStatus.InService => 3,
        _ => 0,
    };

    // Sólo libera al ajustador si sigue asignado a ese siniestro; un evento tardío no pisa una asignación más nueva.
    private async Task Release(ConsumeContext context, Guid adjusterId, Guid claimId, DateTimeOffset at)
    {
        var adjuster = await db.Adjusters.SingleOrDefaultAsync(x => x.Id == adjusterId, context.CancellationToken);
        if (adjuster is null || adjuster.ActiveClaimId != claimId) return;
        var previous = adjuster.ChangeStatus(AdjusterStatus.Available, null, at);
        if (previous is not null) await context.Publish(new AdjusterStatusChanged(adjusterId, previous.Value.ToString(), AdjusterStatus.Available.ToString(), at), context.CancellationToken);
    }
}
