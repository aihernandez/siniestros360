using Siniestros360.ClaimsService.Domain;
using Siniestros360.Contracts.Events;
using Siniestros360.SharedKernel;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Application.Claims.StartService;

internal sealed class StartServiceCommandHandler(ClaimTransition transition, IUserContext user) : ICommandHandler<StartServiceCommand, ClaimResponse>
{
    public Task<Result<ClaimResponse>> Handle(StartServiceCommand command, CancellationToken cancellationToken) => transition.ApplyAsync(
        command.ClaimId,
        (claim, now) =>
        {
            if (user.AdjusterId is not { } adjusterId) return ClaimErrors.AdjusterIdMissing;
            var result = claim.Start(adjusterId, now);
            if (result.IsFailure) return result.Error;
            return Result.Success<object>(new AdjusterServiceStarted(claim.Id, adjusterId, now));
        },
        cancellationToken);
}
