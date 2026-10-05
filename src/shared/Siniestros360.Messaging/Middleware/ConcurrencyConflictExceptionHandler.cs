using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Siniestros360.Messaging.Middleware;

// Dos peticiones cambiaron el mismo registro a la vez (concurrencia optimista). No es un error del servidor: el cliente
// debe reintentar. Antes cada comando lo atrapaba con su propio try/catch; aquí se traduce una vez a 409.
internal sealed class ConcurrencyConflictExceptionHandler(IProblemDetailsService problemDetails, ILogger<ConcurrencyConflictExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException) return false;
        logger.LogWarning(exception, "Conflicto de concurrencia en {Path}", httpContext.Request.Path);
        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = StatusCodes.Status409Conflict, Title = "El registro cambió al mismo tiempo; vuelve a intentarlo." },
        });
    }
}
