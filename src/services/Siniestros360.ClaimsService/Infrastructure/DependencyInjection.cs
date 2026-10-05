using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Siniestros360.ClaimsService.Infrastructure;

public static class DependencyInjection
{
    // Base de datos propia del servicio y mensajería confiable (outbox, inbox) con un consumidor por evento recibido.
    public static IHostApplicationBuilder AddClaimsInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.Services.AddDbContext<ClaimsDbContext>(options =>
        {
            var connectionString = builder.Configuration.GetConnectionString("claimsdb");
            if (string.IsNullOrWhiteSpace(connectionString)) options.UseInMemoryDatabase("claims-development");
            else options.UseNpgsql(connectionString);
        });
        builder.AddReliableMessaging<ClaimsDbContext>("claims-service", bus => bus.AddConsumers(typeof(DependencyInjection).Assembly));
        return builder;
    }
}
