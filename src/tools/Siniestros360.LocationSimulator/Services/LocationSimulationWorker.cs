using Microsoft.Extensions.Options;
using Siniestros360.LocationSimulator.Options;
using Siniestros360.LocationSimulator.Publishing;

namespace Siniestros360.LocationSimulator.Services;

public sealed class LocationSimulationWorker(
    SimulationStateStore store,
    ILocationPublisher publisher,
    IOptions<SimulatorOptions> options,
    ILogger<LocationSimulationWorker> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMilliseconds(options.Value.UpdateIntervalMilliseconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Simulador iniciado con {AdjusterCount} ajustadores y actualización cada {IntervalMs} ms",
            store.Count, _interval.TotalMilliseconds);

        using var timer = new PeriodicTimer(_interval);
        await GenerateAndPublishAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
            await GenerateAndPublishAsync(stoppingToken);
    }

    private async Task GenerateAndPublishAsync(CancellationToken cancellationToken)
    {
        try
        {
            var batch = store.Advance(_interval);
            if (batch is null) return;

            await publisher.PublishAsync(batch, cancellationToken);
            if (batch.Locations.Count > 0 && store.GeneratedBatches % 10 == 0)
            {
                var first = batch.Locations[0];
                logger.LogInformation("Lote {BatchNumber}: {Count} posiciones. {AdjusterId} está en {Latitude}, {Longitude}",
                    store.GeneratedBatches, batch.Locations.Count, first.AdjusterId, first.Latitude, first.Longitude);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Apagado ordenado solicitado por el host.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falló una iteración del simulador; se intentará nuevamente");
        }
    }
}
