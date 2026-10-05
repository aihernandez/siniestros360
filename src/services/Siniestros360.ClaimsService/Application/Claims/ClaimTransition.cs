using MassTransit;
using Siniestros360.ClaimsService.Domain;
using Siniestros360.ClaimsService.Infrastructure;
using Siniestros360.Contracts.Events;
using Siniestros360.SharedKernel;

namespace Siniestros360.ClaimsService.Application.Claims;

// El paso común de las cuatro transiciones (cancelar, llegada, inicio y cierre): sólo participa quien la política permite
// (si no, NotFound, para no revelar el siniestro); el dominio decide si la transición es legal; el evento de la transición
// sale por el outbox junto con ClaimStatusChanged y, al cerrar, ClaimClosed. Lo comparten por composición, no por herencia.
// TODO(2026-10-05): pruebas unitarias de los handlers de Claims (Report, transiciones, consultas) con IUserContext sustituido y DbContext en memoria — y la de que el filtro de publicación pone la correlación (sin PublishCorrelated) leyendo el outbox sin entrega.
internal sealed class ClaimTransition(ClaimsDbContext db, IUserContext user, IPublishEndpoint publish, IDateTimeProvider clock)
{
    public async Task<Result<ClaimResponse>> ApplyAsync(Guid claimId, Func<Claim, DateTimeOffset, Result<object>> transition, CancellationToken ct)
    {
        var claim = await db.FindForUpdateAsync(claimId, ct);
        if (claim is null || !await user.CanAccessClaimAsync(claim.Participants())) return ClaimErrors.NotFound(claimId);

        var previousStatus = claim.Status;
        var result = transition(claim, clock.UtcNow);
        if (result.IsFailure) return result.Error;

        await publish.Publish(result.Value, ct);
        await publish.Publish(new ClaimStatusChanged(claim.Id, previousStatus.ToString(), claim.Status.ToString(), claim.Version, claim.UpdatedAt), ct);
        if (claim.Status == ClaimStatus.Closed) await publish.Publish(new ClaimClosed(claim.Id, claim.UpdatedAt), ct);
        await db.SaveChangesAsync(ct);
        return ClaimResponse.From(claim);
    }
}
