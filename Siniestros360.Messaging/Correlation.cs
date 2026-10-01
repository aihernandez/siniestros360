using MassTransit;
using Microsoft.AspNetCore.Http;

namespace Siniestros360.Messaging;

public static class Correlation
{
    // El gateway asigna X-Correlation-ID a cada petición; los eventos que nacen de ella lo conservan como CorrelationId.
    // Dentro de un consumidor, MassTransit propaga solo ConversationId e InitiatorId (causación).
    public static Guid From(HttpContext http)
        => Guid.TryParse(http.Response.Headers["X-Correlation-ID"].FirstOrDefault(), out var parsed) ? parsed : Guid.NewGuid();

    public static Task PublishCorrelated<T>(this IPublishEndpoint publish, T message, Guid correlationId, CancellationToken cancellationToken)
        where T : class
        => publish.Publish(message, context => context.CorrelationId = correlationId, cancellationToken);
}
