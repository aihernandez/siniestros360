using System.Data;
using Azure.Identity;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siniestros360.Messaging.Idempotency;
using Siniestros360.Messaging.Persistence;

namespace Microsoft.Extensions.Hosting;

public static class MessagingDependencyInjection
{
    // Mensajería del servicio con MassTransit:
    // - Bus outbox de EF Core: lo publicado con IPublishEndpoint se guarda en la transacción del negocio y se envía después del commit.
    // - Consumer outbox + inbox: cada mensaje recibido se procesa una vez y lo que publica viaja en la misma transacción.
    // - Una sola cola por servicio (endpointName) con todos sus consumidores y sagas; los fallos van a la DLQ nativa.
    // Con ConnectionStrings:servicebus usa Azure Service Bus; sin ella, transporte en memoria (pruebas o ejecución aislada).
    public static IHostApplicationBuilder AddReliableMessaging<TDbContext>(
        this IHostApplicationBuilder builder,
        string endpointName,
        Action<IBusRegistrationConfigurator>? configure = null)
        where TDbContext : DbContext, IReliableMessagingDbContext
    {
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IReliableMessagingDbContext>(services => services.GetRequiredService<TDbContext>());
        builder.Services.AddScoped<RollbackActions>();
        builder.Services.AddHostedService<IdempotencyCleanupWorker<TDbContext>>();

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

            var connectionString = builder.Configuration.GetConnectionString("servicebus");
            if (string.IsNullOrWhiteSpace(connectionString) && string.IsNullOrWhiteSpace(builder.Configuration["Messaging:ServiceBus:FullyQualifiedNamespace"]))
            {
                bus.UsingInMemory((context, cfg) => cfg.ReceiveEndpoint(endpointName, endpoint => ConfigureEndpoint<TDbContext>(endpoint, context)));
                return;
            }

            bus.UsingAzureServiceBus((context, cfg) =>
            {
                ConfigureHost(cfg, builder.Configuration, connectionString);
                cfg.ReceiveEndpoint(endpointName, endpoint =>
                {
                    // Consumidor sin éxito tras los reintentos o contrato ilegible → DLQ de la cola, no colas _error adicionales.
                    endpoint.ConfigureDeadLetterQueueErrorTransport();
                    endpoint.ConfigureDeadLetterQueueDeadLetterTransport();
                    endpoint.MaxDeliveryCount = 5;
                    ConfigureEndpoint<TDbContext>(endpoint, context);
                });
            });
        });

        return builder;
    }

    // Aplica las migraciones del servicio. EF InMemory (ejecución aislada) sólo crea el modelo.
    public static async Task InitializeDatabaseAsync<TDbContext>(this WebApplication app) where TDbContext : DbContext
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        if (db.Database.IsRelational()) await db.Database.MigrateAsync();
        else await db.Database.EnsureCreatedAsync();
    }

    // Orden obligatorio: reintento por fuera del outbox, para que cada intento empiece con un DbContext y un outbox limpios.
    private static void ConfigureEndpoint<TDbContext>(IReceiveEndpointConfigurator endpoint, IBusRegistrationContext context)
        where TDbContext : DbContext
    {
        endpoint.UseMessageRetry(retry =>
        {
            retry.Handle<Exception>(IsTransientDatabaseError);
            retry.Intervals(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
        });
        endpoint.UseEntityFrameworkOutbox<TDbContext>(context);
        endpoint.ConfigureConsumers(context);
        endpoint.ConfigureSagas(context);
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
