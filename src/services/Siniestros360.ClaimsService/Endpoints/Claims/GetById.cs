using Siniestros360.ClaimsService.Application.Claims;
using Siniestros360.ClaimsService.Application.Claims.GetById;
using Siniestros360.ServiceDefaults.Endpoints;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Endpoints.Claims;

internal sealed class GetById : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) => app
        .MapGet("/{claimId:guid}", async (Guid claimId, IQueryHandler<GetClaimByIdQuery, ClaimResponse> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new GetClaimByIdQuery(claimId), cancellationToken)).Match(Results.Ok, CustomResults.Problem))
        .WithName("GetClaim")
        .WithSummary("Detalle de un siniestro")
        .WithDescription("404 también cuando el siniestro existe pero el usuario no participa en él: no se revela su existencia.")
        .Produces<ClaimResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound);
}
