namespace Siniestros360.DocumentsService.Domain;

public sealed class ClaimDocument
{
    public Guid Id { get; set; }
    public Guid ClaimId { get; set; }
    public string FileName { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long SizeBytes { get; set; }
    public string StorageKey { get; set; } = default!;
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

// Proyección local de quién puede ver cada siniestro: asegurado dueño y ajustador asignado.
public sealed class ClaimAccessProjection
{
    public Guid ClaimId { get; set; }
    public string InsuredId { get; set; } = default!;
    public Guid? AdjusterId { get; set; }
}
