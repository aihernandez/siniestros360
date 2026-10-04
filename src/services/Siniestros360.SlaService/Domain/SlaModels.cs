namespace Siniestros360.SlaService.Domain;

// El orden importa: una etapa sólo avanza hacia adelante. Closed y Cancelled son terminales.
public enum SlaTrackingStatus { WaitingAssignment, WaitingArrival, WaitingService, InService, Closed, Cancelled }

public sealed class SlaTracking
{
    public Guid ClaimId { get; set; }
    public Guid? AdjusterId { get; set; }
    public SlaTrackingStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset StageStartedAt { get; set; }
    public DateTimeOffset? StageDueAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }

    public bool IsFinished => Status is SlaTrackingStatus.Closed or SlaTrackingStatus.Cancelled;
}

public sealed class SlaAlert
{
    public Guid Id { get; set; }
    public Guid ClaimId { get; set; }
    public string AlertType { get; set; } = default!;
    // Una alerta por tipo y por inicio de etapa: si una reasignación reinicia la llegada, puede volver a alertar.
    public DateTimeOffset StageStartedAt { get; set; }
    public string Message { get; set; } = default!;
    public DateTimeOffset RaisedAt { get; set; }
}

public static class SlaAlertTypes
{
    public const string GpsStale = "GpsStale";
    public static string Warning(SlaTrackingStatus stage) => $"Warning:{stage}";
    public static string Breach(SlaTrackingStatus stage) => $"Breach:{stage}";
}
