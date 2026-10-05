using Siniestros360.ClaimsService.Application.Claims;
using Siniestros360.ClaimsService.Application.Claims.CompleteService;
using Siniestros360.Messaging.Idempotency;
using Siniestros360.ServiceDefaults.Endpoints;
using Siniestros360.SharedKernel;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Endpoints.Claims;

internal sealed class CompleteService : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/{claimId:guid}/service-completed", [Idempotent] async (
            Guid claimId,
            ICommandHandler<CompleteServiceCommand, ClaimResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new CompleteServiceCommand(claimId);
            Result<ClaimResponse> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization(Policies.Adjuster)
        .WithName("CompleteService")
        .WithSummary("Finalizar el servicio")
        .Produces<ClaimResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
