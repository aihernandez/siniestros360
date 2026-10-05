using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Siniestros360.Messaging.Persistence;
using Siniestros360.SharedKernel;

namespace Siniestros360.Messaging.Idempotency;

// Purga periódica de respuestas idempotentes vencidas: sin ella la tabla crece sin límite.
public sealed class IdempotencyCleanupWorker<TDbContext>(IServiceScopeFactory scopes, TimeProvider timerClock, IDateTimeProvider clock, ILogger<IdempotencyCleanupWorker<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext, IReliableMessagingDbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10), timerClock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
                if (!db.Database.IsRelational()) return;
                var now = clock.UtcNow;
                var deleted = await db.IdempotencyRecords.Where(x => x.ExpiresAt <= now).ExecuteDeleteAsync(stoppingToken);
                if (deleted > 0) logger.LogInformation("Purged {Count} expired idempotency records", deleted);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Idempotency cleanup failed; it will be retried");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
