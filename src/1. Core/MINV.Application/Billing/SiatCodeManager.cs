using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Domain.Common;

namespace MINV.Application.Billing;

/// <summary>
/// V4.1 · Códigos del SIN de un punto de venta: CUIS (365 días), CUFD (24 h, con código de control y dirección), hora
/// del SIN y catálogos. Lo usan los comandos de administración (Estado SIAT) y el mantenimiento automático
/// (<see cref="ISiatWorker"/>). NO guarda: el caso de uso guarda. La falta de comunicación sale como
/// <see cref="SiatUnavailableException"/> para que quien llama decida (registrar el fallo, pasar a fuera de línea).
/// </summary>
public sealed class SiatCodeManager(BillingLookups lookups, ISiatGateway gateway)
{
    /// <summary>Margen con el que se pide el CUFD del día antes de que venza el anterior.</summary>
    public static readonly TimeSpan CufdRenewalMargin = TimeSpan.FromHours(1);

    private IMinvDbContext Db => lookups.Db;

    /// <summary>CUIS vigente (o uno nuevo si falta, está por vencer o se fuerza).</summary>
    public async Task<SiatCuis> EnsureCuisAsync(FiscalContext context, SiatBranch branch, SiatPointOfSale pointOfSale, bool force,
        CancellationToken ct)
    {
        var now = lookups.Clock.UtcNow;
        var current = await lookups.CurrentCuisAsync(pointOfSale.Id, now, ct);
        if (current is not null && !force && !current.IsRenewableAt(now))
        {
            return current;
        }
        var reply = await gateway.RequestCuisAsync(context.Connection, BillingLookups.Place(branch, pointOfSale), ct);
        pointOfSale.RecordContact(now);
        if (reply.Code is not { Length: > 0 } code)
        {
            if (current is not null)
            {
                return current; // el SIN no renovó todavía: se sigue con el vigente
            }
            throw new DomainException("siat.cuis_failed", "El SIN no entregó el CUIS: " + Describe(reply.Messages));
        }
        if (current is not null && current.Code == code)
        {
            return current;
        }
        var cuis = new SiatCuis(pointOfSale.TenantId, pointOfSale.BranchId, pointOfSale.Id, code, reply.ValidUntil ?? now.AddDays(365), now);
        Db.Set<SiatCuis>().Add(cuis);
        return cuis;
    }

    /// <summary>CUFD vigente (o uno nuevo: falta, vence en menos de una hora, o se fuerza — p. ej. antes de registrar un
    /// evento significativo o después de renovar el CUIS).</summary>
    public async Task<SiatCufd> EnsureCufdAsync(FiscalContext context, SiatBranch branch, SiatPointOfSale pointOfSale, bool force,
        CancellationToken ct)
    {
        var now = lookups.Clock.UtcNow;
        var cuis = await lookups.RequireCuisAsync(pointOfSale.Id, now, ct);
        var current = await lookups.CurrentCufdAsync(pointOfSale.Id, now, ct);
        if (current is not null && !force && current.ValidUntil - now > CufdRenewalMargin && current.CuisId == cuis.Id)
        {
            return current;
        }
        var reply = await gateway.RequestCufdAsync(context.Connection, BillingLookups.Place(branch, pointOfSale), cuis.Code, ct);
        pointOfSale.RecordContact(now);
        if (reply.Code is not { Length: > 0 } code || reply.ControlCode is not { Length: > 0 } control)
        {
            throw new DomainException("siat.cufd_failed", "El SIN no entregó el CUFD: " + Describe(reply.Messages));
        }
        var cufd = new SiatCufd(pointOfSale.TenantId, pointOfSale.BranchId, pointOfSale.Id, cuis.Id, code, control,
            reply.Address is { Length: > 0 } address ? address : current?.Address ?? branch.Municipality, reply.ValidUntil ?? now.AddHours(24), now);
        Db.Set<SiatCufd>().Add(cufd);
        return cufd;
    }

    /// <summary>Sincroniza el reloj con el del SIN (obligatorio a diario; se recomienda antes de pedir el CUFD).</summary>
    public async Task<DateTimeOffset> SyncClockAsync(FiscalContext context, SiatPlace place, string cuis, CancellationToken ct)
    {
        var before = lookups.Clock.UtcNow;
        var reply = await gateway.SyncClockAsync(context.Connection, place, cuis, ct);
        var after = lookups.Clock.UtcNow;
        if (reply.SiatTime is not { } siatLocal)
        {
            throw new DomainException("siat.clock_failed", "El SIN no devolvió la fecha y hora: " + Describe(reply.Messages));
        }
        // Hora del SIN (sin zona, en la hora de Bolivia) → UTC; se compara con el punto medio de la llamada.
        var siatUtc = new DateTimeOffset(DateTime.SpecifyKind(siatLocal, DateTimeKind.Unspecified), context.Zone.GetUtcOffset(siatLocal));
        var midpoint = before + (after - before) / 2;
        context.Settings.SynchronizeClock(midpoint, siatUtc);
        return midpoint;
    }

    /// <summary>Sincroniza los catálogos (todos o uno). Actualiza lo existente, agrega lo nuevo y retira (sin borrar) lo que
    /// el SIN ya no devuelve. Deja una fila en <see cref="SiatSyncRun"/> por catálogo.</summary>
    public async Task<SiatSyncResult> SyncCatalogsAsync(FiscalContext context, SiatPlace place, string cuis, string? onlyCatalog, Guid? userId,
        CancellationToken ct)
    {
        var now = lookups.Clock.UtcNow;
        var catalogs = onlyCatalog is null
            ? SiatCatalogNames.All.Where(c => c != SiatCatalogNames.DateTime).ToList()
            : [onlyCatalog.Trim().ToUpperInvariant()];
        var items = 0;
        var errors = new List<string>();
        DateTimeOffset? clock = null;
        if (onlyCatalog is null || onlyCatalog.Equals(SiatCatalogNames.DateTime, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                clock = await SyncClockAsync(context, place, cuis, ct);
                Db.Set<SiatSyncRun>().Add(new SiatSyncRun(context.Settings.TenantId, context.Environment, SiatCatalogNames.DateTime, 1, null, now, userId));
            }
            catch (DomainException ex)
            {
                errors.Add(ex.Message);
                Db.Set<SiatSyncRun>().Add(new SiatSyncRun(context.Settings.TenantId, context.Environment, SiatCatalogNames.DateTime, 0, ex.Message, now, userId));
            }
        }
        foreach (var catalog in catalogs.Where(c => c != SiatCatalogNames.DateTime))
        {
            var reply = await gateway.SyncCatalogAsync(context.Connection, place, cuis, catalog, ct);
            if (!reply.Transaction && reply.Rows.Count == 0)
            {
                var error = $"{catalog}: {Describe(reply.Messages)}";
                errors.Add(error);
                Db.Set<SiatSyncRun>().Add(new SiatSyncRun(context.Settings.TenantId, context.Environment, catalog, 0, error, now, userId));
                continue;
            }
            var count = await ApplyAsync(context.Settings.TenantId, catalog, reply.Rows, now, ct);
            items += count;
            Db.Set<SiatSyncRun>().Add(new SiatSyncRun(context.Settings.TenantId, context.Environment, catalog, count, null, now, userId));
        }
        return new SiatSyncResult(catalogs.Count + (clock is null ? 0 : 1), items, errors, clock);
    }

    private async Task<int> ApplyAsync(Guid tenantId, string catalog, IReadOnlyList<SiatCatalogRow> rows, DateTimeOffset now, CancellationToken ct)
    {
        switch (catalog)
        {
            case SiatCatalogNames.Activities:
            {
                var existing = await Db.Set<SiatActivity>().ToDictionaryAsync(a => a.Code, ct);
                foreach (var row in rows.DistinctBy(r => r.Code.Trim()))
                {
                    var code = row.Code.Trim();
                    if (existing.Remove(code, out var activity))
                    {
                        activity.Refresh(row.Description, row.Extra, now);
                    }
                    else
                    {
                        Db.Set<SiatActivity>().Add(new SiatActivity(tenantId, code, row.Description, row.Extra, now));
                    }
                }
                foreach (var retired in existing.Values)
                {
                    retired.Retire();
                }
                return rows.Count;
            }
            case SiatCatalogNames.ActivitySectors:
            {
                var existing = await Db.Set<SiatActivitySector>().ToDictionaryAsync(a => (a.ActivityCode, a.DocumentSector), ct);
                foreach (var row in rows.DistinctBy(r => ((r.ActivityCode ?? string.Empty).Trim(), r.Code.Trim())))
                {
                    var activity = (row.ActivityCode ?? string.Empty).Trim();
                    if (activity.Length == 0 || !int.TryParse(row.Code, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sector))
                    {
                        continue;
                    }
                    if (existing.Remove((activity, sector), out var item))
                    {
                        item.Refresh(row.Extra, now);
                    }
                    else
                    {
                        Db.Set<SiatActivitySector>().Add(new SiatActivitySector(tenantId, activity, sector, row.Extra, now));
                    }
                }
                foreach (var retired in existing.Values)
                {
                    retired.Retire();
                }
                return rows.Count;
            }
            case SiatCatalogNames.Legends:
            {
                var existing = await Db.Set<SiatLegend>().ToListAsync(ct);
                var byKey = existing.GroupBy(l => (l.ActivityCode, l.Text)).ToDictionary(g => g.Key, g => g.First());
                foreach (var row in rows.DistinctBy(r => ((r.ActivityCode ?? string.Empty).Trim(), r.Description.Trim())))
                {
                    var activity = (row.ActivityCode ?? string.Empty).Trim();
                    var text = row.Description.Trim();
                    if (activity.Length == 0 || text.Length == 0)
                    {
                        continue;
                    }
                    if (byKey.Remove((activity, text), out var legend))
                    {
                        legend.Confirm(now);
                    }
                    else
                    {
                        Db.Set<SiatLegend>().Add(new SiatLegend(tenantId, activity, text.Length > 200 ? text[..200] : text, now));
                    }
                }
                foreach (var retired in byKey.Values)
                {
                    retired.Retire();
                }
                return rows.Count;
            }
            case SiatCatalogNames.Products:
            {
                var existing = await Db.Set<SiatProduct>().ToDictionaryAsync(p => (p.ActivityCode, p.ProductCode), ct);
                var seen = new HashSet<(string, int)>();
                foreach (var row in rows)
                {
                    var activity = (row.ActivityCode ?? string.Empty).Trim();
                    if (activity.Length == 0 || !int.TryParse(row.Code, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
                        || !seen.Add((activity, code)))
                    {
                        continue; // sin actividad, código ilegible o repetido en la misma respuesta
                    }
                    if (existing.Remove((activity, code), out var product))
                    {
                        product.Refresh(row.Description, now);
                    }
                    else
                    {
                        Db.Set<SiatProduct>().Add(new SiatProduct(tenantId, activity, code, row.Description, now));
                    }
                }
                foreach (var retired in existing.Values)
                {
                    retired.Retire();
                }
                return seen.Count;
            }
            default:
            {
                var existing = await Db.Set<SiatCatalogItem>().Where(i => i.Catalog == catalog).ToDictionaryAsync(i => i.Code, ct);
                var seen = new HashSet<int>();
                foreach (var row in rows)
                {
                    if (!int.TryParse(row.Code, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) || !seen.Add(code))
                    {
                        continue;
                    }
                    if (existing.Remove(code, out var item))
                    {
                        item.Refresh(row.Description, now);
                    }
                    else
                    {
                        Db.Set<SiatCatalogItem>().Add(new SiatCatalogItem(tenantId, catalog, code, row.Description, now));
                    }
                }
                foreach (var retired in existing.Values)
                {
                    retired.Retire();
                }
                return seen.Count;
            }
        }
    }

    public static string Describe(IReadOnlyList<SiatMessage> messages) =>
        messages.Count == 0 ? "sin detalle" : string.Join(" · ", messages.Select(m => $"{m.Code} {m.Description}"));
}
