using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Warehousing;

namespace MINV.Application.Billing;

/// <summary>
/// V4.1 · Mantenimiento diario de los códigos del SIN de la empresa: por cada punto de venta activo, CUIS vigente
/// (renovación desde 5 días antes), hora del SIN, CUFD del día (renovado antes de vencer) y catálogos si no se
/// sincronizaron hoy. Lo usan <see cref="PrepareSiatCommand"/> (forzado) y el trabajo automático
/// (<see cref="ISiatWorker.MaintainAsync"/>). NO guarda (guarda quien llama).
/// </summary>
/// <remarks>
/// Orden diario del SIN (investigación 01 §11.21): hora → catálogos → CUFD. La hora se sincroniza UNA vez por corrida y los
/// catálogos se toman una sola vez de la casa matriz sin punto de venta (esquema centralizado, investigación 04 §3.1). Si un
/// punto de venta no tiene comunicación se registra el fallo y se sigue con los demás: el paso a fuera de línea lo decide el
/// trabajo en segundo plano (dos fallos seguidos, regla F-09).
/// </remarks>
public static class SiatDailyMaintenance
{
    public static async Task<SiatMaintenanceResult> RunAsync(BillingLookups lookups, SiatCodeManager codes, FiscalContext context, bool force,
        Guid? userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lookups);
        ArgumentNullException.ThrowIfNull(codes);
        ArgumentNullException.ThrowIfNull(context);
        var db = lookups.Db;
        var now = lookups.Clock.UtcNow;
        var environment = context.Environment;
        var tenantId = context.Settings.TenantId;
        var today = lookups.FiscalNow(context).Date;
        var messages = new List<string>();
        var clockPending = force || context.Settings.ClockSyncedAt is not { } synced || FiscalRules.ToFiscalTime(synced, context.Zone).Date != today;
        var catalogsPending = force || !await CatalogsSyncedOnAsync(db, environment, context.Zone, today, ct);
        var cuisRequested = 0;
        var cufdRequested = 0;

        // La casa matriz (código 0) primero: de ella salen la hora y los catálogos
        var mappings = (await db.Set<SiatBranch>().ToListAsync(ct)).Where(m => db.Branches.Allows(m.BranchId)).OrderBy(m => m.SiatCode).ToList();
        if (mappings.Count == 0)
        {
            messages.Add("Ninguna sucursal tiene su código del Padrón: configure al menos la casa matriz (código 0).");
        }
        var branchCodes = await db.Set<Branch>().ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        foreach (var mapping in mappings)
        {
            var branchCode = branchCodes.GetValueOrDefault(mapping.BranchId, "?");
            var points = await db.Set<SiatPointOfSale>()
                .Where(p => p.BranchId == mapping.BranchId && p.Environment == environment && p.ClosedAt == null)
                .OrderBy(p => p.Code).ToListAsync(ct);
            if (!points.Any(p => p.Code == 0))
            {
                var zero = new SiatPointOfSale(tenantId, mapping.BranchId, environment, 0, 0, SiatAdminSupport.PointZeroName, null, null, now);
                db.Set<SiatPointOfSale>().Add(zero);
                points.Insert(0, zero);
                messages.Add($"{branchCode}: se creó el punto 0 (sucursal {mapping.SiatCode} sin punto de venta).");
            }
            foreach (var point in points)
            {
                var label = $"{branchCode} · punto {point.Code}";
                try
                {
                    // 1. CUIS (365 días; renovable desde 5 días antes; forzado → se pide ya)
                    var currentCuis = await lookups.CurrentCuisAsync(point.Id, now, ct);
                    if (force || currentCuis is null || currentCuis.IsRenewableAt(now))
                    {
                        cuisRequested++;
                    }
                    var cuis = await codes.EnsureCuisAsync(context, mapping, point, force, ct);
                    var renewed = currentCuis is null || currentCuis.Id != cuis.Id;
                    var place = BillingLookups.Place(mapping, point);

                    // 2. Hora y catálogos (una vez por corrida; un fallo del SIN aquí no impide pedir el CUFD)
                    if (catalogsPending)
                    {
                        catalogsPending = false;
                        var sync = await TolerantAsync(() => codes.SyncCatalogsAsync(context, place, cuis.Code, null, userId, ct), label, messages);
                        if (sync is not null)
                        {
                            clockPending &= sync.ClockSyncedAt is null;
                            messages.Add($"Catálogos del SIN sincronizados ({label}): {sync.Catalogs} servicios, {sync.Items} filas" +
                                         (sync.Errors.Count > 0 ? $"; con errores: {string.Join(" · ", sync.Errors)}" : "."));
                        }
                    }
                    if (clockPending)
                    {
                        clockPending = false;
                        var clockReply = await TolerantAsync(async () => (object)await codes.SyncClockAsync(context, place, cuis.Code, ct), label, messages);
                        if (clockReply is not null)
                        {
                            db.Set<SiatSyncRun>().Add(new SiatSyncRun(tenantId, environment, SiatCatalogNames.DateTime, 1, null, now, userId));
                            messages.Add($"Hora del SIN sincronizada (desfase {context.Settings.ClockOffsetMs} ms).");
                        }
                    }

                    // 3. CUFD del día (nuevo si falta, vence en menos de una hora, cambió el CUIS o se fuerza)
                    var currentCufd = await lookups.CurrentCufdAsync(point.Id, now, ct);
                    var forceCufd = force || renewed;
                    if (forceCufd || currentCufd is null || currentCufd.ValidUntil - now <= SiatCodeManager.CufdRenewalMargin || currentCufd.CuisId != cuis.Id)
                    {
                        cufdRequested++;
                    }
                    var cufd = await codes.EnsureCufdAsync(context, mapping, point, forceCufd, ct);
                    messages.Add($"{label}: CUIS vigente hasta {SiatAdminSupport.Local(cuis.ValidUntil, context.Zone, "dd/MM/yyyy")}, " +
                                 $"CUFD hasta {SiatAdminSupport.Local(cufd.ValidUntil, context.Zone)}.");
                }
                catch (SiatUnavailableException ex)
                {
                    point.RecordFailure(ex.Message, now);
                    messages.Add($"{label}: sin comunicación con el SIN ({ex.Message}).");
                }
                catch (DomainException ex) when (ex.Code != "siat.token_rejected")
                {
                    messages.Add($"{label}: {ex.Message}");
                }
            }
        }
        return new SiatMaintenanceResult(cuisRequested, cufdRequested, 0, 0, 0, messages);
    }

    /// <summary>¿Hubo hoy (hora fiscal) una sincronización exitosa de catálogos en el ambiente?</summary>
    private static async Task<bool> CatalogsSyncedOnAsync(IMinvDbContext db, int environment, TimeZoneInfo zone, DateTime today, CancellationToken ct)
    {
        var last = await db.Set<SiatSyncRun>()
            .Where(r => r.Environment == environment && r.Error == null && r.Catalog != SiatCatalogNames.DateTime)
            .OrderByDescending(r => r.OccurredAt).Select(r => (DateTimeOffset?)r.OccurredAt).FirstOrDefaultAsync(ct);
        return last is { } at && FiscalRules.ToFiscalTime(at, zone).Date == today;
    }

    /// <summary>La hora y los catálogos no deben impedir el CUFD: sus rechazos quedan como mensaje (la falta de comunicación y el
    /// token rechazado sí se propagan).</summary>
    private static async Task<T?> TolerantAsync<T>(Func<Task<T>> step, string label, List<string> messages) where T : class
    {
        try
        {
            return await step();
        }
        catch (DomainException ex) when (ex.Code != "siat.token_rejected")
        {
            messages.Add($"{label}: {ex.Message}");
            return null;
        }
    }
}
