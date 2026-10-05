using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.SharedKernel.Behaviors;

// Un span de OpenTelemetry y un registro por cada comando o consulta, con el código de error cuando falla. Sustituye los
// StartActivity("Claim.Report") escritos a mano en cada handler. La fuente "Siniestros360.Cqrs" entra por el comodín
// "Siniestros360.*" que ServiceDefaults ya registra.
internal static class InstrumentationDecorator
{
    public static readonly ActivitySource ActivitySource = new("Siniestros360.Cqrs");

    internal sealed class CommandHandler<TCommand, TResponse>(ICommandHandler<TCommand, TResponse> inner, ILogger<CommandHandler<TCommand, TResponse>> logger)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
            => InstrumentAsync("command", typeof(TCommand).Name, logger, () => inner.Handle(command, cancellationToken));
    }

    internal sealed class CommandBaseHandler<TCommand>(ICommandHandler<TCommand> inner, ILogger<CommandBaseHandler<TCommand>> logger)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public Task<Result> Handle(TCommand command, CancellationToken cancellationToken)
            => InstrumentAsync("command", typeof(TCommand).Name, logger, () => inner.Handle(command, cancellationToken));
    }

    internal sealed class QueryHandler<TQuery, TResponse>(IQueryHandler<TQuery, TResponse> inner, ILogger<QueryHandler<TQuery, TResponse>> logger)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken)
            => InstrumentAsync("query", typeof(TQuery).Name, logger, () => inner.Handle(query, cancellationToken));
    }

    private static async Task<TResult> InstrumentAsync<TResult>(string kind, string name, ILogger logger, Func<Task<TResult>> next)
        where TResult : Result
    {
        using var activity = ActivitySource.StartActivity(name);
        activity?.SetTag("siniestros360.cqrs.kind", kind);
        var result = await next();
        if (result.IsSuccess)
        {
            logger.LogDebug("{Kind} {Name} completado", kind, name);
            return result;
        }

        activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
        activity?.SetTag("siniestros360.error.code", result.Error.Code);
        // Un fallo de negocio (no encontrado, conflicto, validación) no es un error del sistema: se registra como aviso.
        logger.LogWarning("{Kind} {Name} terminó con {ErrorType} {ErrorCode}: {ErrorDescription}", kind, name, result.Error.Type, result.Error.Code, result.Error.Description);
        return result;
    }
}
