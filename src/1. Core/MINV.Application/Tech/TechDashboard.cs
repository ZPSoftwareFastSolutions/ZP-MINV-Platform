using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Inventory;
using MINV.Domain.Catalog;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Domain.Service;

namespace MINV.Application.Tech;

/// <summary>
/// V4.2 · Tablero de la tienda de tecnología. Es liviano y acotado (los últimos días y las sucursales visibles, filtros
/// globales de EF Core): va por el modelo de escritura; los reportes pesados de gerencia siguen en el modelo de lectura
/// (regla B-14). Las ventas son las facturas vigentes del período (sin restar devoluciones).
/// </summary>
public sealed class GetTechDashboardHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetTechDashboardQuery, TechDashboardView>
{
    private const int Top = 5;

    public async Task<TechDashboardView> Handle(GetTechDashboardQuery request, CancellationToken ct)
    {
        var config = await new InventoryLookups(db).ConfigAsync(ct);
        var today = clock.TodayIn(config.TimeZoneId);
        var start = today.AddDays(-Math.Clamp(request.Days, 1, 366) + 1);
        var reader = new TechCatalogReader(db);

        // Ventas del período por producto
        var sales = await (from l in db.Set<SalesOrderLine>()
                           join o in db.Set<SalesOrder>() on l.SalesOrderId equals o.Id
                           join i in db.Set<Invoice>() on o.Id equals i.SalesOrderId
                           join v in db.Set<ProductVariant>() on l.VariantId equals v.Id
                           join p in db.Set<Product>() on v.ProductId equals p.Id
                           where i.Status == InvoiceStatus.Issued && o.OrderDate >= start && o.OrderDate <= today
                           select new { ProductId = p.Id, p.Name, p.CategoryId, l.Quantity, l.UnitPrice, l.DiscountPercent }).ToListAsync(ct);
        var lines = sales.Select(s => (s.ProductId, s.Name, s.CategoryId, s.Quantity,
            Amount: decimal.Round(s.Quantity * s.UnitPrice * (1 - s.DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero))).ToList();

        // Categoría raíz de cada categoría (tabla de clausura: el ancestro más lejano)
        var roots = (await db.Set<CategoryHierarchy>().Select(h => new { h.AncestorId, h.DescendantId, h.Depth }).ToListAsync(ct))
            .GroupBy(h => h.DescendantId).ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.Depth).First().AncestorId);
        var categories = await db.Set<Category>().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        string RootName(Guid categoryId) => categories.GetValueOrDefault(roots.GetValueOrDefault(categoryId, categoryId), "?");

        var byCategory = lines.GroupBy(l => RootName(l.CategoryId))
            .Select(g => new NamedAmount(g.Key, g.Sum(x => x.Amount), g.Sum(x => x.Quantity))).OrderByDescending(x => x.Amount).ToList();
        var soldIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var platforms = (await reader.ValuesAsync(soldIds, null, ct)).Where(v => TechSpecCodes.IsPlatform(v.Definition.Code))
            .GroupBy(v => v.ProductId).ToDictionary(g => g.Key, g => g.Select(v => v.Raw).Distinct().ToList());
        var byPlatform = lines.SelectMany(l => platforms.GetValueOrDefault(l.ProductId, []).Select(p => (Platform: p, l.Amount, l.Quantity)))
            .GroupBy(x => x.Platform).Select(g => new NamedAmount(g.Key, g.Sum(x => x.Amount), g.Sum(x => x.Quantity)))
            .OrderByDescending(x => x.Amount).ToList();
        async Task<IReadOnlyList<NamedAmount>> TopOfAsync(string categoryCode)
        {
            var category = await db.Set<Category>().FirstOrDefaultAsync(c => c.Code == categoryCode.Trim().ToUpperInvariant(), ct);
            if (category is null)
            {
                return [];
            }
            var subtree = (await reader.SubtreeAsync(category.Id, ct)).ToHashSet();
            return lines.Where(l => subtree.Contains(l.CategoryId)).GroupBy(l => l.Name)
                .Select(g => new NamedAmount(g.Key, g.Sum(x => x.Amount), g.Sum(x => x.Quantity)))
                .OrderByDescending(x => x.Quantity).ThenByDescending(x => x.Amount).Take(Top).ToList();
        }

        // Casos RMA abiertos
        var claims = await db.Set<WarrantyClaim>().Where(c => c.Status != WarrantyClaimStatus.Delivered)
            .Select(c => new { c.Status, c.IsInWarranty }).ToListAsync(ct);
        var claimsByStatus = claims.GroupBy(c => c.Status).OrderBy(g => g.Key)
            .Select(g => new NamedCount(WarrantyClaim.Describe(g.Key), g.Count())).ToList();

        // Series en stock en las sucursales visibles, por categoría raíz, y variantes serializadas cuyo stock no coincide
        var inStock = await (from s in db.Set<SerialNumber>()
                             join l in db.Set<StockLevel>() on s.StockLevelId equals (Guid?)l.Id
                             join v in db.Set<ProductVariant>() on s.VariantId equals v.Id
                             join p in db.Set<Product>() on v.ProductId equals p.Id
                             where s.Status == SerialNumberStatus.InStock || s.Status == SerialNumberStatus.Reserved
                             select new { l.BranchId, s.VariantId, p.CategoryId }).ToListAsync(ct);
        var serialsByCategory = inStock.GroupBy(s => RootName(s.CategoryId)).Select(g => new NamedCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count).ToList();
        var stock = await (from l in db.Set<StockLevel>()
                           join b in db.Set<Batch>() on l.BatchId equals b.Id
                           join v in db.Set<ProductVariant>() on b.VariantId equals v.Id
                           join p in db.Set<Product>() on v.ProductId equals p.Id
                           where p.TrackingMode == TrackingMode.Serial
                           group l.QuantityOnHand by new { l.BranchId, b.VariantId } into g
                           select new { g.Key.BranchId, g.Key.VariantId, OnHand = g.Sum() }).ToListAsync(ct);
        var serialCounts = inStock.GroupBy(s => (s.BranchId, s.VariantId)).ToDictionary(g => g.Key, g => g.Count());
        var breaches = stock.Count(s => s.OnHand != serialCounts.GetValueOrDefault((s.BranchId, s.VariantId)))
                       + serialCounts.Keys.Count(k => !stock.Any(s => s.BranchId == k.BranchId && s.VariantId == k.VariantId));

        // Armados: cotizaciones vigentes y vendidos en el período
        var builds = await db.Set<PcBuild>().Include(b => b.Lines)
            .Where(b => b.Status == PcBuildStatus.Quoted || b.Status == PcBuildStatus.Sold || b.Status == PcBuildStatus.Reserved).ToListAsync(ct);
        // V6 · Reservas web vigentes (reservadas y no vencidas): cantidad y total en Bs
        var now = clock.UtcNow;
        var webReservations = builds.Where(b => b.Status == PcBuildStatus.Reserved && b.Channel == PcBuildChannel.Web && !b.IsReservationExpired(now)).ToList();
        var soldInvoices = builds.Where(b => b.InvoiceId is not null).Select(b => b.InvoiceId!.Value).ToList();
        var inPeriod = (await (from i in db.Set<Invoice>()
                               join o in db.Set<SalesOrder>() on i.SalesOrderId equals o.Id
                               where soldInvoices.Contains(i.Id) && o.OrderDate >= start && o.OrderDate <= today
                               select i.Id).ToListAsync(ct)).ToHashSet();
        var quotes = builds.Where(b => b.Status is PcBuildStatus.Quoted or PcBuildStatus.Reserved && !b.IsExpiredOn(today)).ToList();
        var sold = builds.Where(b => b.Status == PcBuildStatus.Sold && inPeriod.Contains(b.InvoiceId!.Value)).ToList();

        return new TechDashboardView(byCategory, byPlatform, await TopOfAsync(request.GpuCategoryCode), await TopOfAsync(request.ConsoleCategoryCode),
            claimsByStatus, serialsByCategory, inStock.Count, claims.Count, claims.Count(c => !c.IsInWarranty), quotes.Count, quotes.Sum(b => b.Total),
            sold.Count, sold.Sum(b => b.Total), breaches, webReservations.Count, webReservations.Sum(b => b.Total));
    }
}
