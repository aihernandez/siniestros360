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

// Pone la correlación de la petición HTTP en todo mensaje publicado durante ella, si no trae una. Así los handlers de
// casos de uso publican sin conocer HttpContext. Corre en el scope de la petición, antes de escribir en el outbox.
internal sealed class HttpCorrelationPublishFilter<T>(IHttpContextAccessor accessor) : IFilter<PublishContext<T>>
    where T : class
{
    public Task Send(PublishContext<T> context, IPipe<PublishContext<T>> next)
    {
        if (context.CorrelationId is null && accessor.HttpContext is { } http) context.CorrelationId = Correlation.From(http);
        return next.Send(context);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("http-correlation");
}
