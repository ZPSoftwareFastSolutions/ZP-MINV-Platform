using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>Variante del catálogo tal como la necesita la proyección (datos de catálogo, política y costo).</summary>
public sealed record ProjectionItem(
    Guid VariantId, int CatalogIndex, string Sku, string Name, string Category, string Supplier, string Unit,
    bool IsActive, decimal Minimum, decimal Maximum, decimal UnitCost);

/// <summary>Movimiento consolidado con su signo ya aplicado.</summary>
public sealed record ProjectionMovement(Guid VariantId, decimal SignedQuantity, DateOnly BusinessDate, bool IsSale);

/// <summary>Movimientos ya agregados por variante (lo que devuelve un GROUP BY en PostgreSQL).</summary>
public sealed record ProjectionAggregate(Guid VariantId, decimal Entries, decimal Issues, DateOnly? LastMovement,
    decimal Sales30Days, int Movements);

/// <summary>Proveedor (orden de aparición en el catálogo de proveedores, días de entrega y contacto).</summary>
public sealed record ProjectionSupplier(string Name, int Order, int? LeadTimeDays, string Contact, string Phone, string Email);

/// <summary>Fila de stock (equivale a 15_STOCK de la V2.1, columna por columna).</summary>
public sealed record StockRow(
    Guid VariantId, string Sku, string Name, string Category, string Supplier, string Unit, bool IsActive,
    decimal Entries, decimal Issues, decimal Stock, decimal Minimum, decimal Maximum, decimal? Level,
    StockStatusCode Status, decimal UnitCost, decimal InventoryValue, DateOnly? LastMovement, int? DaysWithoutMovement,
    decimal Sales30Days, int? CoverageDays, int? SalesRank);

/// <summary>Alerta priorizada (equivale a 16_ALERTAS).</summary>
public sealed record AlertRow(
    int Position, StockStatusCode Status, string Sku, string Name, string Category, string Supplier, decimal Stock,
    decimal Minimum, decimal Maximum, string Unit, decimal Shortfall, decimal SuggestedQuantity, DateOnly? LastMovement);

/// <summary>Línea del pedido sugerido (equivale a 18_PEDIDO).</summary>
public sealed record SuggestedOrderLine(
    string Supplier, string Sku, string Name, string Unit, StockStatusCode Status, decimal Stock, decimal Minimum,
    decimal Maximum, decimal QuantityToOrder, decimal UnitCost, decimal Subtotal, int? LeadTimeDays,
    DateOnly? EstimatedDelivery, string Contact, string Phone, string Email);

public sealed record StockProjectionResult(
    IReadOnlyList<StockRow> Stock, IReadOnlyList<AlertRow> Alerts, IReadOnlyList<SuggestedOrderLine> Order, int Movements);

/// <summary>
/// Proyección de lectura de M-INV: stock, semáforo, valor, salidas de 30 días, cobertura, ranking, alertas y pedido
/// sugerido. Es el algoritmo de <c>RecalcularStock.ts</c> y de <c>tools/minv2/lectura.py</c> de la V2.1 con la misma
/// aritmética (<see cref="Quantities.Round6"/>): la paridad se prueba con el libro de la V2.1.
/// </summary>
public static class StockProjection
{
    public const int SalesWindowDays = 30;
    public const string NoSupplier = "(Sin proveedor)";

    /// <summary>Proyección a partir de movimientos individuales (agrega en memoria).</summary>
    public static StockProjectionResult Project(IReadOnlyList<ProjectionItem> items, IEnumerable<ProjectionMovement> movements,
        decimal alertMargin, DateOnly today, IReadOnlyList<ProjectionSupplier> suppliers)
    {
        ArgumentNullException.ThrowIfNull(movements);
        var from = today.AddDays(-(SalesWindowDays - 1));
        var agg = new Dictionary<Guid, (decimal E, decimal S, DateOnly? L, decimal V, int N)>();
        foreach (var m in movements)
        {
            var a = agg.GetValueOrDefault(m.VariantId);
            if (m.SignedQuantity >= 0)
            {
                a.E += m.SignedQuantity;
            }
            else
            {
                a.S -= m.SignedQuantity;
            }
            if (a.L is null || m.BusinessDate > a.L)
            {
                a.L = m.BusinessDate;
            }
            if (m.IsSale && m.BusinessDate >= from)
            {
                a.V -= m.SignedQuantity;
            }
            a.N++;
            agg[m.VariantId] = a;
        }
        var aggregates = agg.Select(kv => new ProjectionAggregate(kv.Key, kv.Value.E, kv.Value.S, kv.Value.L, kv.Value.V, kv.Value.N));
        return ProjectAggregates(items, aggregates, alertMargin, today, suppliers);
    }

    /// <summary>Proyección a partir de agregados por variante (entradas, salidas, último movimiento y ventas de 30 días
    /// en la ventana <c>[hoy − 29, hoy]</c>).</summary>
    public static StockProjectionResult ProjectAggregates(IReadOnlyList<ProjectionItem> items, IEnumerable<ProjectionAggregate> aggregates,
        decimal alertMargin, DateOnly today, IReadOnlyList<ProjectionSupplier> suppliers)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(aggregates);
        ArgumentNullException.ThrowIfNull(suppliers);
        var byVariant = new Dictionary<Guid, ProjectionAggregate>();
        var count = 0;
        foreach (var a in aggregates)
        {
            byVariant[a.VariantId] = a;
            count += a.Movements;
        }
        var supplierByName = new Dictionary<string, ProjectionSupplier>(StringComparer.Ordinal);
        foreach (var s in suppliers.OrderBy(s => s.Order))
        {
            supplierByName.TryAdd(s.Name.Trim(), s);
        }

        var rows = new List<StockRow>();
        var ranking = new List<(decimal Sales, int Index, int Row)>();
        var alerts = new List<((int, int, int) Key, AlertRow Row)>();
        var order = new List<((int, int, int) Key, SuggestedOrderLine Line)>();
        foreach (var it in items)
        {
            var ag = byVariant.GetValueOrDefault(it.VariantId);
            var e = Quantities.Round6(ag?.Entries ?? 0);
            var s = Quantities.Round6(ag?.Issues ?? 0);
            var stock = Quantities.Round6(e - s);
            var status = StockRules.Evaluate(stock, it.Minimum, it.Maximum, it.IsActive, alertMargin);
            decimal? level = it.Maximum > 0 ? Quantities.Round6(Math.Max(0, stock) / it.Maximum) : null;
            var lastDate = ag?.LastMovement;
            int? days = lastDate is { } l ? today.DayNumber - l.DayNumber : null;
            var v30 = Quantities.Round6(ag?.Sales30Days ?? 0);
            rows.Add(new StockRow(it.VariantId, it.Sku, it.Name, it.Category, it.Supplier, it.Unit, it.IsActive, e, s,
                stock, it.Minimum, it.Maximum, level, status, it.UnitCost, Quantities.Round6(Math.Max(0, stock) * it.UnitCost),
                lastDate, days, v30, StockRules.CoverageDays(stock, v30), null));
            if (v30 > 0)
            {
                ranking.Add((v30, it.CatalogIndex, rows.Count - 1));
            }
            if (it.IsActive && StockRules.IsAlert(status))
            {
                var coverage = it.Minimum > 0 ? Math.Max(0, stock) / it.Minimum : 0m;
                var key = (IndexOf(StockRules.AlertOrder, status), (int)Math.Min(99, decimal.Floor(coverage * 10)), it.CatalogIndex);
                alerts.Add((key, new AlertRow(0, status, it.Sku, it.Name, it.Category, it.Supplier, stock, it.Minimum,
                    it.Maximum, it.Unit, Quantities.Round6(Math.Max(0, it.Minimum - stock)),
                    StockRules.SuggestedQuantity(status, stock, it.Minimum, it.Maximum), lastDate)));
            }
            if (it.IsActive && StockRules.NeedsReorder(status))
            {
                var toOrder = Quantities.Round6(Math.Max(0, StockRules.ReorderTarget(it.Minimum, it.Maximum) - Math.Max(0, stock)));
                if (toOrder > 0)
                {
                    var name = it.Supplier.Trim();
                    supplierByName.TryGetValue(name, out var sup);
                    var lead = sup?.LeadTimeDays;
                    var key = (sup?.Order ?? 999, IndexOf(StockRules.ReorderOrder, status), it.CatalogIndex);
                    order.Add((key, new SuggestedOrderLine(name.Length > 0 ? name : NoSupplier, it.Sku, it.Name, it.Unit,
                        status, stock, it.Minimum, it.Maximum, toOrder, it.UnitCost, Quantities.Round6(toOrder * it.UnitCost),
                        lead, lead is { } days2 ? today.AddDays(days2) : null, sup?.Contact ?? "", sup?.Phone ?? "",
                        sup?.Email ?? "")));
                }
            }
        }
        var rank = 0;
        foreach (var (_, _, row) in ranking.OrderByDescending(r => r.Sales).ThenBy(r => r.Index))
        {
            rows[row] = rows[row] with { SalesRank = ++rank };
        }
        var position = 0;
        var alertRows = alerts.OrderBy(a => a.Key).Select(a => a.Row with { Position = ++position }).ToList();
        var orderLines = order.OrderBy(o => o.Key).Select(o => o.Line).ToList();
        return new StockProjectionResult(rows, alertRows, orderLines, count);
    }

    private static int IndexOf(IReadOnlyList<StockStatusCode> list, StockStatusCode status)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == status)
            {
                return i;
            }
        }
        return list.Count;
    }
}
