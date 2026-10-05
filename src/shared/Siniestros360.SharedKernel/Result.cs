using System.Diagnostics.CodeAnalysis;

namespace Siniestros360.SharedKernel;

// Resultado de un caso de uso: éxito, o un Error con código y tipo. Los handlers no lanzan excepciones por reglas de
// negocio; el endpoint traduce el tipo de error a HTTP (CustomResults.Problem).
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if ((isSuccess && error != Error.None) || (!isSuccess && error == Error.None))
            throw new ArgumentException("Un éxito no lleva error y un fallo siempre lleva uno.", nameof(error));
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error) : base(isSuccess, error) => _value = value;

    [NotNull]
    public TValue Value => IsSuccess ? _value! : throw new InvalidOperationException("Un resultado fallido no tiene valor.");

    // Permite `return response;` en el handler; un null se vuelve fallo explícito.
    public static implicit operator Result<TValue>(TValue? value) => value is not null ? Success(value) : Failure<TValue>(Error.NullValue);

    // Permite `return ClaimErrors.NotFound(id);` en un handler que devuelve Result<T>.
    public static implicit operator Result<TValue>(Error error) => Failure<TValue>(error);
}
