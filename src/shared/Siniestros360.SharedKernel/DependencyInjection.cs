using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Siniestros360.SharedKernel.Behaviors;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.SharedKernel;

public static class DependencyInjection
{
    // Registra los handlers y validadores del ensamblado del servicio y los envuelve con los decoradores. El orden de
    // Decorate importa: el último registrado queda por fuera, así que la instrumentación mide también la validación.
    public static IServiceCollection AddApplication(this IServiceCollection services, Assembly assembly)
    {
        services.Scan(scan => scan.FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime());

        services.TryDecorate(typeof(ICommandHandler<,>), typeof(ValidationDecorator.CommandHandler<,>));
        services.TryDecorate(typeof(ICommandHandler<>), typeof(ValidationDecorator.CommandBaseHandler<>));

        services.TryDecorate(typeof(IQueryHandler<,>), typeof(InstrumentationDecorator.QueryHandler<,>));
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(InstrumentationDecorator.CommandHandler<,>));
        services.TryDecorate(typeof(ICommandHandler<>), typeof(InstrumentationDecorator.CommandBaseHandler<>));

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        return services;
    }
}
