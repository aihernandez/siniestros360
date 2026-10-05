using Siniestros360.ServiceDefaults.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Siniestros360.Contracts.Events;
using Siniestros360.LocationService.Application;
using Siniestros360.LocationService.Domain;
using Siniestros360.LocationService.Infrastructure;
using Siniestros360.Messaging;

namespace Siniestros360.LocationService.Endpoints;

/// <summary>Ajustador cercano a un punto, con su distancia en línea recta.</summary>
public sealed record NearbyAdjusterResponse(LocationResponse Location, double DistanceKm);

// GPS de los ajustadores: comandos que registran posiciones (de la app o del simulador) y consultas de la última.
// La idempotencia es natural: una posición más vieja que la guardada se ignora, así que no usan Idempotency-Key.
public static class LocationEndpoints
{
    internal sealed class RecordAdjusterLocationEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/adjusters/{adjusterId:guid}", RecordAsync)
                .RequireAuthorization(Policies.FieldOperations)
                .WithName("RecordAdjusterLocation")
                .WithSummary("Registrar una posición")
                .WithDescription("El ajustador sólo registra la suya (403 si no). 202 si la posición es más vieja que la guardada.");
        }
    }

    internal sealed class RecordLocationBatchEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/batch", RecordBatchAsync)
                .RequireAuthorization(Policies.ControlTower)
                .WithName("RecordLocationBatch")
                .WithSummary("Registrar un lote de posiciones (simulador GPS)");
        }
    }

    internal sealed class GetLatestLocationEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/adjusters/{adjusterId:guid}/latest", GetLatestAsync)
                .RequireAuthorization(Policies.FieldOperations)
                .WithName("GetLatestLocation")
                .WithSummary("Última posición de un ajustador");
        }
    }

    internal sealed class ListLatestLocationsEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/adjusters/recent", ListLatestAsync)
                .RequireAuthorization(Policies.ControlTower)
                .WithName("ListLatestLocations")
                .WithSummary("Última posición de cada ajustador");
        }
    }

    internal sealed class ListNearbyAdjustersEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/adjusters/nearby", ListNearbyAsync)
                .RequireAuthorization(Policies.ControlTower)
                .WithName("ListNearbyAdjusters")
                .WithSummary("Ajustadores dentro de un radio, del más cercano al más lejano");
        }
    }

    private static async Task<Results<Ok<LocationResponse>, Accepted, ForbidHttpResult, ValidationProblem>> RecordAsync(
        Guid adjusterId,
        LocationUpdate request,
        HttpContext httpContext,
        IUserContext userContext,
        LocationDbContext database,
        ILocationEventSink eventSink,
        CancellationToken cancellationToken)
    {
        if (!await userContext.CanActAsAdjusterAsync(adjusterId)) return TypedResults.Forbid();
        if (!IsValid(request.Latitude, request.Longitude)) return InvalidCoordinates();
        var result = await RecordPositionAsync(adjusterId, request, Correlation.From(httpContext), database, eventSink, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        return result is null ? TypedResults.Accepted((string?)null) : TypedResults.Ok(result);
    }

    private static async Task<Results<Accepted, ValidationProblem>> RecordBatchAsync(
        BatchLocationRequest request,
        HttpContext httpContext,
        LocationDbContext database,
        ILocationEventSink eventSink,
        CancellationToken cancellationToken)
    {
        if (request.Locations.Any(x => !IsValid(x.Latitude, x.Longitude))) return InvalidCoordinates();
        var correlationId = Correlation.From(httpContext);
        foreach (var item in request.Locations)
            await RecordPositionAsync(item.AdjusterId, new LocationUpdate(item.Latitude, item.Longitude, item.SpeedKmh, item.Heading, item.Sequence, item.CapturedAt), correlationId, database, eventSink, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<Ok<LocationResponse>, NotFound>> GetLatestAsync(
        Guid adjusterId,
        IUserContext userContext,
        LocationDbContext database,
        CancellationToken cancellationToken)
    {
        if (!await userContext.CanActAsAdjusterAsync(adjusterId)) return TypedResults.NotFound();
        var latest = await database.LatestLocations.AsNoTracking().SingleOrDefaultAsync(x => x.AdjusterId == adjusterId, cancellationToken);
        return latest is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(latest));
    }

    private static async Task<Ok<List<LocationResponse>>> ListLatestAsync(
        LocationDbContext database,
        CancellationToken cancellationToken)
    {
        var locations = await database.LatestLocations.AsNoTracking().ToListAsync(cancellationToken);
        var response = locations.Select(ToResponse).ToList();

        return TypedResults.Ok(response);
    }

    // PROVISIONAL(2026-09-30): filtra en memoria con Haversine; basta para 5 unidades, pero a escala debe usar ST_DWithin sobre el índice GiST — desbloquea consultas con cientos de ajustadores.
    private static async Task<Ok<List<NearbyAdjusterResponse>>> ListNearbyAsync(
        decimal latitude,
        decimal longitude,
        double radiusKm,
        LocationDbContext database,
        CancellationToken cancellationToken)
    {
        var locations = await database.LatestLocations.AsNoTracking().ToListAsync(cancellationToken);
        return TypedResults.Ok(locations
            .Select(x => new NearbyAdjusterResponse(ToResponse(x), Distance(latitude, longitude, (decimal)x.Position.Y, (decimal)x.Position.X)))
            .Where(x => x.DistanceKm <= radiusKm)
            .OrderBy(x => x.DistanceKm)
            .ToList());
    }

    private static async Task<LocationResponse?> RecordPositionAsync(
        Guid adjusterId,
        LocationUpdate request,
        Guid correlationId,
        LocationDbContext database,
        ILocationEventSink eventSink,
        CancellationToken cancellationToken)
    {
        using var activity = LocationTelemetry.ActivitySource.StartActivity("Location.Update");
        var latest = database.LatestLocations.Local.FirstOrDefault(x => x.AdjusterId == adjusterId) ?? await database.LatestLocations.SingleOrDefaultAsync(x => x.AdjusterId == adjusterId, cancellationToken);
        // Manda la hora de captura; la secuencia sólo desempata. La secuencia vuelve a 1 cuando el dispositivo o el simulador
        // reinicia, así que compararla sola descartaría para siempre las posiciones nuevas.
        if (latest is not null && (request.CapturedAt < latest.CapturedAt || (request.CapturedAt == latest.CapturedAt && request.Sequence <= latest.Sequence))) return null;
        var point = new Point((double)request.Longitude, (double)request.Latitude) { SRID = 4326 };
        if (latest is null)
        {
            latest = new LatestAdjusterLocation { AdjusterId = adjusterId };
            database.LatestLocations.Add(latest);
        }
        latest.Position = point; latest.SpeedKmh = request.SpeedKmh; latest.Heading = request.Heading; latest.Sequence = request.Sequence; latest.CapturedAt = request.CapturedAt; latest.LastStaleAlertAt = null;
        database.LocationHistory.Add(new LocationHistory { Id = Guid.NewGuid(), AdjusterId = adjusterId, Position = point, SpeedKmh = request.SpeedKmh, Heading = request.Heading, Sequence = request.Sequence, CapturedAt = request.CapturedAt });
        await eventSink.LocationUpdated(new AdjusterLocationUpdated(adjusterId, request.Latitude, request.Longitude, request.SpeedKmh, request.Heading, request.Sequence, request.CapturedAt), correlationId, cancellationToken);
        return new LocationResponse(adjusterId, request.Latitude, request.Longitude, request.SpeedKmh, request.Heading, request.Sequence, request.CapturedAt);
    }

    private static bool IsValid(
        decimal latitude,
        decimal longitude) => latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    private static ValidationProblem InvalidCoordinates() => TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["location"] = ["Coordenadas inválidas."] });

    private static LocationResponse ToResponse(LatestAdjusterLocation x) => new(x.AdjusterId, (decimal)x.Position.Y, (decimal)x.Position.X, x.SpeedKmh, x.Heading, x.Sequence, x.CapturedAt);

    private static double Distance(
        decimal lat1,
        decimal lon1,
        decimal lat2,
        decimal lon2)
    {
        const double radius = 6371;
        var dLat = (double)(lat2 - lat1) * Math.PI / 180;
        var dLon = (double)(lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos((double)lat1 * Math.PI / 180) * Math.Cos((double)lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return radius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
