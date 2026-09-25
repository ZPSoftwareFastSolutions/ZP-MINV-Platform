using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Inventory;
using MINV.Domain.Accounting;
using MINV.Domain.Catalog;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Purchasing;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Application.Reports;

public sealed record SeriesPoint(DateOnly Date, decimal Amount, int Count);

public sealed record GroupTotal(string Key, string Name, decimal Quantity, decimal Amount, decimal Profit, int Count);

/// <summary>Reporte de ventas del período: indicadores, serie diaria y totales por categoría, producto, medio de pago,
/// vendedor y cliente. El costo se estima al costo promedio vigente de cada producto.</summary>
public sealed record SalesReport(DateOnly From, DateOnly To, decimal Revenue, decimal Tax, decimal NetRevenue, decimal Cost, decimal GrossProfit,
    decimal MarginPercent, int Tickets, decimal AverageTicket, decimal Units, int Voided, IReadOnlyList<SeriesPoint> ByDay,
    IReadOnlyList<GroupTotal> ByCategory, IReadOnlyList<GroupTotal> ByProduct, IReadOnlyList<GroupTotal> ByPaymentMethod,
    IReadOnlyList<GroupTotal> ByCashier, IReadOnlyList<GroupTotal> ByCustomer);

[RequiresPermission(PermissionCodes.ReportsView)]
public sealed record GetSalesReportQuery(DateOnly From, DateOnly To) : IRequest<SalesReport>;

public sealed class GetSalesReportHandler(IMinvDbContext db) : IRequestHandler<GetSalesReportQuery, SalesReport>
{
    public async Task<SalesReport> Handle(GetSalesReportQuery request, CancellationToken ct)
    {
        var start = request.From;
        var end = request.To;
        var lines = await (from l in db.Set<SalesOrderLine>()
                           join so in db.Set<SalesOrder>() on l.SalesOrderId equals so.Id
                           join i in db.Set<Invoice>() on so.Id equals i.SalesOrderId
                           join v in db.Set<ProductVariant>() on l.VariantId equals v.Id
                           join p in db.Set<Product>() on v.ProductId equals p.Id
                           join c in db.Set<Category>() on p.CategoryId equals c.Id
                           join cu in db.Set<Customer>() on so.CustomerId equals cu.Id
                           where so.OrderDate >= start && so.OrderDate <= end && i.Status == InvoiceStatus.Issued
                           select new
                           {
                               InvoiceId = i.Id, so.OrderDate, so.WarehouseId, l.VariantId, v.Sku, p.Name, CategoryCode = c.Code, Category = c.Name,
                               CustomerCode = cu.Code, Customer = cu.Name, l.Quantity, l.UnitPrice, l.DiscountPercent,
                           }).ToListAsync(ct);
        var invoiceIds = lines.Select(l => l.InvoiceId).Distinct().ToList();
        var taxes = await db.Set<InvoiceLine>().Where(x => invoiceIds.Contains(x.InvoiceId)).SumAsync(x => (decimal?)x.TaxAmount, ct) ?? 0;
        var payments = await (from pay in db.Set<Payment>()
                              join m in db.Set<PaymentMethod>() on pay.PaymentMethodId equals m.Id
                              join u in db.Set<User>() on pay.RecordedByUserId equals u.Id
                              where invoiceIds.Contains(pay.InvoiceId)
                              select new { pay.InvoiceId, pay.Amount, Method = m.Name, MethodCode = m.Code, Cashier = u.DisplayName, CashierEmail = u.Email })
            .ToListAsync(ct);
        var voided = await (from i in db.Set<Invoice>()
                            join so in db.Set<SalesOrder>() on i.SalesOrderId equals so.Id
                            where so.OrderDate >= start && so.OrderDate <= end && i.Status == InvoiceStatus.Voided
                            select i.Id).CountAsync(ct);
        var costs = (await db.Set<AverageCostHistory>().Select(x => new { x.VariantId, x.EffectiveAt, x.AverageCost }).ToListAsync(ct))
            .GroupBy(x => x.VariantId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.EffectiveAt).First().AverageCost);

        var enriched = lines.Select(l =>
        {
            var amount = decimal.Round(l.Quantity * l.UnitPrice * (1 - l.DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero);
            var cost = l.Quantity * costs.GetValueOrDefault(l.VariantId);
            return new { l, amount, cost };
        }).ToList();
        var revenue = enriched.Sum(x => x.amount);
        var cost = JournalPoster.Money(enriched.Sum(x => x.cost));
        var net = revenue - taxes;
        var tickets = invoiceIds.Count;
        var byDay = enriched.GroupBy(x => x.l.OrderDate).ToDictionary(g => g.Key, g => (Amount: g.Sum(x => x.amount), Count: g.Select(x => x.l.InvoiceId).Distinct().Count()));
        var series = new List<SeriesPoint>();
        for (var d = start; d <= end && series.Count < 400; d = d.AddDays(1))
        {
            var day = byDay.GetValueOrDefault(d);
            series.Add(new SeriesPoint(d, day.Amount, day.Count));
        }
        var byCategory = enriched.GroupBy(x => (x.l.CategoryCode, x.l.Category))
            .Select(g => new GroupTotal(g.Key.CategoryCode, g.Key.Category, g.Sum(x => x.l.Quantity), g.Sum(x => x.amount),
                JournalPoster.Money(g.Sum(x => x.amount - x.cost)), g.Select(x => x.l.InvoiceId).Distinct().Count()))
            .OrderByDescending(g => g.Amount).ToList();
        var byProduct = enriched.GroupBy(x => (x.l.Sku, x.l.Name))
            .Select(g => new GroupTotal(g.Key.Sku, g.Key.Name, g.Sum(x => x.l.Quantity), g.Sum(x => x.amount),
                JournalPoster.Money(g.Sum(x => x.amount - x.cost)), g.Select(x => x.l.InvoiceId).Distinct().Count()))
            .OrderByDescending(g => g.Amount).ToList();
        var byCustomer = enriched.GroupBy(x => (x.l.CustomerCode, x.l.Customer))
            .Select(g => new GroupTotal(g.Key.CustomerCode, g.Key.Customer, g.Sum(x => x.l.Quantity), g.Sum(x => x.amount),
                JournalPoster.Money(g.Sum(x => x.amount - x.cost)), g.Select(x => x.l.InvoiceId).Distinct().Count()))
            .OrderByDescending(g => g.Amount).Take(15).ToList();
        var byMethod = payments.GroupBy(p => (p.MethodCode, p.Method))
            .Select(g => new GroupTotal(g.Key.MethodCode, g.Key.Method, 0, g.Sum(p => p.Amount), 0, g.Count())).OrderByDescending(g => g.Amount).ToList();
        var byCashier = payments.GroupBy(p => (p.CashierEmail, p.Cashier))
            .Select(g => new GroupTotal(g.Key.CashierEmail, g.Key.Cashier, 0, g.Sum(p => p.Amount), 0, g.Count())).OrderByDescending(g => g.Amount).ToList();
        return new SalesReport(start, end, revenue, taxes, net, cost, JournalPoster.Money(net - cost),
            net > 0 ? decimal.Round((net - cost) / net * 100, 1) : 0, tickets, tickets > 0 ? JournalPoster.Money(revenue / tickets) : 0,
            enriched.Sum(x => x.l.Quantity), voided, series, byCategory, byProduct, byMethod, byCashier, byCustomer);
    }
}

/// <summary>Compras recibidas del período por proveedor y por mes.</summary>
public sealed record PurchasesReport(decimal Received, int Receipts, int OpenOrders, decimal OpenAmount, IReadOnlyList<GroupTotal> BySupplier,
    IReadOnlyList<SeriesPoint> ByDay);

[RequiresPermission(PermissionCodes.ReportsView)]
public sealed record GetPurchasesReportQuery(DateOnly From, DateOnly To) : IRequest<PurchasesReport>;

public sealed class GetPurchasesReportHandler(IMinvDbContext db) : IRequestHandler<GetPurchasesReportQuery, PurchasesReport>
{
    public async Task<PurchasesReport> Handle(GetPurchasesReportQuery request, CancellationToken ct)
    {
        var config = await new InventoryLookups(db).ConfigAsync(ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId);
        var rows = await (from l in db.Set<GoodsReceiptLine>()
                          join g in db.Set<GoodsReceipt>() on l.GoodsReceiptId equals g.Id
                          join po in db.Set<PurchaseOrder>() on g.PurchaseOrderId equals (Guid?)po.Id into orders
                          from po in orders.DefaultIfEmpty()
                          // Arco exclusivo: la recepción viene de una orden (su proveedor) o directamente de un proveedor
                          join s in db.Set<Supplier>() on (g.SupplierId ?? po.SupplierId) equals s.Id
                          where g.Status == GoodsReceiptStatus.Posted
                          select new { g.Id, g.ReceivedAt, s.Code, s.LegalName, Amount = l.Quantity * l.UnitCost }).ToListAsync(ct);
        var inRange = rows.Select(r => new { r, Date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.ReceivedAt, zone).DateTime) })
            .Where(x => x.Date >= request.From && x.Date <= request.To).ToList();
        var open = await db.Set<PurchaseOrder>().Include(o => o.Lines)
            .Where(o => o.Status == PurchaseOrderStatus.Draft || o.Status == PurchaseOrderStatus.Approved).ToListAsync(ct);
        var bySupplier = inRange.GroupBy(x => (x.r.Code, x.r.LegalName))
            .Select(g => new GroupTotal(g.Key.Code, g.Key.LegalName, 0, JournalPoster.Money(g.Sum(x => x.r.Amount)), 0, g.Select(x => x.r.Id).Distinct().Count()))
            .OrderByDescending(g => g.Amount).ToList();
        var byDay = inRange.GroupBy(x => x.Date).ToDictionary(g => g.Key, g => g.Sum(x => x.r.Amount));
        var series = new List<SeriesPoint>();
        for (var d = request.From; d <= request.To && series.Count < 400; d = d.AddDays(1))
        {
            series.Add(new SeriesPoint(d, JournalPoster.Money(byDay.GetValueOrDefault(d)), 0));
        }
        return new PurchasesReport(JournalPoster.Money(inRange.Sum(x => x.r.Amount)), inRange.Select(x => x.r.Id).Distinct().Count(), open.Count,
            JournalPoster.Money(open.Sum(o => o.Total)), bySupplier, series);
    }
}

/// <summary>Movimientos del período (todos o de un tipo) para revisar o exportar.</summary>
public sealed record MovementReportRow(DateOnly Date, DateTimeOffset RecordedAt, string Sku, string Name, string TypeCode, string TypeName,
    decimal Signed, string Unit, string BinCode, string? User, string? Document, string? Notes);

[RequiresPermission(PermissionCodes.ReportsView)]
public sealed record GetMovementsReportQuery(DateOnly From, DateOnly To, string? TypeCode = null) : IRequest<IReadOnlyList<MovementReportRow>>;

public sealed class GetMovementsReportHandler(IMinvDbContext db) : IRequestHandler<GetMovementsReportQuery, IReadOnlyList<MovementReportRow>>
{
    public async Task<IReadOnlyList<MovementReportRow>> Handle(GetMovementsReportQuery request, CancellationToken ct)
    {
        var start = request.From;
        var end = request.To;
        var type = string.IsNullOrWhiteSpace(request.TypeCode) ? null : request.TypeCode.Trim().ToUpperInvariant();
        return await (from m in db.Set<StockMovement>()
                      join t in db.Set<MovementType>() on m.MovementTypeId equals t.Id
                      join l in db.Set<StockLevel>() on m.StockLevelId equals l.Id
                      join b in db.Set<Batch>() on l.BatchId equals b.Id
                      join v in db.Set<ProductVariant>() on b.VariantId equals v.Id
                      join p in db.Set<Product>() on v.ProductId equals p.Id
                      join un in db.Set<UnitOfMeasure>() on p.BaseUnitId equals un.Id
                      join bin in db.Set<Bin>() on l.BinId equals bin.Id
                      join u in db.Set<User>() on m.RecordedByUserId equals u.Id into users
                      from u in users.DefaultIfEmpty()
                      where m.BusinessDate >= start && m.BusinessDate <= end && (type == null || t.Code == type)
                      orderby m.RecordedAt descending
                      select new MovementReportRow(m.BusinessDate, m.RecordedAt, v.Sku, p.Name, t.Code, t.Name, m.Quantity * t.StockFactor, un.Code,
                          bin.Code, u == null ? null : u.DisplayName, m.DocumentReference, m.Notes)).Take(20000).ToListAsync(ct);
    }
}
