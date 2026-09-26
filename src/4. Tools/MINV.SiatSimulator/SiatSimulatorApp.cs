using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using MINV.Application.Abstractions;
using MINV.Infrastructure.Billing.Simulator;
using MINV.Infrastructure.Billing.Soap;

namespace MINV.SiatSimulator;

/// <summary>
/// V4.1 · Armado del simulador HTTP del SIN. Expone los recursos SOAP en <c>/v2/FacturacionCodigos</c>,
/// <c>/v2/FacturacionSincronizacion</c>, <c>/v2/FacturacionOperaciones</c>, <c>/v2/ServicioFacturacionCompraVenta</c>,
/// <c>/v2/ServicioFacturacionComputarizada</c> y <c>/v2/ServicioFacturacionDocumentoAjuste</c> (POST con el sobre SOAP y
/// la cabecera <c>apikey: TokenApi &lt;token&gt;</c>; GET <c>?wsdl</c> con una descripción mínima), y el control:
/// <c>POST /control/offline?on=true|false</c>, <c>POST /control/latencia?ms=N</c>, <c>GET /control/estado</c>,
/// <c>GET /health</c>. Configuración: <c>--urls</c> (por defecto http://localhost:5095), <c>--Siat:Tokens</c> (tokens
/// aceptados separados por coma; vacío = cualquiera de 10+ caracteres), <c>--Siat:StateFile</c> (estado en JSON para
/// sobrevivir reinicios), <c>--Siat:Address</c> (dirección del Padrón, <c>{0}</c> = sucursal) y <c>--Siat:Offline=true</c>.
/// </summary>
public static class SiatSimulatorApp
{
    public const int DefaultPort = 5095;

    /// <summary>Tamaño máximo de una solicitud: paquete de 100 MB en base64 más el sobre.</summary>
    public const long MaxRequestBytes = 160L * 1024 * 1024;

    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]) && string.IsNullOrWhiteSpace(builder.Configuration["http_ports"]))
        {
            builder.WebHost.UseUrls($"http://localhost:{DefaultPort.ToString(CultureInfo.InvariantCulture)}");
        }
        var options = new SiatSimulatorOptions
        {
            AcceptedTokens = (builder.Configuration["Siat:Tokens"] ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StateFile = builder.Configuration["Siat:StateFile"] is { Length: > 0 } file ? file : null,
            StartAvailable = !builder.Configuration.GetValue("Siat:Offline", false),
        };
        if (builder.Configuration["Siat:Address"] is { Length: > 0 } address)
        {
            options.AddressFormat = address;
        }
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(sp => new SiatSimulatorEngine(sp.GetRequiredService<SiatSimulatorOptions>()));
        builder.Services.AddSingleton<SiatSimulatorSoapHandler>();
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = MaxRequestBytes);
        var app = builder.Build();
        Map(app);
        return app;
    }

    public static async Task RunAsync(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var status = app.Services.GetRequiredService<SiatSimulatorEngine>().Status();
        app.Logger.LogInformation("M-INV · simulador del SIN en {Addresses} (disponible: {Available}; estado: {StateFile})",
            string.Join(", ", addresses), status.Available, status.StateFile ?? "solo en memoria");
        await app.WaitForShutdownAsync();
    }

    private static void Map(WebApplication app)
    {
        app.MapPost("/v2/{servicio}", async (string servicio, HttpContext http, SiatSimulatorSoapHandler handler) =>
        {
            string body;
            using (var reader = new StreamReader(http.Request.Body, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync(http.RequestAborted);
            }
            var latency = handler.Engine.Latency;
            if (latency > TimeSpan.Zero)
            {
                await Task.Delay(latency, http.RequestAborted);
            }
            var result = handler.Handle(servicio, http.Request.Headers[SiatSoapContract.ApiKeyHeader].ToString(), body);
            return Results.Content(result.Body, SiatSimulatorHttpResult.ContentType, Encoding.UTF8, result.StatusCode);
        });
        app.MapGet("/v2/{servicio}", (string servicio) => SiatSoapContract.ResourceFromPath(servicio) is { } resource
            ? Results.Content(SiatSimulatorSoapHandler.Describe(resource), SiatSimulatorHttpResult.ContentType, Encoding.UTF8)
            : Results.NotFound());
        app.MapPost("/control/offline", (bool? on, SiatSimulatorEngine engine) =>
        {
            engine.Available = !(on ?? true);
            return Results.Json(engine.Status());
        });
        app.MapPost("/control/latencia", (int? ms, SiatSimulatorEngine engine) =>
        {
            engine.Latency = TimeSpan.FromMilliseconds(Math.Clamp(ms ?? 0, 0, 600_000));
            return Results.Json(engine.Status());
        });
        app.MapGet("/control/estado", (SiatSimulatorEngine engine) => Results.Json(engine.Status()));
        app.MapGet("/health", (SiatSimulatorEngine engine) => Results.Json(new { status = "ok", available = engine.Available }));
        app.MapGet("/", () => Results.Text(string.Join(Environment.NewLine,
        [
            "M-INV · simulador del SIN (Facturación Computarizada en Línea) — DATOS DE SIMULACIÓN, sin valor legal.",
            "POST /v2/{recurso} (SOAP 1.1, cabecera apikey: TokenApi <token>) · GET /v2/{recurso}?wsdl",
            "Recursos: " + string.Join(", ", Enum.GetValues<SiatResource>().Select(SiatSoapContract.ResourcePath)),
            "Control: POST /control/offline?on=true|false · POST /control/latencia?ms=N · GET /control/estado · GET /health",
        ]), "text/plain", Encoding.UTF8));
    }
}
