using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Warehousing;

namespace MINV.Application.Inventory.PhysicalCounts;

// ------------------------------------------------------------------------------------------------ planilla de la toma abierta
/// <summary>La toma física abierta del almacén (o null si no hay): lo contado frente al stock exacto de este momento
/// (en la V2.1: 13_CONTEO con su diferencia en vivo).</summary>
[RequiresPermission(PermissionCodes.PhysicalCountRecord)]
public sealed record GetOpenPhysicalCountQuery(string? WarehouseCode = null) : IRequest<PhysicalCountSheet?>;

public sealed record PhysicalCountSheet(Guid Id, string Number, DateOnly CountDate, string WarehouseCode, string? Notes,
    IReadOnlyList<PhysicalCountSheetLine> Lines);

public sealed record PhysicalCountSheetLine(string Sku, string Name, string Unit, string BinCode, string LotNumber, decimal SystemQuantity,
    decimal CountedQuantity, decimal Difference, string? CountedBy, DateTimeOffset CountedAt);

public sealed class GetOpenPhysicalCountHandler(IMinvDbContext db) : IRequestHandler<GetOpenPhysicalCountQuery, PhysicalCountSheet?>
{
    public async Task<PhysicalCountSheet?> Handle(GetOpenPhysicalCountQuery request, CancellationToken ct)
    {
        var lookups = new InventoryLookups(db);
        var warehouse = await lookups.WarehouseAsync(request.WarehouseCode, await lookups.ConfigAsync(ct), ct);
        var count = await db.Set<PhysicalCount>().Where(c => c.WarehouseId == warehouse.Id && c.Status == PhysicalCountStatus.Open)
            .OrderByDescending(c => c.CountDate).FirstOrDefaultAsync(ct);
        if (count is null)
        {
            return null;
        }
        var lines = await (from line in db.Set<PhysicalCountLine>()
                           join l in db.Set<StockLevel>() on line.StockLevelId equals l.Id
                           join b in db.Set<Batch>() on l.BatchId equals b.Id
                           join v in db.Set<ProductVariant>() on b.VariantId equals v.Id
                           join p in db.Set<Product>() on v.ProductId equals p.Id
                           join un in db.Set<UnitOfMeasure>() on p.BaseUnitId equals un.Id
                           join bin in db.Set<Bin>() on l.BinId equals bin.Id
                           join u in db.Set<User>() on line.CountedByUserId equals u.Id into users
                           from u in users.DefaultIfEmpty()
                           where line.PhysicalCountId == count.Id
                           orderby line.CountedAt descending
                           select new
                           {
                               v.Sku, Name = v.Name == null ? p.Name : p.Name + " · " + v.Name, Unit = un.Code, Bin = bin.Code, b.LotNumber,
                               l.QuantityOnHand, line.CountedQuantity, By = u == null ? null : u.DisplayName, line.CountedAt,
                           }).ToListAsync(ct);
        return new PhysicalCountSheet(count.Id, count.Number, count.CountDate, warehouse.Code, count.Notes,
            lines.Select(x => new PhysicalCountSheetLine(x.Sku, x.Name, x.Unit, x.Bin, x.LotNumber, x.QuantityOnHand, x.CountedQuantity,
                Quantities.Round6(x.CountedQuantity - x.QuantityOnHand), x.By, x.CountedAt)).ToList());
    }
}

// ------------------------------------------------------------------------------------------------ quitar un conteo
/// <summary>Quita lo contado de un producto en una posición (se contó por error) mientras la toma sigue abierta.</summary>
[RequiresPermission(PermissionCodes.PhysicalCountRecord)]
public sealed record RemoveCountCommand(Guid PhysicalCountId, string Sku, string BinCode, string? LotNumber = null)
    : IRequest<bool>, IAuditableRequest
{
    public object AuditDetails => new { PhysicalCountId, Sku, BinCode, LotNumber };
}

public sealed class RemoveCountHandler(IMinvDbContext db) : IRequestHandler<RemoveCountCommand, bool>
{
    public async Task<bool> Handle(RemoveCountCommand request, CancellationToken ct)
    {
        var lookups = new InventoryLookups(db);
        var count = await db.Set<PhysicalCount>().Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == request.PhysicalCountId, ct)
                    ?? throw new NotFoundException("La toma física no existe.");
        var item = await lookups.VariantBySkuAsync(request.Sku, ct);
        var bin = await lookups.BinByCodeAsync(request.BinCode, ct);
        var lot = request.LotNumber?.Trim().ToUpperInvariant();
        var levelIds = await (from l in db.Set<StockLevel>()
                              join b in db.Set<Batch>() on l.BatchId equals b.Id
                              where l.BinId == bin.Id && b.VariantId == item.Variant.Id && (lot == null ? b.IsDefault : b.LotNumber == lot)
                              select l.Id).ToListAsync(ct);
        var existed = count.Lines.Any(l => levelIds.Contains(l.StockLevelId));
        foreach (var id in levelIds)
        {
            count.RemoveCount(id);
        }
        await db.SaveChangesAsync(ct);
        return existed;
    }
}

// ------------------------------------------------------------------------------------------------ anular
/// <summary>Anula la toma física abierta sin generar ajustes (p. ej. se abrió por error).</summary>
[RequiresPermission(PermissionCodes.PhysicalCountPost)]
public sealed record CancelPhysicalCountCommand(Guid PhysicalCountId) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { PhysicalCountId };
}

public sealed class CancelPhysicalCountHandler(IMinvDbContext db) : IRequestHandler<CancelPhysicalCountCommand, string>
{
    public async Task<string> Handle(CancelPhysicalCountCommand request, CancellationToken ct)
    {
        var count = await db.Set<PhysicalCount>().FirstOrDefaultAsync(c => c.Id == request.PhysicalCountId, ct)
                    ?? throw new NotFoundException("La toma física no existe.");
        count.Cancel();
        await db.SaveChangesAsync(ct);
        return $"✔ Toma física {count.Number} anulada: no se generaron ajustes.";
    }
}
