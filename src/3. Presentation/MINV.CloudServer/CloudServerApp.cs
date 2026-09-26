using System.Threading.RateLimiting;
using MINV.Application;
using MINV.Infrastructure.Billing;
using MINV.Infrastructure.Hosting;

namespace MINV.CloudServer;

/// <summary>
/// V4 · Armado del servidor en la nube. Variables: <c>MINV_DB</c> (PostgreSQL con el rol minv_server y
/// <c>SSL Mode=VerifyFull</c>), <c>MINV_INTEGRATION_KEYS</c> (claves maestras) y <c>ASPNETCORE_URLS</c> (https con
/// certificado, o http detrás de un proxy TLS). <c>--Minv:Storage=memoria</c> para pruebas y demostraciones.
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
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Inicio de sesión: 10 intentos por minuto por IP (además del bloqueo de la cuenta por intentos fallidos)
            options.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "?",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("Minv:LoginsPerMinute", 10), Window = TimeSpan.FromMinutes(1) }));
            // Comandos: ráfaga de 120 y 10 por segundo por sesión (token) o por IP si no lo trae
            options.AddPolicy("rpc", http => RateLimitPartition.GetTokenBucketLimiter(
                http.Request.Headers.Authorization.ToString() is { Length: > 20 } token ? token[^16..] : http.Connection.RemoteIpAddress?.ToString() ?? "?",
                _ => new TokenBucketRateLimiterOptions { TokenLimit = 120, TokensPerPeriod = 10, ReplenishmentPeriod = TimeSpan.FromSeconds(1) }));
        });
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 4 * 1024 * 1024);   // imágenes de producto ≤ 1 MB en base64
        var app = builder.Build();
        app.UseRateLimiter();
        RpcEndpoints.Map(app);
        return app;
    }

    public static async Task RunAsync(WebApplication app)
    {
        var storage = app.Services.GetRequiredService<StorageInfo>().Storage;
        if (storage == ServerStorage.Postgres)
        {
            await ServerHosting.VerifyDatabaseAsync(app.Services);
        }
        app.Logger.LogInformation("M-INV CloudServer {Version} · almacenamiento: {Storage}", ServerHosting.Version, storage);
        await app.RunAsync();
    }
}

/// <summary>Almacenamiento elegido al arrancar.</summary>
public sealed record StorageInfo(ServerStorage Storage);
