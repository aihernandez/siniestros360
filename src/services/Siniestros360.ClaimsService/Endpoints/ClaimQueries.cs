using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Siniestros360.ClaimsService.Application;
using Siniestros360.ClaimsService.Domain;
using Siniestros360.ClaimsService.Infrastructure;
using Siniestros360.Contracts.Common;

namespace Siniestros360.ClaimsService.Endpoints;

// Lado de lectura del expediente: consultas sin seguimiento de cambios que devuelven DTO, nunca la entidad.
public static class ClaimQueries
{
    public static RouteGroupBuilder MapClaimQueries(this RouteGroupBuilder claims)
    {
        claims.MapGet("/", ListAsync)
            .WithName("ListClaims")
            .WithSummary("Siniestros visibles para el usuario")
            .WithDescription("La torre y admin ven todos; el ajustador, los asignados a él; el asegurado, los suyos.");
        claims.MapGet("/{claimId:guid}", GetAsync)
            .WithName("GetClaim")
            .WithSummary("Detalle de un siniestro")
            .WithDescription("404 también cuando el siniestro existe pero el usuario no participa en él: no se revela su existencia.");
        return claims;
    }

    // El filtro de la consulta es la misma regla que la política ClaimParticipant, aplicada en SQL para no traer
    // siniestros ajenos y descartarlos en memoria.
    private static async Task<Ok<List<ClaimResponse>>> ListAsync(IUserContext user, ClaimsDbContext db, CancellationToken ct)
    {
        var query = db.Claims.AsNoTracking();
        if (!user.IsControlTower)
        {
            var adjusterId = user.AdjusterId;
            var insuredId = user.UserId;
            query = user.IsInRole(Roles.Adjuster)
                ? query.Where(x => adjusterId != null && x.AssignedAdjusterId == adjusterId)
                : query.Where(x => x.InsuredId == insuredId);
        }
        return TypedResults.Ok(await query.OrderByDescending(x => x.ReportedAt).Select(x => ClaimResponse.From(x)).ToListAsync(ct));
    }

    private static async Task<Results<Ok<ClaimResponse>, NotFound>> GetAsync(Guid claimId, IUserContext user, ClaimsDbContext db, CancellationToken ct)
    {
        var claim = await db.Claims.AsNoTracking().SingleOrDefaultAsync(x => x.Id == claimId, ct);
        return claim is not null && await user.CanAccessClaimAsync(claim.Participants())
            ? TypedResults.Ok(ClaimResponse.From(claim))
            : TypedResults.NotFound();
    }

    public static ClaimParticipants Participants(this Claim claim) => new(claim.InsuredId, claim.AssignedAdjusterId);
}
