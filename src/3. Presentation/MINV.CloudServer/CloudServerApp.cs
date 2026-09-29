using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using MINV.Application;
using MINV.Application.Storefront;
using MINV.Infrastructure.Billing;
using MINV.Infrastructure.Hosting;

namespace MINV.CloudServer;

/// <summary>
/// V4 · Armado del servidor en la nube. Variables: <c>MINV_DB</c> (PostgreSQL con el rol minv_server y
/// <c>SSL Mode=VerifyFull</c>), <c>MINV_INTEGRATION_KEYS</c> (claves maestras) y <c>ASPNETCORE_URLS</c> (https con
/// certificado, o http detrás de un proxy TLS). <c>--Minv:Storage=memoria</c> para pruebas y demostraciones.
/// V7 · Sesión web por cookie (<c>Minv:Web</c>, <see cref="WebEndpoints"/>) e IP real detrás del proxy de la web
/// (<c>Minv:ForwardedHeaders</c>, <see cref="ProxySetup"/>).
/// </summary>
public static class CloudServerApp
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddMinvApplication();
        var storage = builder.Services.AddMinvServerStorage(builder.Configuration["Minv:Storage"],
            builder.Configuration[Infrastructure.DependencyInjection.ConnectionStringVariable]
            ?? Environment.GetEnvironmentVariable(Infrastructure.DependencyInjection.ConnectionStringVariable));
        builder.Services.AddSingleton(new StorageInfo(storage));
        // V4.1 · El servidor en la nube es el único que habla con el SIN en modo nube: envía los documentos pendientes y
        // mantiene CUIS, CUFD, reloj y catálogos de cada empresa con facturación activa (Minv:Siat:Background=false lo apaga).
        if (builder.Configuration.GetValue("Minv:Siat:Background", true))
        {
            builder.Services.AddMinvSiatBackground();
        }
        // V7 · Sesión web: configuración, empresa de la web y la MISMA vigencia de las reservas que la tienda pública
        // (Minv:Storefront:ReservationHours y MaxReservationHours), para las reservas que se hacen por este servidor
        var web = (builder.Configuration.GetSection(WebSettings.Section).Get<WebSettings>() ?? new WebSettings()).Validated();
        builder.Services.AddSingleton(web);
        builder.Services.AddSingleton<WebTenants>();
        builder.Services.AddSingleton(new StorefrontOptions(
            builder.Configuration.GetValue("Minv:Storefront:ReservationHours", StorefrontOptions.DefaultReservationHours),
            builder.Configuration.GetValue("Minv:Storefront:MaxReservationHours", StorefrontOptions.DefaultMaxReservationHours)));
        var trustProxy = ProxySetup.Configure(builder.Services, builder.Configuration);
        var logins = Math.Max(1, builder.Configuration.GetValue("Minv:LoginsPerMinute", 10));
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Inicio de sesión (escritorio y web): 10 intentos por minuto por IP (además del bloqueo de la cuenta por intentos fallidos)
            options.AddPolicy(RpcEndpoints.LoginPolicy, http => RateLimitPartition.GetFixedWindowLimiter(Ip(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = logins, Window = TimeSpan.FromMinutes(1) }));
            // Comandos: ráfaga de 120 y 10 por segundo por sesión (el token Bearer o la cookie de la web) o por IP si no la trae
            options.AddPolicy(RpcEndpoints.RpcPolicy, http => RateLimitPartition.GetTokenBucketLimiter(SessionPartition(http, web.CookieName),
                _ => new TokenBucketRateLimiterOptions { TokenLimit = 120, TokensPerPeriod = 10, ReplenishmentPeriod = TimeSpan.FromSeconds(1) }));
            // V7 · Registro de cuentas de cliente: 5 por hora por IP (cuenta cada intento, también los rechazados)
            options.AddPolicy(WebEndpoints.RegisterPolicy, http => RateLimitPartition.GetFixedWindowLimiter("registro:" + Ip(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = web.EffectiveRegistrationsPerHour, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
        });
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 4 * 1024 * 1024);   // imágenes de producto ≤ 1 MB en base64
        var app = builder.Build();
        if (trustProxy)
        {
            // Primero de todo: los límites por IP y el registro de acceso ven la IP real del visitante
            app.UseForwardedHeaders();
        }
        app.UseRateLimiter();
        RpcEndpoints.Map(app);
        if (web.Enabled)
        {
            // Sin Minv:Web:Enabled las rutas de la web no existen (404): el servidor atiende solo al escritorio
            WebEndpoints.Map(app);
        }
        return app;
    }

    private static string Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "?";

    /// <summary>Partición del límite de comandos: los últimos caracteres del token de la sesión (de la cabecera del escritorio o
    /// de la cookie de la web; el limitador corre ANTES de autenticar y no toca la base) o la IP.</summary>
    private static string SessionPartition(HttpContext http, string cookieName)
    {
        if (http.Request.Headers.Authorization.ToString() is { Length: > 20 } header)
        {
            return header[^16..];
        }
        return http.Request.Cookies[cookieName] is { Length: > 20 } cookie ? "web:" + cookie[^16..] : Ip(http);
    }

    public static async Task RunAsync(WebApplication app)
    {
        var storage = app.Services.GetRequiredService<StorageInfo>().Storage;
        if (storage == ServerStorage.Postgres)
        {
            await ServerHosting.VerifyDatabaseAsync(app.Services);
        }
        var web = app.Services.GetRequiredService<WebSettings>();
        app.Logger.LogInformation("M-INV CloudServer {Version} · almacenamiento: {Storage} · sesión web: {Web}", ServerHosting.Version, storage,
            !web.Enabled ? "apagada" : web.IsConfigured ? "empresa " + web.Tenant : "sin configurar (falta Minv:Web:TenantCode)");
        await app.RunAsync();
    }
}

/// <summary>
/// V7 · Detrás del proxy de la web (nginx del catálogo + túnel) la IP del visitante sale de <c>X-Forwarded-For</c> y el
/// esquema de <c>X-Forwarded-Proto</c>, SOLO si la petición llega desde una red de confianza (<c>Minv:ForwardedHeaders</c>:
/// <c>Enabled</c> y <c>KnownNetworks</c>; por defecto las redes privadas). Es la MISMA lógica del API Gateway: sin esto, los
/// límites por IP los compartirían todos los visitantes (todos llegarían con la IP del proxy) y la cookie no sabría que la
/// petición fue https.
/// </summary>
public static class ProxySetup
{
    public const string Section = "Minv:ForwardedHeaders";

    public static readonly string[] PrivateNetworks = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8"];

    /// <summary>Registra las opciones si está habilitado y dice si hay que usar el middleware.</summary>
    public static bool Configure(IServiceCollection services, IConfiguration configuration)
    {
        var forwarded = configuration.GetSection(Section);
        if (!forwarded.GetValue("Enabled", false))
        {
            return false;
        }
        var networks = forwarded.GetSection("KnownNetworks").Get<string[]>() is { Length: > 0 } configured ? configured : PrivateNetworks;
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var cidr in networks)
            {
                var parts = cidr.Split('/', 2);
                options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse(parts[0]), int.Parse(parts[1], CultureInfo.InvariantCulture)));
            }
        });
        return true;
    }
}

/// <summary>Almacenamiento elegido al arrancar.</summary>
public sealed record StorageInfo(ServerStorage Storage);
