using Siniestros360.SharedKernel;

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

    // Alta del siniestro: folio inmediato, en espera de asignación. La póliza se valida después, por eventos.
    public static Claim Report(string insuredId, string policyNumber, string vehiclePlate, string incidentType, decimal latitude, decimal longitude, bool requiresAmbulance, DateTimeOffset at)
    {
        var id = Guid.NewGuid();
        var claim = new Claim
        {
            Id = id,
            InsuredId = insuredId,
            Folio = $"SIN-{at:yyyy}-{id.ToString("N")[..8].ToUpperInvariant()}",
            PolicyNumber = policyNumber,
            VehiclePlate = vehiclePlate,
            IncidentType = incidentType,
            Latitude = latitude,
            Longitude = longitude,
            RequiresAmbulance = requiresAmbulance,
            Status = ClaimStatus.AssignmentPending,
            ReportedAt = at,
            UpdatedAt = at,
            Version = 1,
        };
        claim.Timeline.Add(claim.NewTimeline("claim.reported", "Claim reported.", at));
        return claim;
    }

    // Asignación o reasignación decidida por Dispatch. Se ignora si el ajustador ya llegó o el siniestro terminó.
    public bool TryAssign(Guid adjusterId, DateTimeOffset at, bool isReassignment)
    {
        if (IsFinished || Status is ClaimStatus.AdjusterArrived or ClaimStatus.InProgress) return false;
        var details = isReassignment ? $"Adjuster {adjusterId} reassigned." : $"Adjuster {adjusterId} assigned.";
        Transition(ClaimStatus.Assigned, at, isReassignment ? "adjuster.reassigned" : "adjuster.assigned", details, () => AssignedAdjusterId = adjusterId);
        return true;
    }

    // Las transiciones del ajustador devuelven Result: una regla incumplida es un resultado esperado (409 o 403), no una
    // excepción. Primero se comprueba que el siniestro siga abierto, luego quién actúa y por último el estado de origen.
    public Result Arrive(Guid adjusterId, DateTimeOffset at)
        => Advance(adjusterId, ClaimStatus.Assigned, ClaimErrors.ArrivalRequiresAssignment, ClaimStatus.AdjusterArrived, at, "adjuster.arrived", "Adjuster arrived at incident.");

    public Result Start(Guid adjusterId, DateTimeOffset at)
        => Advance(adjusterId, ClaimStatus.AdjusterArrived, ClaimErrors.StartRequiresArrival, ClaimStatus.InProgress, at, "service.started", "On-site service started.");

    public Result Complete(Guid adjusterId, DateTimeOffset at)
        => Advance(adjusterId, ClaimStatus.InProgress, ClaimErrors.CompleteRequiresStart, ClaimStatus.Closed, at, "service.completed", "Claim service completed.");

    public Result Cancel(string reason, DateTimeOffset at)
    {
        if (IsFinished) return ClaimErrors.AlreadyFinished;
        Transition(ClaimStatus.Cancelled, at, "claim.cancelled", reason);
        return Result.Success();
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

    private Result Advance(Guid adjusterId, ClaimStatus requiredStatus, Error wrongStatus, ClaimStatus next, DateTimeOffset at, string type, string details)
    {
        if (IsFinished) return ClaimErrors.AlreadyFinished;
        if (AssignedAdjusterId != adjusterId) return ClaimErrors.NotAssignedAdjuster;
        if (Status != requiredStatus) return wrongStatus;
        Transition(next, at, type, details);
        return Result.Success();
    }

    // Quien llama ya comprobó que el siniestro sigue abierto.
    private void Transition(ClaimStatus next, DateTimeOffset at, string type, string details, Action? effect = null)
    {
        effect?.Invoke();
        Status = next;
        UpdatedAt = at;
        Version++;
        Timeline.Add(NewTimeline(type, details, at));
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
