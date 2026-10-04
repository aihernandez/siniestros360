namespace Siniestros360.PolicyService.Domain;

public sealed class PolicyValidation
{
    public Guid ClaimId { get; set; }
    public string PolicyNumber { get; set; } = default!;
    public string Status { get; set; } = default!;
    public string? Reason { get; set; }
    public DateTimeOffset ValidatedAt { get; set; }
}

public sealed class ExternalValidationAttempt
{
    public Guid Id { get; set; }
    public Guid ClaimId { get; set; }
    public string Result { get; set; } = default!;
    public string? Error { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
