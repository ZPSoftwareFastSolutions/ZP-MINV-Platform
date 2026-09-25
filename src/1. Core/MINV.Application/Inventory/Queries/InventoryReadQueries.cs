using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Accounting;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Purchasing;
using MINV.Domain.Warehousing;

namespace MINV.Application.Inventory.Queries;

// ------------------------------------------------------------------------------------------------ empresa de la sesión
/// <summary>Empresa, moneda, almacén de trabajo y «hoy» en su zona horaria (encabezado y formatos del cliente).</summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetWorkspaceQuery : IRequest<WorkspaceInfo>;

public sealed record WorkspaceInfo(string TenantCode, string CompanyName, string? TaxId, string CurrencyCode, string CurrencySymbol,
    int CurrencyDecimals, string WarehouseCode, string WarehouseName, string TimeZoneId, DateOnly Today, decimal AlertMargin,
    int DaysWithoutRotation);

public sealed class GetWorkspaceHandler(IMinvDbContext db, ITenantContext tenant, IClock clock) : IRequestHandler<GetWorkspaceQuery, WorkspaceInfo>
{
    public async Task<WorkspaceInfo> Handle(GetWorkspaceQuery request, CancellationToken ct)
    {
        var lookups = new InventoryLookups(db);
        var config = await lookups.ConfigAsync(ct);
        var company = await db.Set<Tenant>().FirstAsync(t => t.Id == tenant.TenantId, ct);
        var currency = await db.Set<Currency>().FirstAsync(c => c.Id == config.DefaultCurrencyId, ct);
        var warehouse = await lookups.WarehouseAsync(null, config, ct);
        return new WorkspaceInfo(company.Code, company.LegalName, company.TaxId, currency.Code, currency.Symbol, currency.DecimalPlaces,
            warehouse.Code, warehouse.Name, config.TimeZoneId, clock.TodayIn(config.TimeZoneId), config.AlertMargin,
            config.DaysWithoutRotation);
    }
}

// ------------------------------------------------------------------------------------------------ catálogo para búsquedas
/// <summary>Catálogo compacto para buscar y autocompletar por SKU, nombre o código de barras (el operador elige de la
/// lista; no escribe IDs: regla R-05 de la V1).</summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetProductLookupQuery(bool IncludeInactive = false) : IRequest<IReadOnlyList<ProductLookupItem>>;

public sealed record ProductLookupItem(Guid VariantId, string Sku, string Name, string Category, string Unit, bool AllowsDecimals,
    bool IsActive, IReadOnlyList<string> Barcodes, string? PrimaryBin);

public sealed class GetProductLookupHandler(IMinvDbContext db) : IRequestHandler<GetProductLookupQuery, IReadOnlyList<ProductLookupItem>>
{
    public async Task<IReadOnlyList<ProductLookupItem>> Handle(GetProductLookupQuery request, CancellationToken ct)
    {
        var all = request.IncludeInactive;
        var rows = await (from v in db.Set<ProductVariant>()
                          join p in db.Set<Product>() on v.ProductId equals p.Id
                          join c in db.Set<Category>() on p.CategoryId equals c.Id
                          join u in db.Set<UnitOfMeasure>() on p.BaseUnitId equals u.Id
                          where all || (p.IsActive && v.IsActive)
                          orderby v.Sku
                          select new
                          {
                              v.Id, v.Sku, Name = v.Name == null ? p.Name : p.Name + " · " + v.Name, Category = c.Name, Unit = u.Code,
                              u.AllowsDecimals, Active = p.IsActive && v.IsActive,
                          }).ToListAsync(ct);
        var barcodes = (await db.Set<ProductBarcode>().Select(b => new { b.VariantId, b.Code }).ToListAsync(ct))
            .ToLookup(b => b.VariantId, b => b.Code);
        var bins = (await (from a in db.Set<BinAssignment>()
                           join b in db.Set<Bin>() on a.BinId equals b.Id
                           select new { a.VariantId, b.Code, a.IsPrimaryPick }).ToListAsync(ct))
            .GroupBy(b => b.VariantId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.IsPrimaryPick).ThenBy(b => b.Code, StringComparer.Ordinal).First().Code);
        return rows.Select(r => new ProductLookupItem(r.Id, r.Sku, r.Name, r.Category, r.Unit, r.AllowsDecimals, r.Active,
            barcodes[r.Id].ToList(), bins.GetValueOrDefault(r.Id))).ToList();
    }
}

// ------------------------------------------------------------------------------------------------ posiciones
/// <summary>Posiciones activas (para elegir dónde se registra o se cuenta).</summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetBinsQuery(string? WarehouseCode = null) : IRequest<IReadOnlyList<BinItem>>;

public sealed record BinItem(string Code, string Zone, string WarehouseCode);

public sealed class GetBinsHandler(IMinvDbContext db) : IRequestHandler<GetBinsQuery, IReadOnlyList<BinItem>>
{
    public async Task<IReadOnlyList<BinItem>> Handle(GetBinsQuery request, CancellationToken ct)
    {
        var code = request.WarehouseCode?.Trim().ToUpperInvariant();
        return await (from bin in db.Set<Bin>()
                      join s in db.Set<Shelf>() on bin.ShelfId equals s.Id
                      join r in db.Set<Rack>() on s.RackId equals r.Id
                      join a in db.Set<Aisle>() on r.AisleId equals a.Id
                      join z in db.Set<Zone>() on a.ZoneId equals z.Id
                      join w in db.Set<Warehouse>() on z.WarehouseId equals w.Id
                      where bin.IsActive && (code == null || w.Code == code)
                      orderby bin.Code
                      select new BinItem(bin.Code, z.Name, w.Code)).ToListAsync(ct);
    }
}

// ------------------------------------------------------------------------------------------------ tipos de movimiento
/// <summary>Tipos de movimiento con su signo y dominio (la pantalla de registro muestra solo los que el rol permite).</summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetMovementTypesQuery : IRequest<IReadOnlyList<MovementTypeItem>>;

public sealed record MovementTypeItem(string Code, string Name, string? Description, short StockFactor, MovementDomain Domain,
    bool RequiresNotes, bool IsInitialBalance);

public sealed class GetMovementTypesHandler(IMinvDbContext db) : IRequestHandler<GetMovementTypesQuery, IReadOnlyList<MovementTypeItem>>
{
    public async Task<IReadOnlyList<MovementTypeItem>> Handle(GetMovementTypesQuery request, CancellationToken ct) =>
        await db.Set<MovementType>().OrderBy(t => t.Id)
            .Select(t => new MovementTypeItem(t.Code, t.Name, t.Description, t.StockFactor, t.Domain, t.RequiresNotes, t.IsInitialBalance))
            .ToListAsync(ct);
}

// ------------------------------------------------------------------------------------------------ ficha y kardex
/// <summary>
/// Ficha de un producto en el almacén de trabajo (en la V1: 17_KARDEX; en la V2.1: 17_CONSULTA): datos, semáforo,
/// existencias por posición y lote, y el kardex (movimientos con saldo acumulado, del más reciente al más antiguo).
/// </summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetProductCardQuery(string SkuOrBarcode, int Take = 200) : IRequest<ProductCard>;

public sealed record ProductCard(Guid VariantId, string Sku, string Name, string Category, string Unit, bool AllowsDecimals,
    bool IsActive, string? Supplier, IReadOnlyList<string> Barcodes, decimal Minimum, decimal Maximum, decimal UnitCost,
    decimal OnHand, decimal Reserved, decimal Available, StockStatusCode Status, string? PrimaryBin,
    IReadOnlyList<ProductBinStock> Bins, IReadOnlyList<KardexLine> Movements, int TotalMovements);

public sealed record ProductBinStock(string BinCode, string LotNumber, decimal OnHand, decimal Reserved, decimal Available);

public sealed record KardexLine(DateTimeOffset RecordedAt, DateOnly BusinessDate, string TypeCode, string TypeName, decimal Signed,
    decimal Balance, string BinCode, string? Document, string? Notes, string? UserName);

public sealed class GetProductCardHandler(IMinvDbContext db) : IRequestHandler<GetProductCardQuery, ProductCard>
{
    public async Task<ProductCard> Handle(GetProductCardQuery request, CancellationToken ct)
    {
        var lookups = new InventoryLookups(db);
        var item = await lookups.VariantBySkuAsync(request.SkuOrBarcode, ct);
        var config = await lookups.ConfigAsync(ct);
        var warehouse = await lookups.WarehouseAsync(null, config, ct);
        var wid = warehouse.Id;
        var vid = item.Variant.Id;
        var bins = lookups.BinIdsOf(wid);

        var category = await db.Set<Category>().Where(c => c.Id == item.Product.CategoryId).Select(c => c.Name).FirstAsync(ct);
        var supplier = await (from ps in db.Set<ProductSupplier>()
                              join s in db.Set<Supplier>() on ps.SupplierId equals s.Id
                              where ps.ProductId == item.Product.Id
                              orderby ps.IsPreferred descending
                              select s.LegalName).FirstOrDefaultAsync(ct);
        var barcodes = await db.Set<ProductBarcode>().Where(b => b.VariantId == vid).OrderByDescending(b => b.IsPrimary)
            .Select(b => b.Code).ToListAsync(ct);
        var policy = await db.Set<ProductStockPolicy>().Where(x => x.VariantId == vid && x.WarehouseId == wid)
            .Select(x => new { x.MinQuantity, x.MaxQuantity }).FirstOrDefaultAsync(ct);
        var cost = await db.Set<AverageCostHistory>().Where(x => x.VariantId == vid && x.WarehouseId == wid)
            .OrderByDescending(x => x.EffectiveAt).Select(x => (decimal?)x.AverageCost).FirstOrDefaultAsync(ct) ?? 0m;
        var primaryBin = await (from a in db.Set<BinAssignment>()
                                join b in db.Set<Bin>() on a.BinId equals b.Id
                                where a.VariantId == vid
                                orderby a.IsPrimaryPick descending, b.Code
                                select b.Code).FirstOrDefaultAsync(ct);

        var levels = await (from l in db.Set<StockLevel>()
                            join b in db.Set<Batch>() on l.BatchId equals b.Id
                            join bin in db.Set<Bin>() on l.BinId equals bin.Id
                            where b.VariantId == vid && bins.Contains(l.BinId)
                            orderby bin.Code, b.LotNumber
                            select new { bin.Code, b.LotNumber, l.QuantityOnHand, l.QuantityReserved }).ToListAsync(ct);
        var byBin = levels.Select(l => new ProductBinStock(l.Code, l.LotNumber, l.QuantityOnHand, l.QuantityReserved,
            Quantities.Round6(l.QuantityOnHand - l.QuantityReserved))).ToList();

        var history = await (from m in db.Set<StockMovement>()
                             join t in db.Set<MovementType>() on m.MovementTypeId equals t.Id
                             join l in db.Set<StockLevel>() on m.StockLevelId equals l.Id
                             join b in db.Set<Batch>() on l.BatchId equals b.Id
                             join bin in db.Set<Bin>() on l.BinId equals bin.Id
                             join u in db.Set<User>() on m.RecordedByUserId equals u.Id into users
                             from u in users.DefaultIfEmpty()
                             where b.VariantId == vid && bins.Contains(l.BinId)
                             orderby m.RecordedAt, m.Id
                             select new
                             {
                                 m.RecordedAt, m.BusinessDate, t.Code, t.Name, m.Quantity, t.StockFactor, Bin = bin.Code,
                                 m.DocumentReference, m.Notes, UserName = u == null ? null : u.DisplayName,
                             }).ToListAsync(ct);
        var balance = 0m;
        var kardex = new List<KardexLine>(history.Count);
        foreach (var h in history)
        {
            var signed = h.Quantity * h.StockFactor;
            balance = Quantities.Round6(balance + signed);
            kardex.Add(new KardexLine(h.RecordedAt, h.BusinessDate, h.Code, h.Name, signed, balance, h.Bin, h.DocumentReference,
                h.Notes, h.UserName));
        }
        kardex.Reverse();

        var onHand = Quantities.Round6(byBin.Sum(b => b.OnHand));
        var reserved = Quantities.Round6(byBin.Sum(b => b.Reserved));
        var min = policy?.MinQuantity ?? 0m;
        var max = policy?.MaxQuantity ?? 0m;
        var active = item.Product.IsActive && item.Variant.IsActive;
        var name = item.Variant.Name is { } variantName ? item.Product.Name + " · " + variantName : item.Product.Name;
        return new ProductCard(vid, item.Variant.Sku, name, category, item.Unit.UnitCode, item.Unit.AllowsDecimals, active, supplier,
            barcodes, min, max, cost, onHand, reserved, Quantities.Round6(onHand - reserved),
            StockRules.Evaluate(onHand, min, max, active, config.AlertMargin), primaryBin, byBin,
            kardex.Take(Math.Clamp(request.Take, 1, 5000)).ToList(), kardex.Count);
    }
}

// ------------------------------------------------------------------------------------------------ últimos movimientos
/// <summary>Últimos movimientos registrados (tablero y pantalla de registro).</summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetRecentMovementsQuery(int Take = 20) : IRequest<IReadOnlyList<RecentMovement>>;

public sealed record RecentMovement(DateTimeOffset RecordedAt, DateOnly BusinessDate, string Sku, string Name, string TypeCode,
    string TypeName, short StockFactor, decimal Quantity, string Unit, string BinCode, string? UserName, string? Document);

public sealed class GetRecentMovementsHandler(IMinvDbContext db) : IRequestHandler<GetRecentMovementsQuery, IReadOnlyList<RecentMovement>>
{
    public async Task<IReadOnlyList<RecentMovement>> Handle(GetRecentMovementsQuery request, CancellationToken ct) =>
        await (from m in db.Set<StockMovement>()
               join t in db.Set<MovementType>() on m.MovementTypeId equals t.Id
               join l in db.Set<StockLevel>() on m.StockLevelId equals l.Id
               join b in db.Set<Batch>() on l.BatchId equals b.Id
               join v in db.Set<ProductVariant>() on b.VariantId equals v.Id
               join p in db.Set<Product>() on v.ProductId equals p.Id
               join un in db.Set<UnitOfMeasure>() on p.BaseUnitId equals un.Id
               join bin in db.Set<Bin>() on l.BinId equals bin.Id
               join u in db.Set<User>() on m.RecordedByUserId equals u.Id into users
               from u in users.DefaultIfEmpty()
               orderby m.RecordedAt descending, m.Id descending
               select new RecentMovement(m.RecordedAt, m.BusinessDate, v.Sku, v.Name == null ? p.Name : p.Name + " · " + v.Name,
                   t.Code, t.Name, t.StockFactor, m.Quantity, un.Code, bin.Code, u == null ? null : u.DisplayName, m.DocumentReference))
            .Take(Math.Clamp(request.Take, 1, 500)).ToListAsync(ct);
}

// ------------------------------------------------------------------------------------------------ tendencia
/// <summary>Entradas y salidas por día de los últimos <see cref="Days"/> días (gráfico del tablero). Los días sin
/// movimientos vienen en cero.</summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetMovementTrendQuery(int Days = 14) : IRequest<IReadOnlyList<MovementTrendDay>>;

public sealed record MovementTrendDay(DateOnly Date, decimal Entries, decimal Issues, int Movements);

public sealed class GetMovementTrendHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetMovementTrendQuery, IReadOnlyList<MovementTrendDay>>
{
    public async Task<IReadOnlyList<MovementTrendDay>> Handle(GetMovementTrendQuery request, CancellationToken ct)
    {
        var config = await new InventoryLookups(db).ConfigAsync(ct);
        var days = Math.Clamp(request.Days, 1, 366);
        var today = clock.TodayIn(config.TimeZoneId);
        var start = today.AddDays(-(days - 1));
        var totals = await (from m in db.Set<StockMovement>()
                            join t in db.Set<MovementType>() on m.MovementTypeId equals t.Id
                            where m.BusinessDate >= start && m.BusinessDate <= today
                            group new { m.Quantity, t.StockFactor } by m.BusinessDate into g
                            select new
                            {
                                Date = g.Key,
                                Entries = g.Sum(x => x.StockFactor > 0 ? x.Quantity : 0m),
                                Issues = g.Sum(x => x.StockFactor < 0 ? x.Quantity : 0m),
                                Count = g.Count(),
                            }).ToDictionaryAsync(x => x.Date, ct);
        return Enumerable.Range(0, days).Select(i => start.AddDays(i)).Select(d => totals.TryGetValue(d, out var x)
            ? new MovementTrendDay(d, Quantities.Round6(x.Entries), Quantities.Round6(x.Issues), x.Count)
            : new MovementTrendDay(d, 0, 0, 0)).ToList();
    }
}
