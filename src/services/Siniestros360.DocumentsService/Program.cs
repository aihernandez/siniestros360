using System.Diagnostics;
using System.Diagnostics.Metrics;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Siniestros360.Contracts.Events;
using Siniestros360.Contracts.Messaging;
using Siniestros360.DocumentsService.Application;
using Siniestros360.DocumentsService.Domain;
using Siniestros360.DocumentsService.Infrastructure;
using Siniestros360.DocumentsService.Storage;
using Siniestros360.Messaging;
using Siniestros360.Messaging.Idempotency;

const long MaxFileBytes = 15 * 1024 * 1024;
string[] allowedContentTypes = ["image/jpeg", "image/png", "application/pdf"];
var activitySource = new ActivitySource("Siniestros360.DocumentsService");
var uploaded = new Meter("Siniestros360.DocumentsService").CreateCounter<long>("documents.uploaded.count");

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();
builder.Services.AddDbContext<DocumentsDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("documentsdb");
    if (string.IsNullOrWhiteSpace(connection)) options.UseInMemoryDatabase("documents-development"); else options.UseNpgsql(connection);
});
builder.Services.AddHttpClient<AzureBlobObjectStorage>().AddStandardResilienceHandler();
builder.Services.AddScoped<IObjectStorage>(services => builder.Configuration.GetValue("Storage:UseAzureBlob", false)
    ? services.GetRequiredService<AzureBlobObjectStorage>()
    : ActivatorUtilities.CreateInstance<LocalObjectStorage>(services));
builder.AddReliableMessaging<DocumentsDbContext>("documents-service", bus => bus.AddConsumer<ClaimAccessConsumer>());

var app = builder.Build();
app.UseSiniestrosApiDefaults();
await app.InitializeDatabaseAsync<DocumentsDbContext>();

var api = app.MapGroup("/api/v1/documents").RequireAuthorization().WithIdempotency();

api.MapPost("/claims/{claimId:guid}", [Idempotent] async (Guid claimId, IFormFile file, HttpContext http, UserContext user, DocumentsDbContext db, IObjectStorage storage, IPublishEndpoint publish, RollbackActions rollbackActions, CancellationToken ct) =>
{
    using var activity = activitySource.StartActivity("Documents.Upload");
    activity?.SetTag("siniestros360.claim_id", claimId);
    if (file.Length == 0 || file.Length > MaxFileBytes) return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["File must contain 1 byte to 15 MB."] });
    if (!allowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Only JPEG, PNG and PDF are allowed."] });
    if (!await CanAccess(db, claimId, user, ct)) return Results.NotFound();

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
    uploaded.Add(1);
    return Results.Created($"/api/v1/documents/{id}", DocumentDto.From(document));
}).DisableAntiforgery();

api.MapGet("/claims/{claimId:guid}", async (Guid claimId, UserContext user, DocumentsDbContext db, CancellationToken ct) =>
{
    if (!await CanAccess(db, claimId, user, ct)) return Results.NotFound();
    var documents = await db.Documents.AsNoTracking().Where(x => x.ClaimId == claimId && x.DeletedAt == null).OrderBy(x => x.UploadedAt).ToListAsync(ct);
    return Results.Ok(documents.Select(DocumentDto.From));
});

api.MapGet("/{id:guid}", async (Guid id, UserContext user, DocumentsDbContext db, IObjectStorage storage, CancellationToken ct) =>
{
    var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct);
    if (document is null || !await CanAccess(db, document.ClaimId, user, ct)) return Results.NotFound();
    return Results.Stream(await storage.DownloadAsync("claims", document.StorageKey, ct), document.ContentType, document.FileName);
});

// Soft delete: la metadata se conserva con DeletedAt y sólo se elimina el objeto almacenado.
api.MapDelete("/{id:guid}", async (Guid id, HttpContext http, UserContext user, DocumentsDbContext db, IObjectStorage storage, IPublishEndpoint publish, CancellationToken ct) =>
{
    var document = await db.Documents.SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct);
    if (document is null || !await CanAccess(db, document.ClaimId, user, ct)) return Results.NotFound();
    await storage.DeleteAsync("claims", document.StorageKey, ct);
    document.DeletedAt = DateTimeOffset.UtcNow;
    await publish.PublishCorrelated(new DocumentDeleted(document.Id, document.ClaimId, document.DeletedAt.Value), Correlation.From(http), ct);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
}).RequireAuthorization(new AuthorizeAttribute { Roles = $"{Roles.Adjuster},{Roles.ControlTower},{Roles.Admin}" });

app.Run();

// Torre y admin ven todo; el ajustador sólo siniestros asignados a él; el asegurado sólo los suyos.
static async Task<bool> CanAccess(DocumentsDbContext db, Guid claimId, UserContext user, CancellationToken ct)
{
    var access = await db.ClaimAccess.AsNoTracking().SingleOrDefaultAsync(x => x.ClaimId == claimId, ct);
    if (access is null) return false;
    if (user.IsInRole(Roles.ControlTower) || user.IsInRole(Roles.Admin)) return true;
    if (user.IsInRole(Roles.Adjuster)) return user.AdjusterId is not null && access.AdjusterId == user.AdjusterId;
    return access.InsuredId == user.UserId;
}

public sealed record DocumentDto(Guid Id, Guid ClaimId, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt)
{
    public static DocumentDto From(ClaimDocument document) => new(document.Id, document.ClaimId, document.FileName, document.ContentType, document.SizeBytes, document.UploadedAt);
}

public partial class Program;
