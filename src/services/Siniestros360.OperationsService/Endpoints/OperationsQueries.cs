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
    public static RouteGroupBuilder MapOperationsQueries(this RouteGroupBuilder operations)
    {
        operations.MapGet("/claims", ListClaimsAsync).RequireAuthorization(Policies.ControlTower)
            .WithName("ListClaimViews").WithSummary("Siniestros de la operación, del más reciente al más antiguo");
        operations.MapGet("/claims/{id:guid}", GetClaimAsync).RequireAuthorization(Policies.FieldOperations)
            .WithName("GetClaimView").WithSummary("Vista de un siniestro")
            .WithDescription("La torre ve cualquiera; el ajustador sólo el suyo. 404 en los demás casos.");
        operations.MapGet("/adjusters/map", ListAdjustersAsync).RequireAuthorization(Policies.ControlTower)
            .WithName("ListAdjusterViews").WithSummary("Ajustadores con su última posición");
        operations.MapGet("/alerts", ListAlertsAsync).RequireAuthorization(Policies.ControlTower)
            .WithName("ListAlerts").WithSummary("Las 200 alertas más recientes");
        operations.MapGet("/dashboard", DashboardAsync).RequireAuthorization(Policies.ControlTower)
            .WithName("GetDashboard").WithSummary("Contadores del tablero operativo");
        return operations;
    }

    // Folio vacío: la vista nació por un evento anterior a ClaimReported y todavía no está completa.
    private static async Task<Ok<List<ClaimView>>> ListClaimsAsync(OperationsDbContext db, CancellationToken ct)
        => TypedResults.Ok(await db.Claims.AsNoTracking().Where(x => x.Folio != "").OrderByDescending(x => x.ReportedAt).Select(x => ClaimView.From(x)).ToListAsync(ct));

    private static async Task<Results<Ok<ClaimView>, NotFound>> GetClaimAsync(Guid id, IUserContext user, OperationsDbContext db, CancellationToken ct)
    {
        var claim = await db.Claims.AsNoTracking().SingleOrDefaultAsync(x => x.ClaimId == id, ct);
        return claim is not null && await user.CanAccessClaimAsync(claim.Participants())
            ? TypedResults.Ok(ClaimView.From(claim))
            : TypedResults.NotFound();
    }

    private static async Task<Ok<List<AdjusterView>>> ListAdjustersAsync(OperationsDbContext db, CancellationToken ct)
        => TypedResults.Ok(await db.Adjusters.AsNoTracking().OrderBy(x => x.DisplayName).Select(x => AdjusterView.From(x)).ToListAsync(ct));

    private static async Task<Ok<List<AlertView>>> ListAlertsAsync(OperationsDbContext db, CancellationToken ct)
        => TypedResults.Ok(await db.Alerts.AsNoTracking().OrderByDescending(x => x.RaisedAt).Take(200).Select(x => AlertView.From(x)).ToListAsync(ct));

    private static async Task<Ok<DashboardView>> DashboardAsync(OperationsDbContext db, CancellationToken ct) => TypedResults.Ok(new DashboardView(
        Total: await db.Claims.CountAsync(x => x.Folio != "", ct),
        Active: await db.Claims.CountAsync(x => x.Folio != "" && x.Status != "Closed" && x.Status != "Cancelled", ct),
        Alerts: await db.Alerts.CountAsync(x => x.AcknowledgedAt == null, ct),
        Adjusters: await db.Adjusters.CountAsync(ct),
        Available: await db.Adjusters.CountAsync(x => x.IsAvailable, ct)));

    public static ClaimParticipants Participants(this ClaimReadModel claim) => new(claim.InsuredId, claim.AdjusterId);
}
