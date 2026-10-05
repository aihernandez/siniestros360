using Siniestros360.ClaimsService.Application.Claims;
using Siniestros360.ClaimsService.Application.Claims.Cancel;
using Siniestros360.Messaging.Idempotency;
using Siniestros360.ServiceDefaults.Endpoints;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Endpoints.Claims;

internal sealed class Cancel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) => app
        .MapPost("/{claimId:guid}/cancel", [Idempotent] async (Guid claimId, ICommandHandler<CancelClaimCommand, ClaimResponse> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new CancelClaimCommand(claimId), cancellationToken)).Match(Results.Ok, CustomResults.Problem))
        .RequireAuthorization(Policies.ClaimReporters)
        .WithName("CancelClaim")
        .WithSummary("Cancelar un siniestro")
        .Produces<ClaimResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
}
