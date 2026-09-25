using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Accounting;
using MINV.Domain.Catalog;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Purchasing;
using MINV.Domain.Warehousing;

namespace MINV.Application.Inventory.Queries;

/// <summary>
/// Stock, alertas y pedido sugerido de un almacén (en la V2.1: 15_STOCK, 16_ALERTAS y 18_PEDIDO). En la V3 no hay
/// «Recalcular stock»: se calcula al consultar, con un GROUP BY en PostgreSQL y las reglas del dominio.
/// </summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetStockProjectionQuery(string? WarehouseCode = null) : IRequest<StockProjectionView>;

public sealed record StockProjectionView(string WarehouseCode, DateOnly Today, StockProjectionResult Result);

public sealed class GetStockProjectionHandler(IMinvDbContext db, IClock clock)
    : IRequestHandler<GetStockProjectionQuery, StockProjectionView>
{
    public async Task<StockProjectionView> Handle(GetStockProjectionQuery request, CancellationToken ct)
    {
        var lookups = new InventoryLookups(db);
        var config = await lookups.ConfigAsync(ct);
        var today = clock.TodayIn(config.TimeZoneId);
        var warehouse = await lookups.WarehouseAsync(request.WarehouseCode, config, ct);
        var wid = warehouse.Id;
        var salesFrom = today.AddDays(-(StockProjection.SalesWindowDays - 1));
        var warehouseBins = lookups.BinIdsOf(wid);

        var aggregates = await (from m in db.Set<StockMovement>()
                                join t in db.Set<MovementType>() on m.MovementTypeId equals t.Id
                                join l in db.Set<StockLevel>() on m.StockLevelId equals l.Id
                                join b in db.Set<Batch>() on l.BatchId equals b.Id
                                where warehouseBins.Contains(l.BinId)
                                group new { m.Quantity, t.StockFactor, t.Domain, m.BusinessDate } by b.VariantId into g
                                select new
                                {
                                    VariantId = g.Key,
                                    Entries = g.Sum(x => x.StockFactor > 0 ? x.Quantity : 0m),
                                    Issues = g.Sum(x => x.StockFactor < 0 ? x.Quantity : 0m),
                                    Last = g.Max(x => x.BusinessDate),
                                    Sales = g.Sum(x => x.Domain == MovementDomain.Sales && x.StockFactor < 0 && x.BusinessDate >= salesFrom
                                        ? x.Quantity
                                        : 0m),
                                    Count = g.Count(),
                                }).ToListAsync(ct);

        var catalog = await (from v in db.Set<ProductVariant>()
                             join p in db.Set<Product>() on v.ProductId equals p.Id
                             join c in db.Set<Category>() on p.CategoryId equals c.Id
                             join u in db.Set<UnitOfMeasure>() on p.BaseUnitId equals u.Id
                             orderby p.Id, v.Id
                             select new
                             {
                                 v.Id,
                                 v.Sku,
                                 Name = v.Name == null ? p.Name : p.Name + " · " + v.Name,
                                 Category = c.Name,
                                 Unit = u.Code,
                                 Active = p.IsActive && v.IsActive,
                                 Supplier = (from ps in db.Set<ProductSupplier>()
                                             join s in db.Set<Supplier>() on ps.SupplierId equals s.Id
                                             where ps.ProductId == p.Id && ps.IsPreferred
                                             select s.LegalName).FirstOrDefault(),
                                 Min = db.Set<ProductStockPolicy>().Where(x => x.VariantId == v.Id && x.WarehouseId == wid)
                                     .Select(x => (decimal?)x.MinQuantity).FirstOrDefault(),
                                 Max = db.Set<ProductStockPolicy>().Where(x => x.VariantId == v.Id && x.WarehouseId == wid)
                                     .Select(x => (decimal?)x.MaxQuantity).FirstOrDefault(),
                                 Cost = db.Set<AverageCostHistory>().Where(x => x.VariantId == v.Id && x.WarehouseId == wid)
                                     .OrderByDescending(x => x.EffectiveAt).Select(x => (decimal?)x.AverageCost).FirstOrDefault(),
                             }).ToListAsync(ct);

        var suppliers = await (from s in db.Set<Supplier>()
                               orderby s.Id
                               select new
                               {
                                   s.LegalName,
                                   s.LeadTimeDays,
                                   Contact = db.Set<SupplierContact>().Where(x => x.SupplierId == s.Id && x.IsPrimary)
                                       .Select(x => new { x.FullName, x.Phone, x.Email }).FirstOrDefault(),
                               }).ToListAsync(ct);

        var items = catalog.Select((c, i) => new ProjectionItem(c.Id, i, c.Sku, c.Name, c.Category, c.Supplier ?? string.Empty,
            c.Unit, c.Active, c.Min ?? 0, c.Max ?? 0, c.Cost ?? 0)).ToList();
        var aggs = aggregates.Select(a => new ProjectionAggregate(a.VariantId, a.Entries, a.Issues, a.Last, a.Sales, a.Count));
        var sups = suppliers.Select((s, i) => new ProjectionSupplier(s.LegalName, i, s.LeadTimeDays, s.Contact?.FullName ?? "",
            s.Contact?.Phone ?? "", s.Contact?.Email ?? "")).ToList();
        var result = StockProjection.ProjectAggregates(items, aggs, config.AlertMargin, today, sups);
        return new StockProjectionView(warehouse.Code, today, result);
    }
}
