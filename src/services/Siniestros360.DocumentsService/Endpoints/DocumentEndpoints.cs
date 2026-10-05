using Siniestros360.ServiceDefaults.Endpoints;
using Siniestros360.SharedKernel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using MassTransit;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Siniestros360.Contracts.Events;
using Siniestros360.DocumentsService.Domain;
using Siniestros360.DocumentsService.Infrastructure;
using Siniestros360.Contracts.Common;
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

    internal sealed class ListClaimDocumentsEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/claims/{claimId:guid}", ListAsync)
                .WithName("ListClaimDocuments")
                .WithSummary("Evidencias vigentes de un siniestro");
        }
    }

    internal sealed class DownloadDocumentEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/{documentId:guid}", DownloadAsync)
                .WithName("DownloadDocument")
                .WithSummary("Descargar una evidencia");
        }
    }

    internal sealed class UploadDocumentEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/claims/{claimId:guid}", UploadAsync)
                .DisableAntiforgery()
                .WithName("UploadDocument")
                .WithSummary("Subir una evidencia (JPEG, PNG o PDF de hasta 15 MB)");
        }
    }

    internal sealed class DeleteDocumentEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapDelete("/{documentId:guid}", DeleteAsync)
                .RequireAuthorization(Policies.FieldOperations)
                .WithName("DeleteDocument")
                .WithSummary("Borrar una evidencia")
                .WithDescription("Borrado lógico: la metadata se conserva con DeletedAt y sólo se elimina el objeto almacenado.");
        }
    }

    private static async Task<Results<Ok<List<DocumentResponse>>, NotFound>> ListAsync(
        Guid claimId,
        IUserContext userContext,
        DocumentsDbContext database,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessAsync(database, claimId, userContext, cancellationToken)) return TypedResults.NotFound();
        var documents = await database.Documents.AsNoTracking()
            .Where(x => x.ClaimId == claimId && x.DeletedAt == null)
            .OrderBy(x => x.UploadedAt)
            .Select(x => new DocumentResponse(x.Id, x.ClaimId, x.FileName, x.ContentType, x.SizeBytes, x.UploadedAt))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(documents);
    }

    private static async Task<Results<FileStreamHttpResult, NotFound>> DownloadAsync(
        Guid documentId,
        IUserContext userContext,
        DocumentsDbContext database,
        IObjectStorage objectStorage,
        CancellationToken cancellationToken)
    {
        var document = await database.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId && x.DeletedAt == null, cancellationToken);
        if (document is null || !await CanAccessAsync(database, document.ClaimId, userContext, cancellationToken)) return TypedResults.NotFound();
        return TypedResults.Stream(await objectStorage.DownloadAsync("claims", document.StorageKey, cancellationToken), document.ContentType, document.FileName);
    }

    [Idempotent]
    private static async Task<Results<Created<DocumentResponse>, ValidationProblem, NotFound>> UploadAsync(
        Guid claimId,
        IFormFile file,
        HttpContext httpContext,
        IUserContext userContext,
        DocumentsDbContext database,
        IObjectStorage objectStorage,
        IPublishEndpoint publisher,
        RollbackActions rollbackActions,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("Documents.Upload");
        activity?.SetTag("siniestros360.claim_id", claimId);
        if (file.Length == 0 || file.Length > MaxFileBytes) return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["El archivo debe pesar entre 1 byte y 15 MB."] });
        if (!AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase)) return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Sólo se aceptan JPEG, PNG y PDF."] });
        if (!await CanAccessAsync(database, claimId, userContext, cancellationToken)) return TypedResults.NotFound();

        var documentId = Guid.NewGuid();
        var fileName = Path.GetFileName(file.FileName);
        var objectName = $"{claimId}/{documentId}-{fileName}";
        await using (var stream = file.OpenReadStream())
        {
            await objectStorage.UploadAsync("claims", objectName, stream, file.ContentType, cancellationToken);
        }

        // El almacenamiento no participa en la transacción: si el filtro idempotente revierte, se borra el objeto subido.
        // TODO(2026-09-30): conciliación periódica de objetos sin metadatos — cubre la caída del proceso entre la carga y el commit, que RollbackActions no alcanza.
        rollbackActions.Register("Borrar objeto subido", token => objectStorage.DeleteAsync("claims", objectName, token));

        var now = dateTimeProvider.UtcNow;
        var document = new ClaimDocument { Id = documentId, ClaimId = claimId, FileName = fileName, ContentType = file.ContentType, SizeBytes = file.Length, StorageKey = objectName, UploadedAt = now };
        database.Documents.Add(document);
        await publisher.PublishCorrelated(new DocumentUploaded(documentId, claimId, document.FileName, document.ContentType, document.SizeBytes, now), Correlation.From(httpContext), cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        Uploaded.Add(1);
        return TypedResults.Created(ApiVersions.V1Path($"documents/{documentId}"), DocumentResponse.From(document));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid documentId,
        HttpContext httpContext,
        IUserContext userContext,
        DocumentsDbContext database,
        IObjectStorage objectStorage,
        IPublishEndpoint publisher,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        var document = await database.Documents.SingleOrDefaultAsync(x => x.Id == documentId && x.DeletedAt == null, cancellationToken);
        if (document is null || !await CanAccessAsync(database, document.ClaimId, userContext, cancellationToken)) return TypedResults.NotFound();
        await objectStorage.DeleteAsync("claims", document.StorageKey, cancellationToken);
        document.DeletedAt = dateTimeProvider.UtcNow;
        await publisher.PublishCorrelated(new DocumentDeleted(document.Id, document.ClaimId, document.DeletedAt.Value), Correlation.From(httpContext), cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    // Sin proyección todavía (el evento del siniestro no ha llegado) nadie accede; con ella decide la política.
    private static async Task<bool> CanAccessAsync(
        DocumentsDbContext database,
        Guid claimId,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var access = await database.ClaimAccess.AsNoTracking().SingleOrDefaultAsync(x => x.ClaimId == claimId, cancellationToken);
        return access is not null && await userContext.CanAccessClaimAsync(new ClaimParticipants(access.InsuredId, access.AdjusterId));
    }
}
