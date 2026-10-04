namespace Siniestros360.Contracts.Events;

// Plazos por etapa, avisos, vencimientos y escalación.
// El namespace es el mismo en todas las carpetas: forma la URN de MassTransit y el nombre del topic.
public sealed record SlaStageStarted(Guid ClaimId, string Stage, DateTimeOffset StartedAt, DateTimeOffset DueAt);
public sealed record SlaWarningRaised(Guid ClaimId, string WarningType, string Message, DateTimeOffset RaisedAt);
public sealed record SlaBreached(Guid ClaimId, string SlaType, string Message, DateTimeOffset BreachedAt);
public sealed record ClaimEscalated(Guid ClaimId, string Reason, DateTimeOffset EscalatedAt);
