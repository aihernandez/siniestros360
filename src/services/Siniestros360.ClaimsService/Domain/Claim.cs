namespace Siniestros360.ClaimsService.Domain;

public enum ClaimStatus
{
    Reported,
    AssignmentPending,
    Assigned,
    AdjusterArrived,
    InProgress,
    Closed,
    Cancelled
}

public sealed class Claim
{
    public Guid Id { get; set; }
    public string InsuredId { get; set; } = default!;
    public string Folio { get; set; } = default!;
    public string PolicyNumber { get; set; } = default!;
    public string VehiclePlate { get; set; } = default!;
    public string IncidentType { get; set; } = default!;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public bool RequiresAmbulance { get; set; }
    public ClaimStatus Status { get; set; }
    public string CoverageStatus { get; set; } = "Pending";
    public string? CoverageReason { get; set; }
    public Guid? AssignedAdjusterId { get; set; }
    // La escalación es una marca: el siniestro sigue su flujo normal aunque haya vencido un SLA.
    public DateTimeOffset? EscalatedAt { get; set; }
    public string? EscalationReason { get; set; }
    public DateTimeOffset ReportedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
    public List<ClaimTimelineEntry> Timeline { get; set; } = [];

    public bool IsFinished => Status is ClaimStatus.Closed or ClaimStatus.Cancelled;

    // Asignación o reasignación decidida por Dispatch. Se ignora si el ajustador ya llegó o el siniestro terminó.
    public bool TryAssign(Guid adjusterId, DateTimeOffset at, bool isReassignment)
    {
        if (IsFinished || Status is ClaimStatus.AdjusterArrived or ClaimStatus.InProgress) return false;
        var details = isReassignment ? $"Adjuster {adjusterId} reassigned." : $"Adjuster {adjusterId} assigned.";
        Transition(ClaimStatus.Assigned, at, isReassignment ? "adjuster.reassigned" : "adjuster.assigned", details, () => AssignedAdjusterId = adjusterId);
        return true;
    }

    public void Arrive(Guid adjusterId, DateTimeOffset at)
    {
        EnsureAssignedTo(adjusterId);
        if (Status != ClaimStatus.Assigned) throw new ClaimStateException("Sólo se puede registrar la llegada a un siniestro asignado.");
        Transition(ClaimStatus.AdjusterArrived, at, "adjuster.arrived", "Adjuster arrived at incident.");
    }

    public void Start(Guid adjusterId, DateTimeOffset at)
    {
        EnsureAssignedTo(adjusterId);
        if (Status != ClaimStatus.AdjusterArrived) throw new ClaimStateException("La atención sólo puede iniciar después de registrar la llegada.");
        Transition(ClaimStatus.InProgress, at, "service.started", "On-site service started.");
    }

    public void Complete(Guid adjusterId, DateTimeOffset at)
    {
        EnsureAssignedTo(adjusterId);
        if (Status != ClaimStatus.InProgress) throw new ClaimStateException("El servicio sólo puede finalizar después de iniciar la atención.");
        Transition(ClaimStatus.Closed, at, "service.completed", "Claim service completed.");
    }

    public void Cancel(string reason, DateTimeOffset at)
    {
        if (IsFinished) throw new ClaimStateException("Un siniestro cerrado o cancelado ya no puede cambiar.");
        Transition(ClaimStatus.Cancelled, at, "claim.cancelled", reason);
    }

    public void Escalate(string reason, DateTimeOffset at)
    {
        if (IsFinished) return;
        EscalatedAt ??= at;
        EscalationReason = reason;
        UpdatedAt = at;
        Version++;
        Timeline.Add(NewTimeline("claim.escalated", reason, at));
    }

    public void SetCoverage(string status, string? reason, DateTimeOffset at)
    {
        if (IsFinished) return;
        CoverageStatus = status;
        CoverageReason = reason;
        UpdatedAt = at;
        Version++;
        Timeline.Add(NewTimeline("policy.validated", reason ?? status, at));
    }

    public void RecordDispatchIssue(string reason, DateTimeOffset at) => Timeline.Add(NewTimeline("dispatch.unavailable", reason, at));

    private void Transition(ClaimStatus next, DateTimeOffset at, string type, string details, Action? effect = null)
    {
        if (IsFinished) throw new ClaimStateException("Un siniestro cerrado o cancelado ya no puede cambiar.");
        effect?.Invoke();
        Status = next;
        UpdatedAt = at;
        Version++;
        Timeline.Add(NewTimeline(type, details, at));
    }

    private void EnsureAssignedTo(Guid adjusterId)
    {
        if (AssignedAdjusterId != adjusterId) throw new UnauthorizedAccessException("The adjuster is not assigned to this claim.");
    }

    private ClaimTimelineEntry NewTimeline(string type, string details, DateTimeOffset at) => new() { Id = Guid.NewGuid(), ClaimId = Id, Type = type, Details = details, OccurredAt = at };
}

public sealed class ClaimTimelineEntry
{
    public Guid Id { get; set; }
    public Guid ClaimId { get; set; }
    public string Type { get; set; } = default!;
    public string Details { get; set; } = default!;
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class ClaimStateException(string message) : InvalidOperationException(message);
