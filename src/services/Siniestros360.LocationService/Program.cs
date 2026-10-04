using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Siniestros360.Contracts.Events;
using Siniestros360.Contracts.Common;
using Siniestros360.LocationService.Application;
using Siniestros360.LocationService.Domain;
using Siniestros360.LocationService.Infrastructure;
using Siniestros360.LocationService.Workers;
using Siniestros360.Messaging;

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();
builder.Services.AddDbContext<LocationDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("locationsdb");
    if (string.IsNullOrWhiteSpace(connection)) options.UseInMemoryDatabase("locations-development"); else options.UseNpgsql(connection, npgsql => npgsql.UseNetTopologySuite());
});
builder.AddReliableMessaging<LocationDbContext>("location-service");
builder.Services.AddScoped<ILocationEventSink, BusLocationEventSink>();
builder.Services.AddHostedService<GpsStaleWorker>();
var app = builder.Build();
app.UseSiniestrosApiDefaults();
await app.InitializeDatabaseAsync<LocationDbContext>();

var api = app.MapGroup("/api/v1/locations").RequireAuthorization();
api.MapPost("/adjusters/{adjusterId:guid}", async (Guid adjusterId, LocationUpdate request, HttpContext http, UserContext user, LocationDbContext db, ILocationEventSink sink, CancellationToken ct) =>
{
    if (user.IsInRole(Roles.Adjuster) && user.AdjusterId != adjusterId) return Results.Forbid();
    if (!IsValid(request.Latitude, request.Longitude)) return InvalidCoordinates();
    var result = await Record(adjusterId, request, Correlation.From(http), db, sink, ct);
    await db.SaveChangesAsync(ct);
    return result is null ? Results.Accepted() : Results.Ok(result);
}).RequireAuthorization(new AuthorizeAttribute { Roles = $"{Roles.Adjuster},{Roles.ControlTower},{Roles.Admin}" });

// Lote del simulador GPS. La idempotencia es natural: una posición con Sequence menor o igual a la última se ignora.
api.MapPost("/batch", async (BatchLocationRequest request, HttpContext http, LocationDbContext db, ILocationEventSink sink, CancellationToken ct) =>
{
    if (request.Locations.Any(x => !IsValid(x.Latitude, x.Longitude))) return InvalidCoordinates();
    var correlationId = Correlation.From(http);
    foreach (var item in request.Locations)
        await Record(item.AdjusterId, new LocationUpdate(item.Latitude, item.Longitude, item.SpeedKmh, item.Heading, item.Sequence, item.CapturedAt), correlationId, db, sink, ct);
    await db.SaveChangesAsync(ct);
    return Results.Accepted();
}).RequireAuthorization(new AuthorizeAttribute { Roles = $"{Roles.ControlTower},{Roles.Admin}" });

api.MapGet("/adjusters/{adjusterId:guid}/latest", async (Guid adjusterId, UserContext user, LocationDbContext db, CancellationToken ct) =>
{
    if (user.IsInRole(Roles.Adjuster) && user.AdjusterId != adjusterId) return Results.NotFound();
    var x = await db.LatestLocations.AsNoTracking().SingleOrDefaultAsync(x => x.AdjusterId == adjusterId, ct);
    return x is null ? Results.NotFound() : Results.Ok(ToResponse(x));
}).RequireAuthorization(new AuthorizeAttribute { Roles = $"{Roles.Adjuster},{Roles.ControlTower},{Roles.Admin}" });

api.MapGet("/adjusters/recent", async (LocationDbContext db, CancellationToken ct) => Results.Ok((await db.LatestLocations.AsNoTracking().ToListAsync(ct)).Select(ToResponse)))
    .RequireAuthorization(new AuthorizeAttribute { Roles = $"{Roles.ControlTower},{Roles.Admin}" });

// PROVISIONAL(2026-09-30): filtra en memoria con Haversine; basta para 5 unidades, pero a escala debe usar ST_DWithin sobre el índice GiST — desbloquea consultas con cientos de ajustadores.
api.MapGet("/adjusters/nearby", async (decimal latitude, decimal longitude, double radiusKm, LocationDbContext db, CancellationToken ct) =>
{
    var locations = await db.LatestLocations.AsNoTracking().ToListAsync(ct);
    return Results.Ok(locations.Select(x => new { location = ToResponse(x), distanceKm = Distance(latitude, longitude, (decimal)x.Position.Y, (decimal)x.Position.X) }).Where(x => x.distanceKm <= radiusKm).OrderBy(x => x.distanceKm));
}).RequireAuthorization(new AuthorizeAttribute { Roles = $"{Roles.ControlTower},{Roles.Admin}" });

app.Run();

static async Task<LocationResponse?> Record(Guid adjusterId, LocationUpdate request, Guid correlationId, LocationDbContext db, ILocationEventSink sink, CancellationToken ct)
{
    using var activity = LocationTelemetry.ActivitySource.StartActivity("Location.Update");
    var latest = db.LatestLocations.Local.FirstOrDefault(x => x.AdjusterId == adjusterId) ?? db.LatestLocations.SingleOrDefault(x => x.AdjusterId == adjusterId);
    // Manda la hora de captura; la secuencia sólo desempata. La secuencia vuelve a 1 cuando el dispositivo o el simulador
    // reinicia, así que compararla sola descartaría para siempre las posiciones nuevas.
    if (latest is not null && (request.CapturedAt < latest.CapturedAt || (request.CapturedAt == latest.CapturedAt && request.Sequence <= latest.Sequence))) return null;
    var point = new Point((double)request.Longitude, (double)request.Latitude) { SRID = 4326 };
    if (latest is null)
    {
        latest = new LatestAdjusterLocation { AdjusterId = adjusterId };
        db.LatestLocations.Add(latest);
    }
    latest.Position = point; latest.SpeedKmh = request.SpeedKmh; latest.Heading = request.Heading; latest.Sequence = request.Sequence; latest.CapturedAt = request.CapturedAt; latest.LastStaleAlertAt = null;
    db.LocationHistory.Add(new LocationHistory { Id = Guid.NewGuid(), AdjusterId = adjusterId, Position = point, SpeedKmh = request.SpeedKmh, Heading = request.Heading, Sequence = request.Sequence, CapturedAt = request.CapturedAt });
    await sink.LocationUpdated(new AdjusterLocationUpdated(adjusterId, request.Latitude, request.Longitude, request.SpeedKmh, request.Heading, request.Sequence, request.CapturedAt), correlationId, ct);
    return new LocationResponse(adjusterId, request.Latitude, request.Longitude, request.SpeedKmh, request.Heading, request.Sequence, request.CapturedAt);
}
static bool IsValid(decimal latitude, decimal longitude) => latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
static IResult InvalidCoordinates() => Results.ValidationProblem(new Dictionary<string, string[]> { ["location"] = ["Coordinates are invalid."] });
static LocationResponse ToResponse(LatestAdjusterLocation x) => new(x.AdjusterId, (decimal)x.Position.Y, (decimal)x.Position.X, x.SpeedKmh, x.Heading, x.Sequence, x.CapturedAt);
static double Distance(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
{
    const double radius = 6371;
    var dLat = (double)(lat2 - lat1) * Math.PI / 180; var dLon = (double)(lon2 - lon1) * Math.PI / 180;
    var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos((double)lat1 * Math.PI / 180) * Math.Cos((double)lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
    return radius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
}
public partial class Program;
