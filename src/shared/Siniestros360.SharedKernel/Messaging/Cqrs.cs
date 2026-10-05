namespace Siniestros360.SharedKernel.Messaging;

// CQRS sin mediador: el endpoint (o un consumidor) inyecta el handler del caso de uso y lo llama. Los comandos cambian
// estado; las consultas sólo leen. Los decoradores (validación, logging, trazas) envuelven a todos los handlers.

public interface ICommand;

public interface ICommand<TResponse>;

public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<Result> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken);
}
