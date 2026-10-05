using System.Diagnostics;
using System.Diagnostics.Metrics;
using MassTransit;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Siniestros360.Contracts.Events;
using Siniestros360.DocumentsService.Domain;
using Siniestros360.DocumentsService.Infrastructure;
using Siniestros360.DocumentsService.Infrastructure.Storage;
using Siniestros360.Messaging;
using Siniestros360.Messaging.Idempotency;

namespace Siniestros360.DocumentsService.Endpoints;

/// <summary>Evidencia de un siniestro, sin la ruta interna de almacenamiento.</summary>
public sealed record DocumentResponse(Guid Id, Guid ClaimId, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt)
{
    public static DocumentResponse From(ClaimDocument document) => new(document.Id, document.ClaimId, document.FileName, document.ContentType, document.SizeBytes, document.UploadedAt);
}

// Documents tiene pocas operaciones y todas giran alrededor del mismo recurso: comandos y consultas en un archivo,
// separados por sección. El acceso sale de la proyección ClaimAccess, construida con los eventos del siniestro.
public static class DocumentEndpoints
{
    private const long MaxFileBytes = 15 * 1024 * 1024;
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "application/pdf"];
    private static readonly ActivitySource ActivitySource = new("Siniestros360.DocumentsService");
    private static readonly Counter<long> Uploaded = new Meter("Siniestros360.DocumentsService").CreateCounter<long>("documents.uploaded.count");

    public static RouteGroupBuilder MapDocumentEndpoints(this RouteGroupBuilder documents)
    {
        // Consultas
        documents.MapGet("/claims/{claimId:guid}", ListAsync).WithName("ListClaimDocuments").WithSummary("Evidencias vigentes de un siniestro");
        documents.MapGet("/{id:guid}", DownloadAsync).WithName("DownloadDocument").WithSummary("Descargar una evidencia");

        // Comandos
        documents.MapPost("/claims/{claimId:guid}", UploadAsync)
            .DisableAntiforgery()
            .WithName("UploadDocument")
            .WithSummary("Subir una evidencia (JPEG, PNG o PDF de hasta 15 MB)");
        documents.MapDelete("/{id:guid}", DeleteAsync)
            .RequireAuthorization(Policies.FieldOperations)
            .WithName("DeleteDocument")
            .WithSummary("Borrar una evidencia")
            .WithDescription("Borrado lógico: la metadata se conserva con DeletedAt y sólo se elimina el objeto almacenado.");
        return documents;
    }

    private static async Task<Results<Ok<List<DocumentResponse>>, NotFound>> ListAsync(Guid claimId, IUserContext user, DocumentsDbContext db, CancellationToken ct)
    {
        if (!await CanAccessAsync(db, claimId, user, ct)) return TypedResults.NotFound();
        var documents = await db.Documents.AsNoTracking()
            .Where(x => x.ClaimId == claimId && x.DeletedAt == null)
            .OrderBy(x => x.UploadedAt)
            .Select(x => new DocumentResponse(x.Id, x.ClaimId, x.FileName, x.ContentType, x.SizeBytes, x.UploadedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(documents);
    }

    private static async Task<Results<FileStreamHttpResult, NotFound>> DownloadAsync(Guid id, IUserContext user, DocumentsDbContext db, IObjectStorage storage, CancellationToken ct)
    {
        var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct);
        if (document is null || !await CanAccessAsync(db, document.ClaimId, user, ct)) return TypedResults.NotFound();
        return TypedResults.Stream(await storage.DownloadAsync("claims", document.StorageKey, ct), document.ContentType, document.FileName);
    }

    [Idempotent]
    private static async Task<Results<Created<DocumentResponse>, ValidationProblem, NotFound>> UploadAsync(
        Guid claimId, IFormFile file, HttpContext http, IUserContext user, DocumentsDbContext db, IObjectStorage storage, IPublishEndpoint publish, RollbackActions rollbackActions, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("Documents.Upload");
        activity?.SetTag("siniestros360.claim_id", claimId);
        if (file.Length == 0 || file.Length > MaxFileBytes) return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["El archivo debe pesar entre 1 byte y 15 MB."] });
        if (!AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase)) return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Sólo se aceptan JPEG, PNG y PDF."] });
        if (!await CanAccessAsync(db, claimId, user, ct)) return TypedResults.NotFound();

        var id = Guid.NewGuid();
        var fileName = Path.GetFileName(file.FileName);
        var objectName = $"{claimId}/{id}-{fileName}";
        await using (var stream = file.OpenReadStream())
        {
            await storage.UploadAsync("claims", objectName, stream, file.ContentType, ct);
        }

        // El almacenamiento no participa en la transacción: si el filtro idempotente revierte, se borra el objeto subido.
        // TODO(2026-09-30): conciliación periódica de objetos sin metadatos — cubre la caída del proceso entre la carga y el commit, que RollbackActions no alcanza.
        rollbackActions.Register("Borrar objeto subido", token => storage.DeleteAsync("claims", objectName, token));

        var now = DateTimeOffset.UtcNow;
        var document = new ClaimDocument { Id = id, ClaimId = claimId, FileName = fileName, ContentType = file.ContentType, SizeBytes = file.Length, StorageKey = objectName, UploadedAt = now };
        db.Documents.Add(document);
        await publish.PublishCorrelated(new DocumentUploaded(id, claimId, document.FileName, document.ContentType, document.SizeBytes, now), Correlation.From(http), ct);
        await db.SaveChangesAsync(ct);
        Uploaded.Add(1);
        return TypedResults.Created($"/api/v1/documents/{id}", DocumentResponse.From(document));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(Guid id, HttpContext http, IUserContext user, DocumentsDbContext db, IObjectStorage storage, IPublishEndpoint publish, CancellationToken ct)
    {
        var document = await db.Documents.SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct);
        if (document is null || !await CanAccessAsync(db, document.ClaimId, user, ct)) return TypedResults.NotFound();
        await storage.DeleteAsync("claims", document.StorageKey, ct);
        document.DeletedAt = DateTimeOffset.UtcNow;
        await publish.PublishCorrelated(new DocumentDeleted(document.Id, document.ClaimId, document.DeletedAt.Value), Correlation.From(http), ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // Sin proyección todavía (el evento del siniestro no ha llegado) nadie accede; con ella decide la política.
    private static async Task<bool> CanAccessAsync(DocumentsDbContext db, Guid claimId, IUserContext user, CancellationToken ct)
    {
        var access = await db.ClaimAccess.AsNoTracking().SingleOrDefaultAsync(x => x.ClaimId == claimId, ct);
        return access is not null && await user.CanAccessClaimAsync(new ClaimParticipants(access.InsuredId, access.AdjusterId));
    }
}
