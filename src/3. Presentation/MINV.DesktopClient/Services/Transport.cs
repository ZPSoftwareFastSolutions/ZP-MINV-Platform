using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Remote;

namespace MINV.DesktopClient.Services;

/// <summary>
/// V4 · Cómo llegan los casos de uso a la base: en conexión directa (base local o demostración) se ejecutan en este
/// equipo con MediatR; en modo nube viajan al servidor M-INV por HTTPS. Las pantallas no notan la diferencia: envían
/// los mismos comandos y reciben las mismas excepciones.
/// </summary>
public interface IRequestTransport
{
    Task<T> SendAsync<T>(IRequest<T> request, CancellationToken ct = default);
}

/// <summary>Conexión directa: cada envío es una unidad de trabajo con el rastreo limpio.</summary>
public sealed class LocalTransport(IMediator mediator, IMinvDbContext db) : IRequestTransport
{
    public Task<T> SendAsync<T>(IRequest<T> request, CancellationToken ct = default)
    {
        db.ClearTracking();
        return mediator.Send(request, ct);
    }
}

/// <summary>Modo nube: el comando viaja al servidor (el escritorio no tiene credenciales de la base de datos).</summary>
public sealed class CloudTransport(CloudConnection connection) : IRequestTransport
{
    public Task<T> SendAsync<T>(IRequest<T> request, CancellationToken ct = default) => connection.SendAsync(request, ct);
}

/// <summary>Estado del servidor en la nube (pantalla de inicio de sesión).</summary>
public sealed record ServerStatus(bool IsReady, string Server, string Version, string Message);

/// <summary>
/// V4 · Conexión con el servidor M-INV en la nube: HTTPS obligatorio (se valida el certificado y el nombre del servidor;
/// http solo para localhost), token de sesión en memoria (nunca en disco), versión del cliente en cada pedido y
/// reintentos seguros: un comando que no recibió respuesta por un corte de red se reenvía con el MISMO identificador y el
/// servidor devuelve la respuesta ya registrada (no se vende ni se mueve stock dos veces).
/// </summary>
public sealed class CloudConnection : IDisposable
{
    private const int MaxAttempts = 3;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private string? _token;

    public CloudConnection()
    {
        _http.DefaultRequestHeaders.Add("X-MINV-Client-Version", App.Version);
    }

    public Uri? Server => _http.BaseAddress;

    /// <summary>La sesión venció o la cerraron en el servidor: la ventana principal vuelve al inicio de sesión.</summary>
    public event EventHandler? SessionExpired;

    public void Configure(Uri server) => _http.BaseAddress = server;

    /// <summary>Normaliza y valida la dirección: https (o http solo en este equipo), sin credenciales ni ruta.</summary>
    public static Uri ParseServer(string? text)
    {
        var value = (text ?? string.Empty).Trim().TrimEnd('/');
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
        {
            throw new RequestValidationException(["Escriba la dirección del servidor (p. ej. https://minv.suempresa.com)."]);
        }
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        {
            throw new RequestValidationException(["El servidor en la nube debe usar https (http solo se admite en este mismo equipo)."]);
        }
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new RequestValidationException(["La dirección no debe incluir usuario ni contraseña."]);
        }
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    public static async Task<ServerStatus> ProbeAsync(string? server, CancellationToken ct = default)
    {
        Uri uri;
        try
        {
            uri = ParseServer(server);
        }
        catch (RequestValidationException ex)
        {
            return new ServerStatus(false, server ?? "", "", ex.Errors[0]);
        }
        using var http = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(6) };
        try
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync("api/v1/health", ct));
            var version = doc.RootElement.GetProperty("version").GetString() ?? "?";
            var compatible = version.Split('.')[0] == App.Version.Split('.')[0];
            return new ServerStatus(compatible, uri.Authority, version,
                compatible ? "Servidor M-INV disponible" : $"El servidor es M-INV {version} y este escritorio es {App.Version}: actualice el escritorio.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return new ServerStatus(false, uri.Authority, "", "No responde: " + (ex is TaskCanceledException ? "tiempo de espera agotado" : ex.GetBaseException().Message));
        }
    }

    public async Task<CloudLoginResponse> LoginAsync(string tenantCode, string email, string password, CancellationToken ct = default)
    {
        using var response = await PostAsync("api/v1/session/login",
            JsonContent.Create(new CloudLoginRequest(tenantCode, email, password, Environment.MachineName, App.Version), options: RpcJson.Options), ct);
        if (!response.IsSuccessStatusCode)
        {
            throw await ErrorAsync(response, ct);
        }
        var login = await response.Content.ReadFromJsonAsync<CloudLoginResponse>(RpcJson.Options, ct)
                    ?? throw new InvalidOperationException("Respuesta vacía del servidor.");
        _token = login.Token;
        return login;
    }

    public async Task<T> SendAsync<T>(IRequest<T> request, CancellationToken ct = default)
    {
        var envelope = new RpcRequest(Guid.NewGuid(), RpcCatalog.NameOf(request.GetType()),
            JsonSerializer.SerializeToElement(request, request.GetType(), RpcJson.Options));
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/rpc") { Content = JsonContent.Create(envelope, options: RpcJson.Options) };
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                using var response = await _http.SendAsync(message, ct);
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
                    continue;
                }
                var body = await response.Content.ReadFromJsonAsync<RpcResponse>(RpcJson.Options, ct)
                           ?? throw new InvalidOperationException($"Respuesta vacía del servidor (HTTP {(int)response.StatusCode}).");
                if (!body.Ok)
                {
                    var error = RpcCatalog.ToException(body.Error!);
                    if (error is AuthenticationFailedException && response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        SessionExpired?.Invoke(this, EventArgs.Empty);
                    }
                    throw error;
                }
                return body.Result is { } result ? result.Deserialize<T>(RpcJson.Options)! : default!;
            }
            catch (Exception ex) when (attempt < MaxAttempts && !ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
            {
                // Corte de red o tiempo agotado: se reintenta con el MISMO id (el servidor no lo ejecuta dos veces)
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                throw new ConcurrencyConflictException("No hay conexión con el servidor M-INV. Revise internet e intente de nuevo: " +
                                                       "si la operación alcanzó a registrarse, el reintento no la duplica.", ex);
            }
        }
    }

    public async Task LogoutAsync()
    {
        if (_token is null)
        {
            return;
        }
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/session/logout");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            using var _ = await _http.SendAsync(message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            System.Diagnostics.Trace.TraceWarning("M-INV · no se pudo cerrar la sesión en el servidor: {0}", ex.Message);
        }
        _token = null;
    }

    private async Task<HttpResponseMessage> PostAsync(string path, HttpContent content, CancellationToken ct)
    {
        try
        {
            return await _http.PostAsync(path, content, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new AuthenticationFailedException($"No se pudo conectar con el servidor {Server?.Authority}: " +
                                                    (ex is TaskCanceledException ? "tiempo de espera agotado." : ex.GetBaseException().Message));
        }
    }

    private static async Task<Exception> ErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<RpcResponse>(RpcJson.Options, ct);
            if (body?.Error is { } error)
            {
                return error.Kind == RpcErrorKinds.Unsupported ? new AuthenticationFailedException(error.Message) : RpcCatalog.ToException(error);
            }
        }
        catch (JsonException)
        {
        }
        return new AuthenticationFailedException(response.StatusCode == HttpStatusCode.TooManyRequests
            ? "Demasiados intentos de ingreso desde este equipo: espere un minuto."
            : $"El servidor respondió {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    public void Dispose() => _http.Dispose();
}
