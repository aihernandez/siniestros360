using Siniestros360.ClaimsService.Application.Claims;
using Siniestros360.SharedKernel;

namespace Siniestros360.ClaimsService.Application;

public static class DependencyInjection
{
    // Handlers, validadores y decoradores del servicio (AddApplication) y el paso compartido de las transiciones.
    public static IServiceCollection AddClaimsApplication(this IServiceCollection services)
    {
        services.AddApplication(typeof(DependencyInjection).Assembly);
        services.AddScoped<ClaimTransition>();
        return services;
    }
}
