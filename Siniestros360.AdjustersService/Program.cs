using System.Diagnostics.Metrics;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Siniestros360.AdjustersService.Application;
using Siniestros360.AdjustersService.Domain;
using Siniestros360.AdjustersService.Infrastructure;
using Siniestros360.Contracts.Events;
using Siniestros360.Contracts.Messaging;
using Siniestros360.Messaging;

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();
builder.Services.AddDbContext<AdjustersDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("adjustersdb");
    if (string.IsNullOrWhiteSpace(connection)) options.UseInMemoryDatabase("adjusters-development"); else options.UseNpgsql(connection);
});
builder.AddReliableMessaging<AdjustersDbContext>("adjusters-service", bus => bus.AddConsumer<AdjusterEventsConsumer>());

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

var api = app.MapGroup("/api/v1/adjusters").RequireAuthorization();
api.MapGet("/", async (AdjustersDbContext db, CancellationToken ct) => await db.Adjusters.AsNoTracking().OrderBy(x => x.DisplayName).ToListAsync(ct))
    .RequireAuthorization(new AuthorizeAttribute { Roles = $"{Roles.ControlTower},{Roles.Admin}" });
api.MapGet("/{id:guid}", async (Guid id, UserContext user, AdjustersDbContext db, CancellationToken ct) =>
{
    if (user.IsInRole(Roles.Adjuster) && user.AdjusterId != id) return Results.NotFound();
    var value = await db.Adjusters.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    return value is null ? Results.NotFound() : Results.Ok(value);
}).RequireAuthorization(new AuthorizeAttribute { Roles = $"{Roles.Adjuster},{Roles.ControlTower},{Roles.Admin}" });

// PUT de valor absoluto: repetirlo deja el mismo estado, así que no requiere Idempotency-Key.
api.MapPut("/{id:guid}/availability", async (Guid id, AvailabilityRequest request, HttpContext http, UserContext user, AdjustersDbContext db, IPublishEndpoint publish, CancellationToken ct) =>
{
    if (user.IsInRole(Roles.Adjuster) && user.AdjusterId != id) return Results.Forbid();
    var entity = await db.Adjusters.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (entity is null) return Results.NotFound();
    if (entity.ActiveClaimId is not null) return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The adjuster has an active claim.");
    var now = DateTimeOffset.UtcNow;
    var previous = entity.ChangeStatus(request.IsAvailable ? AdjusterStatus.Available : AdjusterStatus.Offline, null, now);
    var correlation = Correlation.From(http);
    await publish.PublishCorrelated(new AdjusterAvailabilityChanged(id, entity.IsAvailable, now), correlation, ct);
    if (previous is not null) await publish.PublishCorrelated(new AdjusterStatusChanged(id, previous.Value.ToString(), entity.Status.ToString(), now), correlation, ct);
    await db.SaveChangesAsync(ct);
    return Results.Ok(entity);
}).RequireAuthorization(new AuthorizeAttribute { Roles = $"{Roles.Adjuster},{Roles.ControlTower},{Roles.Admin}" });

api.MapPut("/{id:guid}/status", async (Guid id, StatusRequest request, HttpContext http, AdjustersDbContext db, IPublishEndpoint publish, CancellationToken ct) =>
{
    var entity = await db.Adjusters.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (entity is null) return Results.NotFound();
    var now = DateTimeOffset.UtcNow;
    var wasAvailable = entity.IsAvailable;
    var previous = entity.ChangeStatus(request.Status, entity.ActiveClaimId, now);
    var correlation = Correlation.From(http);
    if (previous is not null) await publish.PublishCorrelated(new AdjusterStatusChanged(id, previous.Value.ToString(), entity.Status.ToString(), now), correlation, ct);
    if (wasAvailable != entity.IsAvailable) await publish.PublishCorrelated(new AdjusterAvailabilityChanged(id, entity.IsAvailable, now), correlation, ct);
    await db.SaveChangesAsync(ct);
    return Results.Ok(entity);
}).RequireAuthorization(new AuthorizeAttribute { Roles = $"{Roles.ControlTower},{Roles.Admin}" });

app.Run();

// El seed publica por el bus outbox del mismo scope: el registro y sus eventos se confirman juntos.
static async Task SeedDemoAdjusters(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AdjustersDbContext>();
    var publish = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
    var now = DateTimeOffset.UtcNow;
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

public sealed record AvailabilityRequest(bool IsAvailable);
public sealed record StatusRequest(AdjusterStatus Status);
public partial class Program;
