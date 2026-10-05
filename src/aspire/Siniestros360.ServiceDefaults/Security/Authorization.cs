using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Siniestros360.Contracts.Common;
using ContractClaimTypes = Siniestros360.Contracts.Common.ClaimTypes;

namespace Microsoft.Extensions.Hosting;

// Políticas con nombre: los endpoints declaran qué permiten, no qué roles concretos lo cumplen.
// Admin entra en todas las políticas operativas.
public static class Policies
{
    // Torre de control y admin: vistas de toda la operación, despacho manual, alta de lotes GPS.
    public const string ControlTower = "control-tower";

    // Quien atiende en campo o lo supervisa: ajustador, torre y admin.
    public const string FieldOperations = "field-operations";

    // Sólo el ajustador: los comandos de campo (llegada, inicio y cierre) sobre su propio siniestro.
    public const string Adjuster = "adjuster";

    // Quien puede reportar un siniestro: el asegurado y la torre (siniestros de prueba).
    public const string ClaimReporters = "claim-reporters";

    // Recurso: un siniestro (ClaimParticipants). Torre y admin, el asegurado que lo reportó o el ajustador asignado.
    public const string ClaimParticipant = "claim-participant";

    // Recurso: un ajustador (AdjusterResource). Torre y admin, o el propio ajustador.
    public const string AdjusterSelf = "adjuster-self";
}

// Quién participa en un siniestro. Cada servicio lo construye desde su propio modelo (entidad, proyección o vista).
public sealed record ClaimParticipants(string? InsuredId, Guid? AdjusterId);

// Un ajustador como recurso protegido.
public sealed record AdjusterResource(Guid AdjusterId);

public static class AuthorizationSetup
{
    // Política por defecto: todo endpoint exige usuario autenticado salvo AllowAnonymous explícito. Un endpoint nuevo
    // al que se le olvide RequireAuthorization ya no queda anónimo.
    public static IServiceCollection AddSiniestrosAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, ClaimParticipantHandler>();
        services.AddSingleton<IAuthorizationHandler, AdjusterSelfHandler>();
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.ControlTower, policy => policy.RequireRole(Roles.ControlTower, Roles.Admin))
            .AddPolicy(Policies.FieldOperations, policy => policy.RequireRole(Roles.Adjuster, Roles.ControlTower, Roles.Admin))
            .AddPolicy(Policies.Adjuster, policy => policy.RequireRole(Roles.Adjuster))
            .AddPolicy(Policies.ClaimReporters, policy => policy.RequireRole(Roles.Insured, Roles.ControlTower, Roles.Admin))
            .AddPolicy(Policies.ClaimParticipant, policy => policy.AddRequirements(new ClaimParticipantRequirement()))
            .AddPolicy(Policies.AdjusterSelf, policy => policy.AddRequirements(new AdjusterSelfRequirement()));
        return services;
    }

    public static bool IsControlTower(this ClaimsPrincipal user) => user.IsInRole(Roles.ControlTower) || user.IsInRole(Roles.Admin);

    public static Guid? AdjusterId(this ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue(ContractClaimTypes.AdjusterId), out var id) ? id : null;
}

public sealed class ClaimParticipantRequirement : IAuthorizationRequirement;

public sealed class AdjusterSelfRequirement : IAuthorizationRequirement;

// La regla de acceso a un siniestro, en un solo lugar. Antes vivía copiada en Claims, Documents, Operations y el hub.
// Un ajustador sin adjuster_id en el token no participa en ningún siniestro.
public sealed class ClaimParticipantHandler : AuthorizationHandler<ClaimParticipantRequirement, ClaimParticipants>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ClaimParticipantRequirement requirement, ClaimParticipants claim)
    {
        var user = context.User;
        var allowed = user.IsControlTower()
            || (user.IsInRole(Roles.Adjuster) && user.AdjusterId() is { } adjusterId && claim.AdjusterId == adjusterId)
            || (!user.IsInRole(Roles.Adjuster) && claim.InsuredId is not null && claim.InsuredId == user.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier));
        if (allowed) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public sealed class AdjusterSelfHandler : AuthorizationHandler<AdjusterSelfRequirement, AdjusterResource>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdjusterSelfRequirement requirement, AdjusterResource adjuster)
    {
        if (context.User.IsControlTower() || (context.User.IsInRole(Roles.Adjuster) && context.User.AdjusterId() == adjuster.AdjusterId))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
