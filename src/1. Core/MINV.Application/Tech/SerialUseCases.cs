using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Inventory;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Purchasing;
using MINV.Domain.Sales;
using MINV.Domain.Service;
using MINV.Domain.Warehousing;

namespace MINV.Application.Tech;

// =====================================================================================================================
// V4.2 · Series e IMEI (regla T-02): consultas de disponibilidad, búsqueda y trazabilidad, garantía derivada (T-04),
// registro de series de unidades que ya estaban en stock y destino de unidades devueltas o en garantía.
// =====================================================================================================================

/// <summary>V4.2 · Venta (o reposición) vigente de una unidad y su garantía derivada (regla T-04: no se guarda).</summary>
internal sealed record SerialSale(DateTimeOffset? SoldAt, DateOnly? SoldOn, string? InvoiceNumber, Guid? InvoiceId, Guid? InvoiceBranchId,
    Customer? Customer, int WarrantyMonths, DateOnly? WarrantyUntil, bool InWarranty);

/// <summary>V4.2 · Vistas de las series: fila, venta vigente, garantía y bitácora, armadas en lotes (pocas consultas).</summary>
internal sealed class SerialViews(IMinvDbContext db, IClock clock)
{
    /// <summary>Estados en los que la unidad está en manos del cliente (o de la tienda por su garantía).</summary>
    private static bool WithCustomer(SerialNumberStatus status) => status is SerialNumberStatus.Sold or SerialNumberStatus.InRma;

    public async Task<IReadOnlyList<SerialRow>> RowsAsync(IReadOnlyList<SerialNumber> units, CancellationToken ct)
    {
        var sales = await SalesAsync(units, ct);
        var variantIds = units.Select(u => u.VariantId).Distinct().ToList();
        var items = await (from v in db.Set<ProductVariant>()
                           join p in db.Set<Product>() on v.ProductId equals p.Id
                           where variantIds.Contains(v.Id)
                           select new { v.Id, v.Sku, p.Name }).ToDictionaryAsync(x => x.Id, ct);
        var levelIds = units.Where(u => u.StockLevelId is not null).Select(u => u.StockLevelId!.Value).Distinct().ToList();
        var locations = await (from l in db.Set<StockLevel>()
                               join bin in db.Set<Bin>() on l.BinId equals bin.Id
                               join s in db.Set<Shelf>() on bin.ShelfId equals s.Id
                               join r in db.Set<Rack>() on s.RackId equals r.Id
                               join a in db.Set<Aisle>() on r.AisleId equals a.Id
                               join z in db.Set<Zone>() on a.ZoneId equals z.Id
                               join w in db.Set<Warehouse>() on z.WarehouseId equals w.Id
                               where levelIds.Contains(l.Id)
                               select new { l.Id, l.BranchId, Warehouse = w.Code }).ToDictionaryAsync(x => x.Id, ct);
        var ids = units.Select(u => u.Id).ToList();
        var lastBranch = (await db.Set<SerialEvent>().Where(e => ids.Contains(e.SerialNumberId) && e.BranchId != null)
                .Select(e => new { e.SerialNumberId, e.BranchId, e.OccurredAt }).ToListAsync(ct))
            .GroupBy(e => e.SerialNumberId).ToDictionary(g => g.Key, g => g.OrderBy(e => e.OccurredAt).Last().BranchId!.Value);
        var branches = await db.Set<Branch>().ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        return units.Select(u =>
        {
            var location = u.StockLevelId is { } level ? locations.GetValueOrDefault(level) : null;
            var branchId = location?.BranchId ?? (lastBranch.TryGetValue(u.Id, out var b) ? b : (Guid?)null);
            var sale = sales[u.Id];
            var item = items[u.VariantId];
            return new SerialRow(u.Serial, u.Kind, item.Sku, item.Name, u.Status, branchId is { } id ? branches.GetValueOrDefault(id) : null,
                location?.Warehouse, u.ReceivedAt, sale.SoldAt, sale.InvoiceNumber, sale.Customer?.Name, sale.WarrantyUntil);
        }).ToList();
    }

    /// <summary>
    /// Venta vigente de cada unidad que está con su cliente (vendida o en RMA): el último hecho «vendida» o «reposición
    /// entregada» de su bitácora (la bitácora es de la empresa: vale aunque la venta sea de otra sucursal); cliente y
    /// factura de la venta si la sesión ve esa sucursal (o del caso RMA de la reposición); garantía = fecha + meses.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, SerialSale>> SalesAsync(IReadOnlyList<SerialNumber> units, CancellationToken ct)
    {
        var config = await new InventoryLookups(db).ConfigAsync(ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId);
        var today = clock.TodayIn(config.TimeZoneId);
        var ids = units.Select(u => u.Id).ToList();
        var events = (await db.Set<SerialEvent>().AsNoTracking()
                .Where(e => ids.Contains(e.SerialNumberId) && (e.Action == SerialEventAction.Sold || e.Action == SerialEventAction.ReplacementIssued))
                .ToListAsync(ct))
            .GroupBy(e => e.SerialNumberId).ToDictionary(g => g.Key, g => g.OrderBy(e => e.OccurredAt).Last());
        var variantIds = units.Select(u => u.VariantId).Distinct().ToList();
        var productOf = await db.Set<ProductVariant>().Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.ProductId, ct);
        var productIds = productOf.Values.Distinct().ToList();
        var profiles = await db.Set<ProductTechProfile>().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);
        // Ventas visibles de cada unidad (la más reciente) y reposiciones de garantía (cliente del caso)
        var visibleSales = (await (from x in db.Set<SalesOrderLineSerial>()
                                   join line in db.Set<SalesOrderLine>() on x.SalesOrderLineId equals line.Id
                                   join o in db.Set<SalesOrder>() on line.SalesOrderId equals o.Id
                                   join i in db.Set<Invoice>() on o.Id equals i.SalesOrderId
                                   join c in db.Set<Customer>() on o.CustomerId equals c.Id
                                   where ids.Contains(x.SerialNumberId)
                                   select new { x.SerialNumberId, i.Number, InvoiceId = i.Id, i.BranchId, i.IssuedAt, Customer = c }).ToListAsync(ct))
            .GroupBy(x => x.SerialNumberId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.IssuedAt).Last());
        var replacements = (await (from w in db.Set<WarrantyClaim>()
                                   join c in db.Set<Customer>() on w.CustomerId equals c.Id
                                   where w.ReplacementSerialId != null && ids.Contains(w.ReplacementSerialId.Value)
                                   select new { Serial = w.ReplacementSerialId!.Value, Customer = c }).ToListAsync(ct))
            .GroupBy(x => x.Serial).ToDictionary(g => g.Key, g => g.First().Customer);
        var result = new Dictionary<Guid, SerialSale>();
        foreach (var unit in units)
        {
            var months = profiles.GetValueOrDefault(productOf[unit.VariantId])?.WarrantyMonths ?? 0;
            if (!WithCustomer(unit.Status) || !events.TryGetValue(unit.Id, out var sold))
            {
                result[unit.Id] = new SerialSale(null, null, null, null, null, null, months, null, false);
                continue;
            }
            var soldOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(sold.OccurredAt, zone).DateTime);
            var until = soldOn.AddMonths(months);
            Customer? customer = null;
            Guid? invoiceId = null, invoiceBranch = null;
            if (sold.Action == SerialEventAction.Sold && visibleSales.TryGetValue(unit.Id, out var sale) && sale.Number == sold.DocumentNumber)
            {
                (customer, invoiceId, invoiceBranch) = (sale.Customer, sale.InvoiceId, sale.BranchId);
            }
            else if (sold.Action == SerialEventAction.ReplacementIssued)
            {
                customer = replacements.GetValueOrDefault(unit.Id);
            }
            result[unit.Id] = new SerialSale(sold.OccurredAt, soldOn, sold.DocumentNumber, invoiceId, invoiceBranch, customer, months, until,
                today <= until);
        }
        return result;
    }

    public async Task<WarrantyStatusView> WarrantyAsync(SerialNumber unit, CancellationToken ct)
    {
        var sale = (await SalesAsync([unit], ct))[unit.Id];
        var item = await (from v in db.Set<ProductVariant>()
                          join p in db.Set<Product>() on v.ProductId equals p.Id
                          where v.Id == unit.VariantId
                          select new { v.Sku, p.Name }).FirstAsync(ct);
        var open = await db.Set<WarrantyClaim>().Where(c => c.SerialNumberId == unit.Id && c.Status != WarrantyClaimStatus.Delivered)
            .Select(c => c.Number).FirstOrDefaultAsync(ct);
        return new WarrantyStatusView(unit.Serial, item.Sku, item.Name, unit.Status, sale.SoldOn, sale.InvoiceNumber, sale.Customer?.Code,
            sale.Customer?.Name, sale.WarrantyMonths, sale.WarrantyUntil, sale.InWarranty, open);
    }
}

public sealed class GetAvailableSerialsHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetAvailableSerialsQuery, IReadOnlyList<SerialRow>>
{
    public async Task<IReadOnlyList<SerialRow>> Handle(GetAvailableSerialsQuery request, CancellationToken ct)
    {
        var lookups = new InventoryLookups(db);
        var item = await lookups.VariantBySkuAsync(request.Sku, ct);
        var branch = await BranchContext.ResolveAsync(db, null, ct);
        var levels = db.Set<StockLevel>().Where(l => l.BranchId == branch);
        if (!string.IsNullOrWhiteSpace(request.WarehouseCode))
        {
            var bins = lookups.BinIdsOf((await lookups.WarehouseByCodeAsync(request.WarehouseCode, ct)).Id);
            levels = levels.Where(l => bins.Contains(l.BinId));
        }
        var units = await (from s in db.Set<SerialNumber>().AsNoTracking()
                           join l in levels on s.StockLevelId equals (Guid?)l.Id
                           where s.VariantId == item.Variant.Id && s.Status == SerialNumberStatus.InStock
                           orderby s.ReceivedAt, s.Serial
                           select s).ToListAsync(ct);
        return await new SerialViews(db, clock).RowsAsync(units, ct);
    }
}

public sealed class SearchSerialsHandler(IMinvDbContext db, IClock clock) : IRequestHandler<SearchSerialsQuery, IReadOnlyList<SerialRow>>
{
    public async Task<IReadOnlyList<SerialRow>> Handle(SearchSerialsQuery request, CancellationToken ct)
    {
        var query = db.Set<SerialNumber>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Text))
        {
            var text = request.Text.Trim().ToUpperInvariant();
            var digits = Imei.Normalize(text);
            // La pantalla ofrece buscar por serie, IMEI o SKU (V4.2): también las unidades de los productos cuyo SKU coincide
            var bySku = db.Set<ProductVariant>().Where(v => v.Sku.Contains(text)).Select(v => v.Id);
            query = query.Where(s => s.Serial.Contains(text) || (digits.Length > 0 && s.Serial.Contains(digits)) || bySku.Contains(s.VariantId));
        }
        if (request.Status is { } status)
        {
            query = query.Where(s => s.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(request.Sku))
        {
            var item = await new InventoryLookups(db).VariantBySkuAsync(request.Sku, ct);
            query = query.Where(s => s.VariantId == item.Variant.Id);
        }
        // Las más recientes primero (la pantalla las ordena igual): con miles de series se ve lo último que entró
        var units = await query.OrderByDescending(s => s.ReceivedAt).ThenBy(s => s.Serial).Take(Math.Clamp(request.Max, 1, 5000)).ToListAsync(ct);
        return await new SerialViews(db, clock).RowsAsync(units, ct);
    }
}

/// <summary>V4.2 · Totales por estado de TODAS las series (los indicadores de «Series e IMEI» no dependen de la lista, que
/// se limita a las más recientes); la garantía vigente de las vendidas se deriva de su venta (T-04).</summary>
public sealed class GetSerialSummaryHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetSerialSummaryQuery, SerialSummaryView>
{
    public async Task<SerialSummaryView> Handle(GetSerialSummaryQuery request, CancellationToken ct)
    {
        var units = db.Set<SerialNumber>().AsNoTracking();
        var byStatus = (await units.GroupBy(s => s.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Status, x => x.Count);
        int Count(params SerialNumberStatus[] statuses) => statuses.Sum(s => byStatus.GetValueOrDefault(s));
        var products = await units.Where(s => s.Status == SerialNumberStatus.InStock).Select(s => s.VariantId).Distinct().CountAsync(ct);
        var sold = await units.Where(s => s.Status == SerialNumberStatus.Sold).ToListAsync(ct);
        var sales = await new SerialViews(db, clock).SalesAsync(sold, ct);
        return new SerialSummaryView(byStatus.Values.Sum(), Count(SerialNumberStatus.InStock), products, sold.Count,
            sold.Count(s => sales.TryGetValue(s.Id, out var sale) && sale.InWarranty), Count(SerialNumberStatus.InRma, SerialNumberStatus.Returned),
            Count(SerialNumberStatus.Scrapped, SerialNumberStatus.ReturnedToSupplier));
    }
}

public sealed class GetWarrantyStatusHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetWarrantyStatusQuery, WarrantyStatusView>
{
    public async Task<WarrantyStatusView> Handle(GetWarrantyStatusQuery request, CancellationToken ct) =>
        await new SerialViews(db, clock).WarrantyAsync(await SerialLedger.ResolveAsync(db, request.Serial, request.Sku, ct), ct);
}

/// <summary>Trazabilidad completa: bitácora con sucursal, documento y usuario, proveedor de la recepción, garantía y casos.</summary>
public sealed class GetSerialTraceHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetSerialTraceQuery, SerialTraceView>
{
    public async Task<SerialTraceView> Handle(GetSerialTraceQuery request, CancellationToken ct)
    {
        var unit = await SerialLedger.ResolveAsync(db, request.Serial, request.Sku, ct);
        var views = new SerialViews(db, clock);
        var row = (await views.RowsAsync([unit], ct))[0];
        var warranty = await views.WarrantyAsync(unit, ct);
        var events = await db.Set<SerialEvent>().AsNoTracking().Where(e => e.SerialNumberId == unit.Id).OrderBy(e => e.OccurredAt).ToListAsync(ct);
        var userIds = events.Where(e => e.UserId is not null).Select(e => e.UserId!.Value).Distinct().ToList();
        var users = await db.Set<User>().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var branches = await db.Set<Branch>().ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        var receipt = events.FirstOrDefault(e => e.Action == SerialEventAction.Received)?.DocumentNumber;
        string? supplier = null;
        if (await db.Set<GoodsReceipt>().AsNoTracking().FirstOrDefaultAsync(g => g.Number == receipt, ct) is { } goods)
        {
            var supplierId = goods.SupplierId
                             ?? await db.Set<PurchaseOrder>().Where(o => o.Id == goods.PurchaseOrderId).Select(o => (Guid?)o.SupplierId).FirstOrDefaultAsync(ct);
            supplier = await db.Set<Supplier>().Where(x => x.Id == supplierId).Select(x => x.LegalName).FirstOrDefaultAsync(ct);
        }
        var claims = await db.Set<WarrantyClaim>().AsNoTracking().Where(c => c.SerialNumberId == unit.Id || c.ReplacementSerialId == unit.Id)
            .OrderBy(c => c.ReceivedAt).ToListAsync(ct);
        return new SerialTraceView(row, supplier, receipt, warranty.WarrantyMonths, warranty.InWarranty,
            events.Select(e => new SerialEventView(e.OccurredAt, e.Action, e.BranchId is { } b ? branches.GetValueOrDefault(b) : null, e.DocumentNumber,
                e.Note, e.UserId is { } u ? users.GetValueOrDefault(u) : null)).ToList(),
            await new ClaimViews(db, clock).RowsAsync(claims, ct));
    }
}

public sealed class RegisterStockSerialsValidator : AbstractValidator<RegisterStockSerialsCommand>
{
    public RegisterStockSerialsValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().WithMessage("Elija el producto.");
        RuleFor(x => x.Serials).NotEmpty().WithMessage("Indique al menos una serie.");
        RuleFor(x => x.Note).MaximumLength(200);
    }
}

/// <summary>Series de unidades que ya están en stock sin serie en la sucursal activa: se asignan a las existencias con
/// unidades sin serie (de la mayor a la menor), nunca más series que unidades.</summary>
public sealed class RegisterStockSerialsHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<RegisterStockSerialsCommand, string>
{
    public async Task<string> Handle(RegisterStockSerialsCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para registrar series.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await RegisterAsync(request, userId, ct);
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }

    private async Task<string> RegisterAsync(RegisterStockSerialsCommand request, Guid userId, CancellationToken ct)
    {
        var item = await new InventoryLookups(db).VariantBySkuAsync(request.Sku, ct);
        var ledger = new SerialLedger(db);
        var kind = await ledger.KindAsync(item.Product.Id, ct);
        var serials = new List<string>();
        foreach (var serial in request.Serials.Select(s => SerialNumber.Normalize(kind, s)))
        {
            Guard.That(!serials.Contains(serial, StringComparer.Ordinal), SerialErrorCodes.Duplicate, $"La serie {serial} está repetida.");
            serials.Add(serial);
        }
        var existing = await ledger.FindAsync(item.Variant.Id, serials, ct);
        Guard.That(existing.Count == 0, SerialErrorCodes.Duplicate,
            $"Ya están registradas para {item.Variant.Sku}: {string.Join(", ", existing.Keys.Order(StringComparer.Ordinal))}.");
        var branchId = await BranchContext.ResolveAsync(db, null, ct);
        var levels = await (from l in db.Set<StockLevel>()
                            join b in db.Set<Batch>() on l.BatchId equals b.Id
                            where b.VariantId == item.Variant.Id && l.BranchId == branchId && l.QuantityOnHand > 0
                            select l).ToListAsync(ct);
        var levelIds = levels.Select(l => l.Id).ToList();
        var withSerial = (await db.Set<SerialNumber>().Where(s => s.StockLevelId != null && levelIds.Contains(s.StockLevelId.Value)
                                                                 && (s.Status == SerialNumberStatus.InStock || s.Status == SerialNumberStatus.Reserved))
                .Select(s => s.StockLevelId!.Value).ToListAsync(ct))
            .GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
        var free = levels.Select(l => (Level: l, Free: (int)decimal.Floor(l.QuantityOnHand) - withSerial.GetValueOrDefault(l.Id)))
            .Where(x => x.Free > 0).OrderByDescending(x => x.Free).ToList();
        var available = free.Sum(x => x.Free);
        Guard.That(serials.Count <= available, SerialErrorCodes.Count,
            $"{item.Variant.Sku} tiene {available} unidad(es) en stock sin serie en la sucursal: no se pueden registrar {serials.Count} series.");
        var context = new SerialContext(branchId, userId, clock.UtcNow, "SERIES",
            string.IsNullOrWhiteSpace(request.Note) ? "Series de unidades que ya estaban en stock" : request.Note.Trim());
        var next = 0;
        foreach (var (level, count) in free)
        {
            var batch = await ledger.BatchAsync(level.BatchId, ct);
            for (var i = 0; i < count && next < serials.Count; i++)
            {
                db.Set<SerialNumber>().Add(SerialNumber.Receive(kind, serials[next++], batch, level, context));
            }
        }
        await db.SaveChangesAsync(ct);
        return $"✔ {serials.Count} serie(s) registrada(s) para {item.Variant.Sku}: quedan {available - serials.Count} unidad(es) sin serie en la sucursal.";
    }
}

public sealed class DisposeSerialValidator : AbstractValidator<DisposeSerialCommand>
{
    public DisposeSerialValidator()
    {
        RuleFor(x => x.Serial).NotEmpty().WithMessage("Indique la serie o el IMEI.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Indique el motivo.").MaximumLength(250);
    }
}

/// <summary>Destino de una unidad devuelta por falla o que quedó en un RMA ya resuelto: al proveedor o de baja.</summary>
public sealed class DisposeSerialHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<DisposeSerialCommand, string>
{
    public async Task<string> Handle(DisposeSerialCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión.");
        var unit = await SerialLedger.ResolveAsync(db, request.Serial, request.Sku, ct);
        Guard.That(unit.Status is SerialNumberStatus.Returned or SerialNumberStatus.InRma, SerialErrorCodes.NotAvailable,
            $"La serie {unit.Serial} está {SerialNumber.Describe(unit.Status)}: solo se da destino a unidades devueltas o en garantía " +
            "(las que están en stock se dan de baja con un ajuste negativo con su serie).");
        await new SerialLedger(db).EnsureNoOpenClaimAsync(unit, ct);
        var context = new SerialContext(await BranchContext.ResolveAsync(db, null, ct), userId, clock.UtcNow, null, request.Reason.Trim());
        _ = request.Disposal == SerialDisposal.Scrap ? unit.Scrap(null, context) : unit.ReturnToSupplier(null, context);
        await db.SaveChangesAsync(ct);
        return $"✔ Serie {unit.Serial}: {SerialNumber.Describe(unit.Status)}.";
    }
}
