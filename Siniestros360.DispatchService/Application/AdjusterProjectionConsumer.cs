using MassTransit;
using Microsoft.EntityFrameworkCore;
using Siniestros360.Contracts.Events;
using Siniestros360.DispatchService.Domain;
using Siniestros360.DispatchService.Infrastructure;

namespace Siniestros360.DispatchService.Application;

// Proyección local de disponibilidad y GPS: Dispatch decide sin consultar las bases de Adjusters ni de Location.
// Cuando un ajustador queda elegible, pide a las sagas en espera que reintenten la asignación.
public sealed class AdjusterProjectionConsumer(DispatchDbContext db, AdjusterReservations reservations, IConfiguration configuration, TimeProvider clock) :
    IConsumer<AdjusterRegistered>,
    IConsumer<AdjusterAvailabilityChanged>,
    IConsumer<AdjusterLocationUpdated>
{
    public async Task Consume(ConsumeContext<AdjusterRegistered> context)
    {
        await Projection(context.Message.AdjusterId, context.CancellationToken);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<AdjusterAvailabilityChanged> context)
    {
        var projection = await Projection(context.Message.AdjusterId, context.CancellationToken);
        if (projection.ReservedForClaimId is null) { projection.IsAvailable = context.Message.IsAvailable; projection.Version++; }
        await RetryWaitingClaims(context, projection);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<AdjusterLocationUpdated> context)
    {
        var e = context.Message;
        var projection = await Projection(e.AdjusterId, context.CancellationToken);
        // Hora de captura primero; la secuencia reinicia con el dispositivo y sólo desempata.
        if (projection.LastLocationAt is null || e.CapturedAt > projection.LastLocationAt || (e.CapturedAt == projection.LastLocationAt && e.Sequence > projection.LastLocationSequence))
        {
            projection.Latitude = e.Latitude; projection.Longitude = e.Longitude; projection.LastLocationAt = e.CapturedAt; projection.LastLocationSequence = e.Sequence; projection.Version++;
        }
        await RetryWaitingClaims(context, projection);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    // Sólo un ajustador libre con GPS reciente dispara reintentos; así el GPS de unidades ocupadas no genera tráfico.
    private async Task RetryWaitingClaims(ConsumeContext context, AdjusterDispatchProjection projection)
    {
        var freshness = clock.GetUtcNow().AddSeconds(-configuration.GetValue("Dispatch:LocationFreshnessSeconds", 180));
        if (!projection.IsAvailable || projection.ReservedForClaimId is not null || projection.LastLocationAt < freshness) return;
        foreach (var claimId in await reservations.WaitingClaims(context.CancellationToken))
        {
            await context.Publish(new RetryAssignment(claimId), context.CancellationToken);
        }
    }

    private async Task<AdjusterDispatchProjection> Projection(Guid adjusterId, CancellationToken ct)
    {
        var projection = db.Adjusters.Local.FirstOrDefault(x => x.AdjusterId == adjusterId) ?? await db.Adjusters.SingleOrDefaultAsync(x => x.AdjusterId == adjusterId, ct);
        if (projection is not null) return projection;
        projection = new AdjusterDispatchProjection { AdjusterId = adjusterId, Version = 1 };
        db.Adjusters.Add(projection);
        return projection;
    }
}
