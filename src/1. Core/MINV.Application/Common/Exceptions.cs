namespace MINV.Application.Common;

/// <summary>Otra sesión modificó los mismos datos (control de concurrencia optimista) o creó el mismo registro a la
/// vez. El caso de uso reintenta; si persiste, el usuario debe volver a intentarlo.</summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>No existe lo que se buscó (o pertenece a otra empresa).</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>El usuario no tiene el permiso o la empresa no tiene licenciado el módulo.</summary>
public sealed class AccessDeniedException(string message) : Exception(message);

/// <summary>Datos de entrada inválidos (FluentValidation).</summary>
public sealed class RequestValidationException(IReadOnlyList<string> errors)
    : Exception("Datos no válidos: " + string.Join(" · ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>Credenciales incorrectas (mensaje genérico: no revela si el correo existe). V7: puede llevar un código estable
/// (<see cref="AuthenticationCodes"/>) para que el cliente reconozca el caso sin leer el texto.</summary>
public sealed class AuthenticationFailedException(string message, string? code = null) : Exception(message)
{
    /// <summary>Código estable de la falla; null en el caso general (credenciales incorrectas).</summary>
    public string? Code { get; } = code;
}

/// <summary>V7 · Códigos estables de las fallas de autenticación.</summary>
public static class AuthenticationCodes
{
    /// <summary>La cuenta está bloqueada por intentos fallidos (se desbloquea sola al pasar el tiempo de espera).</summary>
    public const string Locked = "auth.locked";
}

/// <summary>V4 · Se repitió una petición idempotente (mismo id) con OTRO contenido: se rechaza (HTTP 422) en lugar de
/// registrar dos veces.</summary>
public sealed class IdempotencyConflictException(string message) : Exception(message);
