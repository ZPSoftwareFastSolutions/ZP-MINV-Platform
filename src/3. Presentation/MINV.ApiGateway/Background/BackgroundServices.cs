using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MINV.ApiGateway.Security;
using MINV.Application.Storefront;
using MINV.Infrastructure.Integration;
using MINV.Infrastructure.Persistence;

namespace MINV.ApiGateway.Background;

/// <summary>V4 · Entrega los webhooks pendientes en segundo plano (cada réplica del gateway toma su lote con SKIP LOCKED).</summary>
public sealed class WebhookDispatcherService(WebhookDispatcher dispatcher, IConfiguration configuration, ILogger<WebhookDispatcherService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idle = TimeSpan.FromSeconds(configuration.GetValue("Minv:Webhooks:IntervalSeconds", 5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var summary = await dispatcher.RunOnceAsync(50, stoppingToken);
                if (summary.Events > 0)
                {
                    log.LogInformation("Webhooks: {Events} eventos, {Delivered} entregas correctas, {Failed} fallidas", summary.Events, summary.Delivered,
                        summary.Failed);
                    continue;   // puede haber más en la cola
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Falla del despachador de webhooks");
            }
            await Task.Delay(idle, stoppingToken);
        }
    }
}

/// <summary>
/// V7 · Envía los correos de confirmación de las reservas en segundo plano (regla P-06: DESPUÉS del COMMIT de la reserva). Solo
/// marca el ritmo: cada pasada la hace <see cref="MailDispatcher"/> (cada réplica del gateway toma su lote con SKIP LOCKED). Si
/// hubo correos repite enseguida; con la cola vacía espera <c>Minv:Mail:IntervalSeconds</c>; si la pasada se cortó porque el
/// servidor de correo falló (conexión o credenciales), espera al menos <see cref="HaltPause"/> para no insistirle.
/// Interruptor: <c>Minv:Mail:Enabled</c> (apagado por defecto).
/// </summary>
public sealed class MailDispatcherService(MailDispatcher dispatcher, MailOptions options, ILogger<MailDispatcherService> log) : BackgroundService
{
    /// <summary>Correos por pasada.</summary>
    public const int Batch = 20;

    /// <summary>Espera mínima después de una pasada cortada por una falla del servidor de correo.</summary>
    public static readonly TimeSpan HaltPause = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("{Options}", options.ToString());
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = options.Interval;
            try
            {
                var summary = await dispatcher.RunOnceAsync(Batch, stoppingToken);
                if (summary.Mails > 0)
                {
                    log.LogInformation("Correos: {Mails} tomados, {Sent} enviados, {Failed} fallidos, {Postponed} pospuestos, {Cancelled} cancelados",
                        summary.Mails, summary.Sent, summary.Failed, summary.Postponed, summary.Cancelled);
                }
                if (summary.Halted)
                {
                    log.LogWarning("Correos: el servidor de correo falló (conexión, credenciales o configuración); se reintenta en {Minutes} min. " +
                                   "El detalle queda en la cola (GetOutgoingMailsQuery).", Math.Max(HaltPause.TotalMinutes, wait.TotalMinutes));
                    wait = wait > HaltPause ? wait : HaltPause;
                }
                else if (summary.Mails > 0)
                {
                    continue;   // puede haber más en la cola
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Falla del despachador de correos");
            }
            await Task.Delay(wait, stoppingToken);
        }
    }
}

/// <summary>V4 · Refresca el modelo de lectura (vistas materializadas de <c>reporting</c>) cada pocos minutos. La función
/// toma un candado consultivo: si otra réplica ya está refrescando, esta pasada no hace nada.</summary>
public sealed class ReportingRefreshService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<ReportingRefreshService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var every = TimeSpan.FromMinutes(configuration.GetValue("Minv:Reporting:RefreshMinutes", 5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
                var refreshed = await db.Database.SqlQueryRaw<bool>("SELECT reporting.refresh_all() AS \"Value\"").SingleAsync(stoppingToken);
                log.LogInformation(refreshed ? "Modelo de lectura refrescado" : "Otra réplica está refrescando el modelo de lectura");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "No se pudo refrescar el modelo de lectura");
            }
            await Task.Delay(every, stoppingToken);
        }
    }
}

/// <summary>
/// V6 · Cierra las reservas de armados vencidas cada pocos minutos (regla S-04: una reserva vencida nunca se cierra «al leer»):
/// corre como el principal técnico de la tienda (misma tubería de MediatR: permisos y auditoría) y ejecuta
/// <see cref="ExpirePcBuildReservationsCommand"/>, que devuelve el stock y deja el armado anulado con motivo «Vencida».
/// </summary>
public sealed class StorefrontReservationExpiryService(IServiceScopeFactory scopes, IOptionsMonitor<StorefrontSettings> settings,
    ILogger<StorefrontReservationExpiryService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var every = TimeSpan.FromMinutes(Math.Clamp(settings.CurrentValue.ExpiryMinutes, 1, 60));
            try
            {
                var closed = await RunOnceAsync(stoppingToken);
                if (closed > 0)
                {
                    log.LogInformation("Tienda web: {Count} reserva(s) de armados vencida(s) cerradas; el stock volvió a estar disponible", closed);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Falla del vencimiento de reservas de la tienda web");
            }
            await Task.Delay(every, stoppingToken);
        }
    }

    /// <summary>Una pasada (también la usan las pruebas): -1 si la tienda no está configurada.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var principal = await scope.ServiceProvider.GetRequiredService<StorefrontAuthenticator>().AuthenticateAsync(ct);
        if (principal is null)
        {
            return -1;
        }
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ExpirePcBuildReservationsCommand(), ct);
    }
}
