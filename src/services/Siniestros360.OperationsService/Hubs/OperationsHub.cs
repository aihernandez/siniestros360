using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Siniestros360.OperationsService.Application;
using Siniestros360.OperationsService.Endpoints;
using Siniestros360.OperationsService.Infrastructure;

namespace Siniestros360.OperationsService.Hubs;

// Los grupos salen del token: la torre recibe todo, el ajustador lo suyo y el asegurado sólo el siniestro al que se suscribe.
[Authorize]
public sealed class OperationsHub(OperationsDbContext db, IAuthorizationService authorization) : Hub
{
    public const string TowerGroup = "tower";
    public static string AdjusterGroup(Guid adjusterId) => $"adjuster:{adjusterId}";
    public static string ClaimGroup(Guid claimId) => $"claim:{claimId}";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User!;
        if (user.IsControlTower()) await Groups.AddToGroupAsync(Context.ConnectionId, TowerGroup);
        if (user.AdjusterId() is { } adjusterId) await Groups.AddToGroupAsync(Context.ConnectionId, AdjusterGroup(adjusterId));
        await base.OnConnectedAsync();
    }

    // Devuelve la vista vigente después de entrar al grupo: un cambio anterior a la suscripción (la asignación llega en
    // menos de un segundo) no se pierde, y uno posterior llega por el grupo. Decide la misma política que los endpoints.
    public async Task<ClaimView?> SubscribeClaim(Guid claimId)
    {
        var claim = await db.Claims.AsNoTracking().SingleOrDefaultAsync(x => x.ClaimId == claimId);
        if (claim is null || !(await authorization.AuthorizeAsync(Context.User!, claim.Participants(), Policies.ClaimParticipant)).Succeeded)
            throw new HubException("You cannot follow this claim.");
        await Groups.AddToGroupAsync(Context.ConnectionId, ClaimGroup(claimId));
        return ClaimView.From(await db.Claims.AsNoTracking().SingleAsync(x => x.ClaimId == claimId));
    }
}
