namespace Siniestros360.Contracts.Events;

// Claims publica el ciclo del siniestro y los comandos del ajustador (llegada, inicio y cierre).
// El namespace es el mismo en todas las carpetas: forma la URN de MassTransit y el nombre del topic.
public sealed record ClaimReported(Guid ClaimId, string InsuredId, string Folio, string PolicyNumber, string VehiclePlate, string IncidentType, decimal Latitude, decimal Longitude, bool RequiresAmbulance, DateTimeOffset ReportedAt);
public sealed record ClaimStatusChanged(Guid ClaimId, string PreviousStatus, string NewStatus, long Version, DateTimeOffset ChangedAt);
public sealed record ClaimClosed(Guid ClaimId, DateTimeOffset ClosedAt);
public sealed record ClaimCancelled(Guid ClaimId, string Reason, DateTimeOffset CancelledAt);
public sealed record AdjusterArrived(Guid ClaimId, Guid AdjusterId, DateTimeOffset ArrivedAt);
public sealed record AdjusterServiceStarted(Guid ClaimId, Guid AdjusterId, DateTimeOffset StartedAt);
public sealed record AdjusterServiceCompleted(Guid ClaimId, Guid AdjusterId, DateTimeOffset CompletedAt);
