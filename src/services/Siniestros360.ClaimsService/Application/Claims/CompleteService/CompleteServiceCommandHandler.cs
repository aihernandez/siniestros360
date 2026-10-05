using Siniestros360.ClaimsService.Domain;
using Siniestros360.Contracts.Events;
using Siniestros360.SharedKernel;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Application.Claims.CompleteService;

internal sealed class CompleteServiceCommandHandler(ClaimTransition transition, IUserContext user) : ICommandHandler<CompleteServiceCommand, ClaimResponse>
{
    public Task<Result<ClaimResponse>> Handle(CompleteServiceCommand command, CancellationToken cancellationToken) => transition.ApplyAsync(
        command.ClaimId,
        (claim, now) =>
        {
            if (user.AdjusterId is not { } adjusterId) return ClaimErrors.AdjusterIdMissing;
            var result = claim.Complete(adjusterId, now);
            if (result.IsFailure) return result.Error;
            ClaimsTelemetry.Closed.Add(1);
            return Result.Success<object>(new AdjusterServiceCompleted(claim.Id, adjusterId, now));
        },
        cancellationToken);
}
