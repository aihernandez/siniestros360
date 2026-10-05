using FluentValidation;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.SharedKernel.Behaviors;

// Valida el comando con sus validadores de FluentValidation antes de ejecutar el handler. Un comando inválido no llega
// al handler: devuelve ValidationError (400) con todas las fallas. Las consultas no se validan aquí.
internal static class ValidationDecorator
{
    internal sealed class CommandHandler<TCommand, TResponse>(ICommandHandler<TCommand, TResponse> inner, IEnumerable<IValidator<TCommand>> validators)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var error = await ValidateAsync(command, validators, cancellationToken);
            return error is null ? await inner.Handle(command, cancellationToken) : Result.Failure<TResponse>(error);
        }
    }

    internal sealed class CommandBaseHandler<TCommand>(ICommandHandler<TCommand> inner, IEnumerable<IValidator<TCommand>> validators)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var error = await ValidateAsync(command, validators, cancellationToken);
            return error is null ? await inner.Handle(command, cancellationToken) : Result.Failure(error);
        }
    }

    private static async Task<ValidationError?> ValidateAsync<TCommand>(TCommand command, IEnumerable<IValidator<TCommand>> validators, CancellationToken ct)
    {
        var all = validators.ToArray();
        if (all.Length == 0) return null;
        var context = new ValidationContext<TCommand>(command);
        var results = await Task.WhenAll(all.Select(validator => validator.ValidateAsync(context, ct)));
        var failures = results.SelectMany(result => result.Errors).Where(failure => failure is not null).ToArray();
        return failures.Length == 0
            ? null
            : new ValidationError(failures.Select(failure => Error.Problem(failure.PropertyName, failure.ErrorMessage)).ToArray());
    }
}
