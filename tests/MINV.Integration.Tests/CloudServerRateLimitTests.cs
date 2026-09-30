using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Corporate;
using MINV.Application.Remote;
using MINV.CloudServer;

namespace MINV.Integration.Tests;

/// <summary>
/// Límites de tasa del servidor en la nube (regla B-10) sobre Kestrel REAL con la base en memoria (sin empresa: el limitador
/// corre ANTES de autenticar, así que las peticiones con credenciales inventadas bastan). La cubeta de comandos de cada ruta solo
/// mira la credencial con la que ESA ruta autentica (la web, la cookie; el escritorio, <c>Authorization</c>) y el tope por IP real
/// frena a quien cambia la credencial en cada petición para estrenar una cubeta.
/// </summary>
public sealed class CloudServerRateLimitTests
{
    private const string WebRpc = "/api/v1/web/rpc";
    private const string DesktopRpc = "/api/v1/rpc";

    /// <summary>Servidor propio de la prueba (su propio limitador), con la web encendida y detrás de un proxy de confianza (las
    /// pruebas llegan desde loopback: la IP real sale de <c>X-Forwarded-For</c>).</summary>
    private static async Task<(WebApplication App, Uri Address)> StartAsync(int requestsPerMinutePerIp)
    {
        var app = CloudServerApp.Build(
        [
            "--urls", "http://127.0.0.1:0", "--Minv:Storage", "memoria", "--Minv:Siat:Background", "false", "--Logging:LogLevel:Default", "Warning",
            "--Minv:Web:Enabled", "true", "--Minv:Web:TenantCode", "NUBE", "--Minv:ForwardedHeaders:Enabled", "true",
            "--" + CloudServerApp.RequestsPerMinutePerIpKey, requestsPerMinutePerIp.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ]);
        await app.StartAsync();
        return (app, new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()));
    }

    /// <summary>Un token con la forma de uno real (256 bits de azar), distinto en cada llamada.</summary>
    private static string Token() => "mses_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Una petición de comando desde la IP real <paramref name="ip"/> con la cookie y la cabecera que se indiquen.</summary>
    private static async Task<HttpStatusCode> SendAsync(Uri address, string path, string ip, string? cookie, string? authorization)
    {
        using var client = new WebTestClient(address) { Cookie = cookie };
        client.Headers["X-Forwarded-For"] = ip;
        if (authorization is not null)
        {
            client.Headers["Authorization"] = "Bearer " + authorization;
        }
        using var response = await client.RpcAsyncRaw(path);
        return response.StatusCode;
    }

    /// <summary>Envía hasta que el servidor responde 429 (o hasta <paramref name="max"/>) y devuelve cuántas pasaron antes.</summary>
    private static async Task<int?> AcceptedUntilLimitedAsync(int max, Func<Task<HttpStatusCode>> send)
    {
        for (var i = 0; i < max; i++)
        {
            if (await send() == HttpStatusCode.TooManyRequests)
            {
                return i;
            }
        }
        return null;
    }

    [Fact]
    public async Task Cambiar_la_cabecera_o_la_cookie_en_cada_peticion_no_escapa_del_tope_por_IP()
    {
        const int limit = 30;
        var (app, address) = await StartAsync(limit);
        await using (app)
        {
            // Escritorio: un Authorization nuevo en cada petición (cubetas de sesión siempre llenas) → el tope por IP corta igual
            Assert.Equal(limit, await AcceptedUntilLimitedAsync(limit + 5, () => SendAsync(address, DesktopRpc, "203.0.113.20", null, Token())));
            // Y esa IP ya no entra por ninguna ruta, tampoco por la web con otra cookie
            Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(address, WebRpc, "203.0.113.20", Token(), null));

            // Web: un visitante anónimo con una cookie inventada distinta en cada petición (y además un Authorization distinto)
            Assert.Equal(limit, await AcceptedUntilLimitedAsync(limit + 5, () => SendAsync(address, WebRpc, "203.0.113.21", Token(), Token())));

            // Otra IP real detrás del mismo proxy tiene su propio tope
            Assert.NotEqual(HttpStatusCode.TooManyRequests, await SendAsync(address, WebRpc, "203.0.113.22", Token(), null));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, await SendAsync(address, DesktopRpc, "203.0.113.22", null, Token()));
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task La_cubeta_de_cada_ruta_solo_mira_la_credencial_con_la_que_esa_ruta_autentica()
    {
        // Tope por IP muy alto: aquí se prueba solo la cubeta de comandos por sesión (ráfaga de 120, 10 por segundo)
        var (app, address) = await StartAsync(100_000);
        await using (app)
        {
            // Web: la misma cookie con un Authorization distinto en cada petición → la cabecera no estrena cubeta (la web no la usa)
            var cookie = Token();
            var web = await AcceptedUntilLimitedAsync(1000, () => SendAsync(address, WebRpc, "203.0.113.30", cookie, Token()));
            Assert.True(web is >= 120, $"La web debió cortar después de la ráfaga de 120 y cortó en {web?.ToString() ?? "ninguna"}.");
            // Otra cookie (otra sesión) desde la MISMA IP sigue teniendo su cubeta
            Assert.NotEqual(HttpStatusCode.TooManyRequests, await SendAsync(address, WebRpc, "203.0.113.30", Token(), null));

            // Escritorio: el mismo Authorization con una cookie distinta en cada petición → la cookie no estrena cubeta
            var bearer = Token();
            var desktop = await AcceptedUntilLimitedAsync(1000, () => SendAsync(address, DesktopRpc, "203.0.113.31", Token(), bearer));
            Assert.True(desktop is >= 120, $"El escritorio debió cortar después de la ráfaga de 120 y cortó en {desktop?.ToString() ?? "ninguna"}.");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, await SendAsync(address, DesktopRpc, "203.0.113.31", null, Token()));

            // Sin credencial, la cubeta es la de la IP: un anónimo no comparte la de una sesión ni la de otra IP
            var anonymous = await AcceptedUntilLimitedAsync(1000, () => SendAsync(address, WebRpc, "203.0.113.32", null, null));
            Assert.True(anonymous is >= 120, $"El anónimo debió cortar después de la ráfaga de 120 y cortó en {anonymous?.ToString() ?? "ninguna"}.");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, await SendAsync(address, WebRpc, "203.0.113.33", null, null));
            await app.StopAsync();
        }
    }
}

/// <summary>Atajos de <see cref="WebTestClient"/> para las pruebas de límites (el cuerpo es un caso de uso cualquiera).</summary>
internal static class WebTestClientLimits
{
    public static Task<HttpResponseMessage> RpcAsyncRaw(this WebTestClient client, string path) =>
        client.PostAsync(path, new RpcRequest(Guid.NewGuid(), RpcCatalog.NameOf(typeof(GetBranchesQuery)),
            System.Text.Json.JsonSerializer.SerializeToElement(new GetBranchesQuery(), RpcJson.Options)));
}
