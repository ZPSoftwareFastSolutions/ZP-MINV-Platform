using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Catalog;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Warehousing;

namespace MINV.Application.Inventory;

/// <summary>Variante con lo que necesitan los casos de uso de inventario.</summary>
public sealed record VariantInfo(ProductVariant Variant, Product Product, UnitRule Unit);

/// <summary>Búsquedas comunes de los casos de uso de inventario (siempre dentro del tenant actual).</summary>
public sealed class InventoryLookups(IMinvDbContext db)
{
    /// <summary>Variante por SKU o, si no hay, por código de barras (lo que envía el escáner del POS).</summary>
    public async Task<VariantInfo> VariantBySkuAsync(string skuOrBarcode, CancellationToken ct)
    {
        var raw = skuOrBarcode.Trim();
        var code = raw.ToUpperInvariant();
        var variantId = await db.Set<ProductVariant>().Where(v => v.Sku == code).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(ct)
                        ?? await db.Set<ProductBarcode>().Where(b => b.Code == raw).Select(b => (Guid?)b.VariantId).FirstOrDefaultAsync(ct)
                        ?? throw new NotFoundException($"El producto {code} no existe en el catálogo (ni como SKU ni como código de barras).");
        var row = await (from v in db.Set<ProductVariant>()
                         join p in db.Set<Product>() on v.ProductId equals p.Id
                         join u in db.Set<UnitOfMeasure>() on p.BaseUnitId equals u.Id
                         where v.Id == variantId
                         select new { v, p, u.Code, u.AllowsDecimals }).FirstAsync(ct);
        return new VariantInfo(row.v, row.p, new UnitRule(row.Code, row.AllowsDecimals));
    }

    public async Task<Bin> BinByCodeAsync(string code, CancellationToken ct)
    {
        var c = code.Trim().ToUpperInvariant();
        var bin = await db.Set<Bin>().FirstOrDefaultAsync(b => b.Code == c, ct)
                  ?? throw new NotFoundException($"La posición {c} no existe.");
        return bin.IsActive ? bin : throw new Domain.Common.DomainException("bin.inactive", $"La posición {c} está inactiva.");
    }

    public async Task<Guid> WarehouseOfBinAsync(Guid binId, CancellationToken ct) =>
        await (from b in db.Set<Bin>()
               join s in db.Set<Shelf>() on b.ShelfId equals s.Id
               join r in db.Set<Rack>() on s.RackId equals r.Id
               join a in db.Set<Aisle>() on r.AisleId equals a.Id
               join z in db.Set<Zone>() on a.ZoneId equals z.Id
               where b.Id == binId
               select z.WarehouseId).FirstAsync(ct);

    public async Task<Warehouse> WarehouseByCodeAsync(string code, CancellationToken ct)
    {
        var c = code.Trim().ToUpperInvariant();
        return await db.Set<Warehouse>().FirstOrDefaultAsync(w => w.Code == c, ct)
               ?? throw new NotFoundException($"El almacén {c} no existe.");
    }

    /// <summary>Lote indicado o, si no se indica, el lote por defecto de la variante (se crea si falta).</summary>
    public async Task<Batch> BatchAsync(ProductVariant variant, string? lotNumber, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(lotNumber))
        {
            var lot = lotNumber.Trim().ToUpperInvariant();
            return await db.Set<Batch>().FirstOrDefaultAsync(b => b.VariantId == variant.Id && b.LotNumber == lot, ct)
                   ?? throw new NotFoundException($"El lote {lot} de {variant.Sku} no existe.");
        }
        var batch = await db.Set<Batch>().FirstOrDefaultAsync(b => b.VariantId == variant.Id && b.IsDefault, ct);
        if (batch is null)
        {
            batch = Batch.CreateDefault(variant.TenantId, variant.Id);
            db.Set<Batch>().Add(batch);
        }
        return batch;
    }

    /// <summary>Existencia (posición + lote); si no existe se abre vacía.</summary>
    public async Task<(StockLevel Level, bool IsNew)> StockLevelAsync(Guid tenantId, Guid binId, Guid batchId, CancellationToken ct)
    {
        var level = await db.Set<StockLevel>().FirstOrDefaultAsync(l => l.BinId == binId && l.BatchId == batchId, ct);
        if (level is not null)
        {
            return (level, false);
        }
        level = StockLevel.Open(tenantId, binId, batchId);
        db.Set<StockLevel>().Add(level);
        return (level, true);
    }

    public async Task<MovementType> MovementTypeAsync(string code, CancellationToken ct)
    {
        var c = code.Trim().ToUpperInvariant();
        return await db.Set<MovementType>().FirstOrDefaultAsync(t => t.Code == c, ct)
               ?? throw new NotFoundException($"El tipo de movimiento {c} no existe.");
    }

    public async Task<CountMovementTypes> CountTypesAsync(CancellationToken ct) =>
        new(await MovementTypeAsync(MovementTypeCodes.InitialBalance, ct),
            await MovementTypeAsync(MovementTypeCodes.AdjustmentIn, ct),
            await MovementTypeAsync(MovementTypeCodes.AdjustmentOut, ct));

    public async Task<TenantConfig> ConfigAsync(CancellationToken ct) =>
        await db.Set<TenantConfig>().FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("La empresa no tiene configuración: aprovisione el tenant.");

    public Task<bool> HasMovementsAsync(Guid stockLevelId, CancellationToken ct) =>
        db.Set<StockMovement>().AnyAsync(m => m.StockLevelId == stockLevelId, ct);
}
