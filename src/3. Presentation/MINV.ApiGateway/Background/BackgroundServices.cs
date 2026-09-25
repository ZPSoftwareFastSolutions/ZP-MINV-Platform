using Microsoft.EntityFrameworkCore;
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
