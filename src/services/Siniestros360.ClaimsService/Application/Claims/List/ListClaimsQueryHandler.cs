using Microsoft.EntityFrameworkCore;
using Siniestros360.ClaimsService.Infrastructure;
using Siniestros360.Contracts.Common;
using Siniestros360.SharedKernel;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Application.Claims.List;

// La torre y admin ven todos; el ajustador, los asignados a él; el asegurado, los suyos. Es la misma regla que la
// política ClaimParticipant, aplicada en SQL para no traer siniestros ajenos y descartarlos en memoria.
internal sealed class ListClaimsQueryHandler(ClaimsDbContext db, IUserContext user) : IQueryHandler<ListClaimsQuery, IReadOnlyList<ClaimResponse>>
{
    public async Task<Result<IReadOnlyList<ClaimResponse>>> Handle(ListClaimsQuery query, CancellationToken cancellationToken)
    {
        var claims = db.Claims.AsNoTracking();
        if (!user.IsControlTower)
        {
            var adjusterId = user.AdjusterId;
            var insuredId = user.UserId;
            claims = user.IsInRole(Roles.Adjuster)
                ? claims.Where(x => adjusterId != null && x.AssignedAdjusterId == adjusterId)
                : claims.Where(x => x.InsuredId == insuredId);
        }
        var list = await claims.OrderByDescending(x => x.ReportedAt).Select(x => ClaimResponse.From(x)).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<ClaimResponse>>(list);
    }
}
