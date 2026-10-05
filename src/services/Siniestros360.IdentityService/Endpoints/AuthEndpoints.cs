using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Siniestros360.IdentityService.Application;
using Siniestros360.IdentityService.Domain;
using ContractClaimTypes = Siniestros360.Contracts.Common.ClaimTypes;

namespace Siniestros360.IdentityService.Endpoints;

/// <summary>Identidad del usuario autenticado, tal como la ven los servicios.</summary>
public sealed record MeResponse(string? Id, string? Email, string? Name, string? AdjusterId, IReadOnlyList<string> Roles);

// Emisión de tokens. Login y refresh son anónimos por definición; el resto exige token (política por defecto).
public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder auth)
    {
        auth.MapPost("/login", LoginAsync).AllowAnonymous()
            .WithName("Login").WithSummary("Iniciar sesión con correo y contraseña");
        auth.MapPost("/refresh", RefreshAsync).AllowAnonymous()
            .WithName("RefreshToken").WithSummary("Renovar el token de acceso");
        auth.MapGet("/me", Me)
            .WithName("GetCurrentUser").WithSummary("Identidad del usuario autenticado");
        return auth;
    }

    // Mismo 401 para correo inexistente y contraseña incorrecta: no se revela qué cuentas existen.
    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> LoginAsync(LoginRequest request, UserManager<ApplicationUser> users, TokenService tokens, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null || !await users.CheckPasswordAsync(user, request.Password))
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Credenciales inválidas.");
        return TypedResults.Ok(await tokens.IssueAsync(user, ct));
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> RefreshAsync(RefreshRequest request, TokenService tokens, CancellationToken ct)
    {
        var result = await tokens.RefreshAsync(request.RefreshToken, ct);
        return result is null
            ? TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "El token de renovación no es válido.")
            : TypedResults.Ok(result);
    }

    private static Ok<MeResponse> Me(ClaimsPrincipal user) => TypedResults.Ok(new MeResponse(
        user.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier),
        user.FindFirstValue(System.Security.Claims.ClaimTypes.Email),
        user.FindFirstValue(System.Security.Claims.ClaimTypes.Name),
        user.FindFirstValue(ContractClaimTypes.AdjusterId),
        user.FindAll(System.Security.Claims.ClaimTypes.Role).Select(x => x.Value).ToList()));
}
