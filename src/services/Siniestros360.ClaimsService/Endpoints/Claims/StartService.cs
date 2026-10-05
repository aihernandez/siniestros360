using Siniestros360.ClaimsService.Application.Claims;
using Siniestros360.ClaimsService.Application.Claims.StartService;
using Siniestros360.Messaging.Idempotency;
using Siniestros360.ServiceDefaults.Endpoints;
using Siniestros360.SharedKernel;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Endpoints.Claims;

internal sealed class StartService : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/{claimId:guid}/service-started", [Idempotent] async (
            Guid claimId,
            ICommandHandler<StartServiceCommand, ClaimResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new StartServiceCommand(claimId);
            Result<ClaimResponse> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization(Policies.Adjuster)
        .WithName("StartService")
        .WithSummary("Iniciar la atención")
        .Produces<ClaimResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
