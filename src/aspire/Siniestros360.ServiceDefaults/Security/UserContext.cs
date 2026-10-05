using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Extensions.Hosting;

// Identidad del usuario autenticado y sus permisos sobre recursos. Los servicios nunca aceptan usuario ni ajustador
// desde el body: salen del token. Las reglas viven en las políticas (Authorization.cs), no en cada endpoint.
public sealed class UserContext(IHttpContextAccessor accessor, IAuthorizationService authorization)
{
    public ClaimsPrincipal User => accessor.HttpContext?.User
        ?? throw new InvalidOperationException("No active HTTP request is available.");

    public string UserId => User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("The authenticated identity has no subject claim.");

    public Guid? AdjusterId => User.AdjusterId();

    public string DisplayName => User.FindFirstValue(System.Security.Claims.ClaimTypes.Name) ?? UserId;

    public bool IsInRole(string role) => User.IsInRole(role);

    // Torre y admin, el asegurado que lo reportó o el ajustador asignado.
    public Task<bool> CanAccessClaimAsync(ClaimParticipants claim) => IsAllowedAsync(claim, Policies.ClaimParticipant);

    // Torre y admin, o el propio ajustador.
    public Task<bool> CanActAsAdjusterAsync(Guid adjusterId) => IsAllowedAsync(new AdjusterResource(adjusterId), Policies.AdjusterSelf);

    private async Task<bool> IsAllowedAsync(object resource, string policy) => (await authorization.AuthorizeAsync(User, resource, policy)).Succeeded;
}
