using System.Data;
using Azure.Identity;
using MassTransit;
using MassTransit.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siniestros360.Messaging;
using Siniestros360.Messaging.Idempotency;
using Siniestros360.Messaging.Middleware;
using Siniestros360.Messaging.Persistence;

namespace Microsoft.Extensions.Hosting;

public static class MessagingDependencyInjection
{
    // Mensajería del servicio con MassTransit:
    // - Bus outbox de EF Core: lo publicado con IPublishEndpoint se guarda en la transacción del negocio y se envía después del commit.
    // - Consumer outbox + inbox: cada mensaje recibido se procesa una vez y lo que publica viaja en la misma transacción.
    // - Una cola por servicio (endpointName) con sus consumidores de negocio y sagas; los fallos van a la DLQ nativa.
    // - La telemetría (GPS, 5 mensajes por segundo) va a su propia cola "{endpointName}-telemetry": si compartiera la cola
    //   del servicio, cada evento del siniestro esperaría detrás de decenas de posiciones.
    // Con ConnectionStrings:servicebus usa Azure Service Bus; sin ella, transporte en memoria (pruebas o ejecución aislada).
    public static IHostApplicationBuilder AddReliableMessaging<TDbContext>(
        this IHostApplicationBuilder builder,
        string endpointName,
        Action<IBusRegistrationConfigurator>? configure = null,
        params Type[] telemetryConsumers)
        where TDbContext : DbContext, IReliableMessagingDbContext
    {
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IReliableMessagingDbContext>(services => services.GetRequiredService<TDbContext>());
        builder.Services.AddScoped<RollbackActions>();
        builder.Services.AddHostedService<IdempotencyCleanupWorker<TDbContext>>();
        builder.Services.AddExceptionHandler<ConcurrencyConflictExceptionHandler>();

        builder.Services.AddMassTransit(bus =>
        {
            bus.AddEntityFrameworkOutbox<TDbContext>(outbox =>
            {
                outbox.UsePostgres();
                // ReadCommitted: la inbox ya se protege con su índice único; Serializable (el predeterminado) hace fallar
                // con 40001 a mensajes concurrentes que tocan las mismas filas.
                outbox.IsolationLevel = IsolationLevel.ReadCommitted;
                outbox.UseBusOutbox();
                outbox.QueryDelay = TimeSpan.FromSeconds(1);
                outbox.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
            });
            configure?.Invoke(bus);
            foreach (var consumer in telemetryConsumers) bus.AddConsumer(consumer);

            var telemetryEndpoint = $"{endpointName}-telemetry";
            var connectionString = builder.Configuration.GetConnectionString("servicebus");
            if (string.IsNullOrWhiteSpace(connectionString) && string.IsNullOrWhiteSpace(builder.Configuration["Messaging:ServiceBus:FullyQualifiedNamespace"]))
            {
                bus.UsingInMemory((context, cfg) =>
                {
                    cfg.UsePublishFilter(typeof(HttpCorrelationPublishFilter<>), context);
                    cfg.ReceiveEndpoint(endpointName, endpoint => ConfigureEndpoint<TDbContext>(endpoint, context, Business(context, telemetryConsumers), sagas: true));
                    if (telemetryConsumers.Length > 0) cfg.ReceiveEndpoint(telemetryEndpoint, endpoint => ConfigureEndpoint<TDbContext>(endpoint, context, telemetryConsumers, sagas: false));
                });
                return;
            }

            // Messaging:EntityPrefix aísla a quien comparte el namespace (por ejemplo, CI junto al desarrollo local):
            // con otro prefijo tiene sus propios topics y colas y no compite por los mensajes.
            var prefix = builder.Configuration["Messaging:EntityPrefix"] ?? "";
            bus.UsingAzureServiceBus((context, cfg) =>
            {
                ConfigureHost(cfg, builder.Configuration, connectionString);
                cfg.UsePublishFilter(typeof(HttpCorrelationPublishFilter<>), context);
                if (prefix.Length > 0) cfg.MessageTopology.SetEntityNameFormatter(new PrefixEntityNameFormatter(cfg.MessageTopology.EntityNameFormatter, prefix));
                cfg.ReceiveEndpoint(prefix + endpointName, endpoint =>
                {
                    ConfigureDeadLetters(endpoint);
                    ConfigureEndpoint<TDbContext>(endpoint, context, Business(context, telemetryConsumers), sagas: true);
                });
                if (telemetryConsumers.Length == 0) return;
                cfg.ReceiveEndpoint(prefix + telemetryEndpoint, endpoint =>
                {
                    ConfigureDeadLetters(endpoint);
                    // Cada posición actualiza una sola fila y el orden lo resuelve CapturedAt, así que se procesan en paralelo.
                    endpoint.PrefetchCount = 32;
                    endpoint.ConcurrentMessageLimit = 16;
                    ConfigureEndpoint<TDbContext>(endpoint, context, telemetryConsumers, sagas: false);
                });
            });
        });

        return builder;
    }

    // Migra al arrancar sólo en Development (Aspire, pruebas) o con Database:MigrateOnStartup=true. En otros ambientes
    // las migraciones son un paso del despliegue (bundle de EF): varias réplicas migrando a la vez, o una migración que
    // falla a mitad del arranque, no deben decidir el esquema de producción. EF InMemory (ejecución aislada) sólo crea el modelo.
    public static async Task InitializeDatabaseAsync<TDbContext>(this WebApplication app) where TDbContext : DbContext
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        if (!db.Database.IsRelational())
        {
            await db.Database.EnsureCreatedAsync();
            return;
        }

        if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Database:MigrateOnStartup", false)) await db.Database.MigrateAsync();
        else app.Logger.LogInformation("Migraciones al arrancar desactivadas fuera de Development; se aplican en el despliegue.");
    }

    // Consumidor sin éxito tras los reintentos o contrato ilegible → DLQ de la cola, no colas _error adicionales.
    private static void ConfigureDeadLetters(IServiceBusReceiveEndpointConfigurator endpoint)
    {
        endpoint.ConfigureDeadLetterQueueErrorTransport();
        endpoint.ConfigureDeadLetterQueueDeadLetterTransport();
        endpoint.MaxDeliveryCount = 5;
    }

    // ConfigureConsumers configuraría también los de telemetría en la cola del servicio (ignora ExcludeFromConfigureEndpoints),
    // así que los consumidores de negocio se listan uno por uno.
    private static Type[] Business(IBusRegistrationContext context, Type[] telemetryConsumers)
        => context.GetServices<IConsumerRegistration>().Select(x => x.Type).Except(telemetryConsumers).ToArray();

    // Orden obligatorio: reintento por fuera del outbox, para que cada intento empiece con un DbContext y un outbox limpios.
    // Las sagas viven en la cola del servicio.
    private static void ConfigureEndpoint<TDbContext>(IReceiveEndpointConfigurator endpoint, IBusRegistrationContext context, Type[] consumers, bool sagas)
        where TDbContext : DbContext
    {
        endpoint.UseMessageRetry(retry =>
        {
            retry.Handle<Exception>(IsTransientDatabaseError);
            retry.Intervals(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
        });
        endpoint.UseEntityFrameworkOutbox<TDbContext>(context);
        foreach (var consumer in consumers) endpoint.ConfigureConsumer(context, consumer);
        if (sagas) endpoint.ConfigureSagas(context);
    }

    // Conflictos que se resuelven reintentando con un DbContext limpio: concurrencia optimista, llave duplicada
    // (otra entrega del mismo mensaje ganó la inbox), serialización (40001) y deadlock (40P01).
    private static bool IsTransientDatabaseError(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateException) return true;
            var sqlState = current.GetType().GetProperty("SqlState")?.GetValue(current) as string;
            if (sqlState is "40001" or "40P01" or "23505") return true;
        }
        return false;
    }

    // SAS (emulador o desarrollo) se usa tal cual; en Azure basta el namespace y la identidad administrada.
    private static void ConfigureHost(IServiceBusBusFactoryConfigurator cfg, IConfiguration configuration, string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString) && connectionString.Contains("SharedAccessKey", StringComparison.OrdinalIgnoreCase))
        {
            cfg.Host(connectionString);
            return;
        }

        var fullyQualifiedNamespace = configuration["Messaging:ServiceBus:FullyQualifiedNamespace"]
            ?? new Uri(connectionString!).Host;
        cfg.Host(new Uri($"sb://{fullyQualifiedNamespace}"), host => host.TokenCredential = new DefaultAzureCredential());
    }
}
