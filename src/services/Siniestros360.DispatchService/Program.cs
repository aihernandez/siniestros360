using MassTransit;
using Microsoft.EntityFrameworkCore;
using Siniestros360.DispatchService.Application;
using Siniestros360.DispatchService.Domain;
using Siniestros360.DispatchService.Infrastructure;
using Siniestros360.Contracts.Common;
using Siniestros360.Messaging.Idempotency;
using Siniestros360.ServiceDefaults.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();
// En este ensamblado: el generador de validación sólo descubre los tipos de los endpoints del proyecto que lo llama.
builder.Services.AddValidation();
builder.Services.AddDbContext<DispatchDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("dispatchdb");
    if (string.IsNullOrWhiteSpace(connection)) options.UseInMemoryDatabase("dispatch-development"); else options.UseNpgsql(connection);
});
builder.Services.AddScoped<AdjusterReservations>();
builder.AddReliableMessaging<DispatchDbContext>("dispatch-service", bus =>
{
    bus.AddConsumer<AdjusterProjectionConsumer>();
    // La saga comparte DbContext y transacción con las reservas y el outbox; xmin resuelve la concurrencia optimista.
    bus.AddSagaStateMachine<AssignmentStateMachine, AssignmentState>()
        .EntityFrameworkRepository(repository =>
        {
            repository.ConcurrencyMode = ConcurrencyMode.Optimistic;
            repository.ExistingDbContext<DispatchDbContext>();
            repository.UsePostgres();
        });
}, typeof(AdjusterLocationConsumer));
builder.Services.AddEndpoints(typeof(Program).Assembly);

var app = builder.Build();
app.UseSiniestrosApiDefaults();
await app.InitializeDatabaseAsync<DispatchDbContext>();

app.MapEndpoints(app.MapApiVersion("dispatch/claims", ApiVersions.V1)
    .WithTags("Dispatch")
    .RequireAuthorization(Policies.ControlTower)
    .WithIdempotency());

app.Run();

public partial class Program;
