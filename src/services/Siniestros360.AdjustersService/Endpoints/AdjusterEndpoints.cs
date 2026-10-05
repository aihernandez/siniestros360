using MassTransit;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Siniestros360.AdjustersService.Domain;
using Siniestros360.AdjustersService.Infrastructure;
using Siniestros360.Contracts.Events;
using Siniestros360.Messaging;

namespace Siniestros360.AdjustersService.Endpoints;

/// <summary>Ajustador del catálogo, sin el token de concurrencia.</summary>
public sealed record AdjusterResponse(Guid Id, string DisplayName, bool IsAvailable, AdjusterStatus Status, Guid? ActiveClaimId, DateTimeOffset UpdatedAt)
{
    public static AdjusterResponse From(Adjuster x) => new(x.Id, x.DisplayName, x.IsAvailable, x.Status, x.ActiveClaimId, x.UpdatedAt);
}

/// <summary>Disponibilidad que declara el ajustador o la torre.</summary>
public sealed record AvailabilityRequest(bool IsAvailable);

/// <summary>Estado operativo que fija la torre.</summary>
public sealed record StatusRequest(AdjusterStatus Status);

// Catálogo y estado de los ajustadores. Los cambios publican AdjusterAvailabilityChanged y AdjusterStatusChanged por el
// bus outbox. Los PUT fijan un valor absoluto: repetirlos deja el mismo estado y no necesitan Idempotency-Key.
public static class AdjusterEndpoints
{
    public static RouteGroupBuilder MapAdjusterEndpoints(this RouteGroupBuilder adjusters)
    {
        // Consultas
        adjusters.MapGet("/", ListAsync).RequireAuthorization(Policies.ControlTower)
            .WithName("ListAdjusters").WithSummary("Catálogo de ajustadores");
        adjusters.MapGet("/{id:guid}", GetAsync).RequireAuthorization(Policies.FieldOperations)
            .WithName("GetAdjuster").WithSummary("Detalle de un ajustador")
            .WithDescription("El ajustador sólo puede leer su propio registro; los demás responden 404.");

        // Comandos
        adjusters.MapPut("/{id:guid}/availability", SetAvailabilityAsync).RequireAuthorization(Policies.FieldOperations)
            .WithName("SetAdjusterAvailability").WithSummary("Marcar disponible o fuera de línea")
            .WithDescription("El ajustador sólo cambia su propia disponibilidad (403 si no). 409 si tiene un siniestro activo.");
        adjusters.MapPut("/{id:guid}/status", SetStatusAsync).RequireAuthorization(Policies.ControlTower)
            .WithName("SetAdjusterStatus").WithSummary("Fijar el estado operativo");
        return adjusters;
    }

    private static async Task<Ok<List<AdjusterResponse>>> ListAsync(AdjustersDbContext db, CancellationToken ct)
        => TypedResults.Ok(await db.Adjusters.AsNoTracking().OrderBy(x => x.DisplayName).Select(x => AdjusterResponse.From(x)).ToListAsync(ct));

    private static async Task<Results<Ok<AdjusterResponse>, NotFound>> GetAsync(Guid id, UserContext user, AdjustersDbContext db, CancellationToken ct)
    {
        if (!await user.CanActAsAdjusterAsync(id)) return TypedResults.NotFound();
        var adjuster = await db.Adjusters.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return adjuster is null ? TypedResults.NotFound() : TypedResults.Ok(AdjusterResponse.From(adjuster));
    }

    private static async Task<Results<Ok<AdjusterResponse>, ForbidHttpResult, NotFound, ProblemHttpResult>> SetAvailabilityAsync(
        Guid id, AvailabilityRequest request, HttpContext http, UserContext user, AdjustersDbContext db, IPublishEndpoint publish, CancellationToken ct)
    {
        if (!await user.CanActAsAdjusterAsync(id)) return TypedResults.Forbid();
        var adjuster = await db.Adjusters.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (adjuster is null) return TypedResults.NotFound();
        if (adjuster.ActiveClaimId is not null) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "El ajustador tiene un siniestro activo.");
        var now = DateTimeOffset.UtcNow;
        var previous = adjuster.ChangeStatus(request.IsAvailable ? AdjusterStatus.Available : AdjusterStatus.Offline, null, now);
        var correlation = Correlation.From(http);
        await publish.PublishCorrelated(new AdjusterAvailabilityChanged(id, adjuster.IsAvailable, now), correlation, ct);
        if (previous is not null) await publish.PublishCorrelated(new AdjusterStatusChanged(id, previous.Value.ToString(), adjuster.Status.ToString(), now), correlation, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(AdjusterResponse.From(adjuster));
    }

    private static async Task<Results<Ok<AdjusterResponse>, NotFound>> SetStatusAsync(Guid id, StatusRequest request, HttpContext http, AdjustersDbContext db, IPublishEndpoint publish, CancellationToken ct)
    {
        var adjuster = await db.Adjusters.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (adjuster is null) return TypedResults.NotFound();
        var now = DateTimeOffset.UtcNow;
        var wasAvailable = adjuster.IsAvailable;
        var previous = adjuster.ChangeStatus(request.Status, adjuster.ActiveClaimId, now);
        var correlation = Correlation.From(http);
        if (previous is not null) await publish.PublishCorrelated(new AdjusterStatusChanged(id, previous.Value.ToString(), adjuster.Status.ToString(), now), correlation, ct);
        if (wasAvailable != adjuster.IsAvailable) await publish.PublishCorrelated(new AdjusterAvailabilityChanged(id, adjuster.IsAvailable, now), correlation, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(AdjusterResponse.From(adjuster));
    }
}
