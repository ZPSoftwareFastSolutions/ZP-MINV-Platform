using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Domain.Billing;
using MINV.Infrastructure.Persistence;

namespace MINV.Infrastructure.Billing.Hosting;

/// <summary>V4.1 · Cadencia del trabajo en segundo plano de la facturación.</summary>
public sealed class SiatBackgroundOptions
{
    /// <summary>Cada cuánto se envían los documentos pendientes (por defecto 10 s).</summary>
    public TimeSpan DispatchInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Cada cuánto corre el mantenimiento: recuperación fuera de línea, CUFD/CUIS, hora, catálogos y correos (60 s).</summary>
    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Documentos por empresa y pasada.</summary>
    public int DispatchBatch { get; set; } = 50;

    /// <summary>Espera antes de la primera pasada (deja arrancar al servidor).</summary>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Resultado de una pasada (para el rastreo y las pruebas).</summary>
public sealed record SiatBackgroundPass(int Tenants, int Dispatched, int Maintained, int Failures);

/// <summary>V4.1 · Empresas con la facturación SIAT activa (proceso de plataforma: se consulta sin empresa fijada).</summary>
public interface ISiatTenantSource
{
    Task<IReadOnlyList<Guid>> ActiveTenantsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// V4.1 · Empresas con <c>siat_settings.is_enabled</c>. En PostgreSQL con la función SECURITY DEFINER
/// <c>billing.siat_active_tenants()</c> (la RLS impide leer la configuración de todas las empresas con el rol de la
/// aplicación); en la base en memoria, consultando <see cref="SiatSettings"/> sin los filtros de empresa (proceso de
/// plataforma documentado, como el despachador de webhooks).
/// </summary>
public sealed class SiatTenantSource(MinvWriteDbContext db) : ISiatTenantSource
{
    public async Task<IReadOnlyList<Guid>> ActiveTenantsAsync(CancellationToken cancellationToken = default)
    {
        if (db.Database.IsRelational())
        {
            return await db.Database.SqlQueryRaw<Guid>("SELECT t.\"Value\" FROM billing.siat_active_tenants() AS t(\"Value\")")
                .ToListAsync(cancellationToken);
        }
        return await db.Set<SiatSettings>().IgnoreQueryFilters().Where(s => s.IsEnabled).Select(s => s.TenantId).Distinct()
            .ToListAsync(cancellationToken);
    }
}

/// <summary>
/// V4.1 · Trabajo en segundo plano de la facturación (servidor en la nube y escritorio en modo local): cada
/// <see cref="SiatBackgroundOptions.DispatchInterval"/> recorre las empresas con facturación activa y, por cada una, en un
/// scope DI NUEVO con la empresa fijada y sin restricción de sucursal (proceso de plataforma), envía los pendientes con
/// <see cref="ISiatWorker.DispatchAsync"/>; cada <see cref="SiatBackgroundOptions.MaintenanceInterval"/> corre además
/// <see cref="ISiatWorker.MaintainAsync"/>. El error de una empresa se registra y no detiene a las demás.
/// </summary>
public sealed class SiatBackgroundService(IServiceScopeFactory scopes, SiatBackgroundOptions options, ILogger<SiatBackgroundService> log,
    TimeProvider? time = null) : BackgroundService
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Delay(options.StartupDelay, stoppingToken);
        var nextMaintenance = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            var maintain = _time.GetUtcNow() >= nextMaintenance;
            try
            {
                var pass = await RunOnceAsync(maintain, stoppingToken);
                if (pass.Failures > 0)
                {
                    log.LogWarning("Facturación SIAT: {Failures} fallas en {Tenants} empresas", pass.Failures, pass.Tenants);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Falla del trabajo en segundo plano de la facturación SIAT");
            }
            if (maintain)
            {
                nextMaintenance = _time.GetUtcNow() + options.MaintenanceInterval;
            }
            await Delay(options.DispatchInterval, stoppingToken);
        }
    }

    /// <summary>Una pasada: despacho (y mantenimiento si <paramref name="maintain"/>) de cada empresa activa.</summary>
    public async Task<SiatBackgroundPass> RunOnceAsync(bool maintain, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> tenants;
        using (var scope = scopes.CreateScope())
        {
            tenants = await scope.ServiceProvider.GetRequiredService<ISiatTenantSource>().ActiveTenantsAsync(cancellationToken);
        }
        int dispatched = 0, maintained = 0, failures = 0;
        foreach (var tenantId in tenants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await RunForTenantAsync(tenantId, "envío de documentos",
                    worker => worker.DispatchAsync(null, Math.Max(1, options.DispatchBatch), cancellationToken), cancellationToken))
            {
                dispatched++;
            }
            else
            {
                failures++;
            }
            if (!maintain)
            {
                continue;
            }
            if (await RunForTenantAsync(tenantId, "mantenimiento", worker => worker.MaintainAsync(false, cancellationToken), cancellationToken))
            {
                maintained++;
            }
            else
            {
                failures++;
            }
        }
        return new SiatBackgroundPass(tenants.Count, dispatched, maintained, failures);
    }

    /// <summary>Un paso para una empresa en un scope nuevo (su propio contexto de datos): false si falló.</summary>
    private async Task<bool> RunForTenantAsync(Guid tenantId, string step, Func<ISiatWorker, Task> action, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenant.Set(tenantId);
            tenant.SetBranches(BranchScope.Unrestricted);   // proceso de plataforma: todas las sucursales de ESA empresa
            await action(scope.ServiceProvider.GetRequiredService<ISiatWorker>());
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogError(ex, "Facturación SIAT: falló el {Step} de la empresa {TenantId}", step, tenantId);
            return false;
        }
    }

    private Task Delay(TimeSpan delay, CancellationToken ct) =>
        delay > TimeSpan.Zero ? Task.Delay(delay, _time, ct) : Task.CompletedTask;
}
