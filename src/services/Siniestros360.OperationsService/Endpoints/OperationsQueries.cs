using Siniestros360.ServiceDefaults.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Siniestros360.OperationsService.Application;
using Siniestros360.OperationsService.Domain;
using Siniestros360.OperationsService.Infrastructure;

namespace Siniestros360.OperationsService.Endpoints;

// Lado de lectura de CQRS: las vistas se construyen con eventos (OperationsProjectionConsumer) y aquí sólo se leen.
// Todo es de la torre salvo el detalle de un siniestro, que también lee el ajustador asignado (trae su plazo de llegada).
public static class OperationsQueries
{
    internal sealed class ListClaimViewsEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/claims", ListClaimsAsync)
                .RequireAuthorization(Policies.ControlTower)
                .WithName("ListClaimViews")
                .WithSummary("Siniestros de la operación, del más reciente al más antiguo");
        }
    }

    internal sealed class GetClaimViewEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/claims/{claimId:guid}", GetClaimAsync)
                .RequireAuthorization(Policies.FieldOperations)
                .WithName("GetClaimView")
                .WithSummary("Vista de un siniestro")
                .WithDescription("La torre ve cualquiera; el ajustador sólo el suyo. 404 en los demás casos.");
        }
    }

    internal sealed class ListAdjusterViewsEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/adjusters/map", ListAdjustersAsync)
                .RequireAuthorization(Policies.ControlTower)
                .WithName("ListAdjusterViews")
                .WithSummary("Ajustadores con su última posición");
        }
    }

    internal sealed class ListAlertsEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/alerts", ListAlertsAsync)
                .RequireAuthorization(Policies.ControlTower)
                .WithName("ListAlerts")
                .WithSummary("Las 200 alertas más recientes");
        }
    }

    internal sealed class GetDashboardEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/dashboard", DashboardAsync)
                .RequireAuthorization(Policies.ControlTower)
                .WithName("GetDashboard")
                .WithSummary("Contadores del tablero operativo");
        }
    }

    // Folio vacío: la vista nació por un evento anterior a ClaimReported y todavía no está completa.
    private static async Task<Ok<List<ClaimView>>> ListClaimsAsync(
        OperationsDbContext database,
        CancellationToken cancellationToken)
    {
        var claims = await database.Claims.AsNoTracking()
            .Where(x => x.Folio != "")
            .OrderByDescending(x => x.ReportedAt)
            .Select(x => ClaimView.From(x))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(claims);
    }

    private static async Task<Results<Ok<ClaimView>, NotFound>> GetClaimAsync(
        Guid claimId,
        IUserContext userContext,
        OperationsDbContext database,
        CancellationToken cancellationToken)
    {
        var claim = await database.Claims.AsNoTracking().SingleOrDefaultAsync(x => x.ClaimId == claimId, cancellationToken);
        return claim is not null && await userContext.CanAccessClaimAsync(claim.Participants())
            ? TypedResults.Ok(ClaimView.From(claim))
            : TypedResults.NotFound();
    }

    private static async Task<Ok<List<AdjusterView>>> ListAdjustersAsync(
        OperationsDbContext database,
        CancellationToken cancellationToken)
    {
        var adjusters = await database.Adjusters.AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .Select(x => AdjusterView.From(x))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(adjusters);
    }

    private static async Task<Ok<List<AlertView>>> ListAlertsAsync(
        OperationsDbContext database,
        CancellationToken cancellationToken)
    {
        var alerts = await database.Alerts.AsNoTracking()
            .OrderByDescending(x => x.RaisedAt)
            .Take(200)
            .Select(x => AlertView.From(x))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(alerts);
    }

    private static async Task<Ok<DashboardView>> DashboardAsync(
        OperationsDbContext database,
        CancellationToken cancellationToken)
    {
        var dashboard = new DashboardView(
            Total: await database.Claims.CountAsync(x => x.Folio != "", cancellationToken),
            Active: await database.Claims.CountAsync(x => x.Folio != "" && x.Status != "Closed" && x.Status != "Cancelled", cancellationToken),
            Alerts: await database.Alerts.CountAsync(x => x.AcknowledgedAt == null, cancellationToken),
            Adjusters: await database.Adjusters.CountAsync(cancellationToken),
            Available: await database.Adjusters.CountAsync(x => x.IsAvailable, cancellationToken));

        return TypedResults.Ok(dashboard);
    }

    public static ClaimParticipants Participants(this ClaimReadModel claim) => new(claim.InsuredId, claim.AdjusterId);
}
