using System.Diagnostics;
using System.Diagnostics.Metrics;
using MassTransit;
using Siniestros360.Contracts.Events;
using Siniestros360.Messaging;

namespace Siniestros360.LocationService.Application;

// Punto de salida de la telemetría GPS. Hoy se publica por el bus outbox de MassTransit a Service Bus; a mayor volumen
// se sustituiría por un adaptador de Azure Event Hubs sin tocar los endpoints ni el worker de GPS desactualizado.
public interface ILocationEventSink
{
    Task LocationUpdated(AdjusterLocationUpdated location, Guid correlationId, CancellationToken cancellationToken);
    Task GpsStale(AdjusterGpsStaleDetected stale, CancellationToken cancellationToken);
}

// Usa el IPublishEndpoint del scope: los eventos se guardan con el SaveChanges del DbContext y se envían tras el commit.
public sealed class BusLocationEventSink(IPublishEndpoint publish) : ILocationEventSink
{
    public async Task LocationUpdated(AdjusterLocationUpdated location, Guid correlationId, CancellationToken cancellationToken)
    {
        await publish.PublishCorrelated(location, correlationId, cancellationToken);
        LocationTelemetry.Updated.Add(1);
    }

    public async Task GpsStale(AdjusterGpsStaleDetected stale, CancellationToken cancellationToken)
    {
        await publish.PublishCorrelated(stale, Guid.NewGuid(), cancellationToken);
        LocationTelemetry.GpsStale.Add(1);
    }
}

public static class LocationTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Siniestros360.LocationService");
    private static readonly Meter Meter = new("Siniestros360.LocationService");
    public static readonly Counter<long> Updated = Meter.CreateCounter<long>("locations.updated.count");
    public static readonly Counter<long> GpsStale = Meter.CreateCounter<long>("locations.gps_stale.count");
}
