using Microsoft.EntityFrameworkCore;
using Siniestros360.ClaimsService.Domain;
using Siniestros360.ClaimsService.Infrastructure;

namespace Siniestros360.ClaimsService.Application.Claims;

internal static class ClaimLoading
{
    // El agregado completo para modificarlo: la bitácora es parte de él.
    public static Task<Claim?> FindForUpdateAsync(this ClaimsDbContext db, Guid claimId, CancellationToken ct)
        => db.Claims.Include(x => x.Timeline).SingleOrDefaultAsync(x => x.Id == claimId, ct);

    // Desde un evento: el siniestro debe existir. Si no, la excepción hace que MassTransit reintente y, al agotar los
    // reintentos, lo deje en la DLQ (un evento de un siniestro inexistente es un error de integración, no de negocio).
    public static async Task<Claim> GetForUpdateAsync(this ClaimsDbContext db, Guid claimId, CancellationToken ct)
        => await db.FindForUpdateAsync(claimId, ct) ?? throw new InvalidOperationException($"Claim {claimId} does not exist.");

    public static ClaimParticipants Participants(this Claim claim) => new(claim.InsuredId, claim.AssignedAdjusterId);
}
