using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Siniestros360.Messaging.Persistence;

// Respuesta guardada de un comando idempotente. La llave es única por usuario: dos usuarios nunca comparten respuestas.
[Index(nameof(UserId), nameof(Key), IsUnique = true)]
[Index(nameof(ExpiresAt))]
public sealed class IdempotencyRecord
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = default!;
    public string Key { get; set; } = default!;
    public string RequestHash { get; set; } = default!;
    public int StatusCode { get; set; }
    public string ResponseBody { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

// Contrato que cumple el DbContext de cada servicio: idempotencia HTTP más las tablas del outbox e inbox de MassTransit.
public interface IReliableMessagingDbContext
{
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
}

public static class ReliableMessagingModel
{
    public const string Schema = "messaging";

    // InboxState deduplica mensajes consumidos; OutboxMessage y OutboxState guardan lo publicado en la misma transacción del negocio.
    // Van en el esquema común "messaging" dentro de la base de cada servicio: MassTransit cachea por proceso el SQL de bloqueo
    // con el nombre de tabla, y un esquema por servicio rompe cualquier proceso que aloje más de un DbContext (pruebas, por ejemplo).
    public static ModelBuilder AddReliableMessaging(this ModelBuilder builder)
    {
        builder.AddInboxStateEntity(entity => entity.ToTable("InboxState", Schema));
        builder.AddOutboxMessageEntity(entity => entity.ToTable("OutboxMessage", Schema));
        builder.AddOutboxStateEntity(entity => entity.ToTable("OutboxState", Schema));
        return builder;
    }
}
