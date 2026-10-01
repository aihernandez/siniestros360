using System.Diagnostics;
using System.Diagnostics.Metrics;
using MassTransit;
using Siniestros360.Contracts.Events;
using Siniestros360.SlaService.Domain;

namespace Siniestros360.SlaService.Application;

// Única fuente de los plazos por etapa (sección Sla de la configuración).
public sealed class SlaPolicy(IConfiguration configuration)
{
    public double WarningRatio => configuration.GetValue("Sla:WarningRatio", 0.8);

    public TimeSpan? Limit(SlaTrackingStatus stage) => stage switch
    {
        SlaTrackingStatus.WaitingAssignment => TimeSpan.FromMinutes(configuration.GetValue("Sla:AssignmentMinutes", 2)),
        SlaTrackingStatus.WaitingArrival => TimeSpan.FromMinutes(configuration.GetValue("Sla:ArrivalMinutes", 15)),
        SlaTrackingStatus.WaitingService => TimeSpan.FromMinutes(configuration.GetValue("Sla:StartMinutes", 10)),
        SlaTrackingStatus.InService => TimeSpan.FromMinutes(configuration.GetValue("Sla:ServiceMinutes", 30)),
        _ => null
    };

    // Inicia una etapa y publica su fecha límite; la torre la usa para el temporizador sin conocer la regla.
    // Con un ConsumeContext la publicación viaja en el consumer outbox; con el IPublishEndpoint de un scope, en el bus outbox.
    public async Task StartStage(SlaTracking tracking, SlaTrackingStatus stage, DateTimeOffset at, Guid? adjusterId, IPublishEndpoint publish, CancellationToken cancellationToken)
    {
        tracking.Status = stage;
        tracking.StageStartedAt = at;
        tracking.StageDueAt = Limit(stage) is { } limit ? at.Add(limit) : null;
        tracking.AdjusterId = adjusterId ?? tracking.AdjusterId;
        tracking.UpdatedAt = at;
        tracking.Version++;
        if (tracking.StageDueAt is { } dueAt)
            await publish.Publish(new SlaStageStarted(tracking.ClaimId, stage.ToString(), at, dueAt), cancellationToken);
    }
}

public static class SlaTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Siniestros360.SlaService");
    private static readonly Meter Meter = new("Siniestros360.SlaService");
    public static readonly Counter<long> Breached = Meter.CreateCounter<long>("sla.breached.count");
}
