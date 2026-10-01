using MassTransit;
using Microsoft.EntityFrameworkCore;
using Siniestros360.Contracts.Events;
using Siniestros360.SlaService.Domain;
using Siniestros360.SlaService.Infrastructure;

namespace Siniestros360.SlaService.Application;

// Lleva el seguimiento por etapa de cada siniestro. El consumer outbox de MassTransit envuelve cada Consume:
// inbox, cambio de etapa y SlaStageStarted en una transacción.
public sealed class SlaEventsConsumer(SlaDbContext db, SlaPolicy policy) :
    IConsumer<ClaimReported>,
    IConsumer<AdjusterAssigned>,
    IConsumer<ClaimReassigned>,
    IConsumer<AdjusterArrived>,
    IConsumer<AdjusterServiceStarted>,
    IConsumer<AdjusterServiceCompleted>,
    IConsumer<ClaimClosed>,
    IConsumer<ClaimCancelled>,
    IConsumer<AdjusterGpsStaleDetected>
{
    public Task Consume(ConsumeContext<ClaimReported> context)
        => Advance(context, context.Message.ClaimId, SlaTrackingStatus.WaitingAssignment, context.Message.ReportedAt, null);

    public Task Consume(ConsumeContext<AdjusterAssigned> context)
        => Advance(context, context.Message.ClaimId, SlaTrackingStatus.WaitingArrival, context.Message.AssignedAt, context.Message.AdjusterId);

    // Una reasignación reinicia el plazo de llegada con el nuevo ajustador, salvo que ya haya llegado alguien.
    public async Task Consume(ConsumeContext<ClaimReassigned> context)
    {
        var e = context.Message;
        var tracking = await Find(e.ClaimId, context.CancellationToken);
        if (tracking is not null && tracking.Status > SlaTrackingStatus.WaitingArrival) return;
        tracking ??= Create(e.ClaimId, e.ReassignedAt);
        await policy.StartStage(tracking, SlaTrackingStatus.WaitingArrival, e.ReassignedAt, e.NewAdjusterId, context, context.CancellationToken);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    public Task Consume(ConsumeContext<AdjusterArrived> context)
        => Advance(context, context.Message.ClaimId, SlaTrackingStatus.WaitingService, context.Message.ArrivedAt, context.Message.AdjusterId);

    public Task Consume(ConsumeContext<AdjusterServiceStarted> context)
        => Advance(context, context.Message.ClaimId, SlaTrackingStatus.InService, context.Message.StartedAt, context.Message.AdjusterId);

    public Task Consume(ConsumeContext<AdjusterServiceCompleted> context)
        => Advance(context, context.Message.ClaimId, SlaTrackingStatus.Closed, context.Message.CompletedAt, context.Message.AdjusterId);

    public Task Consume(ConsumeContext<ClaimClosed> context)
        => Advance(context, context.Message.ClaimId, SlaTrackingStatus.Closed, context.Message.ClosedAt, null);

    public Task Consume(ConsumeContext<ClaimCancelled> context)
        => Advance(context, context.Message.ClaimId, SlaTrackingStatus.Cancelled, context.Message.CancelledAt, null);

    public async Task Consume(ConsumeContext<AdjusterGpsStaleDetected> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;
        var trackings = await db.Trackings.Where(x => x.AdjusterId == e.AdjusterId && x.Status != SlaTrackingStatus.Closed && x.Status != SlaTrackingStatus.Cancelled).ToListAsync(ct);
        foreach (var tracking in trackings)
        {
            var exists = await db.Alerts.AnyAsync(x => x.ClaimId == tracking.ClaimId && x.AlertType == SlaAlertTypes.GpsStale && x.StageStartedAt == tracking.StageStartedAt, ct);
            if (!exists) db.Alerts.Add(new SlaAlert { Id = Guid.NewGuid(), ClaimId = tracking.ClaimId, AlertType = SlaAlertTypes.GpsStale, StageStartedAt = tracking.StageStartedAt, Message = "Adjuster GPS is stale.", RaisedAt = e.DetectedAt });
        }

        await db.SaveChangesAsync(ct);
    }

    // Los eventos llegan por topics distintos y pueden desordenarse: una etapa nunca retrocede y los estados finales no cambian.
    // Si llega primero un evento de etapa posterior, el seguimiento se crea directamente en esa etapa.
    private async Task Advance(ConsumeContext context, Guid claimId, SlaTrackingStatus stage, DateTimeOffset at, Guid? adjusterId)
    {
        var tracking = await Find(claimId, context.CancellationToken);
        if (tracking is null)
        {
            tracking = Create(claimId, at);
        }
        else if (tracking.IsFinished || stage <= tracking.Status)
        {
            return;
        }

        await policy.StartStage(tracking, stage, at, adjusterId, context, context.CancellationToken);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    private async Task<SlaTracking?> Find(Guid claimId, CancellationToken ct)
        => db.Trackings.Local.FirstOrDefault(x => x.ClaimId == claimId) ?? await db.Trackings.SingleOrDefaultAsync(x => x.ClaimId == claimId, ct);

    private SlaTracking Create(Guid claimId, DateTimeOffset at)
    {
        var tracking = new SlaTracking { ClaimId = claimId, Status = SlaTrackingStatus.WaitingAssignment, StartedAt = at, StageStartedAt = at, UpdatedAt = at, Version = 0 };
        db.Trackings.Add(tracking);
        return tracking;
    }
}
