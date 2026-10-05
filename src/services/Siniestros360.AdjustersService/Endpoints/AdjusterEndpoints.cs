using Siniestros360.ServiceDefaults.Endpoints;
using Siniestros360.SharedKernel;
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
    internal sealed class ListAdjustersEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/", ListAsync)
                .RequireAuthorization(Policies.ControlTower)
                .WithName("ListAdjusters")
                .WithSummary("Catálogo de ajustadores");
        }
    }

    internal sealed class GetAdjusterEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/{adjusterId:guid}", GetAsync)
                .RequireAuthorization(Policies.FieldOperations)
                .WithName("GetAdjuster")
                .WithSummary("Detalle de un ajustador")
                .WithDescription("El ajustador sólo puede leer su propio registro; los demás responden 404.");
        }
    }

    internal sealed class SetAdjusterAvailabilityEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPut("/{adjusterId:guid}/availability", SetAvailabilityAsync)
                .RequireAuthorization(Policies.FieldOperations)
                .WithName("SetAdjusterAvailability")
                .WithSummary("Marcar disponible o fuera de línea")
                .WithDescription("El ajustador sólo cambia su propia disponibilidad (403 si no). 409 si tiene un siniestro activo.");
        }
    }

    internal sealed class SetAdjusterStatusEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPut("/{adjusterId:guid}/status", SetStatusAsync)
                .RequireAuthorization(Policies.ControlTower)
                .WithName("SetAdjusterStatus")
                .WithSummary("Fijar el estado operativo");
        }
    }

    private static async Task<Ok<List<AdjusterResponse>>> ListAsync(
        AdjustersDbContext database,
        CancellationToken cancellationToken)
    {
        var adjusters = await database.Adjusters.AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .Select(x => AdjusterResponse.From(x))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(adjusters);
    }

    private static async Task<Results<Ok<AdjusterResponse>, NotFound>> GetAsync(
        Guid adjusterId,
        IUserContext userContext,
        AdjustersDbContext database,
        CancellationToken cancellationToken)
    {
        if (!await userContext.CanActAsAdjusterAsync(adjusterId)) return TypedResults.NotFound();
        var adjuster = await database.Adjusters.AsNoTracking().SingleOrDefaultAsync(x => x.Id == adjusterId, cancellationToken);
        return adjuster is null ? TypedResults.NotFound() : TypedResults.Ok(AdjusterResponse.From(adjuster));
    }

    private static async Task<Results<Ok<AdjusterResponse>, ForbidHttpResult, NotFound, ProblemHttpResult>> SetAvailabilityAsync(
        Guid adjusterId,
        AvailabilityRequest request,
        HttpContext httpContext,
        IUserContext userContext,
        AdjustersDbContext database,
        IPublishEndpoint publisher,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        if (!await userContext.CanActAsAdjusterAsync(adjusterId)) return TypedResults.Forbid();
        var adjuster = await database.Adjusters.SingleOrDefaultAsync(x => x.Id == adjusterId, cancellationToken);
        if (adjuster is null) return TypedResults.NotFound();
        if (adjuster.ActiveClaimId is not null) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "El ajustador tiene un siniestro activo.");
        var now = dateTimeProvider.UtcNow;
        var previous = adjuster.ChangeStatus(request.IsAvailable ? AdjusterStatus.Available : AdjusterStatus.Offline, null, now);
        var correlation = Correlation.From(httpContext);
        await publisher.PublishCorrelated(new AdjusterAvailabilityChanged(adjusterId, adjuster.IsAvailable, now), correlation, cancellationToken);
        if (previous is not null) await publisher.PublishCorrelated(new AdjusterStatusChanged(adjusterId, previous.Value.ToString(), adjuster.Status.ToString(), now), correlation, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(AdjusterResponse.From(adjuster));
    }

    private static async Task<Results<Ok<AdjusterResponse>, NotFound>> SetStatusAsync(
        Guid adjusterId,
        StatusRequest request,
        HttpContext httpContext,
        AdjustersDbContext database,
        IPublishEndpoint publisher,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        var adjuster = await database.Adjusters.SingleOrDefaultAsync(x => x.Id == adjusterId, cancellationToken);
        if (adjuster is null) return TypedResults.NotFound();
        var now = dateTimeProvider.UtcNow;
        var wasAvailable = adjuster.IsAvailable;
        var previous = adjuster.ChangeStatus(request.Status, adjuster.ActiveClaimId, now);
        var correlation = Correlation.From(httpContext);
        if (previous is not null) await publisher.PublishCorrelated(new AdjusterStatusChanged(adjusterId, previous.Value.ToString(), adjuster.Status.ToString(), now), correlation, cancellationToken);
        if (wasAvailable != adjuster.IsAvailable) await publisher.PublishCorrelated(new AdjusterAvailabilityChanged(adjusterId, adjuster.IsAvailable, now), correlation, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(AdjusterResponse.From(adjuster));
    }
}
