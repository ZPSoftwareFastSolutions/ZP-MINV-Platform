using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using MINV.Application.Common;
using MINV.Domain.Common;

namespace MINV.Application.Remote;

/// <summary>
/// V4 · Contrato del servidor en la nube: el escritorio envía el MISMO comando o consulta de MediatR que usaría en
/// conexión directa, serializado en JSON, y el servidor lo ejecuta con SU tubería (validación, permisos y alcance por
/// sucursal calculados en el servidor, auditoría). <see cref="RequestId"/> hace idempotentes los comandos: si la red se
/// corta durante el COMMIT, el reintento con el mismo id devuelve la respuesta guardada en lugar de repetir la venta.
/// </summary>
public sealed record RpcRequest(Guid RequestId, string Type, JsonElement Payload);

public sealed record RpcError(string Kind, string Message, IReadOnlyList<string>? Errors = null, string? Code = null);

public sealed record RpcResponse(bool Ok, JsonElement? Result, RpcError? Error, bool Replayed = false);

/// <summary>Pedido de inicio de sesión en la nube (la contraseña viaja solo por TLS y nunca se guarda en el cliente).</summary>
public sealed record CloudLoginRequest(string TenantCode, string Email, string Password, string MachineName, string ClientVersion);

public sealed record CloudLoginResponse(string Token, DateTimeOffset ExpiresAt, Iam.LoginResult Login, string ServerVersion);

public static class RpcErrorKinds
{
    public const string Validation = "validation";
    public const string AccessDenied = "access_denied";
    public const string Authentication = "authentication";
    public const string NotFound = "not_found";
    public const string Concurrency = "concurrency";
    public const string Domain = "domain";
    public const string Idempotency = "idempotency";
    public const string Unsupported = "unsupported";
    public const string Server = "server";
}

/// <summary>Serialización compartida por el servidor y el cliente (records posicionales, enumeraciones como texto, tuplas).</summary>
public static class RpcJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { IncludeFields = true };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

/// <summary>
/// Catálogo de lo que el servidor acepta: todo <see cref="IRequest{TResponse}"/> público de MINV.Application salvo el
/// inicio de sesión (tiene su propio punto de entrada). Nada fuera de ese ensamblado se puede instanciar desde la red.
/// </summary>
public static class RpcCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, (Type Request, Type Response)>> Types = new(Build);

    public static bool TryResolve(string name, out Type request, out Type response)
    {
        if (Types.Value.TryGetValue(name, out var entry))
        {
            (request, response) = entry;
            return true;
        }
        request = response = typeof(void);
        return false;
    }

    public static IReadOnlyCollection<string> Names => Types.Value.Keys.ToList();

    /// <summary>Nombre con que viaja un comando (FullName sin ensamblado).</summary>
    public static string NameOf(Type requestType) => requestType.FullName!;

    public static Type ResponseTypeOf(Type requestType) =>
        requestType.GetInterfaces().First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)).GetGenericArguments()[0];

    /// <summary>¿Comando que modifica datos? (auditable: se ejecuta en una transacción con idempotencia).</summary>
    public static bool IsCommand(Type requestType) => typeof(IAuditableRequest).IsAssignableFrom(requestType);

    private static IReadOnlyDictionary<string, (Type, Type)> Build() =>
        typeof(RpcCatalog).Assembly.GetTypes()
            .Where(t => t is { IsPublic: true, IsAbstract: false, IsGenericTypeDefinition: false } && t != typeof(Iam.LoginCommand))
            .Select(t => (Type: t, Contract: t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))))
            .Where(x => x.Contract is not null)
            .ToDictionary(x => x.Type.FullName!, x => (x.Type, x.Contract!.GetGenericArguments()[0]), StringComparer.Ordinal);

    /// <summary>Excepción → error del contrato (sin detalles internos en las fallas técnicas).</summary>
    public static RpcError ToError(Exception ex) => ex switch
    {
        RequestValidationException v => new RpcError(RpcErrorKinds.Validation, v.Message, v.Errors),
        AccessDeniedException => new RpcError(RpcErrorKinds.AccessDenied, ex.Message),
        AuthenticationFailedException => new RpcError(RpcErrorKinds.Authentication, ex.Message),
        NotFoundException => new RpcError(RpcErrorKinds.NotFound, ex.Message),
        ConcurrencyConflictException => new RpcError(RpcErrorKinds.Concurrency, ex.Message),
        IdempotencyConflictException => new RpcError(RpcErrorKinds.Idempotency, ex.Message),
        DomainException d => new RpcError(RpcErrorKinds.Domain, d.Message, null, d.Code),
        _ => new RpcError(RpcErrorKinds.Server, "El servidor no pudo completar la operación. Intente de nuevo; si persiste, avise a soporte."),
    };

    /// <summary>Error del contrato → la misma excepción que lanzaría el caso de uso en conexión directa.</summary>
    public static Exception ToException(RpcError error) => error.Kind switch
    {
        RpcErrorKinds.Validation => new RequestValidationException(error.Errors ?? [error.Message]),
        RpcErrorKinds.AccessDenied => new AccessDeniedException(error.Message),
        RpcErrorKinds.Authentication => new AuthenticationFailedException(error.Message),
        RpcErrorKinds.NotFound => new NotFoundException(error.Message),
        RpcErrorKinds.Concurrency => new ConcurrencyConflictException(error.Message),
        RpcErrorKinds.Idempotency => new IdempotencyConflictException(error.Message),
        RpcErrorKinds.Domain => new DomainException(error.Code ?? "domain", error.Message),
        _ => new InvalidOperationException(error.Message),
    };

    /// <summary>Estado HTTP de cada tipo de error.</summary>
    public static int HttpStatusOf(string kind) => kind switch
    {
        RpcErrorKinds.Validation => 400,
        RpcErrorKinds.Authentication => 401,
        RpcErrorKinds.AccessDenied => 403,
        RpcErrorKinds.NotFound => 404,
        RpcErrorKinds.Concurrency => 409,
        RpcErrorKinds.Domain or RpcErrorKinds.Idempotency => 422,
        RpcErrorKinds.Unsupported => 400,
        _ => 500,
    };
}
