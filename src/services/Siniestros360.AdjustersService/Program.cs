using Siniestros360.SharedKernel;
using System.Diagnostics.Metrics;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Siniestros360.AdjustersService.Application;
using Siniestros360.AdjustersService.Domain;
using Siniestros360.AdjustersService.Infrastructure;
using Siniestros360.Contracts.Common;
using Siniestros360.Contracts.Events;
using Siniestros360.Messaging;
using Siniestros360.ServiceDefaults.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();
// En este ensamblado: el generador de validación sólo descubre los tipos de los endpoints del proyecto que lo llama.
builder.Services.AddValidation();
builder.Services.AddDbContext<AdjustersDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("adjustersdb");
    if (string.IsNullOrWhiteSpace(connection)) options.UseInMemoryDatabase("adjusters-development"); else options.UseNpgsql(connection);
});
builder.AddReliableMessaging<AdjustersDbContext>("adjusters-service", bus => bus.AddConsumer<AdjusterEventsConsumer>());
builder.Services.AddEndpoints(typeof(Program).Assembly);

var meter = new Meter("Siniestros360.AdjustersService");
var app = builder.Build();
app.UseSiniestrosApiDefaults();
await app.InitializeDatabaseAsync<AdjustersDbContext>();
await SeedDemoAdjusters(app);
var scopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();
meter.CreateObservableGauge("adjusters.available.count", () =>
{
    using var scope = scopeFactory.CreateScope();
    return scope.ServiceProvider.GetRequiredService<AdjustersDbContext>().Adjusters.Count(x => x.IsAvailable);
});

app.MapEndpoints(app.MapApiVersion("adjusters", ApiVersions.V1).WithTags("Adjusters"));

app.Run();

// El seed publica por el bus outbox del mismo scope: el registro y sus eventos se confirman juntos.
static async Task SeedDemoAdjusters(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AdjustersDbContext>();
    var publish = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
    var now = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().UtcNow;
    foreach (var (id, displayName) in DemoAdjusters.All)
    {
        if (await db.Adjusters.AnyAsync(x => x.Id == id)) continue;
        var correlation = Guid.NewGuid();
        db.Adjusters.Add(new Adjuster { Id = id, DisplayName = displayName, IsAvailable = true, Status = AdjusterStatus.Available, UpdatedAt = now, Version = 1 });
        await publish.PublishCorrelated(new AdjusterRegistered(id, displayName, now), correlation, CancellationToken.None);
        await publish.PublishCorrelated(new AdjusterAvailabilityChanged(id, true, now), correlation, CancellationToken.None);
        // Sin este evento, la torre (cuya vista nace en Offline) mostraba a la unidad fuera de línea hasta su primera asignación.
        await publish.PublishCorrelated(new AdjusterStatusChanged(id, AdjusterStatus.Offline.ToString(), AdjusterStatus.Available.ToString(), now), correlation, CancellationToken.None);
    }

    await db.SaveChangesAsync();
}

public partial class Program;
