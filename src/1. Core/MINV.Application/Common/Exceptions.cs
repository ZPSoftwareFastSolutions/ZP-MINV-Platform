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

/// <summary>Credenciales incorrectas (mensaje genérico: no revela si el correo existe).</summary>
public sealed class AuthenticationFailedException(string message) : Exception(message);
