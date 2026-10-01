using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ContractClaimTypes = Siniestros360.Contracts.Messaging.ClaimTypes;

namespace Microsoft.Extensions.Hosting;

// Identidad del usuario autenticado. Los servicios nunca aceptan usuario ni ajustador desde el body.
public sealed class UserContext(IHttpContextAccessor accessor)
{
    public ClaimsPrincipal User => accessor.HttpContext?.User
        ?? throw new InvalidOperationException("No active HTTP request is available.");

    public string UserId => User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("The authenticated identity has no subject claim.");

    public Guid? AdjusterId => Guid.TryParse(User.FindFirstValue(ContractClaimTypes.AdjusterId), out var id) ? id : null;

    public bool IsInRole(string role) => User.IsInRole(role);
}
