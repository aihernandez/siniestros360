using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Siniestros360.Contracts.Events;
using Siniestros360.OperationsService.Domain;
using Siniestros360.OperationsService.Hubs;
using Siniestros360.OperationsService.Infrastructure;

namespace Siniestros360.OperationsService.Application;

// GPS en la cola de telemetría de Operations, separada de los eventos del siniestro.
// RowVersion (xmin) detecta dos posiciones del mismo ajustador procesadas a la vez; el reintento descarta la más antigua.
public sealed class AdjusterLocationConsumer(OperationsDbContext db, IHubContext<OperationsHub> hub) : IConsumer<AdjusterLocationUpdated>
{
    public async Task Consume(ConsumeContext<AdjusterLocationUpdated> context)
    {
        var e = context.Message;
        var adjuster = await db.Adjusters.SingleOrDefaultAsync(x => x.AdjusterId == e.AdjusterId, context.CancellationToken);
        if (adjuster is null)
        {
            adjuster = new AdjusterReadModel { AdjusterId = e.AdjusterId };
            db.Adjusters.Add(adjuster);
        }

        // Hora de captura primero; la secuencia reinicia con el dispositivo y sólo desempata.
        if (adjuster.CapturedAt is not null && (e.CapturedAt < adjuster.CapturedAt || (e.CapturedAt == adjuster.CapturedAt && e.Sequence <= adjuster.Sequence))) return;
        adjuster.Latitude = e.Latitude; adjuster.Longitude = e.Longitude; adjuster.SpeedKmh = e.SpeedKmh; adjuster.Heading = e.Heading; adjuster.Sequence = e.Sequence; adjuster.CapturedAt = e.CapturedAt; adjuster.GpsStale = false;
        await db.SaveChangesAsync(context.CancellationToken);
        await hub.Clients.Group(OperationsHub.TowerGroup).SendAsync("adjusterUpdated", adjuster, context.CancellationToken);
    }
}
