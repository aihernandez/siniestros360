using Siniestros360.SharedKernel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Siniestros360.DispatchService.Domain;
using Siniestros360.DispatchService.Infrastructure;

namespace Siniestros360.DispatchService.Application;

public sealed record Reservation(Guid AdjusterId, double DistanceKm, DateTimeOffset EstimatedArrivalAt);

// Regla de despacho: el ajustador disponible más cercano con GPS reciente, tomado de la proyección local.
// La reserva y su liberación (la compensación) se guardan en la misma transacción que el estado de la saga;
// la concurrencia optimista de la proyección evita que dos sagas reserven al mismo ajustador.
public sealed class AdjusterReservations(DispatchDbContext db, IConfiguration configuration, IDateTimeProvider clock)
{
    private static readonly ActivitySource ActivitySource = new("Siniestros360.DispatchService");
    private static readonly Meter Meter = new("Siniestros360.DispatchService");
    private static readonly Histogram<double> DistanceMetric = Meter.CreateHistogram<double>("dispatch.distance.km");
    private static readonly Histogram<double> AssignmentDuration = Meter.CreateHistogram<double>("dispatch.assignment.duration", unit: "s");
    private static readonly Counter<long> UnavailableMetric = Meter.CreateCounter<long>("dispatch.no_adjuster_available.count");
    private static readonly Counter<long> ReassignmentMetric = Meter.CreateCounter<long>("dispatch.reassignment.count");

    public async Task<Reservation?> TryReserve(AssignmentState saga, CancellationToken ct, Guid? preferredAdjusterId = null, bool isReassignment = false)
    {
        using var activity = ActivitySource.StartActivity("Dispatch.AssignAdjuster");
        activity?.SetTag("siniestros360.claim_id", saga.CorrelationId);
        var now = clock.UtcNow;
        var freshness = now.AddSeconds(-configuration.GetValue("Dispatch:LocationFreshnessSeconds", 180));
        var candidates = await db.Adjusters
            .Where(x => x.IsAvailable && x.ReservedForClaimId == null && x.LastLocationAt >= freshness && x.Latitude != null && x.Longitude != null && x.AdjusterId != saga.AdjusterId)
            .Where(x => preferredAdjusterId == null || x.AdjusterId == preferredAdjusterId)
            .ToListAsync(ct);

        // PROVISIONAL(2026-09-30): elige por distancia en línea recta sobre la proyección local; falta ETA por calles y shortlist H3/PostGIS — desbloquea el despacho por tiempo real de llegada.
        var selected = candidates
            .Select(x => new { Adjuster = x, Distance = GeoDistance.Kilometers(saga.IncidentLatitude, saga.IncidentLongitude, x.Latitude!.Value, x.Longitude!.Value) })
            .OrderBy(x => x.Distance)
            .FirstOrDefault();

        if (selected is null)
        {
            db.Attempts.Add(new DispatchAttempt { Id = Guid.NewGuid(), ClaimId = saga.CorrelationId, Result = isReassignment ? "NoAdjusterForReassignment" : "NoAdjusterAvailable", OccurredAt = now });
            UnavailableMetric.Add(1);
            return null;
        }

        var previous = saga.AdjusterId;
        selected.Adjuster.IsAvailable = false; selected.Adjuster.ReservedForClaimId = saga.CorrelationId; selected.Adjuster.Version++;
        if (previous is not null) await Release(previous.Value, saga.CorrelationId, ct);
        saga.PreviousAdjusterId = previous; saga.AdjusterId = selected.Adjuster.AdjusterId; saga.DistanceKm = selected.Distance; saga.UpdatedAt = now;
        db.Attempts.Add(new DispatchAttempt { Id = Guid.NewGuid(), ClaimId = saga.CorrelationId, AdjusterId = selected.Adjuster.AdjusterId, Result = isReassignment ? "Reassigned" : "Assigned", DistanceKm = selected.Distance, OccurredAt = now });
        DistanceMetric.Record(selected.Distance);
        if (isReassignment) ReassignmentMetric.Add(1); else AssignmentDuration.Record((now - saga.StartedAt).TotalSeconds);
        return new Reservation(selected.Adjuster.AdjusterId, selected.Distance, now.AddMinutes(Math.Max(5, selected.Distance / 30 * 60)));
    }

    // Compensación: libera al ajustador sólo si sigue reservado para este siniestro (un evento tardío no pisa otra asignación).
    public async Task Release(Guid adjusterId, Guid claimId, CancellationToken ct)
    {
        var adjuster = db.Adjusters.Local.FirstOrDefault(x => x.AdjusterId == adjusterId) ?? await db.Adjusters.SingleOrDefaultAsync(x => x.AdjusterId == adjusterId, ct);
        if (adjuster is null || adjuster.ReservedForClaimId != claimId) return;
        adjuster.IsAvailable = true; adjuster.ReservedForClaimId = null; adjuster.Version++;
        db.Attempts.Add(new DispatchAttempt { Id = Guid.NewGuid(), ClaimId = claimId, AdjusterId = adjusterId, Result = "Released", OccurredAt = clock.UtcNow });
    }

    // Siniestros que esperan ajustador; se les pide reintentar cuando alguien se libera o reporta GPS.
    public Task<List<Guid>> WaitingClaims(CancellationToken ct)
        => db.AssignmentSagas.Where(x => x.CurrentState == nameof(AssignmentStateMachine.Unavailable)).OrderBy(x => x.StartedAt).Select(x => x.CorrelationId).Take(5).ToListAsync(ct);
}

public static class GeoDistance
{
    public static double Kilometers(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
    {
        const double radius = 6371;
        var dLat = (double)(lat2 - lat1) * Math.PI / 180; var dLon = (double)(lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos((double)lat1 * Math.PI / 180) * Math.Cos((double)lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return radius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
