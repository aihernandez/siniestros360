using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Siniestros360.SharedKernel;

namespace Siniestros360.ServiceDefaults.Endpoints;

// Un endpoint por caso de uso. La clase arma el comando o la consulta desde la petición, llama al handler y traduce el
// Result a HTTP; no contiene lógica de negocio. Se descubre por reflexión: agregar un endpoint es agregar una clase.
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}

public static class EndpointExtensions
{
    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        var endpoints = assembly.DefinedTypes
            .Where(type => type is { IsAbstract: false, IsInterface: false } && type.IsAssignableTo(typeof(IEndpoint)))
            .Select(type => ServiceDescriptor.Transient(typeof(IEndpoint), type))
            .ToArray();
        services.TryAddEnumerable(endpoints);
        return services;
    }

    // Mapea todos los endpoints del servicio sobre su grupo (prefijo, tag, idempotencia y autorización del grupo).
    public static RouteGroupBuilder MapEndpoints(this WebApplication app, RouteGroupBuilder group)
    {
        foreach (var endpoint in app.Services.GetRequiredService<IEnumerable<IEndpoint>>()) endpoint.MapEndpoint(group);
        return group;
    }
}

public static class ResultExtensions
{
    public static TOut Match<TOut>(this Result result, Func<TOut> onSuccess, Func<Result, TOut> onFailure)
        => result.IsSuccess ? onSuccess() : onFailure(result);

    public static TOut Match<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> onSuccess, Func<Result<TIn>, TOut> onFailure)
        => result.IsSuccess ? onSuccess(result.Value) : onFailure(result);
}

// Traduce un Error a Problem Details (RFC 7807) con el código HTTP de su tipo. ProblemDetails de ServiceDefaults agrega
// traceId y correlationId. El título es la descripción para el usuario; "code" es el código estable para clientes.
public static class CustomResults
{
    public static IResult Problem(Result result)
    {
        if (result.IsSuccess) throw new InvalidOperationException("Un resultado exitoso no es un problema.");
        var error = result.Error;

        if (error is ValidationError validation)
        {
            var errors = validation.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray());
            return TypedResults.ValidationProblem(errors, title: error.Description, extensions: Extensions(error));
        }

        return TypedResults.Problem(
            statusCode: StatusCode(error.Type),
            title: error.Type == ErrorType.Failure ? "Ocurrió un error inesperado." : error.Description,
            extensions: error.Type == ErrorType.Failure ? null : Extensions(error));
    }

    private static Dictionary<string, object?> Extensions(Error error) => new() { ["code"] = error.Code };

    private static int StatusCode(ErrorType type) => type switch
    {
        ErrorType.Validation or ErrorType.Problem => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status500InternalServerError,
    };
}
