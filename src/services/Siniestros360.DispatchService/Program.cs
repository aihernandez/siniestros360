using MassTransit;
using Microsoft.EntityFrameworkCore;
using Siniestros360.DispatchService.Application;
using Siniestros360.DispatchService.Domain;
using Siniestros360.DispatchService.Endpoints;
using Siniestros360.DispatchService.Infrastructure;
using Siniestros360.Messaging.Idempotency;

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

var app = builder.Build();
app.UseSiniestrosApiDefaults();
await app.InitializeDatabaseAsync<DispatchDbContext>();

app.MapGroup("/api/v1/dispatch/claims")
    .WithTags("Dispatch")
    .WithIdempotency()
    .MapDispatchEndpoints();

app.Run();

public partial class Program;
