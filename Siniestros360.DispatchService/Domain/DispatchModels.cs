using MassTransit;

namespace Siniestros360.DispatchService.Domain;

// Estado persistido de la saga de asignación (una por siniestro). CurrentState guarda el nombre del estado de la máquina.
public sealed class AssignmentState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = default!;
    public decimal IncidentLatitude { get; set; }
    public decimal IncidentLongitude { get; set; }
    public Guid? AdjusterId { get; set; }
    public Guid? PreviousAdjusterId { get; set; }
    public double? DistanceKm { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    // xmin de PostgreSQL: concurrencia optimista sin columna propia.
    public uint RowVersion { get; set; }
}

public sealed class AdjusterDispatchProjection
{
    public Guid AdjusterId { get; set; }
    public bool IsAvailable { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public DateTimeOffset? LastLocationAt { get; set; }
    public long LastLocationSequence { get; set; }
    public Guid? ReservedForClaimId { get; set; }
    public long Version { get; set; }
}

public sealed class DispatchAttempt
{
    public Guid Id { get; set; }
    public Guid ClaimId { get; set; }
    public Guid? AdjusterId { get; set; }
    public string Result { get; set; } = default!;
    public double? DistanceKm { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

// Mensajes internos de Dispatch: sólo los consume su propia saga.
public sealed record RetryAssignment(Guid ClaimId);
public sealed record ManualAssignmentRequested(Guid ClaimId, Guid? AdjusterId);
public sealed record ManualReassignmentRequested(Guid ClaimId, string Reason);
