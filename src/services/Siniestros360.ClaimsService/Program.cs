using Siniestros360.ClaimsService.Application;
using Siniestros360.ClaimsService.Infrastructure;
using Siniestros360.Messaging.Idempotency;
using Siniestros360.ServiceDefaults.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();
builder.Services.AddClaimsApplication();
builder.AddClaimsInfrastructure();
builder.Services.AddEndpoints(typeof(Program).Assembly);

var app = builder.Build();
app.UseSiniestrosApiDefaults();
await app.InitializeDatabaseAsync<ClaimsDbContext>();

// Cada caso de uso es una clase IEndpoint en Endpoints/Claims; se descubren por reflexión y se mapean sobre el grupo.
app.MapEndpoints(app.MapGroup("/api/v1/claims").WithTags("Claims").WithIdempotency());

app.Run();

public partial class Program;
