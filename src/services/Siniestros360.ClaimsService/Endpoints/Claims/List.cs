using Siniestros360.ClaimsService.Application.Claims;
using Siniestros360.ClaimsService.Application.Claims.List;
using Siniestros360.ServiceDefaults.Endpoints;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Endpoints.Claims;

internal sealed class List : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) => app
        .MapGet("/", async (IQueryHandler<ListClaimsQuery, IReadOnlyList<ClaimResponse>> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new ListClaimsQuery(), cancellationToken)).Match(Results.Ok, CustomResults.Problem))
        .WithName("ListClaims")
        .WithSummary("Siniestros visibles para el usuario")
        .WithDescription("La torre y admin ven todos; el ajustador, los asignados a él; el asegurado, los suyos.")
        .Produces<IReadOnlyList<ClaimResponse>>();
}
