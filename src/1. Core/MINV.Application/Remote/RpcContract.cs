using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using MINV.Application.Common;
using MINV.Domain.Common;
using MINV.Domain.Iam;

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

/// <summary>V7 · Inicio de sesión de la web (<c>POST /api/v1/web/session/login</c>). La empresa NO viaja: sale de la
/// configuración del servidor (regla P-02).</summary>
public sealed record WebLoginRequest(string? Email, string? Password)
{
    public override string ToString() => $"WebLoginRequest {{ Email = {Email} }}";
}

/// <summary>V7 · Registro de una cuenta de cliente (<c>POST /api/v1/web/account/register</c>). No lleva rol, sucursal ni
/// permisos: el servidor crea SIEMPRE una cuenta con el rol CLIENTE (regla P-03) e ignora cualquier otro campo.</summary>
public sealed record WebRegisterRequest(string? Name, string? Email, string? Phone, string? Password)
{
    public override string ToString() => $"WebRegisterRequest {{ Email = {Email} }}";
}

/// <summary>
/// V7 · Sesión web tal como la ve la página (regla P-02). NUNCA lleva el token: viaja solo en la cookie <c>HttpOnly</c>.
/// <paramref name="Kind"/>: <c>staff</c> (personal: entra al panel) o <c>customer</c> (su único rol es CLIENTE: entra a su
/// cuenta). Los permisos son informativos (ocultar un botón es comodidad): el servidor los recalcula en cada petición.
/// </summary>
public sealed record WebSession(string DisplayName, string Email, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions,
    bool MustChangePassword, Iam.BranchAccess Access, string Kind, DateTimeOffset ExpiresAt, string ServerVersion, string Company);

/// <summary>V7 · Tipo de una sesión web.</summary>
public static class WebSessionKinds
{
    public const string Staff = "staff";
    public const string Customer = "customer";

    /// <summary><c>customer</c> cuando el ÚNICO rol del usuario es CLIENTE; en cualquier otro caso, <c>staff</c>.</summary>
    public static string Of(IReadOnlyCollection<string> roles) => RoleCodes.IsCustomerOnly(roles) ? Customer : Staff;
}

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
/// inicio de sesión y, V7, el registro de una cuenta de cliente (son previos a la sesión y tienen su propio punto de
/// entrada). Nada fuera de ese ensamblado se puede instanciar desde la red.
/// </summary>
public static class RpcCatalog
{
    /// <summary>Casos de uso previos a la sesión: nunca viajan por RPC.</summary>
    private static readonly IReadOnlySet<Type> PreSession = new HashSet<Type> { typeof(Iam.LoginCommand), typeof(Accounts.RegisterCustomerAccountCommand) };

    /// <summary>V7 · Casos de uso de la propia sesión que también puede ejecutar un cliente (no exigen permiso).</summary>
    private static readonly IReadOnlySet<Type> OwnSession = new HashSet<Type> { typeof(Iam.ChangePasswordCommand), typeof(Iam.LogoutCommand) };

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

    /// <summary>Permisos que exige el caso de uso (<see cref="RequiresPermissionAttribute"/>), en el orden declarado.</summary>
    public static IReadOnlyList<string> PermissionsOf(Type requestType) =>
        requestType.GetCustomAttributes<RequiresPermissionAttribute>().Select(p => p.Permission).ToList();

    /// <summary>Módulos comerciales que exige el caso de uso (<see cref="RequiresModuleAttribute"/>).</summary>
    public static IReadOnlyList<string> ModulesOf(Type requestType) =>
        requestType.GetCustomAttributes<RequiresModuleAttribute>().Select(m => m.ModuleCode).ToList();

    /// <summary>
    /// V7 · ¿Una sesión de CLIENTE puede ejecutar este caso de uso? (regla P-04). Solo los que exigen permisos y TODOS
    /// empiezan con <c>account.</c>, más los de su propia sesión (<c>ChangePasswordCommand</c> y <c>LogoutCommand</c>). Un
    /// caso de uso sin permisos declarados (los comprueba por dentro) NO está permitido. Es defensa en profundidad: la
    /// tubería de permisos sigue aplicando. Lo comprueban las DOS rutas de RPC del servidor en la nube.
    /// </summary>
    public static bool IsAllowedForCustomer(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        if (OwnSession.Contains(requestType))
        {
            return true;
        }
        var permissions = PermissionsOf(requestType);
        return permissions.Count > 0 && permissions.All(p => p.StartsWith(PermissionCodes.AccountPrefix, StringComparison.Ordinal));
    }

    private static IReadOnlyDictionary<string, (Type, Type)> Build() =>
        typeof(RpcCatalog).Assembly.GetTypes()
            .Where(t => t is { IsPublic: true, IsAbstract: false, IsGenericTypeDefinition: false } && !PreSession.Contains(t))
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
