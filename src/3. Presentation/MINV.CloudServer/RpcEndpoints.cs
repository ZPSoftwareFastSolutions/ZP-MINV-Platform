using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
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
/// V4 · Puntos de entrada del servidor en la nube:
/// <list type="bullet">
/// <item><c>POST /api/v1/session/login</c>: inicia sesión con la tubería (bloqueo por intentos, registro de acceso) y
/// devuelve un token de sesión (en la base solo queda su hash; vence a las 12 h sin actividad).</item>
/// <item><c>POST /api/v1/rpc</c>: ejecuta un comando o consulta de MINV.Application. Los permisos y el alcance por
/// sucursal los recalcula el servidor en CADA petición a partir del token (el escritorio no decide qué ve). Los comandos
/// corren en una transacción junto con su registro de idempotencia.</item>
/// <item><c>POST /api/v1/session/logout</c> y <c>GET /api/v1/health</c>.</item>
/// </list>
/// </summary>
public static class RpcEndpoints
{
    public const string ClientVersionHeader = "X-MINV-Client-Version";

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/v1/health", () => Results.Json(new { status = "ok", product = "M-INV", version = ServerHosting.Version }, RpcJson.Options));
        app.MapPost("/api/v1/session/login", LoginAsync).RequireRateLimiting("login");
        app.MapPost("/api/v1/session/logout", LogoutAsync).RequireRateLimiting("rpc");
        app.MapPost("/api/v1/rpc", ExecuteAsync).RequireRateLimiting("rpc");
    }

    private static IResult Error(RpcError error) => Results.Json(new RpcResponse(false, null, error), RpcJson.Options,
        statusCode: RpcCatalog.HttpStatusOf(error.Kind));

    /// <summary>El escritorio debe ser de la misma versión mayor que el servidor (el contrato de los comandos cambia).</summary>
    private static RpcError? CheckClient(HttpContext http)
    {
        var version = http.Request.Headers[ClientVersionHeader].ToString();
        return int.TryParse(version.Split('.')[0], out var major) && major == ServerHosting.Major
            ? null
            : new RpcError(RpcErrorKinds.Unsupported,
                $"Este servidor es M-INV {ServerHosting.Version}: actualice el escritorio (versión recibida: {(version.Length == 0 ? "ninguna" : version)}).");
    }

    private static async Task<IResult> LoginAsync(HttpContext http, IServiceProvider sp, CancellationToken ct)
    {
        if (CheckClient(http) is { } unsupported)
        {
            return Error(unsupported);
        }
        CloudLoginRequest? body;
        try
        {
            body = await http.Request.ReadFromJsonAsync<CloudLoginRequest>(RpcJson.Options, ct);
        }
        catch (JsonException)
        {
            body = null;
        }
        if (body is null)
        {
            return Error(new RpcError(RpcErrorKinds.Validation, "Pedido de inicio de sesión inválido."));
        }
        var tenant = sp.GetRequiredService<ITenantContext>();
        tenant.SetBranches(new BranchScope(false, [Guid.Empty], null));   // nada visible hasta autenticar
        sp.GetRequiredService<IRequestOrigin>().Set(RequestChannels.Cloud, null);
        try
        {
            var login = await sp.GetRequiredService<ISender>().Send(new LoginCommand(body.TenantCode, body.Email, body.Password,
                Limit(body.MachineName, 100), Limit(body.ClientVersion, 30)), ct);
            var db = sp.GetRequiredService<MinvWriteDbContext>();
            var clock = sp.GetRequiredService<IClock>();
            var token = await CloudSessions.IssueTokenAsync(db, login.SessionId, clock, ct);
            return Results.Json(new CloudLoginResponse(token, clock.UtcNow + CloudSessionAuthenticator.SlidingExpiration, login, ServerHosting.Version),
                RpcJson.Options);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error(RpcCatalog.ToError(ex));
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
        if (CheckClient(http) is { } unsupported)
        {
            return Error(unsupported);
        }
        var principal = await sp.GetRequiredService<CloudSessionAuthenticator>().AuthenticateAsync(Bearer(http), ct);
        if (principal is null)
        {
            return Error(new RpcError(RpcErrorKinds.Authentication, "La sesión venció o se cerró: vuelva a iniciar sesión."));
        }
        RpcRequest? envelope;
        try
        {
            envelope = await http.Request.ReadFromJsonAsync<RpcRequest>(RpcJson.Options, ct);
        }
        catch (JsonException)
        {
            envelope = null;
        }
        if (envelope is null || envelope.RequestId == Guid.Empty || !RpcCatalog.TryResolve(envelope.Type, out var requestType, out var responseType))
        {
            return Error(new RpcError(RpcErrorKinds.Unsupported, "Operación desconocida o pedido mal formado."));
        }
        object request;
        try
        {
            request = envelope.Payload.Deserialize(requestType, RpcJson.Options)
                      ?? throw new JsonException("vacío");
        }
        catch (JsonException)
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
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("MINV.CloudServer").LogError(ex, "Falla al ejecutar {Type}", envelope.Type);
            }
            return Error(RpcCatalog.ToError(ex));
        }
    }

    /// <summary>
    /// Comando idempotente: si el id ya se procesó con el mismo contenido, devuelve la respuesta guardada (la red se cortó
    /// durante el COMMIT y el escritorio reintentó); con otro contenido, lo rechaza. Si no, lo ejecuta en UNA transacción
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

    private static string? Bearer(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
    }

    private static string Limit(string? text, int max) => string.IsNullOrWhiteSpace(text) ? "?" : text.Length > max ? text[..max] : text;
}
