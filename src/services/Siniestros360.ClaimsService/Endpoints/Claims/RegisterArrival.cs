using Siniestros360.ClaimsService.Application.Claims;
using Siniestros360.ClaimsService.Application.Claims.RegisterArrival;
using Siniestros360.Messaging.Idempotency;
using Siniestros360.ServiceDefaults.Endpoints;
using Siniestros360.SharedKernel;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Endpoints.Claims;

internal sealed class RegisterArrival : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/{claimId:guid}/adjuster-arrived", [Idempotent] async (
            Guid claimId,
            ICommandHandler<RegisterArrivalCommand, ClaimResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new RegisterArrivalCommand(claimId);
            Result<ClaimResponse> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization(Policies.Adjuster)
        .WithName("RegisterArrival")
        .WithSummary("Registrar la llegada del ajustador")
        .Produces<ClaimResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
