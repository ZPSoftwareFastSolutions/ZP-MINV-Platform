using System.Collections.Concurrent;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Accounts;
using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.Application.Remote;
using MINV.Infrastructure.Hosting;
using MINV.Infrastructure.Integration;
using MINV.Infrastructure.Persistence;

namespace MINV.CloudServer;

/// <summary>
/// V7 · Sesión web del servidor en la nube (<c>/api/v1/web</c>, reglas P-02 a P-04): la web (tienda con cuentas de cliente
/// y panel del personal) es otro cliente del servidor. Usa la MISMA sesión que el escritorio (token <c>mses_…</c> de 256
/// bits, en la base solo su SHA-256, 12 h sin actividad), pero el token viaja SOLO en una cookie <c>HttpOnly</c>,
/// <c>SameSite=Strict</c>, <c>Path=/api/v1/web</c> y <c>Secure</c> bajo https: nunca en el cuerpo de una respuesta.
/// <list type="bullet">
/// <item><c>POST /session/login</c> <c>{ email, password }</c> → 200 <see cref="WebSession"/> + cookie · 401 con un mensaje
/// único · 429.</item>
/// <item><c>GET /session</c> → 200 <see cref="WebSession"/> · 401.</item>
/// <item><c>POST /session/logout</c> → 204 (cierra la sesión en el servidor y borra la cookie).</item>
/// <item><c>POST /account/register</c> <c>{ name, email, phone, password }</c> → 201 <see cref="WebSession"/> + cookie · 400 ·
/// 422 <c>account.email_taken</c> · 429. Crea SIEMPRE una cuenta con el rol CLIENTE.</item>
/// <item><c>POST /rpc</c> → mismo contrato que <c>/api/v1/rpc</c> (<see cref="RpcExecution"/>).</item>
/// </list>
/// La empresa sale de <c>Minv:Web:TenantCode</c>, nunca de la petición; una sesión de otra empresa no entra por estas rutas.
/// Toda ruta que no es GET pasa por <see cref="WebGuard"/> (anti-CSRF). El servidor no publica CORS.
/// </summary>
public static class WebEndpoints
{
    public const string Prefix = "/api/v1/web";

    /// <summary>Política de límite del registro de cuentas (por IP y por hora).</summary>
    public const string RegisterPolicy = "web-register";

    /// <summary>Mensaje ÚNICO de cualquier falla de inicio de sesión: no distingue un correo que no existe de una contraseña
    /// incorrecta, una cuenta bloqueada o un usuario inactivo (regla P-03). La cuenta bloqueada lleva ADEMÁS el código
    /// estable <see cref="AuthenticationCodes.Locked"/> (<c>auth.locked</c>).</summary>
    public const string LoginFailedMessage = "Correo o contraseña incorrectos.";

    public const string UnavailableMessage = "La sesión web no está configurada en este servidor.";

    /// <summary>Tope de lo que se acepta como contraseña al ingresar (no se calcula el hash de un texto mayor).</summary>
    private const int MaxLoginPasswordLength = 256;

    public static void Map(WebApplication app)
    {
        var web = app.MapGroup(Prefix).AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            // Las respuestas llevan datos de la persona: ningún intermediario ni el navegador las guarda
            http.Response.Headers.CacheControl = "no-store";
            if (!http.RequestServices.GetRequiredService<WebSettings>().IsConfigured)
            {
                return Results.Json(new RpcResponse(false, null, new RpcError(RpcErrorKinds.Server, UnavailableMessage)), RpcJson.Options,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            if (!HttpMethods.IsGet(http.Request.Method) && WebGuard.Check(http) is { } rejected)
            {
                return RpcExecution.Error(rejected);
            }
            return await next(context);
        });
        web.MapPost("/session/login", LoginAsync).RequireRateLimiting(RpcEndpoints.LoginPolicy);
        web.MapGet("/session", SessionAsync).RequireRateLimiting(RpcEndpoints.RpcPolicy);
        web.MapPost("/session/logout", LogoutAsync).RequireRateLimiting(RpcEndpoints.RpcPolicy);
        web.MapPost("/account/register", RegisterAsync).RequireRateLimiting(RegisterPolicy);
        web.MapPost("/rpc", ExecuteAsync).RequireRateLimiting(RpcEndpoints.RpcPolicy);
    }

    private static async Task<IResult> LoginAsync(HttpContext http, IServiceProvider sp, CancellationToken ct)
    {
        var settings = sp.GetRequiredService<WebSettings>();
        var body = await RpcExecution.ReadBodyAsync<WebLoginRequest>(http, ct);
        if (body is null)
        {
            return RpcExecution.Error(new RpcError(RpcErrorKinds.Validation, "Pedido de inicio de sesión inválido."));
        }
        if (body.Email is { Length: > 254 } || body.Password is { Length: > MaxLoginPasswordLength })
        {
            return LoginFailed(http, settings);
        }
        RpcExecution.BeforeSession(sp, RequestChannels.Web);
        try
        {
            // La empresa la pone el servidor (regla P-02): el cuerpo solo trae el correo y la contraseña
            var login = await sp.GetRequiredService<ISender>().Send(new LoginCommand(settings.Tenant, body.Email ?? string.Empty,
                body.Password ?? string.Empty, Machine(http), ClientVersion(http)), ct);
            return await OpenAsync(http, sp, settings, login, StatusCodes.Status200OK, ct);
        }
        catch (AuthenticationFailedException ex)
        {
            // El mensaje es SIEMPRE el mismo; la cuenta bloqueada lleva además su código estable (auth.locked) para que la
            // página avise que hay que esperar. Ningún otro código del caso de uso sale por aquí.
            return LoginFailed(http, settings, ex.Code == AuthenticationCodes.Locked ? AuthenticationCodes.Locked : null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure(sp, ex, "el inicio de sesión web");
        }
    }

    private static async Task<IResult> RegisterAsync(HttpContext http, IServiceProvider sp, CancellationToken ct)
    {
        var settings = sp.GetRequiredService<WebSettings>();
        // Cualquier otro campo del cuerpo (rol, sucursal, permisos…) se ignora: WebRegisterRequest no tiene dónde recibirlo
        var body = await RpcExecution.ReadBodyAsync<WebRegisterRequest>(http, ct);
        if (body is null)
        {
            return RpcExecution.Error(new RpcError(RpcErrorKinds.Validation, "Pedido de registro inválido."));
        }
        RpcExecution.BeforeSession(sp, RequestChannels.Web);
        try
        {
            var account = await sp.GetRequiredService<ISender>().Send(new RegisterCustomerAccountCommand(settings.Tenant, body.Name ?? string.Empty,
                body.Email ?? string.Empty, body.Phone ?? string.Empty, body.Password ?? string.Empty, settings.BranchCode, Machine(http),
                ClientVersion(http)), ct);
            return await OpenAsync(http, sp, settings, account.Login, StatusCodes.Status201Created, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure(sp, ex, "el registro de una cuenta");
        }
    }

    private static async Task<IResult> SessionAsync(HttpContext http, IServiceProvider sp, CancellationToken ct)
    {
        var settings = sp.GetRequiredService<WebSettings>();
        var principal = await AuthenticateAsync(http, sp, settings, ct);
        if (principal is null)
        {
            return SessionExpired(http, settings);
        }
        var db = sp.GetRequiredService<MinvWriteDbContext>();
        var mustChange = await db.UserCredentials.AsNoTracking().Where(c => c.UserId == principal.UserId).Select(c => c.MustChangePassword)
            .FirstOrDefaultAsync(ct);
        return Results.Json(new WebSession(principal.DisplayName, principal.Email, principal.Roles, principal.Permissions, mustChange, principal.Access,
            WebSessionKinds.Of(principal.Roles), principal.ExpiresAt, ServerHosting.Version, await CompanyAsync(db, principal.TenantId, ct)), RpcJson.Options);
    }

    private static async Task<IResult> LogoutAsync(HttpContext http, IServiceProvider sp, CancellationToken ct)
    {
        var settings = sp.GetRequiredService<WebSettings>();
        try
        {
            var principal = await AuthenticateAsync(http, sp, settings, ct);
            if (principal is not null)
            {
                // Cierra la sesión EN EL SERVIDOR: el token deja de valer aunque alguien conserve la cookie
                await sp.GetRequiredService<ISender>().Send(new LogoutCommand(principal.SessionId), ct);
            }
        }
        finally
        {
            DeleteCookie(http, settings);
        }
        return Results.NoContent();
    }

    private static async Task<IResult> ExecuteAsync(HttpContext http, IServiceProvider sp, CancellationToken ct)
    {
        var settings = sp.GetRequiredService<WebSettings>();
        var principal = await AuthenticateAsync(http, sp, settings, ct);
        return principal is null ? SessionExpired(http, settings) : await RpcExecution.ExecuteAsync(http, sp, principal, ct, ownSession: true);
    }

    // ------------------------------------------------------------------------------------------------ sesión y cookie
    /// <summary>La sesión de la cookie, con el canal <c>web</c>. Solo vale una sesión de la empresa de la web.</summary>
    private static async Task<CloudPrincipal?> AuthenticateAsync(HttpContext http, IServiceProvider sp, WebSettings settings, CancellationToken ct)
    {
        var token = http.Request.Cookies[settings.CookieName];
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }
        var principal = await sp.GetRequiredService<CloudSessionAuthenticator>().AuthenticateAsync(token, RequestChannels.Web, ct);
        if (principal is null)
        {
            return null;
        }
        var tenants = sp.GetRequiredService<WebTenants>();
        return await tenants.IsWebTenantAsync(sp.GetRequiredService<MinvWriteDbContext>(), principal.TenantId, settings.Tenant, ct) ? principal : null;
    }

    /// <summary>Emite el token de la sesión recién abierta, lo pone en la cookie y responde la sesión SIN el token.</summary>
    private static async Task<IResult> OpenAsync(HttpContext http, IServiceProvider sp, WebSettings settings, LoginResult login, int status,
        CancellationToken ct)
    {
        var db = sp.GetRequiredService<MinvWriteDbContext>();
        var clock = sp.GetRequiredService<IClock>();
        var token = await CloudSessions.IssueTokenAsync(db, login.SessionId, clock, ct);
        db.ChangeTracker.Clear();
        var email = sp.GetRequiredService<ICurrentUser>().Email ?? string.Empty;
        var session = new WebSession(login.DisplayName, email, login.Roles, login.Permissions, login.MustChangePassword, login.Access,
            WebSessionKinds.Of(login.Roles), clock.UtcNow + CloudSessionAuthenticator.SlidingExpiration, ServerHosting.Version,
            await CompanyAsync(db, login.TenantId, ct));
        http.Response.Cookies.Append(settings.CookieName, token, Cookie(http));
        return Results.Json(session, RpcJson.Options, statusCode: status);
    }

    /// <summary>Cookie de sesión del navegador (sin fecha: se va al cerrarlo; el vencimiento de 12 h lo lleva el servidor).
    /// <c>Secure</c> cuando la petición llegó por https (directo o, detrás del proxy, por <c>X-Forwarded-Proto</c>).</summary>
    private static CookieOptions Cookie(HttpContext http) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Path = Prefix,
        Secure = http.Request.IsHttps,
        IsEssential = true,
    };

    private static void DeleteCookie(HttpContext http, WebSettings settings) => http.Response.Cookies.Delete(settings.CookieName, Cookie(http));

    private static IResult LoginFailed(HttpContext http, WebSettings settings, string? code = null)
    {
        DeleteCookie(http, settings);
        return RpcExecution.Error(new RpcError(RpcErrorKinds.Authentication, LoginFailedMessage, null, code));
    }

    private static IResult SessionExpired(HttpContext http, WebSettings settings)
    {
        if (http.Request.Cookies.ContainsKey(settings.CookieName))
        {
            DeleteCookie(http, settings);   // la cookie ya no sirve: el navegador deja de enviarla
        }
        return RpcExecution.Error(RpcExecution.SessionExpired);
    }

    private static IResult Failure(IServiceProvider sp, Exception ex, string what)
    {
        var error = RpcCatalog.ToError(ex);
        if (error.Kind == RpcErrorKinds.Server)
        {
            sp.GetRequiredService<ILoggerFactory>().CreateLogger("MINV.CloudServer").LogError(ex, "Falla en {What}", what);
        }
        return RpcExecution.Error(error);
    }

    private static async Task<string> CompanyAsync(MinvWriteDbContext db, Guid tenantId, CancellationToken ct) =>
        await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.LegalName).FirstOrDefaultAsync(ct) ?? string.Empty;

    /// <summary>«Equipo» del registro de acceso: la web y la IP real del visitante (sale de <c>X-Forwarded-For</c> solo desde
    /// redes de confianza, ver <see cref="ProxySetup"/>).</summary>
    private static string Machine(HttpContext http) => RpcExecution.Limit("web " + (http.Connection.RemoteIpAddress?.ToString() ?? "?"), 100);

    private static string ClientVersion(HttpContext http) => RpcExecution.Limit(http.Request.Headers[RpcEndpoints.ClientVersionHeader].ToString(), 30);
}

/// <summary>
/// V7 · Anti-CSRF de la sesión web (regla P-02), además de <c>SameSite=Strict</c>: toda ruta <c>/api/v1/web/*</c> que no es
/// GET exige la cabecera propia <c>X-MINV-Client-Version</c> (un formulario de otro sitio no puede ponerla) con la misma
/// versión mayor del servidor; si el navegador envía <c>Sec-Fetch-Site</c>, debe ser <c>same-origin</c>; y si envía
/// <c>Origin</c>, su host debe ser el de la petición.
/// </summary>
internal static class WebGuard
{
    public const string SecFetchSite = "Sec-Fetch-Site";
    public const string SameOrigin = "same-origin";

    public const string MissingHeaderMessage = "Petición rechazada: falta la cabecera X-MINV-Client-Version.";
    public const string OtherSiteMessage = "Petición rechazada: llegó desde otro sitio.";

    public static RpcError? Check(HttpContext http)
    {
        var request = http.Request;
        if (request.Headers[RpcEndpoints.ClientVersionHeader].ToString().Length == 0)
        {
            return new RpcError(RpcErrorKinds.AccessDenied, MissingHeaderMessage);
        }
        if (RpcExecution.CheckClient(http, "la página") is { } unsupported)
        {
            return unsupported;
        }
        var site = request.Headers[SecFetchSite].ToString();
        if (site.Length > 0 && !string.Equals(site, SameOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return new RpcError(RpcErrorKinds.AccessDenied, OtherSiteMessage);
        }
        var origin = request.Headers.Origin.ToString();
        if (origin.Length > 0 && !IsSameHost(origin, request.Host))
        {
            return new RpcError(RpcErrorKinds.AccessDenied, OtherSiteMessage);
        }
        return null;
    }

    /// <summary>¿El <c>Origin</c> es del mismo host que la petición? Se compara el nombre del host (el proxy de la web
    /// reenvía <c>Host</c> sin el puerto); <c>null</c> y cualquier valor que no sea una URL http(s) se rechazan.</summary>
    internal static bool IsSameHost(string origin, HostString host) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && host.HasValue && string.Equals(uri.IdnHost, host.Host, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// V7 · Empresa de la web (regla P-02): recuerda el identificador de la empresa de <c>Minv:Web:TenantCode</c> para comprobar,
/// en cada petición con cookie, que la sesión es de ESA empresa (el token de una sesión de otra empresa no entra por la web).
/// </summary>
public sealed class WebTenants
{
    private readonly ConcurrentDictionary<string, Guid> _ids = new(StringComparer.Ordinal);

    public async Task<bool> IsWebTenantAsync(MinvWriteDbContext db, Guid tenantId, string tenantCode, CancellationToken ct)
    {
        if (_ids.TryGetValue(tenantCode, out var known))
        {
            return known == tenantId;
        }
        var id = await db.Tenants.AsNoTracking().Where(t => t.Code == tenantCode && t.IsActive).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        if (id is null)
        {
            return false;
        }
        _ids[tenantCode] = id.Value;
        return id.Value == tenantId;
    }
}
