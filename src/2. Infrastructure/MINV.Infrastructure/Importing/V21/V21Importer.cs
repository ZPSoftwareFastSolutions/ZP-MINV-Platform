using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Domain.Common;
using MINV.Domain.Inventory;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Provisioning;

namespace MINV.Infrastructure.Importing.V21;

/// <summary>Resultado de comparar la V3 con la instantánea que la V2.1 dejó en el libro (15_STOCK, 16_ALERTAS, 18_PEDIDO).</summary>
public sealed record V21ParityReport(bool Checked, int Products, int Alerts, int OrderLines, IReadOnlyList<string> Differences)
{
    public bool Ok => Checked && Differences.Count == 0;
}

/// <summary>
/// Paridad V2.1 ↔ V3: con los movimientos migrados hasta el último «Recalcular stock» de la V2.1, la proyección de la
/// V3 debe reproducir exactamente la instantánea del libro (stock, semáforo, salidas de 30 días, cobertura, ranking,
/// orden de las alertas y pedido sugerido). Es la prueba de que la V3 se construyó sobre las reglas de la V2.1.
/// </summary>
public static class V21Parity
{
    public static V21ParityReport Check(V21Workbook wb, V21ImportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(wb);
        ArgumentNullException.ThrowIfNull(plan);
        if (wb.SnapshotSerial is not { } cut)
        {
            return new V21ParityReport(false, 0, 0, 0, ["La V2.1 nunca calculó la instantánea: no hay con qué comparar."]);
        }
        var today = DateOnly.FromDateTime(XlsxTableReader.FromSerial(Math.Floor(cut)));
        var suppliers = wb.Suppliers().Select(s => new ProjectionSupplier(s.Name, s.Index, s.LeadTimeDays, s.Contact ?? "",
            s.Phone ?? "", s.Email ?? "")).ToList();
        var items = wb.Products().Where(p => plan.BySku.ContainsKey(p.Sku)).Select(p => new ProjectionItem(plan.BySku[p.Sku].Variant.Id,
            p.Index, p.Sku, p.Name, p.Category, p.Supplier, p.Unit, p.Active, p.Minimum, p.Maximum, p.UnitCost)).ToList();
        var movements = plan.Movements.Where(m => m.Source.TimestampSerial <= cut)
            .Select(m => new ProjectionMovement(m.VariantId, m.Type.Signed(m.Movement.Quantity), m.Movement.BusinessDate, m.Type.IsSale));
        var v3 = StockProjection.Project(items, movements, wb.AlertMargin, today, suppliers);

        var diffs = new List<string>();
        var bySku = v3.Stock.ToDictionary(r => r.Sku, StringComparer.Ordinal);
        var snapshot = wb.Snapshot();
        foreach (var s in snapshot)
        {
            if (!bySku.TryGetValue(s.Sku, out var r))
            {
                diffs.Add($"{s.Sku}: no está en la V3");
                continue;
            }
            Compare(diffs, s.Sku, "entradas", s.Entries, r.Entries);
            Compare(diffs, s.Sku, "salidas", s.Issues, r.Issues);
            Compare(diffs, s.Sku, "stock", s.Stock, r.Stock);
            Compare(diffs, s.Sku, "salidas 30 d", s.Sales30Days, r.Sales30Days);
            if (s.Status != StockRules.Label(r.Status))
            {
                diffs.Add($"{s.Sku}: estado {s.Status} (V2.1) ≠ {StockRules.Label(r.Status)} (V3)");
            }
            if (s.CoverageDays != r.CoverageDays)
            {
                diffs.Add($"{s.Sku}: cobertura {s.CoverageDays} ≠ {r.CoverageDays}");
            }
            if (s.Rank != r.SalesRank)
            {
                diffs.Add($"{s.Sku}: ranking {s.Rank} ≠ {r.SalesRank}");
            }
        }
        var alerts = wb.SnapshotAlerts();
        if (!alerts.SequenceEqual(v3.Alerts.Select(a => a.Sku)))
        {
            diffs.Add($"16_ALERTAS: orden distinto (V2.1 {string.Join(",", alerts)} · V3 {string.Join(",", v3.Alerts.Select(a => a.Sku))})");
        }
        var order = wb.SnapshotOrder();
        var v3Order = v3.Order.Select(o => (o.Sku, o.QuantityToOrder)).ToList();
        if (order.Count != v3Order.Count || order.Zip(v3Order).Any(p => p.First.Sku != p.Second.Sku || p.First.Quantity != p.Second.QuantityToOrder))
        {
            diffs.Add("18_PEDIDO: líneas o cantidades distintas");
        }
        return new V21ParityReport(true, snapshot.Count, alerts.Count, order.Count, diffs);
    }

    private static void Compare(List<string> diffs, string sku, string what, decimal v21, decimal v3)
    {
        if (Math.Abs(v21 - v3) > 0.000001m)
        {
            diffs.Add($"{sku}: {what} {Quantities.Format(v21)} (V2.1) ≠ {Quantities.Format(v3)} (V3)");
        }
    }
}

/// <summary>Solicitud de migración de un libro V2.1 a una empresa nueva de la V3.</summary>
public sealed record V21ImportRequest(
    string WorkbookPath,
    string TenantCode,
    string AdminEmail,
    string AdminName,
    string AdminPassword,
    string TimeZoneId = "America/La_Paz",
    string? CurrencyCode = null,
    string CountryIso = "BO",
    string CountryName = "Bolivia",
    bool ImportOpenCount = true);

public sealed record V21ImportResult(ProvisionedTenant Tenant, V21ImportReport Report, V21ParityReport Parity);

public sealed class V21ImportException(IReadOnlyList<string> errors)
    : Exception($"La migración se canceló: {errors.Count} problema(s). " + string.Join(" · ", errors.Take(10)))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>Migra un libro colaborativo de la V2.1 a PostgreSQL en UNA transacción (todo o nada).</summary>
public sealed class V21Importer(MinvWriteDbContext db, TenantProvisioner provisioner, IClock clock)
{
    public async Task<V21ImportResult> ImportAsync(V21ImportRequest request, CancellationToken ct = default)
    {
        var wb = V21Workbook.Open(request.WorkbookPath);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var tenant = await provisioner.ProvisionAsync(new ProvisionTenantRequest(request.TenantCode, wb.CompanyName, wb.TaxId,
            request.AdminEmail, request.AdminName, request.AdminPassword, request.CurrencyCode ?? wb.CurrencyCode ?? "BOB",
            request.TimeZoneId, request.CountryIso, request.CountryName, WarehouseName: wb.WarehouseName, AlertMargin: wb.AlertMargin,
            DaysWithoutRotation: wb.DaysWithoutRotation, MinBusinessDate: wb.MinBusinessDate), ct);

        var seeds = new V21Seeds(tenant.TenantId, tenant.AdminUserId, tenant.BranchId, tenant.WarehouseId, tenant.WarehouseCode,
            tenant.PickingLocationTypeId, tenant.DefaultBinId,
            await db.UnitsOfMeasure.ToDictionaryAsync(u => u.Code, StringComparer.OrdinalIgnoreCase, ct),
            tenant.Roles,
            await db.MovementTypes.ToDictionaryAsync(m => m.Code, StringComparer.Ordinal, ct),
            await db.Users.ToDictionaryAsync(u => u.Email, StringComparer.OrdinalIgnoreCase, ct));
        var plan = V21ImportPlanner.Build(wb, seeds, new V21ImportOptions(request.TimeZoneId, request.ImportOpenCount), clock.UtcNow);
        if (plan.Errors.Count > 0)
        {
            throw new V21ImportException(plan.Errors);
        }
        db.AddRange(plan.Entities);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new V21ImportResult(tenant, plan.Report!, V21Parity.Check(wb, plan));
    }
}
