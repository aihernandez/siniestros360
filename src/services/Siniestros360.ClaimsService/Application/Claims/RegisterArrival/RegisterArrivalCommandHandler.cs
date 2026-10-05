using Siniestros360.ClaimsService.Domain;
using Siniestros360.Contracts.Events;
using Siniestros360.SharedKernel;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Application.Claims.RegisterArrival;

internal sealed class RegisterArrivalCommandHandler(ClaimTransition transition, IUserContext user) : ICommandHandler<RegisterArrivalCommand, ClaimResponse>
{
    public Task<Result<ClaimResponse>> Handle(RegisterArrivalCommand command, CancellationToken cancellationToken) => transition.ApplyAsync(
        command.ClaimId,
        (claim, now) =>
        {
            if (user.AdjusterId is not { } adjusterId) return ClaimErrors.AdjusterIdMissing;
            var result = claim.Arrive(adjusterId, now);
            if (result.IsFailure) return result.Error;
            return Result.Success<object>(new AdjusterArrived(claim.Id, adjusterId, now));
        },
        cancellationToken);
}
