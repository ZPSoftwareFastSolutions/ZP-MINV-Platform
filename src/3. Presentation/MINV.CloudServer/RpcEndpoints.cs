using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Iam;
using MINV.Application.Remote;
using MINV.Domain.Iam;
using MINV.Infrastructure.Hosting;
using MINV.Infrastructure.Integration;
using MINV.Infrastructure.Persistence;

namespace MINV.CloudServer;

/// <summary>Marca del ensamblado del servidor (pruebas con WebApplicationFactory).</summary>
public sealed class CloudServerMarker;

/// <summary>
/// V4 · Puntos de entrada del ESCRITORIO en el servidor en la nube (token Bearer):
/// <list type="bullet">
/// <item><c>POST /api/v1/session/login</c>: inicia sesión con la tubería (bloqueo por intentos, registro de acceso) y
/// devuelve un token de sesión (en la base solo queda su hash; vence a las 12 h sin actividad).</item>
/// <item><c>POST /api/v1/rpc</c>: ejecuta un comando o consulta de MINV.Application. Los permisos y el alcance por
/// sucursal los recalcula el servidor en CADA petición a partir del token (el escritorio no decide qué ve). Los comandos
/// corren en una transacción junto con su registro de idempotencia.</item>
/// <item><c>POST /api/v1/session/logout</c> y <c>GET /api/v1/health</c>.</item>
/// </list>
/// V7 · La web entra por <see cref="WebEndpoints"/> (<c>/api/v1/web</c>, cookie); lo común vive en <see cref="RpcExecution"/>.
/// </summary>
public static class RpcEndpoints
{
    public const string ClientVersionHeader = "X-MINV-Client-Version";

    /// <summary>Política de límite del inicio de sesión (por IP).</summary>
    public const string LoginPolicy = "login";

    /// <summary>Política de límite de los comandos (por sesión o por IP).</summary>
    public const string RpcPolicy = "rpc";

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/v1/health", () => Results.Json(new { status = "ok", product = "M-INV", version = ServerHosting.Version }, RpcJson.Options));
        app.MapPost("/api/v1/session/login", LoginAsync).RequireRateLimiting(LoginPolicy);
        app.MapPost("/api/v1/session/logout", LogoutAsync).RequireRateLimiting(RpcPolicy);
        app.MapPost("/api/v1/rpc", ExecuteAsync).RequireRateLimiting(RpcPolicy);
    }

    private static async Task<IResult> LoginAsync(HttpContext http, IServiceProvider sp, CancellationToken ct)
    {
        if (RpcExecution.CheckClient(http, "el escritorio") is { } unsupported)
        {
            return RpcExecution.Error(unsupported);
        }
        var body = await RpcExecution.ReadBodyAsync<CloudLoginRequest>(http, ct);
        if (body is null)
        {
            return RpcExecution.Error(new RpcError(RpcErrorKinds.Validation, "Pedido de inicio de sesión inválido."));
        }
        RpcExecution.BeforeSession(sp, RequestChannels.Cloud);
        try
        {
            var login = await sp.GetRequiredService<ISender>().Send(new LoginCommand(body.TenantCode, body.Email, body.Password,
                RpcExecution.Limit(body.MachineName, 100), RpcExecution.Limit(body.ClientVersion, 30)), ct);
            var db = sp.GetRequiredService<MinvWriteDbContext>();
            var clock = sp.GetRequiredService<IClock>();
            var token = await CloudSessions.IssueTokenAsync(db, login.SessionId, clock, ct);
            return Results.Json(new CloudLoginResponse(token, clock.UtcNow + CloudSessionAuthenticator.SlidingExpiration, login, ServerHosting.Version),
                RpcJson.Options);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return RpcExecution.Error(RpcCatalog.ToError(ex));
        }
    }

    private static async Task<IResult> LogoutAsync(HttpContext http, IServiceProvider sp, CancellationToken ct)
    {
        var principal = await sp.GetRequiredService<CloudSessionAuthenticator>().AuthenticateAsync(Bearer(http), ct);
        if (principal is null)
        {
            return Results.NoContent();
        }
        await sp.GetRequiredService<ISender>().Send(new LogoutCommand(principal.SessionId), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ExecuteAsync(HttpContext http, IServiceProvider sp, CancellationToken ct)
    {
        if (RpcExecution.CheckClient(http, "el escritorio") is { } unsupported)
        {
            return RpcExecution.Error(unsupported);
        }
        var principal = await sp.GetRequiredService<CloudSessionAuthenticator>().AuthenticateAsync(Bearer(http), ct);
        return principal is null ? RpcExecution.Error(RpcExecution.SessionExpired) : await RpcExecution.ExecuteAsync(http, sp, principal, ct);
    }

    internal static string? Bearer(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
    }
}

/// <summary>
/// Lo común de las DOS rutas de RPC (<c>/api/v1/rpc</c> del escritorio y, V7, <c>/api/v1/web/rpc</c> de la web): mismo
/// contrato, misma versión mayor del cliente, mismo mapeo de errores, misma idempotencia de los comandos y la MISMA lista de
/// permitidos de una sesión de cliente (regla P-04). Lo único que cambia entre las dos es de dónde sale el token.
/// </summary>
internal static class RpcExecution
{
    public static readonly RpcError SessionExpired = new(RpcErrorKinds.Authentication, "La sesión venció o se cerró: vuelva a iniciar sesión.");

    public const string CustomerDeniedMessage = "Su cuenta de cliente no puede realizar esta operación.";

    public static IResult Error(RpcError error) => Results.Json(new RpcResponse(false, null, error), RpcJson.Options,
        statusCode: RpcCatalog.HttpStatusOf(error.Kind));

    /// <summary>El cliente (escritorio o página web) debe ser de la misma versión mayor que el servidor (el contrato de los
    /// comandos cambia).</summary>
    public static RpcError? CheckClient(HttpContext http, string client)
    {
        var version = http.Request.Headers[RpcEndpoints.ClientVersionHeader].ToString();
        return int.TryParse(version.Split('.')[0], out var major) && major == ServerHosting.Major
            ? null
            : new RpcError(RpcErrorKinds.Unsupported,
                $"Este servidor es M-INV {ServerHosting.Version}: actualice {client} (versión recibida: {(version.Length == 0 ? "ninguna" : Limit(version, 30))}).");
    }

    /// <summary>Cuerpo JSON de la petición; null si falta o está mal formado.</summary>
    public static async Task<T?> ReadBodyAsync<T>(HttpContext http, CancellationToken ct) where T : class
    {
        try
        {
            return await http.Request.ReadFromJsonAsync<T>(RpcJson.Options, ct);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or BadHttpRequestException)
        {
            return null;   // sin cuerpo, con otro tipo de contenido o mal formado
        }
    }

    /// <summary>Antes de autenticar (inicio de sesión o registro): nada de sucursal visible y el canal de la petición.</summary>
    public static void BeforeSession(IServiceProvider sp, string channel)
    {
        sp.GetRequiredService<ITenantContext>().SetBranches(new BranchScope(false, [Guid.Empty], null));   // nada visible hasta autenticar
        sp.GetRequiredService<IRequestOrigin>().Set(channel, null);
    }

    /// <summary>Ejecuta el comando o la consulta del sobre con la sesión ya autenticada.</summary>
    public static async Task<IResult> ExecuteAsync(HttpContext http, IServiceProvider sp, CloudPrincipal principal, CancellationToken ct)
    {
        var envelope = await ReadBodyAsync<RpcRequest>(http, ct);
        if (envelope is null || envelope.RequestId == Guid.Empty || envelope.Type is null
            || !RpcCatalog.TryResolve(envelope.Type, out var requestType, out var responseType))
        {
            return Error(new RpcError(RpcErrorKinds.Unsupported, "Operación desconocida o pedido mal formado."));
        }
        // V7 · Una sesión de cliente solo ejecuta los casos de uso de su cuenta y los de su propia sesión (regla P-04). Se
        // comprueba antes de leer los datos del pedido y antes de la tubería, que además vuelve a comprobar los permisos.
        if (principal.IsCustomer && !RpcCatalog.IsAllowedForCustomer(requestType))
        {
            Logger(sp).LogWarning("Sesión de cliente rechazada al pedir {Type}", envelope.Type);
            return Error(new RpcError(RpcErrorKinds.AccessDenied, CustomerDeniedMessage));
        }
        object request;
        try
        {
            request = envelope.Payload.Deserialize(requestType, RpcJson.Options)
                      ?? throw new JsonException("vacío");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Error(new RpcError(RpcErrorKinds.Validation, "Los datos del pedido no tienen el formato esperado."));
        }
        var sender = sp.GetRequiredService<ISender>();
        try
        {
            if (!RpcCatalog.IsCommand(requestType))
            {
                var answer = await sender.Send(request, ct);
                return Results.Json(new RpcResponse(true, JsonSerializer.SerializeToElement(answer, responseType, RpcJson.Options), null), RpcJson.Options);
            }
            return await ExecuteCommandAsync(sp, principal, envelope, request, responseType, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (RpcCatalog.ToError(ex).Kind == RpcErrorKinds.Server)
            {
                Logger(sp).LogError(ex, "Falla al ejecutar {Type}", envelope.Type);
            }
            return Error(RpcCatalog.ToError(ex));
        }
    }

    /// <summary>
    /// Comando idempotente: si el id ya se procesó con el mismo contenido, devuelve la respuesta guardada (la red se cortó
    /// durante el COMMIT y el cliente reintentó); con otro contenido, lo rechaza. Si no, lo ejecuta en UNA transacción
    /// con su registro en <c>iam.processed_requests</c>: o quedan ambos o ninguno.
    /// </summary>
    private static async Task<IResult> ExecuteCommandAsync(IServiceProvider sp, CloudPrincipal principal, RpcRequest envelope, object request,
        Type responseType, CancellationToken ct)
    {
        var db = sp.GetRequiredService<MinvWriteDbContext>();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.Type + "\n" + envelope.Payload.GetRawText()))).ToLowerInvariant();
        var previous = await db.ProcessedRequests.AsNoTracking().FirstOrDefaultAsync(x => x.RequestId == envelope.RequestId, ct);
        if (previous is not null)
        {
            if (previous.RequestHash != hash || previous.UserId != principal.UserId)
            {
                return Error(new RpcError(RpcErrorKinds.Idempotency, "Ese identificador de pedido ya se usó con otro contenido."));
            }
            using var saved = JsonDocument.Parse(previous.Response);
            return Results.Json(new RpcResponse(true, saved.RootElement.Clone(), null, Replayed: true), RpcJson.Options);
        }
        var clock = sp.GetRequiredService<IClock>();
        await using var transaction = await ((IMinvDbContext)db).BeginTransactionAsync(ct);
        var answer = await sp.GetRequiredService<ISender>().Send(request, ct);
        var json = JsonSerializer.SerializeToElement(answer, responseType, RpcJson.Options);
        db.ChangeTracker.Clear();
        db.ProcessedRequests.Add(new ProcessedRequest(principal.TenantId, envelope.RequestId, principal.UserId, envelope.Type, hash, json.GetRawText(),
            clock.UtcNow));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Json(new RpcResponse(true, json, null), RpcJson.Options);
    }

    public static string Limit(string? text, int max) => string.IsNullOrWhiteSpace(text) ? "?" : text.Length > max ? text[..max] : text;

    private static ILogger Logger(IServiceProvider sp) => sp.GetRequiredService<ILoggerFactory>().CreateLogger("MINV.CloudServer");
}
