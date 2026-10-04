namespace Siniestros360.Contracts.Events;

// Validación asíncrona de póliza: Claims la pide y Policy responde.
// El namespace es el mismo en todas las carpetas: forma la URN de MassTransit y el nombre del topic.
public sealed record PolicyValidationRequested(Guid ClaimId, string PolicyNumber, DateTimeOffset RequestedAt);
public sealed record PolicyValidationCompleted(Guid ClaimId, string PolicyNumber, string CoverageStatus, string? Reason, DateTimeOffset ValidatedAt);
public sealed record PolicyValidationFailed(Guid ClaimId, string PolicyNumber, string ErrorCode, string ErrorMessage, DateTimeOffset FailedAt);
public sealed record PolicyValidationUnavailable(Guid ClaimId, string PolicyNumber, string Reason, DateTimeOffset OccurredAt);
