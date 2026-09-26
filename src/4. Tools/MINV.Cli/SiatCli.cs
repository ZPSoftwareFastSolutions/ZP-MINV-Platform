using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Domain.Billing;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Services;

/// <summary>
/// V4.1 · Comandos de la facturación SIAT de <c>minv</c>. Los de la empresa (<c>estado</c>, <c>preparar</c>,
/// <c>sincronizar</c>, <c>procesar</c>) corren como el trabajo en segundo plano del servidor en la nube: proceso de
/// plataforma con la empresa fijada y todas sus sucursales, contra las URL de su configuración (el simulador HTTP o el
/// SIN), con el token descifrado por la clave maestra de este equipo. Los del simulador (<c>simulador-estado</c>,
/// <c>simulador-apagar</c>, <c>simulador-encender</c>) usan su control HTTP (<c>/control</c>) para simular un corte de
/// internet sin tocar la base.
/// </summary>
internal static class SiatCli
{
    public const string DefaultSimulatorUrl = "http://localhost:5095";

    /// <summary>Carpeta local de M-INV (credenciales, claves y el estado del simulador; nunca el repositorio).</summary>
    public static string LocalFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "M-INV");

    public static string DefaultStateFile => Path.Combine(LocalFolder, "siat-simulador.json");

    /// <summary>
    /// Archivo de estado del simulador para una carga NUEVA de datos de prueba: el anterior (de otra base) se conserva como
    /// <c>.anterior.json</c> y se empieza de cero (el simulador HTTP debe estar detenido: tools\bd_local.ps1 lo detiene).
    /// </summary>
    public static string FreshStateFile(string? path)
    {
        var file = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? DefaultStateFile : path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (File.Exists(file))
        {
            File.Move(file, Path.ChangeExtension(file, ".anterior.json"), true);
            Console.WriteLine($"   Estado anterior del simulador del SIN guardado como {Path.ChangeExtension(file, ".anterior.json")}");
        }
        return file;
    }

    /// <summary>Si falta la variable MINV_INTEGRATION_KEYS, la toma de la línea «MINV_INTEGRATION_KEYS=…» del archivo local de
    /// claves de integración (solo este equipo; tools\bd_local.ps1 la genera).</summary>
    public static void LoadIntegrationKeys()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AesGcmSecretProtector.KeysVariable)))
        {
            return;
        }
        var file = Path.Combine(LocalFolder, "claves-integracion.txt");
        if (!File.Exists(file))
        {
            return;
        }
        var prefix = AesGcmSecretProtector.KeysVariable + "=";
        if (File.ReadLines(file).FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal)) is { } line)
        {
            Environment.SetEnvironmentVariable(AesGcmSecretProtector.KeysVariable, line[prefix.Length..].Trim());
        }
    }

    // ================================================================================================ empresa
    public static async Task<int> RunAsync(string action, IServiceProvider sp, MinvWriteDbContext db, IReadOnlyDictionary<string, string> options)
    {
        var code = (options.GetValueOrDefault("codigo") ?? "MINV").Trim().ToUpperInvariant();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Code == code) ?? throw new InvalidOperationException($"La empresa {code} no existe.");
        var context = sp.GetRequiredService<ITenantContext>();
        context.Set(tenant.Id);
        context.SetBranches(BranchScope.Unrestricted);   // proceso de plataforma: todas las sucursales de ESA empresa
        await db.SyncTenantSessionAsync();
        switch (action)
        {
            case "estado":
                Print(await HandleAsync(sp, new GetSiatStatusQuery()), tenant.LegalName);
                return 0;
            case "preparar":
                Print(await HandleAsync(sp, new PrepareSiatCommand()));
                return 0;
            case "sincronizar":
            {
                var sync = await HandleAsync(sp, new SyncSiatCatalogsCommand());
                Console.WriteLine($"✔ {sync.Catalogs} catálogos del SIN sincronizados ({sync.Items} filas)" +
                                  (sync.ClockSyncedAt is { } at ? $"; hora del SIN sincronizada a las {at.ToLocalTime():HH:mm:ss}." : "."));
                foreach (var error in sync.Errors)
                {
                    Console.WriteLine("   ⚠ " + error);
                }
                return sync.Errors.Count == 0 ? 0 : 2;
            }
            case "procesar":
            {
                var worker = sp.GetRequiredService<ISiatWorker>();
                var first = await worker.DispatchAsync(null, 500);
                var maintenance = await worker.MaintainAsync(options.ContainsKey("forzar"));
                var second = await worker.DispatchAsync(null, 500);
                Console.WriteLine($"✔ Envío al SIN: {first.Sent + second.Sent} enviados, {first.Valid + second.Valid} válidos, " +
                                  $"{first.Rejected + second.Rejected} rechazados, {first.WentOffline + second.WentOffline} fuera de línea.");
                Print(maintenance);
                foreach (var message in first.Messages.Concat(second.Messages).Distinct())
                {
                    Console.WriteLine("   · " + message);
                }
                return 0;
            }
            default:
                throw new InvalidOperationException($"Subcomando desconocido: siat {action}.");
        }
    }

    /// <summary>El caso de uso sin la tubería de permisos (no hay usuario: es un proceso de plataforma, como el servidor).</summary>
    private static Task<T> HandleAsync<T>(IServiceProvider sp, IRequest<T> request)
    {
        var handler = sp.GetRequiredService(typeof(IRequestHandler<,>).MakeGenericType(request.GetType(), typeof(T)));
        return (Task<T>)handler.GetType().GetMethod("Handle")!.Invoke(handler, [request, CancellationToken.None])!;
    }

    private static void Print(SiatStatusView status, string company)
    {
        if (!status.Configured)
        {
            Console.WriteLine($"La facturación SIAT de {company} no está configurada.");
            return;
        }
        Console.WriteLine($"Facturación SIAT de {company}: {(status.Enabled ? "ACTIVA" : "desactivada")} · NIT {status.NitText()} · ambiente " +
                          $"{status.Environment} ({(status.Environment == SiatCodes.EnvironmentProduction ? "producción" : "pruebas, SIN VALOR LEGAL")}) · token " +
                          (status.HasToken ? $"cargado{(status.TokenValidUntil is { } until ? $" (vence el {until:dd/MM/yyyy})" : string.Empty)}" : "FALTA"));
        Console.WriteLine($"Hoy: {status.DocumentsToday} documentos por Bs {status.BilledToday.ToString("N2", CultureInfo.GetCultureInfo("es-BO"))} · " +
                          $"{status.PendingDocuments} pendientes de envío · {status.OfflineDocuments} fuera de línea · {status.OpenEvents} eventos abiertos");
        Console.WriteLine();
        Console.WriteLine($"{"Sucursal",-9} {"PV",3} {"Caja",-10} {"Modo",-22} {"CUIS hasta",-12} {"CUFD hasta",-17} Pendientes");
        foreach (var p in status.Points)
        {
            Console.WriteLine($"{p.BranchCode,-9} {p.Code,3} {p.RegisterCode ?? "—",-10} {Mode(p.Mode),-22} {Date(p.CuisValidUntil, "dd/MM/yyyy"),-12} " +
                              $"{Date(p.CufdValidUntil, "dd/MM/yyyy HH:mm"),-17} {p.PendingDocuments + p.OfflineDocuments}");
        }
        foreach (var alert in status.Alerts)
        {
            Console.WriteLine($"{(alert.Severity == SiatAlertSeverity.Danger ? "✖" : alert.Severity == SiatAlertSeverity.Warning ? "⚠" : "·")} {alert.Title}: {alert.Detail}");
        }
    }

    private static void Print(SiatMaintenanceResult result)
    {
        Console.WriteLine($"✔ Mantenimiento: {result.CuisRequested} CUIS y {result.CufdRequested} CUFD pedidos, {result.Recovered} puntos recuperados, " +
                          $"{result.PackagesValidated} paquetes validados, {result.DocumentsSent} documentos enviados.");
        foreach (var message in result.Messages)
        {
            Console.WriteLine("   · " + message);
        }
    }

    private static string NitText(this SiatStatusView status) => status.Nit?.ToString(CultureInfo.InvariantCulture) ?? "—";

    private static string Mode(SiatConnectionMode mode) => mode switch
    {
        SiatConnectionMode.Online => "en línea",
        SiatConnectionMode.Offline => "FUERA DE LÍNEA",
        SiatConnectionMode.ManualContingency => "contingencia manual",
        SiatConnectionMode.Recovering => "recuperando",
        _ => mode.ToString(),
    };

    private static string Date(DateTimeOffset? value, string format) => value?.ToLocalTime().ToString(format, CultureInfo.InvariantCulture) ?? "—";

    // ================================================================================================ simulador HTTP
    public static async Task<int> SimulatorAsync(string command, IReadOnlyDictionary<string, string> options)
    {
        var baseUrl = (options.GetValueOrDefault("simulador") ?? DefaultSimulatorUrl).TrimEnd('/');
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        HttpResponseMessage response;
        try
        {
            response = command switch
            {
                "siat simulador-apagar" => await http.PostAsync($"{baseUrl}/control/offline?on=true", null),
                "siat simulador-encender" => await http.PostAsync($"{baseUrl}/control/offline?on=false", null),
                "siat simulador-estado" => await http.GetAsync($"{baseUrl}/control/estado"),
                _ => throw new InvalidOperationException($"Subcomando desconocido: {command}."),
            };
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"✖ El simulador del SIN no responde en {baseUrl} ({ex.Message}). Inícielo con tools\\servidores_locales.ps1.");
            return 1;
        }
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<JsonElement>();
        var available = status.GetProperty("available").GetBoolean();
        Console.WriteLine(available
            ? $"✔ Simulador del SIN en {baseUrl}: RESPONDE (hay «internet» con el SIN)."
            : $"✔ Simulador del SIN en {baseUrl}: APAGADO (corte de internet simulado): las cajas facturan fuera de línea y se recuperan solas al encenderlo.");
        Console.WriteLine($"   {Number(status, "documents")} documentos, {Number(status, "voidedDocuments")} anulados, {Number(status, "packages")} paquetes, " +
                          $"{Number(status, "events")} eventos, {Number(status, "pointsOfSale")} puntos de venta · estado: " +
                          (status.TryGetProperty("stateFile", out var file) && file.ValueKind == JsonValueKind.String ? file.GetString() : "solo en memoria"));
        return 0;
    }

    private static int Number(JsonElement status, string name) =>
        status.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
}
