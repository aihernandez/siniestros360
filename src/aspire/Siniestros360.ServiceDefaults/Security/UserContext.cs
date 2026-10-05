using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Extensions.Hosting;

// Quién ejecuta el caso de uso y qué puede hacer. Los handlers dependen de la interfaz: no conocen HTTP ni ClaimsPrincipal,
// y en las pruebas unitarias se sustituye por un doble. El usuario y el ajustador siempre salen del token, nunca del body.
public interface IUserContext
{
    string UserId { get; }

    Guid? AdjusterId { get; }

    string DisplayName { get; }

    bool IsControlTower { get; }

    bool IsInRole(string role);

    // Torre y admin, el asegurado que lo reportó o el ajustador asignado (política ClaimParticipant).
    Task<bool> CanAccessClaimAsync(ClaimParticipants claim);

    // Torre y admin, o el propio ajustador (política AdjusterSelf).
    Task<bool> CanActAsAdjusterAsync(Guid adjusterId);
}

internal sealed class UserContext(IHttpContextAccessor accessor, IAuthorizationService authorization) : IUserContext
{
    private ClaimsPrincipal User => accessor.HttpContext?.User
        ?? throw new InvalidOperationException("No hay una petición HTTP activa.");

    public string UserId => User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("La identidad autenticada no tiene claim de sujeto.");

    public Guid? AdjusterId => User.AdjusterId();

    public string DisplayName => User.FindFirstValue(System.Security.Claims.ClaimTypes.Name) ?? UserId;

    public bool IsControlTower => User.IsControlTower();

    public bool IsInRole(string role) => User.IsInRole(role);

    public Task<bool> CanAccessClaimAsync(ClaimParticipants claim) => IsAllowedAsync(claim, Policies.ClaimParticipant);

    public Task<bool> CanActAsAdjusterAsync(Guid adjusterId) => IsAllowedAsync(new AdjusterResource(adjusterId), Policies.AdjusterSelf);

    private async Task<bool> IsAllowedAsync(object resource, string policy) => (await authorization.AuthorizeAsync(User, resource, policy)).Succeeded;
}
