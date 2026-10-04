namespace Siniestros360.Contracts.Events;

// Evidencias del siniestro.
// El namespace es el mismo en todas las carpetas: forma la URN de MassTransit y el nombre del topic.
public sealed record DocumentUploaded(Guid DocumentId, Guid ClaimId, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt);
public sealed record DocumentDeleted(Guid DocumentId, Guid ClaimId, DateTimeOffset DeletedAt);
