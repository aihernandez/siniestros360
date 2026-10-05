using Microsoft.EntityFrameworkCore;
using Siniestros360.ClaimsService.Application;
using Siniestros360.ClaimsService.Endpoints;
using Siniestros360.ClaimsService.Infrastructure;
using Siniestros360.Messaging.Idempotency;

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();
// En este ensamblado: el generador de validación sólo descubre los tipos de los endpoints del proyecto que lo llama.
builder.Services.AddValidation();
builder.Services.AddDbContext<ClaimsDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("claimsdb");
    if (string.IsNullOrWhiteSpace(connectionString)) options.UseInMemoryDatabase("claims-development");
    else options.UseNpgsql(connectionString);
});
builder.AddReliableMessaging<ClaimsDbContext>("claims-service", bus => bus.AddConsumer<ClaimEventsConsumer>());

var app = builder.Build();
app.UseSiniestrosApiDefaults();
await app.InitializeDatabaseAsync<ClaimsDbContext>();

// Comandos y consultas viven en Endpoints/; aquí sólo se componen. La política por defecto exige autenticación y
// cada endpoint declara la suya.
app.MapGroup("/api/v1/claims")
    .WithTags("Claims")
    .WithIdempotency()
    .MapClaimQueries()
    .MapClaimCommands();

app.Run();

public partial class Program;
