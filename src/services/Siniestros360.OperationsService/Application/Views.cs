using Siniestros360.OperationsService.Domain;

namespace Siniestros360.OperationsService.Application;

// Lo que sale de Operations, por REST y por SignalR. Los read models son tablas de EF con campos internos
// (dueño del siniestro, versión, secuencia, RowVersion); estas vistas sólo llevan lo que la torre y las apps consumen.
// Los nombres de campo son el contrato JSON con las tres apps: cambiarlos rompe a los clientes.

/// <summary>Siniestro tal como lo ve la torre.</summary>
public sealed record ClaimView(
    Guid ClaimId, string Folio, string PolicyNumber, string VehiclePlate, string IncidentType, decimal Latitude, decimal Longitude,
    bool RequiresAmbulance, string Status, string CoverageStatus, string? CoverageReason, Guid? AdjusterId, string? AdjusterName,
    double? DistanceKm, DateTimeOffset? AssignedAt, DateTimeOffset? EstimatedArrivalAt, string? SlaStage, DateTimeOffset? SlaDueAt,
    DateTimeOffset? EscalatedAt, DateTimeOffset ReportedAt, DateTimeOffset UpdatedAt, int DocumentCount)
{
    public static ClaimView From(ClaimReadModel x) => new(x.ClaimId, x.Folio, x.PolicyNumber, x.VehiclePlate, x.IncidentType, x.Latitude, x.Longitude,
        x.RequiresAmbulance, x.Status, x.CoverageStatus, x.CoverageReason, x.AdjusterId, x.AdjusterName, x.DistanceKm, x.AssignedAt, x.EstimatedArrivalAt,
        x.SlaStage, x.SlaDueAt, x.EscalatedAt, x.ReportedAt, x.UpdatedAt, x.DocumentCount);
}

/// <summary>Ajustador en el mapa de la torre.</summary>
public sealed record AdjusterView(
    Guid AdjusterId, string DisplayName, string Status, bool IsAvailable, Guid? ActiveClaimId, decimal? Latitude, decimal? Longitude,
    double? SpeedKmh, double? Heading, DateTimeOffset? CapturedAt, bool GpsStale)
{
    public static AdjusterView From(AdjusterReadModel x) => new(x.AdjusterId, x.DisplayName, x.Status, x.IsAvailable, x.ActiveClaimId, x.Latitude, x.Longitude,
        x.SpeedKmh, x.Heading, x.CapturedAt, x.GpsStale);
}

/// <summary>Alerta operativa y su atención.</summary>
public sealed record AlertView(Guid Id, Guid? ClaimId, Guid? AdjusterId, string Type, string Message, DateTimeOffset RaisedAt, DateTimeOffset? AcknowledgedAt, string? AcknowledgedBy)
{
    public static AlertView From(OperationalAlert x) => new(x.Id, x.ClaimId, x.AdjusterId, x.Type, x.Message, x.RaisedAt, x.AcknowledgedAt, x.AcknowledgedBy);
}

/// <summary>Contadores del tablero operativo.</summary>
public sealed record DashboardView(int Total, int Active, int Alerts, int Adjusters, int Available);
