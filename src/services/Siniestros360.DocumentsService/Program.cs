using Microsoft.EntityFrameworkCore;
using Siniestros360.DocumentsService.Application;
using Siniestros360.DocumentsService.Endpoints;
using Siniestros360.DocumentsService.Infrastructure;
using Siniestros360.DocumentsService.Infrastructure.Storage;
using Siniestros360.Messaging.Idempotency;


var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();
// En este ensamblado: el generador de validación sólo descubre los tipos de los endpoints del proyecto que lo llama.
builder.Services.AddValidation();
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

app.MapGroup("/api/v1/documents")
    .WithTags("Documents")
    .WithIdempotency()
    .MapDocumentEndpoints();

app.Run();

public partial class Program;
