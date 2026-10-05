using Siniestros360.ServiceDefaults.Endpoints;
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
    internal sealed class LoginEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/login", LoginAsync)
                .AllowAnonymous()
                .WithName("Login")
                .WithSummary("Iniciar sesión con correo y contraseña");
        }
    }

    internal sealed class RefreshTokenEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/refresh", RefreshAsync)
                .AllowAnonymous()
                .WithName("RefreshToken")
                .WithSummary("Renovar el token de acceso");
        }
    }

    internal sealed class GetCurrentUserEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/me", Me)
                .WithName("GetCurrentUser")
                .WithSummary("Identidad del usuario autenticado");
        }
    }

    // Mismo 401 para correo inexistente y contraseña incorrecta: no se revela qué cuentas existen.
    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        UserManager<ApplicationUser> userManager,
        TokenService tokenService,
        CancellationToken cancellationToken)
    {
        var applicationUser = await userManager.FindByEmailAsync(request.Email);
        if (applicationUser is null || !await userManager.CheckPasswordAsync(applicationUser, request.Password))
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Credenciales inválidas.");
        return TypedResults.Ok(await tokenService.IssueAsync(applicationUser, cancellationToken));
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> RefreshAsync(
        RefreshRequest request,
        TokenService tokenService,
        CancellationToken cancellationToken)
    {
        var tokenResponse = await tokenService.RefreshAsync(request.RefreshToken, cancellationToken);
        return tokenResponse is null
            ? TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "El token de renovación no es válido.")
            : TypedResults.Ok(tokenResponse);
    }

    private static Ok<MeResponse> Me(ClaimsPrincipal principal)
    {
        var response = new MeResponse(
            principal.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier),
            principal.FindFirstValue(System.Security.Claims.ClaimTypes.Email),
            principal.FindFirstValue(System.Security.Claims.ClaimTypes.Name),
            principal.FindFirstValue(ContractClaimTypes.AdjusterId),
            principal.FindAll(System.Security.Claims.ClaimTypes.Role).Select(claim => claim.Value).ToList());

        return TypedResults.Ok(response);
    }
}
