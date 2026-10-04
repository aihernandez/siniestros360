using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using MassTransit;
using MassTransit.Serialization;
using Siniestros360.Contracts.Events;

namespace Siniestros360.Tests.Contracts;

// Pruebas de contrato entre productores y consumidores de eventos de integración.
// Los servicios se despliegan por separado: un cambio en un contrato que no rompe la compilación sí rompe al consumidor
// que corre con la versión anterior. Estas pruebas hacen visible cada cambio y comprueban que cada evento consumido
// tenga productor.
public sealed class ContractTests
{
    private static readonly Type[] Events = typeof(ClaimReported).Assembly.GetExportedTypes()
        .Where(x => x.Namespace == typeof(ClaimReported).Namespace && x.IsSealed)
        .OrderBy(x => x.Name, StringComparer.Ordinal)
        .ToArray();

    private static readonly string Root = FindRepositoryRoot();

    // El esquema aprobado vive en ContractSchema.approved.txt. Agregar un evento o un campo nuevo es compatible si los
    // consumidores toleran su ausencia; renombrar, quitar o cambiar el tipo de un campo, o mover el tipo de namespace
    // (cambia la URN de MassTransit y el topic), rompe a los consumidores desplegados. Tras revisar el cambio:
    //   $env:UPDATE_CONTRACTS = "1"; dotnet test tests/Siniestros360.Tests.Contracts
    [Fact]
    public void Contracts_match_the_approved_schema()
    {
        var approvedPath = Path.Combine(Root, "tests", "Siniestros360.Tests.Contracts", "ContractSchema.approved.txt");
        var current = Schema();
        if (Environment.GetEnvironmentVariable("UPDATE_CONTRACTS") == "1") File.WriteAllText(approvedPath, current);

        var approved = File.Exists(approvedPath) ? File.ReadAllText(approvedPath).ReplaceLineEndings("\n") : "";
        var removed = approved.Split('\n').Except(current.Split('\n')).Where(x => x.Length > 0).ToArray();
        var added = current.Split('\n').Except(approved.Split('\n')).Where(x => x.Length > 0).ToArray();
        (removed.Length + added.Length).Should().Be(0,
            "el contrato cambió.\nQuitado:\n  {0}\nAgregado:\n  {1}\nSi es intencional y compatible, actualiza el esquema aprobado con UPDATE_CONTRACTS=1.",
            string.Join("\n  ", removed), string.Join("\n  ", added));
    }

    // Cada evento viaja con el serializador del bus. Un record que no se puede reconstruir (constructor sin parámetros
    // que correspondan a sus propiedades, tipos no serializables) falla aquí y no en producción.
    [Fact]
    public void Every_contract_round_trips_with_the_bus_serializer()
    {
        foreach (var type in Events)
        {
            var sample = Sample(type);
            var json = JsonSerializer.Serialize(sample, type, SystemTextJsonMessageSerializer.Options);
            var copy = JsonSerializer.Deserialize(json, type, SystemTextJsonMessageSerializer.Options);
            copy.Should().Be(sample, "{0} debe sobrevivir a la serialización del bus", type.Name);
        }
    }

    // Lectura tolerante: un consumidor con la versión anterior del contrato recibe campos que no conoce.
    [Fact]
    public void Consumers_ignore_fields_added_by_a_newer_producer()
    {
        foreach (var type in Events)
        {
            var node = JsonSerializer.SerializeToNode(Sample(type), type, SystemTextJsonMessageSerializer.Options)!.AsObject();
            node["fieldAddedInAFutureVersion"] = "value";
            var read = () => node.Deserialize(type, SystemTextJsonMessageSerializer.Options);
            read.Should().NotThrow("{0} debe ignorar campos desconocidos", type.Name);
        }
    }

    // Un consumidor que espera un evento que nadie publica nunca se ejecuta, y compila sin advertencias.
    [Fact]
    public void Every_consumed_contract_has_a_producer()
    {
        var sources = ServiceSources();
        var consumed = Consumed(sources);
        var produced = Produced(sources);

        consumed.Should().NotBeEmpty();
        consumed.Except(produced).Should().BeEmpty("cada evento consumido debe tener al menos un servicio que lo publique");
    }

    // Lo publicado sin consumidor es tráfico y costo sin efecto. Se acepta sólo si está listado aquí con su motivo.
    [Fact]
    public void Every_published_contract_has_a_consumer_or_a_reason()
    {
        string[] withoutConsumer =
        [
            nameof(AdjusterAssignmentRequested), // Auditoría: la propia saga reserva al ajustador en la misma transacción.
            nameof(ClaimReassignmentRequested), // Auditoría de la saga antes de intentar reasignar.
        ];
        var sources = ServiceSources();
        var unconsumed = Produced(sources).Except(Consumed(sources)).Order().ToArray();
        unconsumed.Should().BeEquivalentTo(withoutConsumer, "un evento nuevo sin consumidor debe justificarse en esta lista");
    }

    private static string Schema()
    {
        var builder = new StringBuilder();
        foreach (var type in Events)
        {
            // Una línea autosuficiente por dato: el diff muestra exactamente qué evento y qué campo cambió.
            builder.Append(type.Name).Append(" = ").Append(MessageUrn.ForTypeString(type)).Append('\n');
            var parameters = type.GetConstructors().Single().GetParameters();
            foreach (var parameter in parameters) builder.Append(type.Name).Append('.').Append(parameter.Name).Append(": ").Append(TypeName(parameter)).Append('\n');
        }
        return builder.ToString();
    }

    private static string TypeName(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        var underlying = Nullable.GetUnderlyingType(type);
        var nullable = underlying is not null || new NullabilityInfoContext().Create(parameter).WriteState == NullabilityState.Nullable;
        return (underlying ?? type).Name + (nullable ? "?" : "");
    }

    private static object Sample(Type type)
    {
        var parameters = type.GetConstructors().Single().GetParameters();
        return Activator.CreateInstance(type, parameters.Select(x => SampleValue(x.ParameterType, x.Name!)).ToArray())!;
    }

    private static object SampleValue(Type type, string name)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type switch
        {
            _ when type == typeof(Guid) => Guid.NewGuid(),
            _ when type == typeof(string) => $"{name}-value",
            _ when type == typeof(decimal) => 25.6866123m,
            _ when type == typeof(double) => 12.5,
            _ when type == typeof(long) => 42L,
            _ when type == typeof(int) => 7,
            _ when type == typeof(bool) => true,
            _ when type == typeof(DateTimeOffset) => new DateTimeOffset(2026, 10, 3, 12, 30, 15, TimeSpan.Zero),
            _ => throw new NotSupportedException($"Agrega un valor de ejemplo para {type.Name} ({name}).")
        };
    }

    // Fuentes de los proyectos de src/ salvo Contracts (sin bin/obj ni migraciones). La prueba lee el código porque la
    // publicación no deja rastro en los metadatos del ensamblado.
    private static string[] ServiceSources() => Directory.GetDirectories(Path.Combine(Root, "src"), "Siniestros360.*", SearchOption.AllDirectories)
        .Where(x => !x.EndsWith("Contracts", StringComparison.Ordinal))
        .SelectMany(x => Directory.GetFiles(x, "*.cs", SearchOption.AllDirectories))
        .Where(x => !x.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !x.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !x.Contains("Migrations"))
        .Select(File.ReadAllText)
        .ToArray();

    private static string[] Names() => Events.Select(x => x.Name).ToArray();

    private static string[] Consumed(string[] sources)
    {
        var names = Names();
        return sources.SelectMany(x => Regex.Matches(x, @"(?:IConsumer|Event)<(\w+)>").Select(m => m.Groups[1].Value))
            .Where(names.Contains).Distinct().Order().ToArray();
    }

    private static string[] Produced(string[] sources)
    {
        var names = Names();
        // Fuera de las pruebas, un servicio sólo construye un contrato para publicarlo: directamente, por el evento que
        // devuelve un comando de Claims (MapCommand) o a través de una abstracción como LocationEventSink.
        return sources.SelectMany(x => Regex.Matches(x, @"\bnew (\w+)\(").Select(m => m.Groups[1].Value))
            .Where(names.Contains).Distinct().Order().ToArray();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Siniestros360.slnx"))) return directory.FullName;
        }
        throw new InvalidOperationException("No se encontró Siniestros360.slnx.");
    }
}
