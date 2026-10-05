namespace Siniestros360.SharedKernel;

public enum ErrorType
{
    Failure = 0,
    Validation = 1,
    Problem = 2,
    NotFound = 3,
    Conflict = 4,
    // No está en la plantilla de referencia: actuar como otro ajustador responde 403.
    Forbidden = 5,
}

// Error de negocio con código estable (para clientes y pruebas) y descripción para el usuario. Cada agregado declara su
// catálogo (ClaimErrors, DocumentErrors…) en vez de escribir los mensajes en los handlers.
public record Error(string Code, string Description, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);
    public static readonly Error NullValue = new("General.Null", "Se recibió un valor nulo.", ErrorType.Failure);

    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);

    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);

    public static Error Problem(string code, string description) => new(code, description, ErrorType.Problem);

    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);

    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);
}

// Varios errores de validación de entrada a la vez (responde 400 con la lista).
public sealed record ValidationError(Error[] Errors)
    : Error("Validation.General", "Uno o más datos no son válidos.", ErrorType.Validation);
