using System.Data;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Siniestros360.Messaging.Persistence;

namespace Siniestros360.Messaging.Idempotency;

// Marca un comando como idempotente. Se aplica en el handler: [Idempotent] async (...) => ...
// El filtro lo activa la convención WithIdempotency() del grupo de rutas.
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute(int expirationMinutes = IdempotentAttribute.DefaultExpirationMinutes) : Attribute
{
    public const int DefaultExpirationMinutes = 60;
    public TimeSpan Expiration { get; } = TimeSpan.FromMinutes(expirationMinutes);
}

public static class IdempotencyEndpointExtensions
{
    public const string HeaderName = "Idempotency-Key";

    public static TBuilder WithIdempotency<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.AddEndpointFilterFactory((factoryContext, next) =>
        {
            var attribute = factoryContext.MethodInfo.GetCustomAttribute<IdempotentAttribute>();
            if (attribute is null) return next;
            var requestParameters = RequestParameters(factoryContext);
            return invocation => new IdempotencyFilter(attribute, requestParameters).InvokeAsync(invocation, next);
        });

    // Parámetros que forman la petición (ruta, query y body). Se excluyen servicios de DI e infraestructura
    // con la misma regla que usa Minimal APIs para inferir de dónde viene cada parámetro.
    private static int[] RequestParameters(EndpointFilterFactoryContext factoryContext)
    {
        var services = factoryContext.ApplicationServices.GetService<IServiceProviderIsService>();
        return factoryContext.MethodInfo.GetParameters()
            .Select((parameter, index) => (parameter, index))
            .Where(x => x.parameter.ParameterType != typeof(HttpContext)
                && x.parameter.ParameterType != typeof(CancellationToken)
                && x.parameter.ParameterType != typeof(ClaimsPrincipal)
                && services?.IsService(x.parameter.ParameterType) != true)
            .Select(x => x.index)
            .ToArray();
    }
}

// Todo el comando corre en UNA transacción local: reserva de la llave, cambio de negocio, outbox y respuesta guardada.
// Si el handler lanza, responde algo distinto de 2xx o el commit falla, se revierte todo y se ejecutan las RollbackActions
// (por ejemplo, borrar un archivo ya subido). Una petición gemela con la misma llave espera en el índice único al commit
// de la primera y después recibe su respuesta guardada.
internal sealed class IdempotencyFilter(IdempotentAttribute attribute, int[] requestParameters)
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!http.Request.Headers.TryGetValue(IdempotencyEndpointExtensions.HeaderName, out var header) || !Guid.TryParse(header, out var idempotencyKey))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [IdempotencyEndpointExtensions.HeaderName] = ["A GUID Idempotency-Key header is required."] });
        }

        var store = http.RequestServices.GetRequiredService<IReliableMessagingDbContext>();
        var db = (DbContext)store;
        var rollbackActions = http.RequestServices.GetRequiredService<RollbackActions>();
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        var key = idempotencyKey.ToString();
        var hash = await RequestHash(context);
        var ct = http.RequestAborted;

        var replay = await Replay(store, userId, key, hash, http, ct);
        if (replay is not null) return replay;

        // EF InMemory no soporta transacciones: sólo se usa al ejecutar un servicio aislado, nunca en pruebas de este filtro.
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            : null;
        try
        {
            var now = DateTimeOffset.UtcNow;
            await store.IdempotencyRecords.Where(x => x.UserId == userId && x.Key == key && x.ExpiresAt <= now).ExecuteDeleteIfRelationalAsync(db, ct);
            var reservation = new IdempotencyRecord { Id = Guid.NewGuid(), UserId = userId, Key = key, RequestHash = hash, StatusCode = 0, ResponseBody = "", CreatedAt = now, ExpiresAt = now.Add(attribute.Expiration) };
            store.IdempotencyRecords.Add(reservation);
            await db.SaveChangesAsync(ct);

            var result = await next(context);
            if (result is not IStatusCodeHttpResult { StatusCode: >= 200 and < 300 } status || result is not IValueHttpResult value)
            {
                await Rollback(transaction, db, rollbackActions);
                return result;
            }

            reservation.StatusCode = status.StatusCode ?? StatusCodes.Status200OK;
            reservation.ResponseBody = JsonSerializer.Serialize(value.Value, JsonOptions(http));
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            // La petición gemela confirmó primero: se responde con lo que ella guardó.
            await Rollback(transaction, db, rollbackActions);
            return await Replay(store, userId, key, hash, http, CancellationToken.None)
                ?? Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "A request with this Idempotency-Key is still in progress.");
        }
        catch
        {
            await Rollback(transaction, db, rollbackActions);
            throw;
        }
    }

    private static async Task<IResult?> Replay(IReliableMessagingDbContext store, string userId, string key, string hash, HttpContext http, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await store.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.Key == key && x.ExpiresAt > now, ct);
        if (existing is null) return null;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(existing.RequestHash), Encoding.UTF8.GetBytes(hash)))
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Idempotency-Key was already used with a different request.");
        http.Response.Headers["Idempotent-Replayed"] = "true";
        return Results.Content(existing.ResponseBody, "application/json", Encoding.UTF8, existing.StatusCode);
    }

    private static async Task Rollback(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction, DbContext db, RollbackActions actions)
    {
        if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        await actions.RunAsync(CancellationToken.None);
    }

    // 23505 = unique_violation en PostgreSQL.
    private static bool IsDuplicateKey(DbUpdateException exception)
        => exception.InnerException is { } inner && inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string == "23505";

    private async Task<string> RequestHash(EndpointFilterInvocationContext context)
    {
        var request = context.HttpContext.Request;
        var parts = new List<object?> { request.Method, request.Path.Value };
        foreach (var index in requestParameters)
        {
            var argument = context.Arguments[index];
            parts.Add(argument is IFormFile file ? await Describe(file) : argument);
        }

        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(parts, JsonOptions(context.HttpContext))));
    }

    private static async Task<object> Describe(IFormFile file)
    {
        await using var stream = file.OpenReadStream();
        return new { file.FileName, file.ContentType, file.Length, Content = Convert.ToHexString(await SHA256.HashDataAsync(stream)) };
    }

    private static JsonSerializerOptions JsonOptions(HttpContext http)
        => http.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
}

internal static class QueryableExtensions
{
    public static async Task ExecuteDeleteIfRelationalAsync<T>(this IQueryable<T> query, DbContext db, CancellationToken ct)
    {
        if (db.Database.IsRelational()) await query.ExecuteDeleteAsync(ct);
    }
}
