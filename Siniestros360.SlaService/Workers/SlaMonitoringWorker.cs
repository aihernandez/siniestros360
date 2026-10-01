using MassTransit;
using Microsoft.EntityFrameworkCore;
using Siniestros360.Contracts.Events;
using Siniestros360.Messaging;
using Siniestros360.SlaService.Application;
using Siniestros360.SlaService.Domain;
using Siniestros360.SlaService.Infrastructure;

namespace Siniestros360.SlaService.Workers;

// Revisa cada 10 s las etapas activas: al WarningRatio del plazo avisa y al vencer publica breach y escalación,
// una sola vez por etapa (índice único de SlaAlert).
public sealed class SlaMonitoringWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<SlaMonitoringWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), clock);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Check(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "SLA check failed; it will be retried on the next tick");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task Check(CancellationToken ct)
    {
        using var activity = SlaTelemetry.ActivitySource.StartActivity("Sla.CheckBreaches");
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SlaDbContext>();
        var policy = scope.ServiceProvider.GetRequiredService<SlaPolicy>();
        // IPublishEndpoint del mismo scope: alertas y eventos se confirman con el mismo SaveChanges (bus outbox).
        var publish = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var now = clock.GetUtcNow();
        var active = await db.Trackings.Where(x => x.Status != SlaTrackingStatus.Closed && x.Status != SlaTrackingStatus.Cancelled && x.StageDueAt != null).ToListAsync(ct);
        foreach (var item in active)
        {
            var limit = policy.Limit(item.Status);
            if (limit is null || item.StageDueAt is not { } dueAt) continue;
            var stage = item.Status.ToString();

            var breachType = SlaAlertTypes.Breach(item.Status);
            if (dueAt <= now)
            {
                if (await Exists(db, item, breachType, ct)) continue;
                var message = $"SLA {stage} exceeded {limit.Value.TotalMinutes:0} minutes.";
                db.Alerts.Add(new SlaAlert { Id = Guid.NewGuid(), ClaimId = item.ClaimId, AlertType = breachType, StageStartedAt = item.StageStartedAt, Message = message, RaisedAt = now });
                var correlation = Guid.NewGuid();
                await publish.PublishCorrelated(new SlaBreached(item.ClaimId, stage, message, now), correlation, ct);
                await publish.PublishCorrelated(new ClaimEscalated(item.ClaimId, message, now), correlation, ct);
                SlaTelemetry.Breached.Add(1, new KeyValuePair<string, object?>("sla.stage", stage));
                continue;
            }

            var warningType = SlaAlertTypes.Warning(item.Status);
            var warningAt = item.StageStartedAt.Add(limit.Value * policy.WarningRatio);
            if (warningAt <= now && !await Exists(db, item, warningType, ct))
            {
                var remaining = Math.Max(1, Math.Ceiling((dueAt - now).TotalMinutes));
                var message = $"SLA {stage} expires in {remaining:0} minutes.";
                db.Alerts.Add(new SlaAlert { Id = Guid.NewGuid(), ClaimId = item.ClaimId, AlertType = warningType, StageStartedAt = item.StageStartedAt, Message = message, RaisedAt = now });
                await publish.PublishCorrelated(new SlaWarningRaised(item.ClaimId, stage, message, now), Guid.NewGuid(), ct);
            }
        }

        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
    }

    private static Task<bool> Exists(SlaDbContext db, SlaTracking item, string alertType, CancellationToken ct)
        => db.Alerts.AnyAsync(x => x.ClaimId == item.ClaimId && x.AlertType == alertType && x.StageStartedAt == item.StageStartedAt, ct);
}
