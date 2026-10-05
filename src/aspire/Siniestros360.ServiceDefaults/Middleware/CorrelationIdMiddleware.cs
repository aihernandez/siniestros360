using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Siniestros360.ServiceDefaults.Middleware;

// Toma X-Correlation-ID de la petición (o el TraceId si no viene), lo devuelve en la respuesta y lo agrega a todos los
// logs de la petición. Con IncludeScopes de OpenTelemetry aparece como atributo en los logs estructurados de Aspire.
// MassTransit lo lleva a los eventos (Correlation.From) para seguir un siniestro de punta a punta.
internal sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId)) correlationId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        context.Request.Headers[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) => app.UseMiddleware<CorrelationIdMiddleware>();
}
