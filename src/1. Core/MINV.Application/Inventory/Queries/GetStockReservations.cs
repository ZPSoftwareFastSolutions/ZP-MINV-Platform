using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;

namespace MINV.Application.Inventory.Queries;

/// <summary>V6 · Unidades reservadas de un producto en el almacén de trabajo (reservas de armados web y del escritorio y
/// reservas de caja): el escritorio las muestra junto al stock (regla S-08) y disponible = existencias − reservado.</summary>
public sealed record ReservedStockRow(string Sku, decimal Reserved);

/// <summary>Productos con unidades reservadas en el almacén de trabajo (o en <see cref="WarehouseCode"/>); los que no tienen
/// reservas no aparecen.</summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetStockReservationsQuery(string? WarehouseCode = null) : IRequest<IReadOnlyList<ReservedStockRow>>;

public sealed class GetStockReservationsHandler(IMinvDbContext db) : IRequestHandler<GetStockReservationsQuery, IReadOnlyList<ReservedStockRow>>
{
    public async Task<IReadOnlyList<ReservedStockRow>> Handle(GetStockReservationsQuery request, CancellationToken ct)
    {
        var lookups = new InventoryLookups(db);
        var warehouse = await lookups.WarehouseAsync(request.WarehouseCode, await lookups.ConfigAsync(ct), ct);
        var bins = lookups.BinIdsOf(warehouse.Id);
        var rows = await (from l in db.Set<StockLevel>()
                          join b in db.Set<Batch>() on l.BatchId equals b.Id
                          join v in db.Set<ProductVariant>() on b.VariantId equals v.Id
                          where bins.Contains(l.BinId) && l.QuantityReserved > 0
                          select new { v.Sku, l.QuantityReserved }).ToListAsync(ct);
        return rows.GroupBy(r => r.Sku, StringComparer.Ordinal)
            .Select(g => new ReservedStockRow(g.Key, Quantities.Round6(g.Sum(r => r.QuantityReserved))))
            .Where(r => r.Reserved > 0)
            .OrderBy(r => r.Sku, StringComparer.Ordinal).ToList();
    }
}
