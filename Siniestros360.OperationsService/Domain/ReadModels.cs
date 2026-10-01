using System.Text.Json.Serialization;

namespace Siniestros360.OperationsService.Domain;

// Vista consolidada del siniestro para la torre. Se construye sólo con eventos; nunca consulta otras bases.
public sealed class ClaimReadModel
{
    public Guid ClaimId { get; set; }
    [JsonIgnore] public string InsuredId { get; set; } = "";
    public string Folio { get; set; } = "";
    public string PolicyNumber { get; set; } = "";
    public string VehiclePlate { get; set; } = "";
    public string IncidentType { get; set; } = "";
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public bool RequiresAmbulance { get; set; }
    public string Status { get; set; } = "AssignmentPending";
    public string CoverageStatus { get; set; } = "Pending";
    public string? CoverageReason { get; set; }
    public Guid? AdjusterId { get; set; }
    public string? AdjusterName { get; set; }
    public double? DistanceKm { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public DateTimeOffset? EstimatedArrivalAt { get; set; }
    public string? SlaStage { get; set; }
    public DateTimeOffset? SlaDueAt { get; set; }
    public DateTimeOffset? EscalatedAt { get; set; }
    public DateTimeOffset ReportedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int DocumentCount { get; set; }
    [JsonIgnore] public long LastVersion { get; set; }
    [JsonIgnore] public bool IsFinished => Status is "Closed" or "Cancelled";
}

public sealed class AdjusterReadModel
{
    public Guid AdjusterId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = "Offline";
    public bool IsAvailable { get; set; }
    public Guid? ActiveClaimId { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public double? SpeedKmh { get; set; }
    public double? Heading { get; set; }
    [JsonIgnore] public long Sequence { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public bool GpsStale { get; set; }
}

public sealed class OperationalAlert
{
    public Guid Id { get; set; }
    public Guid? ClaimId { get; set; }
    public Guid? AdjusterId { get; set; }
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset RaisedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
}
