using Siniestros360.ClaimsService.Domain;
using Siniestros360.Contracts.Common;
using Siniestros360.Contracts.Events;
using Siniestros360.SharedKernel;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Application.Claims.Cancel;

internal sealed class CancelClaimCommandHandler(ClaimTransition transition, IUserContext user) : ICommandHandler<CancelClaimCommand, ClaimResponse>
{
    public Task<Result<ClaimResponse>> Handle(CancelClaimCommand command, CancellationToken cancellationToken) => transition.ApplyAsync(
        command.ClaimId,
        (claim, now) =>
        {
            var reason = user.IsInRole(Roles.Insured) ? "Cancelado por el asegurado." : "Cancelado por la torre de control.";
            var result = claim.Cancel(reason, now);
            if (result.IsFailure) return result.Error;
            ClaimsTelemetry.Cancelled.Add(1);
            return Result.Success<object>(new ClaimCancelled(claim.Id, reason, now));
        },
        cancellationToken);
}
