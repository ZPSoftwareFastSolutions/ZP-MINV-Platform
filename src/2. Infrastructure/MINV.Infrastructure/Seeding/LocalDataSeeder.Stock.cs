using Microsoft.EntityFrameworkCore;
using MINV.Application.Sales;
using MINV.Domain.Inventory;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Seeding.Tecnologia;

namespace MINV.Infrastructure.Seeding;

public sealed partial class LocalDataSeeder
{
    /// <summary>
    /// V4.2 · Lo que la carga sabe del inventario para armar documentos que el sistema acepte: disponible por (almacén, SKU) y
    /// las series EN STOCK de cada almacén (lo que un cajero escanea). Se RELEE de la base (la verdad) varias veces al día
    /// —después de las recepciones y de las devoluciones— y entre lecturas descuenta lo que toma cada venta, transferencia o
    /// reposición; si el sistema rechaza el documento, lo tomado se devuelve. Nunca decide nada: el poka-yoke y las reglas
    /// de series (T-02) siguen siendo las del dominio.
    /// </summary>
    private sealed class SeedStock(MinvWriteDbContext db, TechSeedCatalog catalog)
    {
        private readonly Dictionary<(string Warehouse, string Sku), decimal> _available = new();
        private readonly Dictionary<(string Warehouse, string Sku), List<string>> _serials = new();
        private Dictionary<Guid, (string Bin, string Warehouse)> _bins = [];

        /// <summary>Relee de la base el disponible (existencia − reservas) y las series en stock de cada almacén.</summary>
        public async Task RefreshAsync(CancellationToken ct)
        {
            db.ChangeTracker.Clear();
            await LoadBinsAsync(ct);
            var warehouses = _bins.ToDictionary(x => x.Key, x => x.Value.Warehouse);
            var levels = await (from l in db.StockLevels
                                join b in db.Batches on l.BatchId equals b.Id
                                join v in db.ProductVariants on b.VariantId equals v.Id
                                select new { l.BinId, v.Sku, Available = l.QuantityOnHand - l.QuantityReserved }).AsNoTracking().ToListAsync(ct);
            var units = await (from s in db.SerialNumbers
                               where s.Status == SerialNumberStatus.InStock
                               join l in db.StockLevels on s.StockLevelId equals (Guid?)l.Id
                               join v in db.ProductVariants on s.VariantId equals v.Id
                               select new { l.BinId, v.Sku, s.Serial }).AsNoTracking().ToListAsync(ct);
            _available.Clear();
            _serials.Clear();
            foreach (var level in levels)
            {
                var key = (warehouses[level.BinId], level.Sku);
                _available[key] = _available.GetValueOrDefault(key) + level.Available;
            }
            foreach (var unit in units.OrderBy(u => u.Serial, StringComparer.Ordinal))
            {
                var key = (warehouses[unit.BinId], unit.Sku);
                if (!_serials.TryGetValue(key, out var list))
                {
                    _serials[key] = list = [];
                }
                list.Add(unit.Serial);
            }
        }

        public decimal Available(string warehouse, string sku) => _available.GetValueOrDefault((warehouse, sku));

        /// <summary>Existencia y disponible de un producto por posición de un almacén, leídos de la base (lo mismo que muestra
        /// la ficha del producto, sin cargar su imagen: en memoria cada lectura de las imágenes copia la tabla completa).</summary>
        public async Task<IReadOnlyList<(string BinCode, decimal OnHand, decimal Available)>> BinsAsync(string warehouse, string sku, CancellationToken ct)
        {
            var levels = await (from l in db.StockLevels
                                join b in db.Batches on l.BatchId equals b.Id
                                join v in db.ProductVariants on b.VariantId equals v.Id
                                where v.Sku == sku
                                select new { l.BinId, l.QuantityOnHand, l.QuantityReserved }).AsNoTracking().ToListAsync(ct);
            if (levels.Any(l => !_bins.ContainsKey(l.BinId)))
            {
                await LoadBinsAsync(ct);
            }
            return levels.Where(l => _bins[l.BinId].Warehouse == warehouse).GroupBy(l => _bins[l.BinId].Bin, StringComparer.Ordinal)
                .Select(g => (g.Key, g.Sum(l => l.QuantityOnHand), g.Sum(l => l.QuantityOnHand - l.QuantityReserved))).ToList();
        }

        /// <summary>Posiciones de todos los almacenes (código de la posición y del almacén).</summary>
        private async Task LoadBinsAsync(CancellationToken ct) =>
            _bins = await (from bin in db.Bins
                           join s in db.Shelves on bin.ShelfId equals s.Id
                           join r in db.Racks on s.RackId equals r.Id
                           join a in db.Aisles on r.AisleId equals a.Id
                           join z in db.Zones on a.ZoneId equals z.Id
                           join w in db.Warehouses on z.WarehouseId equals w.Id
                           select new { bin.Id, Bin = bin.Code, Warehouse = w.Code }).AsNoTracking()
                .ToDictionaryAsync(x => x.Id, x => (x.Bin, x.Warehouse), ct);

        /// <summary>Series en stock de un producto en un almacén (las que se pueden escanear).</summary>
        public int SerialCount(string warehouse, string sku) => _serials.TryGetValue((warehouse, sku), out var list) ? list.Count : 0;

        /// <summary>Toma <paramref name="quantity"/> unidades (y sus series al azar si el producto es serializado) si las hay.</summary>
        public bool TryTake(string warehouse, TechProduct product, int quantity, Random rng, out IReadOnlyList<string>? serials)
        {
            serials = null;
            var key = (warehouse, product.Sku);
            if (quantity <= 0 || _available.GetValueOrDefault(key) < quantity)
            {
                return false;
            }
            if (product.TracksSerials)
            {
                if (!_serials.TryGetValue(key, out var list) || list.Count < quantity)
                {
                    return false;
                }
                var taken = new List<string>(quantity);
                for (var i = 0; i < quantity; i++)
                {
                    var index = rng.Next(list.Count);
                    taken.Add(list[index]);
                    list.RemoveAt(index);
                }
                serials = taken;
            }
            _available[key] -= quantity;
            return true;
        }

        /// <summary>Devuelve lo tomado para una venta que el sistema rechazó.</summary>
        public void GiveBack(string warehouse, IEnumerable<SaleLineInput> lines)
        {
            foreach (var line in lines)
            {
                GiveBack(warehouse, line.Sku, line.Quantity, line.Serials);
            }
        }

        public void GiveBack(string warehouse, string sku, decimal quantity, IReadOnlyList<string>? serials)
        {
            var key = (warehouse, sku);
            _available[key] = _available.GetValueOrDefault(key) + quantity;
            if (serials is { Count: > 0 })
            {
                if (!_serials.TryGetValue(key, out var list))
                {
                    _serials[key] = list = [];
                }
                list.AddRange(serials);
            }
        }

        /// <summary>¿El producto está en el catálogo de la carga? (los SKU de la base siempre lo están)</summary>
        public TechProduct Product(string sku) => catalog.Product(sku);
    }
}
