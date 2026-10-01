using Microsoft.EntityFrameworkCore;
using Siniestros360.Contracts.Events;
using Siniestros360.LocationService.Application;
using Siniestros360.LocationService.Infrastructure;

namespace Siniestros360.LocationService.Workers;

public sealed class GpsStaleWorker(IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider clock, ILogger<GpsStaleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Check(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "GPS stale check failed");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    // Una alerta por periodo sin señal: se vuelve a alertar sólo si llega una nueva posición y luego se detiene otra vez.
    private async Task Check(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocationDbContext>();
        // El sink usa el IPublishEndpoint de este mismo scope: alerta y marca LastStaleAlertAt se confirman juntas.
        var sink = scope.ServiceProvider.GetRequiredService<ILocationEventSink>();
        var now = clock.GetUtcNow();
        var cutoff = now.AddSeconds(-configuration.GetValue("Location:StaleAfterSeconds", 120));
        var stale = await db.LatestLocations.Where(x => x.CapturedAt < cutoff && (x.LastStaleAlertAt == null || x.LastStaleAlertAt < x.CapturedAt)).ToListAsync(ct);
        foreach (var location in stale)
        {
            await sink.GpsStale(new AdjusterGpsStaleDetected(location.AdjusterId, location.CapturedAt, now), ct);
            location.LastStaleAlertAt = now;
        }
        if (stale.Count > 0) await db.SaveChangesAsync(ct);
    }
}
