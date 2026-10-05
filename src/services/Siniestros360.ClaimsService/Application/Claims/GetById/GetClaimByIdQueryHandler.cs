using Microsoft.EntityFrameworkCore;
using Siniestros360.ClaimsService.Domain;
using Siniestros360.ClaimsService.Infrastructure;
using Siniestros360.SharedKernel;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Application.Claims.GetById;

// NotFound también cuando el siniestro existe pero el usuario no participa en él: no se revela su existencia.
internal sealed class GetClaimByIdQueryHandler(ClaimsDbContext db, IUserContext user) : IQueryHandler<GetClaimByIdQuery, ClaimResponse>
{
    public async Task<Result<ClaimResponse>> Handle(GetClaimByIdQuery query, CancellationToken cancellationToken)
    {
        var claim = await db.Claims.AsNoTracking().SingleOrDefaultAsync(x => x.Id == query.ClaimId, cancellationToken);
        return claim is not null && await user.CanAccessClaimAsync(claim.Participants())
            ? ClaimResponse.From(claim)
            : ClaimErrors.NotFound(query.ClaimId);
    }
}
