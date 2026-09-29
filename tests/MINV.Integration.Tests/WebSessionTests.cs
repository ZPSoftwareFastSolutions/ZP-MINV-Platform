using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Accounting;
using MINV.Application.Accounts;
using MINV.Application.Billing;
using MINV.Application.Common;
using MINV.Application.Corporate;
using MINV.Application.Iam;
using MINV.Application.Inventory.Movements;
using MINV.Application.Inventory.Queries;
using MINV.Application.Partners;
using MINV.Application.Remote;
using MINV.Application.Storefront;
using MINV.Application.Tech;
using MINV.CloudServer;
using MINV.Domain.Iam;
using MINV.Domain.Sales;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Provisioning;
using MINV.Infrastructure.Seeding;

namespace MINV.Integration.Tests;

/// <summary>Servidor en la nube REAL con la sesión web encendida para la empresa NUBE (sucursal CM) y detrás de un proxy de
/// confianza (las pruebas llegan desde loopback: <c>X-Forwarded-For</c> y <c>X-Forwarded-Proto</c> se respetan).</summary>
public class WebServerFixture : SeededServer
{
    protected override string[] ExtraArgs =>
    [
        "--Minv:Web:Enabled", "true", "--Minv:Web:TenantCode", "nube", "--Minv:Web:BranchCode", "CM",
        "--Minv:Web:RegistrationsPerHour", RegistrationsPerHour, "--Minv:ForwardedHeaders:Enabled", "true",
    ];

    protected virtual string RegistrationsPerHour => "1000";

    protected override SeedOptions Options => new("NUBE", Days: 2, Seed: 11, Billing: false);

    protected override WebApplication Build(string[] args) => CloudServerApp.Build(args);
}

/// <summary>El mismo servidor con el límite de registros en 2 por hora (otra instancia, otro puerto).</summary>
public sealed class WebLimitsFixture : WebServerFixture
{
    protected override string RegistrationsPerHour => "2";
}

/// <summary>
/// Navegador mínimo de la web: una cookie (como la guardaría el navegador, sin que el «JavaScript» de la prueba la use para
/// nada más), la cabecera propia <c>X-MINV-Client-Version</c> y el sobre del RPC. No usa el contenedor de cookies de
/// <see cref="HttpClient"/>: así la prueba ve EXACTAMENTE la cabecera <c>Set-Cookie</c> que envió el servidor.
/// </summary>
internal sealed class WebTestClient : IDisposable
{
    public const string CookieName = "minv_session";
    private readonly HttpClient _http;

    public WebTestClient(Uri baseAddress) => _http = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = baseAddress };

    /// <summary>Valor de la cookie de sesión que envió el servidor (el token); null si no hay o si la borró.</summary>
    public string? Cookie { get; set; }

    /// <summary>Última cabecera <c>Set-Cookie</c> de la cookie de sesión, tal como llegó.</summary>
    public string? LastSetCookie { get; private set; }

    public bool SendVersion { get; set; } = true;

    public string Version { get; set; } = RpcTestClient.ClientVersion;

    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: RpcJson.Options);
        }
        if (SendVersion)
        {
            request.Headers.Add("X-MINV-Client-Version", Version);
        }
        foreach (var (name, value) in Headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
        if (Cookie is not null)
        {
            request.Headers.Add("Cookie", $"{CookieName}={Cookie}");
        }
        var response = await _http.SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.FirstOrDefault(c => c.StartsWith(CookieName + "=", StringComparison.Ordinal)) is { } set)
        {
            LastSetCookie = set;
            var value = set[(CookieName.Length + 1)..].Split(';')[0];
            Cookie = value.Length == 0 ? null : value;
        }
        return response;
    }

    public Task<HttpResponseMessage> PostAsync(string path, object? body = null) => SendAsync(HttpMethod.Post, path, body);

    public Task<HttpResponseMessage> GetAsync(string path) => SendAsync(HttpMethod.Get, path);

    public Task<HttpResponseMessage> LoginAsync(string email, string password) => PostAsync("/api/v1/web/session/login", new { email, password });

    public Task<HttpResponseMessage> RegisterAsync(string name, string email, string phone = "71234567", string password = WebSessionTests.Password) =>
        PostAsync("/api/v1/web/account/register", new { name, email, phone, password });

    public async Task<(HttpStatusCode Status, RpcResponse Response)> RpcAsync(object request, Guid? requestId = null, string path = "/api/v1/web/rpc")
    {
        var response = await PostAsync(path, new RpcRequest(requestId ?? Guid.NewGuid(), RpcCatalog.NameOf(request.GetType()),
            JsonSerializer.SerializeToElement(request, request.GetType(), RpcJson.Options)));
        return (response.StatusCode, (await response.Content.ReadFromJsonAsync<RpcResponse>(RpcJson.Options))!);
    }

    /// <summary>Un caso de uso con los datos que la prueba quiera (campos que el contrato no tiene): lo que enviaría una página
    /// manipulada.</summary>
    public async Task<(HttpStatusCode Status, RpcResponse Response)> PostRpcAsync(Type type, object payload, string path = "/api/v1/web/rpc")
    {
        var response = await PostAsync(path, new RpcRequest(Guid.NewGuid(), RpcCatalog.NameOf(type),
            JsonSerializer.SerializeToElement(payload, payload.GetType(), RpcJson.Options)));
        return (response.StatusCode, (await response.Content.ReadFromJsonAsync<RpcResponse>(RpcJson.Options))!);
    }

    public async Task<T> SendAsync<T>(IRequest<T> request)
    {
        var (_, response) = await RpcAsync(request);
        if (!response.Ok)
        {
            throw RpcCatalog.ToException(response.Error!);
        }
        return response.Result!.Value.Deserialize<T>(RpcJson.Options)!;
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Error del sobre <c>{ ok: false, error }</c>.</summary>
    public static async Task<RpcError> ErrorAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<RpcResponse>(RpcJson.Options))!.Error!;

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// V7 · Sesión web del servidor en la nube (<c>/api/v1/web</c>, reglas P-02 a P-04 y P-14) sobre Kestrel REAL con la base en
/// memoria: la cookie y sus atributos, el token que nunca sale en el cuerpo, el mensaje único, el anti-CSRF, el registro que
/// crea SIEMPRE un cliente, la lista de permitidos de una sesión de cliente en las DOS rutas de RPC, el aislamiento entre
/// clientes y el personal trabajando por la web con los mismos casos de uso del escritorio.
/// </summary>
public sealed class WebSessionTests(WebServerFixture server) : IClassFixture<WebServerFixture>
{
    public const string Password = "Cliente-2026";

    private WebTestClient Browser() => new(server.BaseAddress);

    private SeedUser User(string role, string? branches = null) =>
        server.Seed.Users.First(u => u.RoleCode == role && (branches is null || u.Branches == branches));

    private static string Email(string prefix) => $"{prefix}-{Guid.NewGuid():N}@correo.example";

    /// <summary>La hora del servidor (el reloj de la empresa de prueba no es el del equipo).</summary>
    private DateTimeOffset Now => server.Services.GetRequiredService<IClock>().UtcNow;

    private async Task<(WebTestClient Browser, string Email)> RegisteredAsync(string name, string phone = "71234567")
    {
        var browser = Browser();
        var email = Email("cliente");
        var response = await browser.RegisterAsync(name, email, phone);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (browser, email);
    }

    /// <summary>Consulta la base del servidor con la empresa de la prueba fijada (sin sesión de usuario).</summary>
    private async Task<T> InDatabaseAsync<T>(Func<MinvWriteDbContext, Task<T>> query)
    {
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var tenantId = await db.Tenants.AsNoTracking().Where(t => t.Code == "NUBE").Select(t => t.Id).SingleAsync();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId);
        return await query(db);
    }

    // ------------------------------------------------------------------------------------------------ inicio de sesión
    [Fact]
    public async Task Iniciar_sesion_pone_la_cookie_con_sus_atributos_y_el_cuerpo_no_trae_el_token()
    {
        using var browser = Browser();
        var manager = User(RoleCodes.Management);
        var response = await browser.LoginAsync(manager.Email.ToUpperInvariant(), manager.Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);

        // La cookie: HttpOnly, SameSite=Strict, Path=/api/v1/web; sin Secure porque la petición llegó por http; de sesión (sin fecha)
        var cookie = browser.LastSetCookie!;
        var attributes = cookie.Split(';').Skip(1).Select(a => a.Trim().ToLowerInvariant()).ToList();
        Assert.StartsWith("minv_session=mses_", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", attributes);
        Assert.Contains("samesite=strict", attributes);
        Assert.Contains("path=/api/v1/web", attributes);
        Assert.DoesNotContain("secure", attributes);
        Assert.DoesNotContain(attributes, a => a.StartsWith("expires=", StringComparison.Ordinal) || a.StartsWith("max-age=", StringComparison.Ordinal)
                                               || a.StartsWith("domain=", StringComparison.Ordinal));
        Assert.Single(response.Headers.GetValues("Set-Cookie"));

        // El cuerpo es la sesión, SIN el token (ni con ese nombre ni con ningún otro)
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("mses_", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(browser.Cookie!, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("token", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(manager.Password, raw, StringComparison.Ordinal);
        var session = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(new[] { "access", "company", "displayName", "email", "expiresAt", "kind", "mustChangePassword", "permissions", "roles", "serverVersion" },
            session.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal((manager.Email, manager.Name, "staff", false), (session.GetProperty("email").GetString(), session.GetProperty("displayName").GetString(),
            session.GetProperty("kind").GetString(), session.GetProperty("mustChangePassword").GetBoolean()));
        Assert.Equal([RoleCodes.Management], session.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(PermissionCodes.ForRole(RoleCodes.Management).Order(StringComparer.Ordinal),
            session.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).Order(StringComparer.Ordinal));
        Assert.True(session.GetProperty("access").GetProperty("allBranches").GetBoolean());
        Assert.Equal(["CB", "CM", "SC"], session.GetProperty("access").GetProperty("branches").EnumerateArray().Select(b => b.GetProperty("code").GetString()));
        Assert.Equal(server.Seed.CompanyName, session.GetProperty("company").GetString());
        Assert.Equal(RpcTestClient.ClientVersion, session.GetProperty("serverVersion").GetString());
        // Vence a las 12 h sin actividad, según el reloj del SERVIDOR (el de la empresa de prueba va adelantado o atrasado)
        Assert.InRange((session.GetProperty("expiresAt").GetDateTimeOffset() - Now).TotalHours, 11.9, 12.1);

        // En la base solo queda el SHA-256 del token, nunca el token
        var token = browser.Cookie!;
        Assert.Equal(1, await InDatabaseAsync(db => db.Sessions.AsNoTracking()
            .CountAsync(s => s.TokenHash == MINV.Application.Integration.ApiKeyTokens.Hash(token) && s.EndedAt == null)));
        Assert.Equal(0, await InDatabaseAsync(db => db.Sessions.AsNoTracking().CountAsync(s => s.TokenHash == token)));
    }

    [Fact]
    public async Task Bajo_https_la_cookie_es_Secure()
    {
        using var browser = Browser();
        // Detrás del proxy (red de confianza) la petición llegó por https: X-Forwarded-Proto
        browser.Headers["X-Forwarded-Proto"] = "https";
        var manager = User(RoleCodes.Management);
        Assert.Equal(HttpStatusCode.OK, (await browser.LoginAsync(manager.Email, manager.Password)).StatusCode);
        var attributes = browser.LastSetCookie!.Split(';').Skip(1).Select(a => a.Trim().ToLowerInvariant()).ToList();
        Assert.Contains("secure", attributes);
        Assert.Contains("httponly", attributes);
        Assert.Contains("samesite=strict", attributes);
        Assert.Contains("path=/api/v1/web", attributes);
        // Al cerrar la sesión, la cookie se borra con los mismos atributos
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("/api/v1/web/session/logout")).StatusCode);
        Assert.Null(browser.Cookie);
        Assert.Contains("secure", browser.LastSetCookie!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=Thu, 01 Jan 1970", browser.LastSetCookie!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Con_credenciales_incorrectas_no_se_pasa_mensaje_unico_y_sin_cookie()
    {
        var manager = User(RoleCodes.Management);
        var (locked, lockedEmail) = await RegisteredAsync("Cuenta Bloqueada");
        locked.Dispose();
        var attempts = new List<(string Email, string Password, string? Code)>
        {
            (manager.Email, "Clave-Equivocada-1", null),                 // contraseña incorrecta
            ("nadie-" + Guid.NewGuid().ToString("N") + "@correo.example", manager.Password, null),   // el correo no existe
            (manager.Email, new string('x', 300), null),                  // texto enorme: ni se calcula el hash
            (TenantProvisioner.StorefrontEmail("NUBE", server.Seed.Users[0].Email), "Clave-Tecnica-1", null),   // usuario técnico de la tienda
        };
        // Cinco intentos fallidos bloquean la cuenta: el quinto y el sexto (este con la contraseña CORRECTA) reciben el mismo
        // mensaje y, además, el código estable auth.locked
        attempts.AddRange(Enumerable.Repeat((lockedEmail, "Clave-Equivocada-1", (string?)null), UserCredential.MaxFailedAttempts - 1));
        attempts.Add((lockedEmail, "Clave-Equivocada-1", AuthenticationCodes.Locked));
        attempts.Add((lockedEmail, Password, AuthenticationCodes.Locked));
        Assert.Equal("auth.locked", AuthenticationCodes.Locked);
        foreach (var (email, password, code) in attempts)
        {
            using var browser = Browser();
            var response = await browser.LoginAsync(email, password);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var error = await WebTestClient.ErrorAsync(response);
            Assert.Equal((RpcErrorKinds.Authentication, WebEndpoints.LoginFailedMessage, code), (error.Kind, error.Message, error.Code));
            Assert.Null(error.Errors);
            Assert.Null(browser.Cookie);
            Assert.DoesNotContain("mses_", string.Join(';', response.Headers.TryGetValues("Set-Cookie", out var set) ? set : []), StringComparison.Ordinal);
            // Sin cookie no hay sesión ni RPC
            Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/web/session")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await browser.RpcAsync(new GetBranchesQuery())).Status);
        }
        // Por la ruta del escritorio la cuenta bloqueada conserva su mensaje de siempre y lleva el mismo código estable
        var desktop = new RpcTestClient(RpcTestClient.Configure(server.CreateClient()));
        var (lockedStatus, _, lockedError) = await desktop.LoginAsync("NUBE", new SeedUser(RoleCodes.Customer, "Cliente web", "Cuenta Bloqueada", lockedEmail, Password));
        Assert.Equal((HttpStatusCode.Unauthorized, RpcErrorKinds.Authentication, AuthenticationCodes.Locked),
            (lockedStatus, lockedError!.Error!.Kind, lockedError.Error.Code));
        Assert.Contains("bloqueada", lockedError.Error.Message, StringComparison.Ordinal);
        var (wrongStatus, _, wrongError) = await desktop.LoginAsync("NUBE", manager, "Clave-Equivocada-1");
        Assert.Equal((HttpStatusCode.Unauthorized, (string?)null), (wrongStatus, wrongError!.Error!.Code));
        // La empresa sale de la configuración: un «tenantCode» en el cuerpo no cambia nada
        using (var browser = Browser())
        {
            var response = await browser.PostAsync("/api/v1/web/session/login", new { email = manager.Email, password = manager.Password, tenantCode = "OTRA" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(server.Seed.CompanyName, (await WebTestClient.JsonAsync(response)).GetProperty("company").GetString());
        }
        // Datos que faltan: validación (400), sin cookie
        using (var browser = Browser())
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/api/v1/web/session/login", new { email = "", password = "" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/api/v1/web/session/login")).StatusCode);
            Assert.Null(browser.Cookie);
        }
        // Los intentos quedaron en el registro de acceso, con el equipo «web …» y sin la contraseña
        var logs = await InDatabaseAsync(db => db.AccessLogs.AsNoTracking().Where(a => a.AttemptedEmail == lockedEmail).ToListAsync());
        var fromWeb = logs.Where(a => a.MachineName != "PRUEBAS").ToList();
        Assert.Equal(UserCredential.MaxFailedAttempts + 1, fromWeb.Count(a => !a.Succeeded));
        Assert.All(fromWeb, a => Assert.StartsWith("web ", a.MachineName, StringComparison.Ordinal));
        Assert.Single(logs, a => a.MachineName == "PRUEBAS" && !a.Succeeded);   // el intento por la ruta del escritorio
        Assert.DoesNotContain(logs, a => (a.FailureReason ?? string.Empty).Contains(Password, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------------ anti-CSRF
    [Fact]
    public async Task Sin_la_cabecera_propia_o_desde_otro_sitio_la_peticion_se_rechaza()
    {
        var manager = User(RoleCodes.Management);
        using var session = Browser();
        Assert.Equal(HttpStatusCode.OK, (await session.LoginAsync(manager.Email, manager.Password)).StatusCode);
        var token = session.Cookie!;
        var host = server.BaseAddress.Host;
        var rejections = new (string Case, Action<WebTestClient> Setup, HttpStatusCode Status, string Kind, string Message)[]
        {
            ("sin la cabecera propia", b => b.SendVersion = false, HttpStatusCode.Forbidden, RpcErrorKinds.AccessDenied, "falta la cabecera"),
            ("otra versión mayor", b => b.Version = "3.1.0", HttpStatusCode.BadRequest, RpcErrorKinds.Unsupported, "actualice la página"),
            ("Sec-Fetch-Site: cross-site", b => b.Headers["Sec-Fetch-Site"] = "cross-site", HttpStatusCode.Forbidden, RpcErrorKinds.AccessDenied, "otro sitio"),
            ("Sec-Fetch-Site: same-site", b => b.Headers["Sec-Fetch-Site"] = "same-site", HttpStatusCode.Forbidden, RpcErrorKinds.AccessDenied, "otro sitio"),
            ("Sec-Fetch-Site: none", b => b.Headers["Sec-Fetch-Site"] = "none", HttpStatusCode.Forbidden, RpcErrorKinds.AccessDenied, "otro sitio"),
            ("Origin de otro host", b => b.Headers["Origin"] = "https://tienda-falsa.example", HttpStatusCode.Forbidden, RpcErrorKinds.AccessDenied, "otro sitio"),
            ("Origin null", b => b.Headers["Origin"] = "null", HttpStatusCode.Forbidden, RpcErrorKinds.AccessDenied, "otro sitio"),
            ("Origin que contiene el host", b => b.Headers["Origin"] = $"https://{host}.tienda-falsa.example", HttpStatusCode.Forbidden,
                RpcErrorKinds.AccessDenied, "otro sitio"),
        };
        foreach (var (name, setup, status, kind, message) in rejections)
        {
            // Las cuatro rutas que cambian algo, con una sesión válida en la cookie: ninguna llega a ejecutarse
            var posts = new (string Path, object? Body)[]
            {
                ("/api/v1/web/session/login", new { email = manager.Email, password = manager.Password }),
                ("/api/v1/web/account/register", new { name = "Ana Quispe", email = Email("csrf"), phone = "71234567", password = Password }),
                ("/api/v1/web/rpc", new RpcRequest(Guid.NewGuid(), RpcCatalog.NameOf(typeof(GetBranchesQuery)), JsonSerializer.SerializeToElement(new { }))),
                ("/api/v1/web/session/logout", null),
            };
            foreach (var (path, body) in posts)
            {
                using var browser = Browser();
                browser.Cookie = token;
                setup(browser);
                var response = await browser.PostAsync(path, body);
                Assert.True(status == response.StatusCode, $"{name} · {path}: {(int)response.StatusCode}");
                var error = await WebTestClient.ErrorAsync(response);
                Assert.Equal(kind, error.Kind);
                Assert.Contains(message, error.Message, StringComparison.Ordinal);
                Assert.Null(browser.LastSetCookie);   // ni cookie nueva ni cookie borrada
            }
        }
        // La sesión sigue abierta (el cierre de sesión rechazado no la cerró) y ninguna cuenta «csrf» se creó
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/v1/web/session")).StatusCode);
        Assert.Equal(0, await InDatabaseAsync(db => db.Users.AsNoTracking().CountAsync(u => u.Email.StartsWith("csrf-"))));

        // Lo que envía un navegador desde la misma página sí pasa: Sec-Fetch-Site: same-origin y Origin del mismo host
        using var same = Browser();
        same.Cookie = token;
        same.Headers["Sec-Fetch-Site"] = "same-origin";
        same.Headers["Origin"] = $"http://{host}:5173";
        Assert.Equal(HttpStatusCode.OK, (await same.RpcAsync(new GetBranchesQuery())).Status);
        // Una lectura (GET) no exige la cabecera propia
        using var reader = Browser();
        reader.Cookie = token;
        reader.SendVersion = false;
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/v1/web/session")).StatusCode);
        // El servidor en la nube no publica CORS: ni una petición previa ni una cabecera de permiso
        using var preflight = Browser();
        preflight.Headers["Origin"] = "https://tienda-falsa.example";
        preflight.Headers["Access-Control-Request-Method"] = "POST";
        var options = await preflight.SendAsync(HttpMethod.Options, "/api/v1/web/rpc");
        Assert.False(options.IsSuccessStatusCode);
        Assert.False(options.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False((await same.GetAsync("/api/v1/web/session")).Headers.Contains("Access-Control-Allow-Origin"));
    }

    // ------------------------------------------------------------------------------------------------ sesión y cierre
    [Fact]
    public async Task La_sesion_se_lee_con_la_cookie_y_cerrarla_la_invalida_en_el_servidor()
    {
        using var browser = Browser();
        // Sin cookie: 401 (no es una falla: nadie ingresó)
        var anonymous = await browser.GetAsync("/api/v1/web/session");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(RpcErrorKinds.Authentication, (await WebTestClient.ErrorAsync(anonymous)).Kind);
        Assert.Null(browser.LastSetCookie);

        var cashier = User(RoleCodes.Cashier, "CB");
        Assert.Equal(HttpStatusCode.OK, (await browser.LoginAsync(cashier.Email, cashier.Password)).StatusCode);
        var token = browser.Cookie!;
        var current = await browser.GetAsync("/api/v1/web/session");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        var raw = await current.Content.ReadAsStringAsync();
        Assert.DoesNotContain("mses_", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("token", raw, StringComparison.OrdinalIgnoreCase);
        var session = JsonDocument.Parse(raw).RootElement;
        Assert.Equal((cashier.Email, "staff", false), (session.GetProperty("email").GetString(), session.GetProperty("kind").GetString(),
            session.GetProperty("access").GetProperty("allBranches").GetBoolean()));
        Assert.Equal(["CB"], session.GetProperty("access").GetProperty("branches").EnumerateArray().Select(b => b.GetProperty("code").GetString()));
        Assert.Equal(PermissionCodes.ForRole(RoleCodes.Cashier).Order(StringComparer.Ordinal),
            session.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).Order(StringComparer.Ordinal));
        Assert.Null(browser.LastSetCookie is { } set && set.Contains("expires=", StringComparison.OrdinalIgnoreCase) ? set : null);

        // Una cookie inventada o manipulada no vale, y el servidor pide borrarla
        using (var forged = Browser())
        {
            forged.Cookie = "mses_" + new string('A', 43);
            Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync("/api/v1/web/session")).StatusCode);
            Assert.Null(forged.Cookie);
            Assert.Contains("expires=", forged.LastSetCookie!, StringComparison.OrdinalIgnoreCase);
        }

        // Cerrar sesión: 204, borra la cookie y cierra la sesión EN EL SERVIDOR (quien conserve el token ya no entra)
        var logout = await browser.PostAsync("/api/v1/web/session/logout");
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Null(browser.Cookie);
        Assert.StartsWith("minv_session=;", browser.LastSetCookie!, StringComparison.Ordinal);
        using var thief = Browser();
        thief.Cookie = token;
        Assert.Equal(HttpStatusCode.Unauthorized, (await thief.GetAsync("/api/v1/web/session")).StatusCode);
        thief.Cookie = token;
        Assert.Equal(HttpStatusCode.Unauthorized, (await thief.RpcAsync(new GetBranchesQuery())).Status);
        // …ni por la ruta del escritorio
        var desktop = RpcTestClient.Configure(server.CreateClient());
        desktop.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var rpc = await desktop.PostAsJsonAsync("/api/v1/rpc", new RpcRequest(Guid.NewGuid(), RpcCatalog.NameOf(typeof(GetBranchesQuery)),
            JsonSerializer.SerializeToElement(new { })), RpcJson.Options);
        Assert.Equal(HttpStatusCode.Unauthorized, rpc.StatusCode);
        Assert.Equal(1, await InDatabaseAsync(db => db.Sessions.AsNoTracking()
            .CountAsync(s => s.TokenHash == MINV.Application.Integration.ApiKeyTokens.Hash(token) && s.EndedAt != null)));
        // Cerrar otra vez (sin sesión) deja lo mismo: 204
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("/api/v1/web/session/logout")).StatusCode);
    }

    [Fact]
    public async Task La_sesion_de_otra_empresa_no_entra_por_la_web()
    {
        // Otra empresa en el MISMO servidor, con su administrador: ingresa por la ruta del escritorio (con su código de empresa)
        const string adminEmail = "admin@otra.example";
        using (var scope = server.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<TenantProvisioner>()
                .ProvisionAsync(new ProvisionTenantRequest("OTRA", "Otra Empresa S.R.L.", null, adminEmail, "Administrador", Password));
        }
        var desktop = new RpcTestClient(RpcTestClient.Configure(server.CreateClient()));
        var (status, login, _) = await desktop.LoginAsync("OTRA", new SeedUser(RoleCodes.Admin, "Administrador", "Administrador", adminEmail, Password));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotEmpty(await desktop.SendAsync(new GetBranchesQuery()));

        // Por la web, ese correo no existe (la empresa es la de la configuración) y su token no vale como cookie
        using var browser = Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.LoginAsync(adminEmail, Password)).StatusCode);
        browser.Cookie = login!.Token;
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/web/session")).StatusCode);
        browser.Cookie = login.Token;
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.RpcAsync(new GetBranchesQuery())).Status);
        // Un registro crea la cuenta en la empresa de la web, no en la otra
        var (customer, email) = await RegisteredAsync("Cliente De La Web");
        customer.Dispose();
        Assert.Equal(1, await InDatabaseAsync(db => db.Users.AsNoTracking().CountAsync(u => u.Email == email)));
        using var other = server.Services.CreateScope();
        var otherDb = other.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        other.ServiceProvider.GetRequiredService<ITenantContext>().Set(await otherDb.Tenants.Where(t => t.Code == "OTRA").Select(t => t.Id).SingleAsync());
        Assert.Equal(0, await otherDb.Users.AsNoTracking().CountAsync(u => u.Email == email));
        Assert.Equal(0, await otherDb.Set<CustomerAccount>().AsNoTracking().CountAsync());
    }

    // ------------------------------------------------------------------------------------------------ registro
    [Fact]
    public async Task Registrarse_crea_una_cuenta_de_cliente_aunque_el_cuerpo_traiga_campos_de_rol()
    {
        using var browser = Browser();
        var email = Email("Valentina.Aguirre");
        var response = await browser.PostAsync("/api/v1/web/account/register", new
        {
            name = "  Valentina Aguirre ", email = "  " + email.ToUpperInvariant() + " ", phone = "+591 7123-4567", password = Password,
            // Nada de esto existe en el contrato: el servidor lo ignora (regla P-03)
            role = RoleCodes.Admin, roleCode = RoleCodes.Admin, roles = new[] { RoleCodes.Admin, RoleCodes.Management },
            permissions = new[] { PermissionCodes.UsersManage, PermissionCodes.BranchesAll }, branchCode = "SC", branchCodes = new[] { "SC", "CB" },
            tenantCode = "OTRA", kind = "staff", isActive = true, mustChangePassword = false, customerCode = "CF", customerId = Guid.NewGuid(),
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.StartsWith("minv_session=mses_", browser.LastSetCookie!, StringComparison.Ordinal);
        Assert.Contains("httponly", browser.LastSetCookie!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", browser.LastSetCookie!, StringComparison.OrdinalIgnoreCase);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("mses_", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("token", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Password, raw, StringComparison.Ordinal);
        var session = JsonDocument.Parse(raw).RootElement;
        Assert.Equal((email.ToLowerInvariant(), "Valentina Aguirre", "customer", false), (session.GetProperty("email").GetString(),
            session.GetProperty("displayName").GetString(), session.GetProperty("kind").GetString(), session.GetProperty("mustChangePassword").GetBoolean()));
        Assert.Equal([RoleCodes.Customer], session.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal([PermissionCodes.AccountManage, PermissionCodes.AccountReserve],
            session.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).Order(StringComparer.Ordinal));
        var access = session.GetProperty("access");
        Assert.False(access.GetProperty("allBranches").GetBoolean());
        Assert.Equal("CM", Assert.Single(access.GetProperty("branches").EnumerateArray()).GetProperty("code").GetString());

        // En la base: usuario + credencial PBKDF2 + SOLO el rol CLIENTE + la sucursal de la tienda + cliente WEB-… + la cuenta
        var stored = await InDatabaseAsync(async db =>
        {
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email.ToLowerInvariant());
            var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == user.Id select r.Code).ToListAsync();
            var branches = await (from bu in db.BranchUsers join b in db.Branches on bu.BranchId equals b.Id where bu.UserId == user.Id select b.Code).ToListAsync();
            var credential = await db.UserCredentials.AsNoTracking().SingleAsync(c => c.UserId == user.Id);
            var account = await db.CustomerAccounts.AsNoTracking().SingleAsync(a => a.UserId == user.Id);
            var customer = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == account.CustomerId);
            var general = await db.Customers.AsNoTracking().Where(c => c.Code == "CF").Select(c => c.CustomerCategoryId).SingleAsync();
            var audits = await db.AuditLogs.AsNoTracking().Where(a => a.Action == "RegisterCustomerAccount").ToListAsync();
            var access = await db.AccessLogs.AsNoTracking().Where(a => a.AttemptedEmail == user.Email).ToListAsync();
            return (user, roles, branches, credential, customer, general, audits, access);
        });
        Assert.Equal(("Valentina Aguirre", true), (stored.user.DisplayName, stored.user.IsActive));
        Assert.Equal([RoleCodes.Customer], stored.roles);
        Assert.Equal(["CM"], stored.branches);
        Assert.Equal(("PBKDF2-SHA256", false), (stored.credential.Algorithm, stored.credential.MustChangePassword));
        Assert.DoesNotContain(Password, stored.credential.PasswordHash, StringComparison.Ordinal);
        Assert.Matches(@"^WEB-\d{6}$", stored.customer.Code);
        Assert.Equal(("Valentina Aguirre", email.ToLowerInvariant(), "+59171234567", stored.general, true, (int?)null),
            (stored.customer.Name, stored.customer.Email, stored.customer.Phone, stored.customer.CustomerCategoryId, stored.customer.IsActive,
                stored.customer.DocumentType));
        // Rastro: auditoría (canal web, a nombre del usuario nuevo) y registro de acceso, SIN la contraseña y con el teléfono enmascarado
        var audit = Assert.Single(stored.audits, a => a.UserId == stored.user.Id);
        Assert.Equal((AuditOutcome.Succeeded, RequestChannels.Web), (audit.Outcome, audit.Channel));
        Assert.DoesNotContain(Password, audit.Details!, StringComparison.Ordinal);
        Assert.DoesNotContain("7123-4567", audit.Details!, StringComparison.Ordinal);
        Assert.DoesNotContain("71234567", audit.Details!, StringComparison.Ordinal);
        Assert.Contains(stored.customer.Code, audit.Details!, StringComparison.Ordinal);
        Assert.True(Assert.Single(stored.access).Succeeded);

        // La cuenta sirve de inmediato (la cookie ya está puesta) y también al ingresar después con su correo y su contraseña
        var mine = await browser.SendAsync(new GetMyAccountQuery());
        Assert.Equal(("Valentina Aguirre", email.ToLowerInvariant(), "+59171234567", stored.customer.Code), (mine.Name, mine.Email, mine.Phone, mine.CustomerCode));
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("/api/v1/web/session/logout")).StatusCode);
        var again = await browser.LoginAsync(email, Password);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("customer", (await WebTestClient.JsonAsync(again)).GetProperty("kind").GetString());

        // Los códigos de cliente son correlativos
        var (second, secondEmail) = await RegisteredAsync("Mateo Condori", "76543210");
        var next = await second.SendAsync(new GetMyAccountQuery());
        second.Dispose();
        Assert.Equal(secondEmail, next.Email);
        Assert.Matches(@"^WEB-\d{6}$", next.CustomerCode);
        Assert.True(int.Parse(stored.customer.Code[4..], System.Globalization.CultureInfo.InvariantCulture) < int.Parse(next.CustomerCode[4..],
            System.Globalization.CultureInfo.InvariantCulture), $"{stored.customer.Code} → {next.CustomerCode}");
    }

    [Fact]
    public async Task Un_correo_repetido_o_una_contrasena_debil_no_crean_la_cuenta()
    {
        var (first, email) = await RegisteredAsync("Lucía Rojas");
        first.Dispose();
        var users = await InDatabaseAsync(db => db.Users.AsNoTracking().CountAsync());
        var accounts = await InDatabaseAsync(db => db.CustomerAccounts.AsNoTracking().CountAsync());

        // Correo repetido (también con otras mayúsculas o espacios): 422 account.email_taken, sin cookie
        foreach (var repeated in new[] { email, email.ToUpperInvariant(), "  " + email + " " })
        {
            using var browser = Browser();
            var response = await browser.RegisterAsync("Otra Persona", repeated, "76543210", "Otra-Clave-2026");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            var error = await WebTestClient.ErrorAsync(response);
            Assert.Equal((RpcErrorKinds.Domain, AccountRules.EmailTaken), (error.Kind, error.Code));
            Assert.Null(browser.Cookie);
        }
        // El correo de un usuario del personal tampoco se puede registrar (ni le cambia la contraseña)
        using (var browser = Browser())
        {
            var manager = User(RoleCodes.Management);
            var response = await browser.RegisterAsync("Gerente Falso", manager.Email, "76543210", "Otra-Clave-2026");
            Assert.Equal(AccountRules.EmailTaken, (await WebTestClient.ErrorAsync(response)).Code);
            Assert.Equal(HttpStatusCode.Unauthorized, (await browser.LoginAsync(manager.Email, "Otra-Clave-2026")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await browser.LoginAsync(manager.Email, manager.Password)).StatusCode);
        }

        // Contraseña débil y datos mal formados: validación (400) con su mensaje, sin cookie
        var invalid = new (object Body, string Message)[]
        {
            (new { name = "Ana Quispe", email = Email("a"), phone = "71234567", password = "Abc-123" }, AccountRules.PasswordLengthMessage),
            (new { name = "Ana Quispe", email = Email("a"), phone = "71234567", password = "solamenteletras" }, AccountRules.PasswordMixMessage),
            (new { name = "Ana Quispe", email = Email("a"), phone = "71234567", password = "1234567890" }, AccountRules.PasswordMixMessage),
            (new { name = "Ana Quispe", email = Email("a"), phone = "71234567", password = "A1" + new string('x', 127) }, AccountRules.PasswordLengthMessage),
            (new { name = "Ana Quispe", email = Email("a"), phone = "71234567" }, "Indique una contraseña."),
            (new { name = "Ana Quispe", email = "sin-arroba.example", phone = "71234567", password = Password }, AccountRules.EmailMessage),
            (new { name = "Ana Quispe", email = "ana@correo.example, otra@correo.example", phone = "71234567", password = Password }, AccountRules.EmailMessage),
            (new { name = "Ana Quispe", email = "Ana <ana@correo.example>", phone = "71234567", password = Password }, AccountRules.EmailMessage),
            (new { name = "A", email = Email("a"), phone = "71234567", password = Password }, "El nombre debe tener al menos 2 caracteres."),
            (new { name = "Ana\r\nBcc: otro@correo.example", email = Email("a"), phone = "71234567", password = Password },
                "El nombre " + ReservationRules.ControlCharacters),
            (new { email = Email("a"), phone = "71234567", password = Password }, "Indique su nombre."),
            (new { name = "Ana Quispe", email = Email("a"), password = Password }, "Indique un teléfono o WhatsApp."),
        };
        foreach (var (body, message) in invalid)
        {
            using var browser = Browser();
            var response = await browser.PostAsync("/api/v1/web/account/register", body);
            Assert.True(HttpStatusCode.BadRequest == response.StatusCode, $"{message}: {(int)response.StatusCode}");
            var error = await WebTestClient.ErrorAsync(response);
            Assert.Equal(RpcErrorKinds.Validation, error.Kind);
            Assert.Contains(message, error.Errors!);
            Assert.DoesNotContain(error.Errors!, e => e.Contains(Password, StringComparison.Ordinal));
            Assert.Null(browser.Cookie);
        }
        // Teléfono que no es boliviano: regla del dominio (422), la misma de las reservas
        using (var browser = Browser())
        {
            var response = await browser.RegisterAsync("Ana Quispe", Email("a"), "12345");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal("pcbuild.contact_phone", (await WebTestClient.ErrorAsync(response)).Code);
        }
        // Un cuerpo que no es JSON: 400
        using (var browser = Browser())
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/api/v1/web/account/register")).StatusCode);
        }
        // Nada de lo rechazado creó usuarios ni cuentas
        Assert.Equal(users, await InDatabaseAsync(db => db.Users.AsNoTracking().CountAsync()));
        Assert.Equal(accounts, await InDatabaseAsync(db => db.CustomerAccounts.AsNoTracking().CountAsync()));
        // El registro no existe en el RPC (como el inicio de sesión): ni siquiera para la administración
        using var admin = Browser();
        Assert.Equal(HttpStatusCode.OK, (await admin.LoginAsync(server.Seed.Users[0].Email, server.Seed.Users[0].Password)).StatusCode);
        var (status, rpc) = await admin.RpcAsync(new RegisterCustomerAccountCommand("NUBE", "Ana Quispe", Email("rpc"), "71234567", Password));
        Assert.Equal((HttpStatusCode.BadRequest, RpcErrorKinds.Unsupported), (status, rpc.Error!.Kind));
    }

    // ------------------------------------------------------------------------------------------------ lista de permitidos
    [Fact]
    public async Task Una_sesion_de_cliente_no_ejecuta_casos_de_uso_del_personal_por_ninguna_de_las_dos_rutas()
    {
        var (browser, email) = await RegisteredAsync("Diego Mamani");
        using var _ = browser;
        // El mismo cliente por la ruta del escritorio (con su token Bearer): la lista de permitidos es la misma
        var desktop = new RpcTestClient(RpcTestClient.Configure(server.CreateClient()));
        var (status, login, _) = await desktop.LoginAsync("NUBE", new SeedUser(RoleCodes.Customer, "Cliente web", "Diego Mamani", email, Password));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal([RoleCodes.Customer], login!.Login.Roles);

        var staff = new object[]
        {
            new GetUsersQuery(), new GetStockProjectionQuery(), new GetRolesQuery(), new GetCustomersQuery(), new GetBranchesQuery(),
            new GetCompanySettingsQuery(), new GetActivityQuery(), new GetPcBuildsQuery(), new GetStorefrontCatalogQuery(),
            new GetStorefrontReservationQuery("RES-WEB-000001", "71234567"),
            new SaveUserCommand(null, Email("intruso"), "Intruso", RoleCodes.Admin, true, Password),
            new SaveUserCommand(email, email, "Diego Mamani", RoleCodes.Admin, true, null),   // darse a sí mismo el rol de administrador
            new ResetUserPasswordCommand(User(RoleCodes.Management).Email, Password),
            new SaveCustomerCommand(null, "Cliente", null, null, null, "GENERAL", true),
            new ReserveCartCommand([new CartItemInput("CASE-COR-4000D")], "Diego Mamani", "71234567"),
            new ReleasePcBuildReservationCommand("RES-WEB-000001", "porque sí"),
            new CreateStorefrontReservationCommand([new StorefrontReservationLineInput("CASE-COR-4000D")], new StorefrontContactInput("Diego", "71234567"), null, "llave"),
            new CancelStorefrontReservationCommand("RES-WEB-000001", "71234567"),
            new ExpirePcBuildReservationsCommand(),
            // Casos de uso SIN permiso declarado (lo comprueban por dentro o solo piden una sesión): tampoco están en la lista
            new RegisterMovementCommand("CASE-COR-4000D", "ALM01-GENERAL", "SALIDA", 1),
            new SelectBranchCommand(login.Login.SessionId, null),
            new SelectBranchCommand(login.Login.SessionId, login.Login.Access.ActiveBranchId),   // ni siquiera a su propia sucursal
            new GetBillingAccessQuery(),
        };
        foreach (var request in staff)
        {
            Assert.False(RpcCatalog.IsAllowedForCustomer(request.GetType()), request.GetType().Name);
            var (webStatus, web) = await browser.RpcAsync(request);
            Assert.True(HttpStatusCode.Forbidden == webStatus, $"web · {request.GetType().Name}: {(int)webStatus}");
            Assert.Equal((RpcErrorKinds.AccessDenied, "Su cuenta de cliente no puede realizar esta operación."), (web.Error!.Kind, web.Error.Message));
            var (desktopStatus, cloud) = await desktop.SendRawAsync(request);
            Assert.True(HttpStatusCode.Forbidden == desktopStatus, $"escritorio · {request.GetType().Name}: {(int)desktopStatus}");
            Assert.Equal((RpcErrorKinds.AccessDenied, "Su cuenta de cliente no puede realizar esta operación."), (cloud.Error!.Kind, cloud.Error.Message));
        }
        // Nada cambió: sigue siendo cliente y no existe el intruso
        Assert.Equal([RoleCodes.Customer], await InDatabaseAsync(async db =>
        {
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
            return await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == user.Id select r.Code).ToListAsync();
        }));
        Assert.Equal(0, await InDatabaseAsync(db => db.Users.AsNoTracking().CountAsync(u => u.Email.StartsWith("intruso-"))));

        // Lo suyo sí, por las dos rutas: su cuenta y su contraseña
        Assert.Equal(email, (await browser.SendAsync(new GetMyAccountQuery())).Email);
        Assert.Equal(email, (await desktop.SendAsync(new GetMyAccountQuery())).Email);
        Assert.Empty(await desktop.SendAsync(new GetMyReservationsQuery()));
        Assert.True(await browser.SendAsync(new ChangePasswordCommand(Password, "Nueva-Clave-2027")));
        using var again = Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await again.LoginAsync(email, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await again.LoginAsync(email, "Nueva-Clave-2027")).StatusCode);
        // Cerrar SU sesión por RPC también está permitido
        Assert.True(await desktop.SendAsync(new LogoutCommand(login.Login.SessionId)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await desktop.SendRawAsync(new GetMyAccountQuery())).Status);
    }

    // ------------------------------------------------------------------------------------------------ mis reservas
    [Fact]
    public async Task Un_cliente_reserva_con_su_cuenta_y_no_ve_ni_cancela_la_reserva_de_otro()
    {
        var (ana, anaEmail) = await RegisteredAsync("Ana Quispe", "71234567");
        var (luis, _) = await RegisteredAsync("Luis Rojas", "76543210");
        using var a = ana;
        using var l = luis;
        using var admin = Browser();
        Assert.Equal(HttpStatusCode.OK, (await admin.LoginAsync(server.Seed.Users[0].Email, server.Seed.Users[0].Password)).StatusCode);
        var catalog = await admin.SendAsync(new GetStorefrontCatalogQuery());
        var game = catalog.Products.Where(p => p.Category == "JUE" && !p.Serialized && p.Available >= 6).OrderBy(p => p.Sku, StringComparer.Ordinal).First();
        var cabinet = catalog.Products.Where(p => p.Category == "CASE" && p.Available >= 2).OrderBy(p => p.Sku, StringComparer.Ordinal).First();

        // Ana reserva un carrito (2 días) y Luis, un armado: cada reserva queda ligada a SU cliente, con el contacto de SU cuenta
        var id = Guid.NewGuid();
        var command = new CreateMyReservationCommand([new StorefrontReservationLineInput(game.Sku, 2)], PcBuildKind.Cart, 2, "Paso el sábado");
        var (created, first) = await ana.RpcAsync(command, id);
        Assert.Equal(HttpStatusCode.OK, created);
        var mine = first.Result!.Value.Deserialize<StorefrontReservationView>(RpcJson.Options)!;
        Assert.StartsWith("RES-WEB-", mine.Number, StringComparison.Ordinal);
        Assert.Equal(("Reserved", "cart", "Ana Quispe", "CM", "Paso el sábado", 2 * game.Price), (mine.Status, mine.Kind, mine.ContactName, mine.Branch, mine.Notes, mine.Total));
        Assert.InRange((mine.ReservedUntil!.Value - Now).TotalHours, 47.9, 48.1);
        // Idempotente por el identificador del pedido: el reintento devuelve la misma reserva
        var (_, replayed) = await ana.RpcAsync(command, id);
        Assert.True(replayed.Replayed);
        Assert.Equal(mine.Number, replayed.Result!.Value.GetProperty("number").GetString());
        var other = await luis.SendAsync(new CreateMyReservationCommand([new StorefrontReservationLineInput(cabinet.Sku, 1)], PcBuildKind.Build, Name: "PC de Luis"));
        Assert.StartsWith("ARM-WEB-", other.Number, StringComparison.Ordinal);
        Assert.Equal(("Reserved", "build", "Luis Rojas", "case"), (other.Status, other.Kind, other.ContactName, Assert.Single(other.Lines).Slot));
        Assert.Equal(game.Reserved + 2, (await admin.SendAsync(new GetStorefrontProductQuery(game.Slug))).Reserved);

        var stored = await InDatabaseAsync(async db =>
        {
            var customer = await (from u in db.Users join x in db.CustomerAccounts on u.Id equals x.UserId join c in db.Customers on x.CustomerId equals c.Id
                                  where u.Email == anaEmail select c).AsNoTracking().SingleAsync();
            var build = await db.PcBuilds.IgnoreQueryFilters().AsNoTracking().SingleAsync(b => b.Number == mine.Number);
            return (customer, build);
        });
        Assert.Equal((stored.customer.Id, PcBuildChannel.Web, PcBuildKind.Cart, "71234567", anaEmail, false),
            (stored.build.CustomerId!.Value, stored.build.Channel, stored.build.Kind, stored.build.ContactPhone, stored.build.ContactEmail, stored.build.HasBuyer));

        // Cada uno ve SOLO lo suyo
        Assert.Equal([mine.Number], (await ana.SendAsync(new GetMyReservationsQuery())).Select(r => r.Number));
        Assert.Equal([other.Number], (await luis.SendAsync(new GetMyReservationsQuery())).Select(r => r.Number));
        // Luis no puede cancelar la reserva de Ana: para él «no existe» (igual que un número inventado)
        var (forbidden, denied) = await luis.RpcAsync(new CancelMyReservationCommand(mine.Number));
        Assert.Equal((HttpStatusCode.NotFound, RpcErrorKinds.NotFound), (forbidden, denied.Error!.Kind));
        var (missing, unknown) = await luis.RpcAsync(new CancelMyReservationCommand("RES-WEB-999999"));
        Assert.Equal(HttpStatusCode.NotFound, missing);
        Assert.Equal(denied.Error.Message.Replace(mine.Number, "X", StringComparison.Ordinal), unknown.Error!.Message.Replace("RES-WEB-999999", "X", StringComparison.Ordinal));
        Assert.Equal("Reserved", Assert.Single(await ana.SendAsync(new GetMyReservationsQuery())).Status);
        Assert.Equal(game.Reserved + 2, (await admin.SendAsync(new GetStorefrontProductQuery(game.Slug))).Reserved);
        // Los casos de uso de la cuenta no reciben ningún identificador de cliente: un «customerId» en el pedido se ignora
        var (_, spoofed) = await luis.PostRpcAsync(typeof(GetMyReservationsQuery), new { customerId = stored.customer.Id, customerCode = stored.customer.Code, userId = Guid.NewGuid() });
        Assert.Equal([other.Number], spoofed.Result!.Value.EnumerateArray().Select(r => r.GetProperty("number").GetString()));
        var (_, spoofedAccount) = await luis.PostRpcAsync(typeof(GetMyAccountQuery), new { customerId = stored.customer.Id, email = anaEmail });
        Assert.Equal("Luis Rojas", spoofedAccount.Result!.Value.GetProperty("name").GetString());

        // El personal ve la reserva de Ana con su cliente; Ana la cancela y el stock vuelve
        var row = (await admin.SendAsync(new GetPcBuildsQuery(Kind: PcBuildKind.Cart))).Single(r => r.Number == mine.Number);
        Assert.Equal(("Ana Quispe", "Ana Quispe", "71234567"), (row.Customer, row.ContactName, row.ContactPhone));
        var cancelled = await ana.SendAsync(new CancelMyReservationCommand(mine.Number.ToLowerInvariant()));
        Assert.Equal(("Cancelled", "Cancelada", CancelMyReservationHandler.Reason), (cancelled.Status, cancelled.StatusText, cancelled.CancelReason));
        Assert.Equal(game.Reserved, (await admin.SendAsync(new GetStorefrontProductQuery(game.Slug))).Reserved);
        var (twice, error) = await ana.RpcAsync(new CancelMyReservationCommand(mine.Number));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "pcbuild.state"), (twice, error.Error!.Code));
        Assert.Equal("Cancelled", Assert.Single(await ana.SendAsync(new GetMyReservationsQuery())).Status);

        // Sin stock suficiente no se reserva nada; datos inválidos, 400
        var (shortStatus, shortage) = await ana.RpcAsync(new CreateMyReservationCommand([new StorefrontReservationLineInput(cabinet.Sku, 16), new StorefrontReservationLineInput(game.Sku, 1)]));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, StorefrontStockException.ErrorCode), (shortStatus, shortage.Error!.Code));
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.RpcAsync(new CreateMyReservationCommand([]))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.RpcAsync(new CreateMyReservationCommand([new StorefrontReservationLineInput(game.Sku)], HoldDays: 4))).Status);
        Assert.Equal(game.Reserved, (await admin.SendAsync(new GetStorefrontProductQuery(game.Slug))).Reserved);

        // Sus datos: los actualiza ella; la auditoría queda con el canal web y sin el teléfono ni el documento completos
        var updated = await ana.SendAsync(new UpdateMyAccountCommand(" Ana Quispe Mamani ", "(2) 221-2345", 1, " 4567890 ", "1a"));
        Assert.Equal(("Ana Quispe Mamani", anaEmail, "22212345", 1, "4567890", "1A"),
            (updated.Name, updated.Email, updated.Phone, updated.DocumentType, updated.DocumentNumber, updated.Complement));
        Assert.Equal(updated, await ana.SendAsync(new GetMyAccountQuery()));
        Assert.Equal("Luis Rojas", (await luis.SendAsync(new GetMyAccountQuery())).Name);
        var session = await WebTestClient.JsonAsync(await ana.GetAsync("/api/v1/web/session"));
        Assert.Equal("Ana Quispe Mamani", session.GetProperty("displayName").GetString());
        Assert.Equal("buyer.doc_numeric", (await ana.RpcAsync(new UpdateMyAccountCommand("Ana Quispe", "71234567", 1, "45678-LP"))).Response.Error!.Code);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.RpcAsync(new UpdateMyAccountCommand("Ana Quispe", "71234567", 5, "1023456029", "1A"))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.RpcAsync(new UpdateMyAccountCommand("Ana Quispe", "71234567", null, "4567890"))).Status);
        var audits = await InDatabaseAsync(db => db.AuditLogs.AsNoTracking()
            .Where(x => x.Action == "UpdateMyAccount" || x.Action == "CreateMyReservation" || x.Action == "CancelMyReservation").ToListAsync());
        Assert.Contains(audits, x => x.Action == "UpdateMyAccount" && x.Outcome == AuditOutcome.Succeeded);
        Assert.Contains(audits, x => x.Action == "CreateMyReservation" && x.Outcome == AuditOutcome.Succeeded);
        Assert.Contains(audits, x => x.Action == "CancelMyReservation" && x.Outcome == AuditOutcome.Rejected);
        Assert.All(audits, x => Assert.Equal(RequestChannels.Web, x.Channel));
        Assert.DoesNotContain(audits, x => x.Action == "UpdateMyAccount"
                                           && (x.Details!.Contains("4567890", StringComparison.Ordinal) || x.Details.Contains("221-2345", StringComparison.Ordinal)
                                               || x.Details.Contains("22212345", StringComparison.Ordinal) || x.Details.Contains("1A", StringComparison.OrdinalIgnoreCase)));
    }

    // ------------------------------------------------------------------------------------------------ personal
    [Fact]
    public async Task El_personal_ejecuta_sus_casos_de_uso_por_la_web_con_su_cookie()
    {
        using var manager = Browser();
        var who = User(RoleCodes.Management);
        Assert.Equal(HttpStatusCode.OK, (await manager.LoginAsync(who.Email, who.Password)).StatusCode);
        // Consultas: las mismas del escritorio, con el alcance que calcula el servidor
        Assert.Equal(["CB", "CM", "SC"], (await manager.SendAsync(new GetBranchesQuery())).Where(b => b.IsVisible).Select(b => b.Code).Order(StringComparer.Ordinal));
        Assert.NotEmpty((await manager.SendAsync(new GetStockProjectionQuery())).Result.Stock);
        // Comando idempotente, en una transacción con su registro (mismo contrato que /api/v1/rpc)
        var id = Guid.NewGuid();
        var command = new CreateJournalEntryCommand(server.Seed.To, "Pago de servicios desde el panel web",
            [new JournalLineSpec("6.1.03", 75, 0), new JournalLineSpec(MINV.Domain.Accounting.AccountCodes.Bank, 0, 75)]);
        var (_, first) = await manager.RpcAsync(command, id);
        var (_, again) = await manager.RpcAsync(command, id);
        Assert.True(first.Ok && again.Ok && !first.Replayed && again.Replayed);
        Assert.Equal(first.Result!.Value.GetString(), again.Result!.Value.GetString());
        var (conflict, error) = await manager.RpcAsync(command with { Description = "Otro contenido con el mismo id" }, id);
        Assert.Equal((HttpStatusCode.UnprocessableEntity, RpcErrorKinds.Idempotency), (conflict, error.Error!.Kind));
        Assert.Single(await manager.SendAsync(new GetJournalQuery(server.Seed.To, server.Seed.To)), e => e.Description.Contains("panel web", StringComparison.Ordinal));
        var audit = await InDatabaseAsync(db => db.AuditLogs.AsNoTracking().Where(a => a.Action == "CreateJournalEntry" && a.Details!.Contains("panel web")).ToListAsync());
        Assert.Equal(RequestChannels.Web, Assert.Single(audit).Channel);
        // Los permisos los decide el servidor en cada petición: la gerencia no administra usuarios; la administración sí
        var (denied, rejected) = await manager.RpcAsync(new GetUsersQuery());
        Assert.Equal((HttpStatusCode.Forbidden, RpcErrorKinds.AccessDenied), (denied, rejected.Error!.Kind));
        Assert.Contains(PermissionCodes.UsersManage, rejected.Error.Message, StringComparison.Ordinal);
        using var admin = Browser();
        Assert.Equal(HttpStatusCode.OK, (await admin.LoginAsync(server.Seed.Users[0].Email, server.Seed.Users[0].Password)).StatusCode);
        var users = await admin.SendAsync(new GetUsersQuery());
        Assert.Contains(users, u => u.Email == who.Email);
        // Un usuario del personal con todos los permisos no tiene cuenta de cliente: los casos de uso de la cuenta lo dicen
        var (noAccount, missing) = await admin.RpcAsync(new GetMyAccountQuery());
        Assert.Equal((HttpStatusCode.UnprocessableEntity, AccountRules.Missing), (noAccount, missing.Error!.Code));
        Assert.Equal(AccountRules.Missing, (await admin.RpcAsync(new GetMyReservationsQuery())).Response.Error!.Code);
        // Un cajero de una sucursal ve solo la suya y cambia de sucursal solo dentro de su alcance
        using var cashier = Browser();
        var cb = User(RoleCodes.Cashier, "CB");
        Assert.Equal(HttpStatusCode.OK, (await cashier.LoginAsync(cb.Email, cb.Password)).StatusCode);
        Assert.Equal(["CB"], (await cashier.SendAsync(new GetBranchesQuery())).Where(b => b.IsVisible).Select(b => b.Code));
        await Assert.ThrowsAsync<AccessDeniedException>(() => cashier.SendAsync(new GetUsersQuery()));
        var branches = await manager.SendAsync(new GetBranchesQuery());
        var (outside, outsideError) = await cashier.RpcAsync(new SelectBranchCommand(Guid.NewGuid(), branches.Single(b => b.Code == "SC").Id));
        Assert.Equal((HttpStatusCode.Forbidden, RpcErrorKinds.AccessDenied), (outside, outsideError.Error!.Kind));
        // La página no conoce el identificador de su sesión: el servidor usa SIEMPRE la sesión de la cookie (el que venga se ignora)
        var chosen = await manager.SendAsync(new SelectBranchCommand(Guid.NewGuid(), branches.Single(b => b.Code == "CB").Id));
        Assert.Equal("CB", chosen.Active!.Code);
        var afterChange = await WebTestClient.JsonAsync(await manager.GetAsync("/api/v1/web/session"));
        Assert.Equal(chosen.ActiveBranchId, afterChange.GetProperty("access").GetProperty("activeBranchId").GetGuid());
        Assert.True(afterChange.GetProperty("access").GetProperty("allBranches").GetBoolean());
        // …y no puede tocar la sesión de otro usuario aunque conozca su identificador
        var cashierSession = await InDatabaseAsync(db => (from s in db.Sessions join u in db.Users on s.UserId equals u.Id
                                                          where u.Email == cb.Email && s.EndedAt == null select s.Id).FirstAsync());
        Assert.Null((await manager.SendAsync(new SelectBranchCommand(cashierSession, null))).ActiveBranchId);
        Assert.True(await manager.SendAsync(new LogoutCommand(cashierSession)));   // cierra la SUYA, no la del cajero
        Assert.Equal(HttpStatusCode.Unauthorized, (await manager.GetAsync("/api/v1/web/session")).StatusCode);
        var cashierNow = await WebTestClient.JsonAsync(await cashier.GetAsync("/api/v1/web/session"));
        Assert.Equal(cb.Email, cashierNow.GetProperty("email").GetString());
        Assert.Equal("CB", cashierNow.GetProperty("access").GetProperty("active").GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await manager.LoginAsync(who.Email, who.Password)).StatusCode);
        // Errores del contrato: operación desconocida, datos mal formados y validación
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync("/api/v1/web/rpc", new RpcRequest(Guid.NewGuid(), "MINV.Application.NoExiste", JsonSerializer.SerializeToElement(new { })))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync("/api/v1/web/rpc", new { requestId = Guid.NewGuid(), type = RpcCatalog.NameOf(typeof(LoginCommand)), payload = new { } })).StatusCode);
        // Las rutas del escritorio siguen con su contrato: token en el cuerpo y cabecera Bearer; la cookie no sirve ahí
        var desktop = new RpcTestClient(RpcTestClient.Configure(server.CreateClient()));
        var (status, login, _) = await desktop.LoginAsync("NUBE", who);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.StartsWith("mses_", login!.Token, StringComparison.Ordinal);
        Assert.NotEmpty(await desktop.SendAsync(new GetBranchesQuery()));
        using var cookieOnly = Browser();
        cookieOnly.Cookie = manager.Cookie;
        Assert.Equal(HttpStatusCode.Unauthorized, (await cookieOnly.RpcAsync(new GetBranchesQuery(), path: "/api/v1/rpc")).Status);
    }
}

/// <summary>Registros por hora y por IP real (la de <c>X-Forwarded-For</c> cuando la petición llega desde la red del proxy).</summary>
public sealed class WebLimitsTests(WebLimitsFixture server) : IClassFixture<WebLimitsFixture>
{
    [Fact]
    public async Task El_registro_tiene_su_limite_por_hora_y_por_IP_real()
    {
        async Task<HttpStatusCode> RegisterAsync(string? ip)
        {
            using var browser = new WebTestClient(server.BaseAddress);
            if (ip is not null)
            {
                browser.Headers["X-Forwarded-For"] = ip;
            }
            return (await browser.RegisterAsync("Ana Quispe", $"limite-{Guid.NewGuid():N}@correo.example")).StatusCode;
        }
        // 2 por hora desde la misma IP: el tercero se rechaza (cuenta cada intento, también los inválidos)
        Assert.Equal(HttpStatusCode.Created, await RegisterAsync("203.0.113.7"));
        using (var invalid = new WebTestClient(server.BaseAddress))
        {
            invalid.Headers["X-Forwarded-For"] = "203.0.113.7";
            Assert.Equal(HttpStatusCode.BadRequest, (await invalid.RegisterAsync("Ana Quispe", "limite@correo.example", password: "corta")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, await RegisterAsync("203.0.113.7"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await RegisterAsync("203.0.113.7"));
        // Otro visitante (otra IP real detrás del mismo proxy) tiene su propio límite
        Assert.Equal(HttpStatusCode.Created, await RegisterAsync("203.0.113.8"));
        Assert.Equal(HttpStatusCode.Created, await RegisterAsync("203.0.113.8"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await RegisterAsync("203.0.113.8"));
        // Sin la cabecera, la IP es la de la conexión (loopback): otro límite más
        Assert.Equal(HttpStatusCode.Created, await RegisterAsync(null));
        // El límite del registro no afecta al inicio de sesión ni al resto del servidor
        using var other = new WebTestClient(server.BaseAddress);
        other.Headers["X-Forwarded-For"] = "203.0.113.7";
        var manager = server.Seed.Users.First(u => u.RoleCode == RoleCodes.Management);
        Assert.Equal(HttpStatusCode.OK, (await other.LoginAsync(manager.Email, manager.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await server.CreateClient().GetAsync("/api/v1/health")).StatusCode);
        // El registro de acceso guardó la IP real del visitante
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(await db.Tenants.Where(t => t.Code == "NUBE").Select(t => t.Id).SingleAsync());
        Assert.Contains(await db.AccessLogs.AsNoTracking().Select(a => a.MachineName).ToListAsync(), m => m == "web 203.0.113.8");
    }
}

/// <summary>Servidores sin empresa de prueba (no hace falta para comprobar qué rutas existen).</summary>
public sealed class WebSwitchTests
{
    private static readonly (HttpMethod Method, string Path)[] Routes =
    [
        (HttpMethod.Post, "/api/v1/web/session/login"), (HttpMethod.Get, "/api/v1/web/session"), (HttpMethod.Post, "/api/v1/web/session/logout"),
        (HttpMethod.Post, "/api/v1/web/account/register"), (HttpMethod.Post, "/api/v1/web/rpc"),
    ];

    private static async Task<IReadOnlyList<(string Path, HttpStatusCode Status, string Body)>> ProbeAsync(params string[] extra)
    {
        string[] args = ["--urls", "http://127.0.0.1:0", "--Minv:Storage", "memoria", "--Minv:Siat:Background", "false", "--Logging:LogLevel:Default", "Warning", .. extra];
        await using var app = CloudServerApp.Build(args);
        await app.StartAsync();
        var address = new Uri(app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.First());
        using var browser = new WebTestClient(address);
        var result = new List<(string, HttpStatusCode, string)>();
        foreach (var (method, path) in Routes)
        {
            var response = await browser.SendAsync(method, path, method == HttpMethod.Get ? null : new { email = "ana@correo.example", password = "Cliente-2026" });
            result.Add((path, response.StatusCode, await response.Content.ReadAsStringAsync()));
        }
        // El escritorio no depende de la sesión web
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/health")).StatusCode);
        await app.StopAsync();
        return result;
    }

    [Fact]
    public async Task Con_la_sesion_web_apagada_las_rutas_responden_404()
    {
        Assert.All(await ProbeAsync(), r => Assert.True(HttpStatusCode.NotFound == r.Status, $"{r.Path}: {(int)r.Status}"));
        Assert.All(await ProbeAsync("--Minv:Web:Enabled", "false", "--Minv:Web:TenantCode", "NUBE"),
            r => Assert.True(HttpStatusCode.NotFound == r.Status, $"{r.Path}: {(int)r.Status}"));
    }

    [Fact]
    public async Task Encendida_sin_empresa_responde_503_y_un_nombre_de_cookie_invalido_no_arranca()
    {
        var probes = await ProbeAsync("--Minv:Web:Enabled", "true");
        Assert.All(probes, r => Assert.True(HttpStatusCode.ServiceUnavailable == r.Status, $"{r.Path}: {(int)r.Status}"));
        Assert.All(probes, r =>
        {
            var error = JsonSerializer.Deserialize<RpcResponse>(r.Body, RpcJson.Options)!.Error!;
            Assert.Equal((RpcErrorKinds.Server, WebEndpoints.UnavailableMessage), (error.Kind, error.Message));
        });
        var error = Assert.Throws<InvalidOperationException>(() => CloudServerApp.Build(
            ["--Minv:Storage", "memoria", "--Minv:Web:Enabled", "true", "--Minv:Web:TenantCode", "NUBE", "--Minv:Web:CookieName", "sesion; Path=/"]));
        Assert.Contains("CookieName", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void La_configuracion_de_la_sesion_web_viene_apagada_y_se_normaliza()
    {
        var settings = new WebSettings { Enabled = true, TenantCode = " techzone ", RegistrationsPerHour = 0 }.Validated();
        Assert.Equal((true, "TECHZONE", WebSettings.DefaultCookieName, 1), (settings.IsConfigured, settings.Tenant, settings.CookieName, settings.EffectiveRegistrationsPerHour));
        Assert.False(new WebSettings().Enabled);
        Assert.False(new WebSettings { Enabled = true, TenantCode = " " }.IsConfigured);
        Assert.Equal(5, new WebSettings().RegistrationsPerHour);
    }
}
