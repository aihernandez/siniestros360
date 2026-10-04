namespace Siniestros360.Contracts.Events;

// La saga de asignación de Dispatch.
// El namespace es el mismo en todas las carpetas: forma la URN de MassTransit y el nombre del topic.
public sealed record AdjusterAssignmentRequested(Guid ClaimId, decimal Latitude, decimal Longitude, DateTimeOffset RequestedAt);
public sealed record AdjusterAssigned(Guid ClaimId, Guid AdjusterId, double DistanceKm, DateTimeOffset AssignedAt, DateTimeOffset EstimatedArrivalAt);
public sealed record NoAdjusterAvailable(Guid ClaimId, string Reason, DateTimeOffset OccurredAt);
public sealed record ClaimReassignmentRequested(Guid ClaimId, string Reason, DateTimeOffset RequestedAt);
public sealed record ClaimReassigned(Guid ClaimId, Guid PreviousAdjusterId, Guid NewAdjusterId, string Reason, DateTimeOffset ReassignedAt);
