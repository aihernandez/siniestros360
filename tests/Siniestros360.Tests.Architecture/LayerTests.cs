using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Siniestros360.SharedKernel;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.Tests.Architecture;

// La plantilla de referencia separa las capas en proyectos y el compilador impide las dependencias prohibidas. Aquí cada
// microservicio es un proyecto con carpetas por capa (Domain, Application, Infrastructure, Endpoints), así que estas
// pruebas hacen ese trabajo: fallan en cuanto una capa interna conoce a una externa.
public sealed class LayerTests
{
    private static readonly Service[] Services =
    [
        new("Adjusters", typeof(Siniestros360.AdjustersService.Infrastructure.AdjustersDbContext).Assembly),
        new("Claims", typeof(Siniestros360.ClaimsService.Infrastructure.ClaimsDbContext).Assembly),
        new("Dispatch", typeof(Siniestros360.DispatchService.Infrastructure.DispatchDbContext).Assembly),
        new("Documents", typeof(Siniestros360.DocumentsService.Infrastructure.DocumentsDbContext).Assembly),
        new("Identity", typeof(Siniestros360.IdentityService.Infrastructure.IdentityData).Assembly),
        new("Location", typeof(Siniestros360.LocationService.Infrastructure.LocationDbContext).Assembly),
        new("Operations", typeof(Siniestros360.OperationsService.Infrastructure.OperationsDbContext).Assembly),
        new("Policy", typeof(Siniestros360.PolicyService.Infrastructure.PolicyDbContext).Assembly),
        new("Sla", typeof(Siniestros360.SlaService.Infrastructure.SlaDbContext).Assembly),
    ];

    // Servicios ya organizados por casos de uso (CQRS con handlers). Crece con cada fase del refactor (ADR-008).
    // PROVISIONAL(2026-10-05): sólo los migrados cumplen las reglas de Application y Endpoints — se cierra cuando la lista incluya a los siete servicios con API.
    private static readonly string[] MigratedToUseCases = ["Claims"];

    // Excepciones conocidas, con motivo. Cada una debe desaparecer, no crecer.
    // VERIFICAR(2026-10-05): AssignmentState es el estado persistido de la saga de MassTransit (SagaStateMachineInstance); moverlo a Application cambia el modelo de EF y requiere migración — se decide en la fase de Dispatch.
    // ApplicationUser hereda de IdentityUser: así funciona ASP.NET Core Identity, y la identidad es un subdominio genérico.
    private static readonly string[] KnownDomainExceptions =
    [
        "Dispatch: Siniestros360.DispatchService.Domain.AssignmentState",
        "Identity: Siniestros360.IdentityService.Domain.ApplicationUser",
    ];

    [Fact]
    public void The_domain_does_not_depend_on_other_layers_or_frameworks()
    {
        var violations = Services.SelectMany(service => Violations(service, "Domain",
            [service.Layer("Application"), service.Layer("Infrastructure"), service.Layer("Endpoints"), "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "MassTransit"]));
        violations.Except(KnownDomainExceptions).Should().BeEmpty("cada violación aparece como Servicio: Tipo; si es deliberada, va a KnownDomainExceptions con su motivo: {0}", string.Join(", ", violations.Except(KnownDomainExceptions)));
    }

    [Fact]
    public void Use_case_handlers_do_not_know_http_or_endpoints()
    {
        var violations = Migrated().SelectMany(service => Violations(service, "Application",
            [service.Layer("Endpoints"), "Microsoft.AspNetCore.Http", "Microsoft.AspNetCore.Routing"]));
        violations.Should().BeEmpty();
    }

    [Fact]
    public void Endpoints_only_talk_to_use_case_handlers()
    {
        var violations = Migrated().SelectMany(service => Violations(service, "Endpoints",
            [service.Layer("Infrastructure"), service.Layer("Domain"), "Microsoft.EntityFrameworkCore", "MassTransit"]));
        violations.Should().BeEmpty();
    }

    [Fact]
    public void Use_case_handlers_are_sealed()
    {
        var unsealed = Services.SelectMany(service => Types.InAssembly(service.Assembly)
            .That().ImplementInterface(typeof(ICommandHandler<>))
            .Or().ImplementInterface(typeof(ICommandHandler<,>))
            .Or().ImplementInterface(typeof(IQueryHandler<,>))
            .Should().BeSealed().GetResult().FailingTypeNames ?? []);
        unsealed.Should().BeEmpty();
    }

    [Fact]
    public void The_shared_kernel_knows_neither_http_nor_persistence_nor_messaging()
    {
        var result = Types.InAssembly(typeof(Result).Assembly)
            .ShouldNot().HaveDependencyOnAny("Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "MassTransit", "Siniestros360.Messaging")
            .GetResult();
        (result.FailingTypeNames ?? []).Should().BeEmpty();
    }

    private static IEnumerable<Service> Migrated() => Services.Where(service => MigratedToUseCases.Contains(service.Name));

    private static IEnumerable<string> Violations(Service service, string layer, string[] forbidden)
        => (Types.InAssembly(service.Assembly).That().ResideInNamespace(service.Layer(layer))
            .ShouldNot().HaveDependencyOnAny(forbidden).GetResult().FailingTypeNames ?? [])
            .Select(type => $"{service.Name}: {type}");

    private sealed record Service(string Name, Assembly Assembly)
    {
        public string Layer(string layer) => $"Siniestros360.{Name}Service.{layer}";
    }
}
