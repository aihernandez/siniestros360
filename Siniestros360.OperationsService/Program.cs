using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Siniestros360.Contracts.Messaging;
using Siniestros360.OperationsService.Application;
using Siniestros360.OperationsService.Hubs;
using Siniestros360.OperationsService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();
builder.Services.AddSignalR();
builder.Services.AddDbContext<OperationsDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("operationsdb");
    if (string.IsNullOrWhiteSpace(connection)) options.UseInMemoryDatabase("operations-development"); else options.UseNpgsql(connection);
});
builder.AddReliableMessaging<OperationsDbContext>("operations-service", bus => bus.AddConsumer<OperationsProjectionConsumer>(), typeof(AdjusterLocationConsumer));

var app = builder.Build();
app.UseSiniestrosApiDefaults();
await app.InitializeDatabaseAsync<OperationsDbContext>();

var api = app.MapGroup("/api/v1/operations").RequireAuthorization(new AuthorizeAttribute { Roles = $"{Roles.ControlTower},{Roles.Admin}" });
api.MapGet("/claims", async (OperationsDbContext db, CancellationToken ct) => await db.Claims.AsNoTracking().Where(x => x.Folio != "").OrderByDescending(x => x.ReportedAt).ToListAsync(ct));
// El ajustador también lee la vista de su siniestro asignado: trae la fecha límite del SLA para su contador.
app.MapGet("/api/v1/operations/claims/{id:guid}", async (Guid id, UserContext user, OperationsDbContext db, CancellationToken ct) =>
{
    var claim = await db.Claims.AsNoTracking().SingleOrDefaultAsync(x => x.ClaimId == id, ct);
    var allowed = user.IsInRole(Roles.ControlTower) || user.IsInRole(Roles.Admin) || (claim is not null && user.AdjusterId is not null && claim.AdjusterId == user.AdjusterId);
    return claim is null || !allowed ? Results.NotFound() : Results.Ok(claim);
}).RequireAuthorization(new AuthorizeAttribute { Roles = $"{Roles.Adjuster},{Roles.ControlTower},{Roles.Admin}" });
api.MapGet("/adjusters/map", async (OperationsDbContext db, CancellationToken ct) => await db.Adjusters.AsNoTracking().OrderBy(x => x.DisplayName).ToListAsync(ct));
api.MapGet("/alerts", async (OperationsDbContext db, CancellationToken ct) => await db.Alerts.AsNoTracking().OrderByDescending(x => x.RaisedAt).Take(200).ToListAsync(ct));

// Atender una alerta es idempotente: repetirlo devuelve la misma alerta con la primera fecha de atención.
api.MapPost("/alerts/{id:guid}/acknowledge", async (Guid id, UserContext user, OperationsDbContext db, IHubContext<OperationsHub> hub, CancellationToken ct) =>
{
    var alert = await db.Alerts.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (alert is null) return Results.NotFound();
    if (alert.AcknowledgedAt is null)
    {
        alert.AcknowledgedAt = DateTimeOffset.UtcNow;
        alert.AcknowledgedBy = user.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? user.UserId;
        await db.SaveChangesAsync(ct);
        await hub.Clients.Group(OperationsHub.TowerGroup).SendAsync("alertAcknowledged", alert, ct);
    }
    return Results.Ok(alert);
});

api.MapGet("/dashboard", async (OperationsDbContext db, CancellationToken ct) => Results.Ok(new
{
    total = await db.Claims.CountAsync(x => x.Folio != "", ct),
    active = await db.Claims.CountAsync(x => x.Folio != "" && x.Status != "Closed" && x.Status != "Cancelled", ct),
    alerts = await db.Alerts.CountAsync(x => x.AcknowledgedAt == null, ct),
    adjusters = await db.Adjusters.CountAsync(ct),
    available = await db.Adjusters.CountAsync(x => x.IsAvailable, ct)
}));

app.MapHub<OperationsHub>("/hubs/operations");
app.Run();

public partial class Program;
