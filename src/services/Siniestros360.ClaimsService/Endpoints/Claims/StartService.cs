using Siniestros360.ClaimsService.Application.Claims;
using Siniestros360.ClaimsService.Application.Claims.StartService;
using Siniestros360.Messaging.Idempotency;
using Siniestros360.ServiceDefaults.Endpoints;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Endpoints.Claims;

internal sealed class StartService : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) => app
        .MapPost("/{claimId:guid}/service-started", [Idempotent] async (Guid claimId, ICommandHandler<StartServiceCommand, ClaimResponse> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new StartServiceCommand(claimId), cancellationToken)).Match(Results.Ok, CustomResults.Problem))
        .RequireAuthorization(Policies.Adjuster)
        .WithName("StartService")
        .WithSummary("Iniciar la atención")
        .Produces<ClaimResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
}
