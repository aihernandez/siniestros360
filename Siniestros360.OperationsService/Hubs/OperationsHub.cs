using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Siniestros360.Contracts.Messaging;
using Siniestros360.OperationsService.Infrastructure;
using ContractClaimTypes = Siniestros360.Contracts.Messaging.ClaimTypes;

namespace Siniestros360.OperationsService.Hubs;

// Los grupos salen del token: la torre recibe todo, el ajustador lo suyo y el asegurado sólo el siniestro al que se suscribe.
[Authorize]
public sealed class OperationsHub(OperationsDbContext db) : Hub
{
    public const string TowerGroup = "tower";
    public static string AdjusterGroup(Guid adjusterId) => $"adjuster:{adjusterId}";
    public static string ClaimGroup(Guid claimId) => $"claim:{claimId}";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User!;
        if (user.IsInRole(Roles.ControlTower) || user.IsInRole(Roles.Admin)) await Groups.AddToGroupAsync(Context.ConnectionId, TowerGroup);
        if (Guid.TryParse(user.FindFirstValue(ContractClaimTypes.AdjusterId), out var adjusterId)) await Groups.AddToGroupAsync(Context.ConnectionId, AdjusterGroup(adjusterId));
        await base.OnConnectedAsync();
    }

    public async Task SubscribeClaim(Guid claimId)
    {
        var user = Context.User!;
        var claim = await db.Claims.AsNoTracking().SingleOrDefaultAsync(x => x.ClaimId == claimId);
        var allowed = user.IsInRole(Roles.ControlTower) || user.IsInRole(Roles.Admin)
            || (claim is not null && claim.InsuredId == user.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier))
            || (claim is not null && Guid.TryParse(user.FindFirstValue(ContractClaimTypes.AdjusterId), out var adjusterId) && claim.AdjusterId == adjusterId);
        if (!allowed) throw new HubException("You cannot follow this claim.");
        await Groups.AddToGroupAsync(Context.ConnectionId, ClaimGroup(claimId));
    }
}
