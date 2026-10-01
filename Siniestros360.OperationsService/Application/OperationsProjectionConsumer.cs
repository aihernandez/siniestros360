using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Siniestros360.Contracts.Events;
using Siniestros360.OperationsService.Domain;
using Siniestros360.OperationsService.Hubs;
using Siniestros360.OperationsService.Infrastructure;

namespace Siniestros360.OperationsService.Application;

// CQRS ligero: cada evento actualiza una vista de lectura y luego se empuja el snapshot por SignalR.
// Los duplicados los descarta la inbox del consumer outbox; los eventos fuera de orden se resuelven con versión o secuencia.
// La notificación sale tras SaveChanges pero antes del commit del consumer outbox: si el commit falla, el mensaje se
// reentrega y la vista se vuelve a empujar. Las vistas son la fuente de verdad; SignalR sólo avisa.
public sealed class OperationsProjectionConsumer(OperationsDbContext db, IHubContext<OperationsHub> hub) :
    IConsumer<ClaimReported>,
    IConsumer<ClaimStatusChanged>,
    IConsumer<ClaimCancelled>,
    IConsumer<ClaimClosed>,
    IConsumer<AdjusterAssigned>,
    IConsumer<ClaimReassigned>,
    IConsumer<NoAdjusterAvailable>,
    IConsumer<PolicyValidationCompleted>,
    IConsumer<PolicyValidationUnavailable>,
    IConsumer<PolicyValidationFailed>,
    IConsumer<SlaStageStarted>,
    IConsumer<SlaWarningRaised>,
    IConsumer<SlaBreached>,
    IConsumer<ClaimEscalated>,
    IConsumer<DocumentUploaded>,
    IConsumer<DocumentDeleted>,
    IConsumer<AdjusterRegistered>,
    IConsumer<AdjusterAvailabilityChanged>,
    IConsumer<AdjusterStatusChanged>,
    IConsumer<AdjusterLocationUpdated>,
    IConsumer<AdjusterGpsStaleDetected>
{
    public async Task Consume(ConsumeContext<ClaimReported> context)
    {
        var e = context.Message;
        var claim = await Claim(e.ClaimId, context.CancellationToken);
        claim.InsuredId = e.InsuredId; claim.Folio = e.Folio; claim.PolicyNumber = e.PolicyNumber; claim.VehiclePlate = e.VehiclePlate; claim.IncidentType = e.IncidentType;
        claim.Latitude = e.Latitude; claim.Longitude = e.Longitude; claim.RequiresAmbulance = e.RequiresAmbulance; claim.ReportedAt = e.ReportedAt; claim.UpdatedAt = e.ReportedAt;
        claim.LastVersion = Math.Max(claim.LastVersion, 1);
        await SaveAndNotifyClaim(e.ClaimId, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ClaimStatusChanged> context)
    {
        var e = context.Message;
        var claim = await Claim(e.ClaimId, context.CancellationToken);
        if (e.Version > claim.LastVersion)
        {
            claim.Status = e.NewStatus; claim.LastVersion = e.Version; claim.UpdatedAt = e.ChangedAt;
            if (claim.IsFinished) claim.SlaDueAt = null;
        }
        await SaveAndNotifyClaim(e.ClaimId, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ClaimCancelled> context)
    {
        Finish(await Claim(context.Message.ClaimId, context.CancellationToken), "Cancelled", context.Message.CancelledAt);
        await SaveAndNotifyClaim(context.Message.ClaimId, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ClaimClosed> context)
    {
        Finish(await Claim(context.Message.ClaimId, context.CancellationToken), "Closed", context.Message.ClosedAt);
        await SaveAndNotifyClaim(context.Message.ClaimId, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<AdjusterAssigned> context)
    {
        var e = context.Message;
        var claim = await Claim(e.ClaimId, context.CancellationToken);
        var adjuster = await Adjuster(e.AdjusterId, context.CancellationToken);
        if (!claim.IsFinished && claim.Status == "AssignmentPending") claim.Status = "Assigned";
        claim.AdjusterId = e.AdjusterId; claim.AdjusterName = NameOf(adjuster); claim.DistanceKm = e.DistanceKm; claim.AssignedAt = e.AssignedAt; claim.EstimatedArrivalAt = e.EstimatedArrivalAt; claim.UpdatedAt = e.AssignedAt;
        adjuster.ActiveClaimId = e.ClaimId;
        await SaveAndNotifyClaim(e.ClaimId, context.CancellationToken, e.AdjusterId);
    }

    public async Task Consume(ConsumeContext<ClaimReassigned> context)
    {
        var e = context.Message;
        var claim = await Claim(e.ClaimId, context.CancellationToken);
        var adjuster = await Adjuster(e.NewAdjusterId, context.CancellationToken);
        var previous = await Adjuster(e.PreviousAdjusterId, context.CancellationToken);
        if (previous.ActiveClaimId == e.ClaimId) previous.ActiveClaimId = null;
        claim.AdjusterId = e.NewAdjusterId; claim.AdjusterName = NameOf(adjuster); claim.AssignedAt = e.ReassignedAt; claim.UpdatedAt = e.ReassignedAt;
        adjuster.ActiveClaimId = e.ClaimId;
        await SaveAndNotifyClaim(e.ClaimId, context.CancellationToken, e.NewAdjusterId, e.PreviousAdjusterId);
    }

    public Task Consume(ConsumeContext<NoAdjusterAvailable> context)
        => RaiseAlert(context, context.Message.ClaimId, null, "NoAdjusterAvailable", context.Message.Reason, context.Message.OccurredAt);

    public async Task Consume(ConsumeContext<PolicyValidationCompleted> context)
    {
        Coverage(await Claim(context.Message.ClaimId, context.CancellationToken), context.Message.CoverageStatus, context.Message.Reason);
        await SaveAndNotifyClaim(context.Message.ClaimId, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<PolicyValidationUnavailable> context)
    {
        Coverage(await Claim(context.Message.ClaimId, context.CancellationToken), "Unavailable", context.Message.Reason);
        await SaveAndNotifyClaim(context.Message.ClaimId, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<PolicyValidationFailed> context)
    {
        Coverage(await Claim(context.Message.ClaimId, context.CancellationToken), "Failed", context.Message.ErrorMessage);
        await SaveAndNotifyClaim(context.Message.ClaimId, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<SlaStageStarted> context)
    {
        var e = context.Message;
        var claim = await Claim(e.ClaimId, context.CancellationToken);
        if (!claim.IsFinished) { claim.SlaStage = e.Stage; claim.SlaDueAt = e.DueAt; }
        await SaveAndNotifyClaim(e.ClaimId, context.CancellationToken);
    }

    public Task Consume(ConsumeContext<SlaWarningRaised> context)
        => RaiseAlert(context, context.Message.ClaimId, null, "SlaWarning", context.Message.Message, context.Message.RaisedAt);

    public Task Consume(ConsumeContext<SlaBreached> context)
        => RaiseAlert(context, context.Message.ClaimId, null, "SlaBreached", context.Message.Message, context.Message.BreachedAt);

    public async Task Consume(ConsumeContext<ClaimEscalated> context)
    {
        (await Claim(context.Message.ClaimId, context.CancellationToken)).EscalatedAt ??= context.Message.EscalatedAt;
        await SaveAndNotifyClaim(context.Message.ClaimId, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<DocumentUploaded> context)
    {
        (await Claim(context.Message.ClaimId, context.CancellationToken)).DocumentCount++;
        await SaveAndNotifyClaim(context.Message.ClaimId, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<DocumentDeleted> context)
    {
        var claim = await Claim(context.Message.ClaimId, context.CancellationToken);
        claim.DocumentCount = Math.Max(0, claim.DocumentCount - 1);
        await SaveAndNotifyClaim(context.Message.ClaimId, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<AdjusterRegistered> context)
    {
        (await Adjuster(context.Message.AdjusterId, context.CancellationToken)).DisplayName = context.Message.DisplayName;
        await SaveAndNotifyAdjuster(context.Message.AdjusterId, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<AdjusterAvailabilityChanged> context)
    {
        (await Adjuster(context.Message.AdjusterId, context.CancellationToken)).IsAvailable = context.Message.IsAvailable;
        await SaveAndNotifyAdjuster(context.Message.AdjusterId, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<AdjusterStatusChanged> context)
    {
        var adjuster = await Adjuster(context.Message.AdjusterId, context.CancellationToken);
        adjuster.Status = context.Message.NewStatus;
        adjuster.IsAvailable = context.Message.NewStatus == "Available";
        if (adjuster.IsAvailable) adjuster.ActiveClaimId = null;
        await SaveAndNotifyAdjuster(context.Message.AdjusterId, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<AdjusterLocationUpdated> context)
    {
        var e = context.Message;
        var adjuster = await Adjuster(e.AdjusterId, context.CancellationToken);
        // Hora de captura primero; la secuencia reinicia con el dispositivo y sólo desempata.
        if (adjuster.CapturedAt is null || e.CapturedAt > adjuster.CapturedAt || (e.CapturedAt == adjuster.CapturedAt && e.Sequence > adjuster.Sequence))
        {
            adjuster.Latitude = e.Latitude; adjuster.Longitude = e.Longitude; adjuster.SpeedKmh = e.SpeedKmh; adjuster.Heading = e.Heading; adjuster.Sequence = e.Sequence; adjuster.CapturedAt = e.CapturedAt; adjuster.GpsStale = false;
        }
        await SaveAndNotifyAdjuster(e.AdjusterId, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<AdjusterGpsStaleDetected> context)
    {
        var e = context.Message;
        var adjuster = await Adjuster(e.AdjusterId, context.CancellationToken);
        adjuster.GpsStale = true;
        var alert = NewAlert(context, adjuster.ActiveClaimId, e.AdjusterId, "GpsStale", $"{NameOf(adjuster)} sin señal GPS desde {e.LastSeenAt:HH:mm:ss}.", e.DetectedAt);
        await db.SaveChangesAsync(context.CancellationToken);
        await NotifyAdjuster(e.AdjusterId, context.CancellationToken);
        await hub.Clients.Group(OperationsHub.TowerGroup).SendAsync("alertRaised", alert, context.CancellationToken);
    }

    private async Task RaiseAlert(ConsumeContext context, Guid? claimId, Guid? adjusterId, string type, string message, DateTimeOffset at)
    {
        var alert = NewAlert(context, claimId, adjusterId, type, message, at);
        await db.SaveChangesAsync(context.CancellationToken);
        await hub.Clients.Group(OperationsHub.TowerGroup).SendAsync("alertRaised", alert, context.CancellationToken);
    }

    // El Id de la alerta es el MessageId: una reentrega nunca crea una segunda alerta.
    private OperationalAlert NewAlert(ConsumeContext context, Guid? claimId, Guid? adjusterId, string type, string message, DateTimeOffset at)
    {
        var alert = new OperationalAlert { Id = context.MessageId!.Value, ClaimId = claimId, AdjusterId = adjusterId, Type = type, Message = message, RaisedAt = at };
        db.Alerts.Add(alert);
        return alert;
    }

    private async Task SaveAndNotifyClaim(Guid claimId, CancellationToken ct, params Guid[] adjusters)
    {
        await db.SaveChangesAsync(ct);
        var claim = await db.Claims.AsNoTracking().SingleAsync(x => x.ClaimId == claimId, ct);
        var targets = new List<string> { OperationsHub.TowerGroup, OperationsHub.ClaimGroup(claim.ClaimId) };
        if (claim.AdjusterId is not null) targets.Add(OperationsHub.AdjusterGroup(claim.AdjusterId.Value));
        targets.AddRange(adjusters.Select(OperationsHub.AdjusterGroup));
        await hub.Clients.Groups(targets.Distinct().ToList()).SendAsync("claimUpdated", claim, ct);
        foreach (var adjusterId in adjusters.Distinct()) await NotifyAdjuster(adjusterId, ct);
    }

    private async Task SaveAndNotifyAdjuster(Guid adjusterId, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        await NotifyAdjuster(adjusterId, ct);
    }

    private async Task NotifyAdjuster(Guid adjusterId, CancellationToken ct)
    {
        var adjuster = await db.Adjusters.AsNoTracking().SingleOrDefaultAsync(x => x.AdjusterId == adjusterId, ct);
        if (adjuster is not null) await hub.Clients.Group(OperationsHub.TowerGroup).SendAsync("adjusterUpdated", adjuster, ct);
    }

    private static void Finish(ClaimReadModel claim, string status, DateTimeOffset at)
    {
        claim.Status = status; claim.SlaDueAt = null; claim.UpdatedAt = at;
    }

    private static void Coverage(ClaimReadModel claim, string status, string? reason)
    {
        claim.CoverageStatus = status; claim.CoverageReason = reason;
    }

    private static string NameOf(AdjusterReadModel adjuster) => string.IsNullOrWhiteSpace(adjuster.DisplayName) ? adjuster.AdjusterId.ToString()[..8] : adjuster.DisplayName;

    // Un evento puede llegar antes que ClaimReported o AdjusterRegistered: se crea la vista y el evento que falta la completa.
    private async Task<ClaimReadModel> Claim(Guid claimId, CancellationToken ct)
    {
        var claim = db.Claims.Local.FirstOrDefault(x => x.ClaimId == claimId) ?? await db.Claims.SingleOrDefaultAsync(x => x.ClaimId == claimId, ct);
        if (claim is not null) return claim;
        claim = new ClaimReadModel { ClaimId = claimId, UpdatedAt = DateTimeOffset.UtcNow };
        db.Claims.Add(claim);
        return claim;
    }

    private async Task<AdjusterReadModel> Adjuster(Guid adjusterId, CancellationToken ct)
    {
        var adjuster = db.Adjusters.Local.FirstOrDefault(x => x.AdjusterId == adjusterId) ?? await db.Adjusters.SingleOrDefaultAsync(x => x.AdjusterId == adjusterId, ct);
        if (adjuster is not null) return adjuster;
        adjuster = new AdjusterReadModel { AdjusterId = adjusterId };
        db.Adjusters.Add(adjuster);
        return adjuster;
    }
}
