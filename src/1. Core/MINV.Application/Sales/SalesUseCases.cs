using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Catalog;
using MINV.Application.Common;
using MINV.Application.Inventory;
using MINV.Domain.Accounting;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Events;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Application.Sales;

// ------------------------------------------------------------------------------------------------ estado del punto de venta
public sealed record PosOption(string Code, string Name, bool OpensCashDrawer = false);

public sealed record PosSessionInfo(Guid Id, string RegisterCode, string RegisterName, DateTimeOffset OpenedAt, decimal OpeningCash, int Tickets,
    decimal Sales, decimal CashSales, decimal ExpectedCash);

public sealed record PosState(IReadOnlyList<PosOption> Registers, IReadOnlyList<PosOption> PaymentMethods, IReadOnlyList<PosOption> Customers,
    PosSessionInfo? Session, decimal TaxRate, string CompanyName, string? TaxId, string BranchName);

/// <summary>Cajas, medios de pago, clientes, IVA vigente y el turno abierto del usuario (si tiene).</summary>
[RequiresPermission(PermissionCodes.PosOperate)]
public sealed record GetPosStateQuery : IRequest<PosState>;

public sealed class GetPosStateHandler(IMinvDbContext db, ICurrentUser user, ITenantContext tenant, IClock clock) : IRequestHandler<GetPosStateQuery, PosState>
{
    public async Task<PosState> Handle(GetPosStateQuery request, CancellationToken ct)
    {
        var config = await new InventoryLookups(db).ConfigAsync(ct);
        var registers = await db.Set<PosRegister>().Where(r => r.IsActive).OrderBy(r => r.Code).ToListAsync(ct);
        var methods = await db.Set<PaymentMethod>().Where(m => m.IsActive).OrderBy(m => m.Code == "EFECTIVO" ? 0 : 1).ThenBy(m => m.Name)
            .Select(m => new PosOption(m.Code, m.Name, m.OpensCashDrawer)).ToListAsync(ct);
        var customers = await db.Set<Customer>().Where(c => c.IsActive).OrderBy(c => c.Code == "CF" ? 0 : 1).ThenBy(c => c.Name)
            .Select(c => new PosOption(c.Code, c.Name, false)).ToListAsync(ct);
        var company = await db.Set<Tenant>().FirstAsync(t => t.Id == tenant.TenantId, ct);
        var branch = await db.Set<Branch>().OrderBy(b => b.Code).Select(b => b.Name).FirstOrDefaultAsync(ct) ?? "";
        var session = await db.Set<PosSession>().Where(s => s.Status == PosSessionStatus.Open && s.OpenedByUserId == user.UserId)
            .OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync(ct);
        PosSessionInfo? info = null;
        if (session is not null)
        {
            var register = registers.First(r => r.Id == session.PosRegisterId);
            var totals = await Cash.TotalsAsync(db, session.Id, ct);
            info = new PosSessionInfo(session.Id, register.Code, register.Name, session.OpenedAt, session.OpeningCash, totals.Tickets, totals.Sales,
                totals.CashSales, session.ExpectedCash(totals.CashSales, totals.CashIn, totals.CashOut));
        }
        return new PosState(registers.Select(r => new PosOption(r.Code, r.Name)).ToList(), methods, customers, info,
            await Pricing.TaxRateAsync(db, clock.TodayIn(config.TimeZoneId), ct), company.LegalName, company.TaxId, branch);
    }
}

// ------------------------------------------------------------------------------------------------ productos vendibles
public sealed record SellableProduct(Guid VariantId, string Sku, string Name, string CategoryCode, string Category, string Unit, bool AllowsDecimals,
    decimal Price, decimal Available, IReadOnlyList<string> Barcodes);

[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetSellableProductsQuery : IRequest<IReadOnlyList<SellableProduct>>;

public sealed class GetSellableProductsHandler(IMinvDbContext db) : IRequestHandler<GetSellableProductsQuery, IReadOnlyList<SellableProduct>>
{
    public async Task<IReadOnlyList<SellableProduct>> Handle(GetSellableProductsQuery request, CancellationToken ct)
    {
        var lookups = new InventoryLookups(db);
        var warehouse = await lookups.WarehouseAsync(null, await lookups.ConfigAsync(ct), ct);
        var bins = lookups.BinIdsOf(warehouse.Id);
        var rows = await (from v in db.Set<ProductVariant>()
                          join p in db.Set<Product>() on v.ProductId equals p.Id
                          join c in db.Set<Category>() on p.CategoryId equals c.Id
                          join u in db.Set<UnitOfMeasure>() on p.BaseUnitId equals u.Id
                          join i in db.Set<PriceListItem>() on v.Id equals i.VariantId
                          join l in db.Set<PriceList>() on i.PriceListId equals l.Id
                          where l.IsDefault && p.IsActive && v.IsActive
                          orderby c.Name, p.Name
                          select new { v.Id, v.Sku, Name = v.Name == null ? p.Name : p.Name + " · " + v.Name, CategoryCode = c.Code, Category = c.Name,
                              Unit = u.Code, u.AllowsDecimals, i.UnitPrice }).ToListAsync(ct);
        var stock = (await (from l in db.Set<StockLevel>()
                            join b in db.Set<Batch>() on l.BatchId equals b.Id
                            where bins.Contains(l.BinId)
                            select new { b.VariantId, l.QuantityOnHand, l.QuantityReserved }).ToListAsync(ct))
            .GroupBy(x => x.VariantId).ToDictionary(g => g.Key, g => g.Sum(x => x.QuantityOnHand - x.QuantityReserved));
        var barcodes = (await db.Set<ProductBarcode>().Select(b => new { b.VariantId, b.Code }).ToListAsync(ct)).ToLookup(b => b.VariantId, b => b.Code);
        return rows.Select(r => new SellableProduct(r.Id, r.Sku, r.Name, r.CategoryCode, r.Category, r.Unit, r.AllowsDecimals, r.UnitPrice,
            Quantities.Round6(stock.GetValueOrDefault(r.Id)), barcodes[r.Id].ToList())).ToList();
    }
}

// ------------------------------------------------------------------------------------------------ cobrar
public sealed record SaleLineInput(string Sku, decimal Quantity, decimal DiscountPercent = 0);

/// <summary>
/// Cobra una venta del punto de venta en UNA transacción: pedido confirmado, salida de stock (VENTA POS) con el poka-yoke
/// del dominio, factura emitida con el IVA incluido, pago en el turno de caja y asiento contable (ventas, IVA débito y
/// costo de ventas al costo promedio). Si otra caja vendió la última unidad, se reintenta con el stock real.
/// </summary>
[RequiresPermission(PermissionCodes.PosOperate)]
[RequiresPermission(PermissionCodes.MovementsRegisterSales)]
public sealed record CheckoutCommand(string CustomerCode, string PaymentMethodCode, IReadOnlyList<SaleLineInput> Lines, decimal? CashReceived = null,
    string? PaymentReference = null) : IRequest<CheckoutResult>, IAuditableRequest
{
    public object AuditDetails => new { CustomerCode, PaymentMethodCode, Lines = Lines.Select(l => new { l.Sku, l.Quantity, l.DiscountPercent }), CashReceived };
}

public sealed record CheckoutResult(string InvoiceNumber, string OrderNumber, DateTimeOffset IssuedAt, decimal Total, decimal Tax, decimal Change,
    string Customer, string PaymentMethod, IReadOnlyList<ReceiptLine> Lines);

public sealed class CheckoutValidator : AbstractValidator<CheckoutCommand>
{
    public CheckoutValidator()
    {
        RuleFor(x => x.CustomerCode).NotEmpty().WithMessage("Elija el cliente.");
        RuleFor(x => x.PaymentMethodCode).NotEmpty().WithMessage("Elija el medio de pago.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Agregue al menos un producto.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Cada cantidad debe ser mayor que 0.");
            l.RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 100).WithMessage("El descuento va de 0 a 100 %.");
        });
    }
}

public sealed class CheckoutHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<CheckoutCommand, CheckoutResult>
{
    public async Task<CheckoutResult> Handle(CheckoutCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para vender.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var session = await db.Set<PosSession>().Where(s => s.Status == PosSessionStatus.Open && s.OpenedByUserId == userId)
                                  .OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync(ct)
                              ?? throw new DomainException("pos.closed", "Abra un turno de caja antes de cobrar.");
                var register = await db.Set<PosRegister>().FirstAsync(x => x.Id == session.PosRegisterId, ct);
                var (result, _) = await SaleWriter.SellAsync(db, clock, userId, new SaleOrigin(session.BranchId, register.WarehouseId, session.Id, SaleChannels.Pos),
                    request.CustomerCode, request.PaymentMethodCode, request.Lines, request.CashReceived, request.PaymentReference, ct);
                await db.SaveChangesAsync(ct);
                return result;
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }
}

/// <summary>Canales de venta (van en el evento sale.completed y en la auditoría).</summary>
public static class SaleChannels
{
    public const string Pos = "pos";
    public const string Api = "api";
}

/// <summary>V4 · Origen de una venta: caja (turno abierto, arco a la sesión) o canal externo (arco al almacén de la
/// sucursal, sin turno).</summary>
internal sealed record SaleOrigin(Guid BranchId, Guid WarehouseId, Guid? PosSessionId, string Channel);

/// <summary>
/// Registro de una venta cobrada (lo comparten la caja y los pedidos del e-commerce por la API): pedido confirmado,
/// salida de stock con el poka-yoke del dominio, factura con el IVA incluido, pago y asiento (ventas, IVA débito y costo
/// de ventas al costo promedio). Publica <c>sale.completed</c> en el outbox. NO guarda: el caso de uso guarda (y
/// reintenta ante un conflicto de concurrencia).
/// </summary>
internal static class SaleWriter
{
    public static async Task<(CheckoutResult Result, SalesOrder Order)> SellAsync(IMinvDbContext db, IClock clock, Guid userId, SaleOrigin origin,
        string customerCodeInput, string paymentMethodCode, IReadOnlyList<SaleLineInput> lines, decimal? cashReceived, string? paymentReference,
        CancellationToken ct)
    {
        var customerCode = customerCodeInput.Trim().ToUpperInvariant();
        var customer = await db.Set<Customer>().FirstOrDefaultAsync(c => c.Code == customerCode && c.IsActive, ct)
                       ?? throw new NotFoundException($"El cliente {customerCode} no existe o está inactivo.");
        var methodCode = paymentMethodCode.Trim().ToUpperInvariant();
        var method = await db.Set<PaymentMethod>().FirstOrDefaultAsync(m => m.Code == methodCode && m.IsActive, ct)
                     ?? throw new NotFoundException($"El medio de pago {methodCode} no existe.");
        Guard.That(!method.RequiresReference || !string.IsNullOrWhiteSpace(paymentReference), "payment.reference",
            $"El pago con {method.Name} exige una referencia (número de operación o voucher).");
        var lookups = new InventoryLookups(db);
        var config = await lookups.ConfigAsync(ct);
        var warehouseId = origin.WarehouseId;
        var bins = lookups.BinIdsOf(warehouseId);
        var today = clock.TodayIn(config.TimeZoneId);
        var now = clock.UtcNow;
        var priceList = await db.Set<PriceList>().FirstAsync(l => l.IsDefault, ct);
        var saleType = await lookups.MovementTypeAsync(MovementTypeCodes.Sale, ct);
        var taxRate = await Pricing.TaxRateAsync(db, today, ct);
        var taxRateId = await (from x in db.Set<TaxRate>()
                               join t in db.Set<Tax>() on x.TaxId equals t.Id
                               where t.Code == "IVA" && x.ValidFrom <= today && (x.ValidTo == null || x.ValidTo >= today)
                               orderby x.ValidFrom descending
                               select (Guid?)x.Id).FirstOrDefaultAsync(ct);

        var orderNumber = await Documents.NextForBranchAsync<SalesOrder>(db, o => o.Number, "PV", origin.BranchId, ct);
        var invoiceNumber = await Documents.NextForBranchAsync<Invoice>(db, i => i.Number, "F", origin.BranchId, ct);
        // Arco exclusivo (ck_sales_orders_origen): la venta de caja se asocia a la sesión (el almacén sale de la caja); la
        // del canal externo, al almacén de la sucursal. Nunca a ambos.
        var order = new SalesOrder(customer.TenantId, origin.BranchId, orderNumber, customer.Id, origin.PosSessionId,
            origin.PosSessionId is null ? warehouseId : null, priceList.Id, today);
        var items = new List<(SalesOrderLine Line, VariantInfo Item)>();
        foreach (var input in lines)
        {
            var item = await lookups.VariantBySkuAsync(input.Sku, ct);
            Guard.That(item.Product.IsActive && item.Variant.IsActive, "product.inactive", $"El producto {item.Variant.Sku} está inactivo.");
            Quantities.EnsureAllowed(input.Quantity, item.Unit.AllowsDecimals, item.Unit.UnitCode);
            var price = await db.Set<PriceListItem>().Where(i => i.PriceListId == priceList.Id && i.VariantId == item.Variant.Id)
                            .Select(i => (decimal?)i.UnitPrice).FirstOrDefaultAsync(ct)
                        ?? throw new DomainException("price.missing", $"{item.Variant.Sku} no tiene precio en la lista {priceList.Name}.");
            items.Add((order.AddLine(item.Variant.Id, item.Product.BaseUnitId, input.Quantity, price, input.DiscountPercent), item));
        }
        order.Confirm();

        // Salidas de stock: de la posición con más disponible (el dominio bloquea el negativo)
        var cost = 0m;
        foreach (var (line, item) in items)
        {
            var batch = await lookups.BatchAsync(item.Variant, null, ct);
            var level = await (from l in db.Set<StockLevel>()
                               where l.BatchId == batch.Id && bins.Contains(l.BinId)
                               orderby l.QuantityOnHand - l.QuantityReserved descending
                               select l).FirstOrDefaultAsync(ct);
            if (level is null)
            {
                (level, _) = await lookups.StockLevelAsync(item.Variant.TenantId, await PurchasingBins.BinForAsync(db, item.Variant.Id, warehouseId, bins, ct), batch.Id, ct);
            }
            var context = new MovementContext(userId, today, now, invoiceNumber, $"Venta {orderNumber} · {customer.Name}");
            var movement = level.Register(saleType, line.Quantity, item.Unit, context);
            db.Set<StockMovement>().Add(movement);
            line.LinkMovement(movement.Id);
            cost += line.Quantity * await AverageCosts.CurrentAsync(db, item.Variant.Id, warehouseId, ct);
        }
        order.MarkFulfilled();

        var invoice = new Invoice(customer.TenantId, origin.BranchId, invoiceNumber, order.Id, null);
        var tax = 0m;
        foreach (var (line, _) in items)
        {
            var lineTax = Pricing.IncludedTax(line.Amount, taxRate);
            tax += lineTax;
            invoice.AddLine(line.Id, taxRateId, lineTax);
        }
        invoice.Issue(now);
        order.MarkInvoiced();
        var total = order.Total;
        var change = 0m;
        if (method.OpensCashDrawer && cashReceived is { } received)
        {
            Guard.That(received >= total, "payment.insufficient", $"El efectivo recibido ({received:0.00}) no cubre el total ({total:0.00}).");
            change = JournalPoster.Money(received - total);
        }
        db.Set<SalesOrder>().Add(order);
        db.Set<Invoice>().Add(invoice);
        db.Set<Payment>().Add(new Payment(customer.TenantId, origin.BranchId, invoice.Id, method.Id, total, paymentReference, now, origin.PosSessionId, userId));
        await JournalPoster.PostAsync(db, customer.TenantId, origin.BranchId, userId, today, $"Venta {invoiceNumber} · {customer.Name} · {method.Name}",
        [
            new JournalLineSpec(method.OpensCashDrawer ? AccountCodes.Cash : AccountCodes.Bank, total, 0),
            new JournalLineSpec(AccountCodes.Sales, 0, total - tax),
            new JournalLineSpec(AccountCodes.VatDebit, 0, tax),
            new JournalLineSpec(AccountCodes.CostOfSales, JournalPoster.Money(cost), 0),
            new JournalLineSpec(AccountCodes.Inventory, 0, JournalPoster.Money(cost)),
        ], now, order.Id, ct);
        db.Publish(new SaleCompletedEvent(invoiceNumber, orderNumber, origin.BranchId, customer.Code, total, JournalPoster.Money(tax), origin.Channel,
            items.Select(x => new SaleEventLine(x.Item.Variant.Sku, x.Line.Quantity, x.Line.UnitPrice, x.Line.DiscountPercent)).ToList(), now));
        var result = new CheckoutResult(invoiceNumber, orderNumber, now, total, JournalPoster.Money(tax), change, customer.Name, method.Name,
            items.Select(x => new ReceiptLine(x.Item.Product.Name, x.Line.Quantity, x.Line.Amount / x.Line.Quantity)).ToList());
        return (result, order);
    }
}

// ------------------------------------------------------------------------------------------------ historial y anulación
public sealed record SaleRow(string InvoiceNumber, string OrderNumber, DateTimeOffset IssuedAt, DateOnly Date, string CustomerCode, string Customer,
    string Cashier, string PaymentMethod, int Items, decimal Total, decimal Tax, InvoiceStatus Status, string? VoidReason);

public sealed record SaleLineRow(string Sku, string Name, decimal Quantity, string Unit, decimal UnitPrice, decimal DiscountPercent, decimal Amount);

[RequiresPermission(PermissionCodes.SalesView)]
public sealed record GetSalesQuery(DateOnly From, DateOnly To) : IRequest<IReadOnlyList<SaleRow>>;

public sealed class GetSalesHandler(IMinvDbContext db) : IRequestHandler<GetSalesQuery, IReadOnlyList<SaleRow>>
{
    public async Task<IReadOnlyList<SaleRow>> Handle(GetSalesQuery request, CancellationToken ct)
    {
        var start = request.From;
        var end = request.To;
        var rows = await (from i in db.Set<Invoice>()
                          join so in db.Set<SalesOrder>() on i.SalesOrderId equals so.Id
                          join c in db.Set<Customer>() on so.CustomerId equals c.Id
                          join p in db.Set<Payment>() on i.Id equals p.InvoiceId
                          join m in db.Set<PaymentMethod>() on p.PaymentMethodId equals m.Id
                          join u in db.Set<User>() on p.RecordedByUserId equals u.Id
                          where so.OrderDate >= start && so.OrderDate <= end && i.IssuedAt != null
                          orderby i.IssuedAt descending
                          select new
                          {
                              i.Number, Order = so.Number, IssuedAt = i.IssuedAt!.Value, so.OrderDate, CustomerCode = c.Code, Customer = c.Name,
                              Cashier = u.DisplayName, Method = m.Name, Items = so.Lines.Count, Total = p.Amount,
                              Tax = i.Lines.Sum(l => l.TaxAmount), i.Status, i.VoidReason,
                          }).ToListAsync(ct);
        return rows.Select(x => new SaleRow(x.Number, x.Order, x.IssuedAt, x.OrderDate, x.CustomerCode, x.Customer, x.Cashier, x.Method, x.Items,
            x.Total, x.Tax, x.Status, x.VoidReason)).ToList();
    }
}

[RequiresPermission(PermissionCodes.SalesView)]
public sealed record GetSaleLinesQuery(string InvoiceNumber) : IRequest<IReadOnlyList<SaleLineRow>>;

public sealed class GetSaleLinesHandler(IMinvDbContext db) : IRequestHandler<GetSaleLinesQuery, IReadOnlyList<SaleLineRow>>
{
    public async Task<IReadOnlyList<SaleLineRow>> Handle(GetSaleLinesQuery request, CancellationToken ct)
    {
        var number = request.InvoiceNumber.Trim().ToUpperInvariant();
        var rows = await (from i in db.Set<Invoice>()
                          join l in db.Set<SalesOrderLine>() on i.SalesOrderId equals l.SalesOrderId
                          join v in db.Set<ProductVariant>() on l.VariantId equals v.Id
                          join p in db.Set<Product>() on v.ProductId equals p.Id
                          join u in db.Set<UnitOfMeasure>() on l.UnitId equals u.Id
                          where i.Number == number
                          select new { v.Sku, p.Name, l.Quantity, Unit = u.Code, l.UnitPrice, l.DiscountPercent }).ToListAsync(ct);
        return rows.Select(x => new SaleLineRow(x.Sku, x.Name, x.Quantity, x.Unit, x.UnitPrice, x.DiscountPercent,
            decimal.Round(x.Quantity * x.UnitPrice * (1 - x.DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero))).ToList();
    }
}

/// <summary>
/// Anula una factura: queda el registro con el motivo, el stock vuelve con DEVOLUCIÓN DE CLIENTE (movimientos
/// compensatorios; nunca se borra nada) y se registra el asiento inverso.
/// </summary>
[RequiresPermission(PermissionCodes.PosOperate)]
[RequiresPermission(PermissionCodes.SalesView)]
public sealed record VoidSaleCommand(string InvoiceNumber, string Reason) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { InvoiceNumber, Reason };
}

public sealed class VoidSaleValidator : AbstractValidator<VoidSaleCommand>
{
    public VoidSaleValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("Indique el motivo de la anulación.").MaximumLength(200);
}

public sealed class VoidSaleHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<VoidSaleCommand, string>
{
    public async Task<string> Handle(VoidSaleCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para anular.");
        var number = request.InvoiceNumber.Trim().ToUpperInvariant();
        var invoice = await db.Set<Invoice>().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Number == number, ct)
                      ?? throw new NotFoundException($"La factura {number} no existe.");
        var order = await db.Set<SalesOrder>().Include(o => o.Lines).FirstAsync(o => o.Id == invoice.SalesOrderId, ct);
        var payment = await db.Set<Payment>().FirstAsync(p => p.InvoiceId == invoice.Id, ct);
        var method = await db.Set<PaymentMethod>().FirstAsync(m => m.Id == payment.PaymentMethodId, ct);
        var lookups = new InventoryLookups(db);
        var config = await lookups.ConfigAsync(ct);
        var today = clock.TodayIn(config.TimeZoneId);
        var now = clock.UtcNow;
        var warehouseId = order.WarehouseId ?? await (from s in db.Set<PosSession>()
                                                      join reg in db.Set<PosRegister>() on s.PosRegisterId equals reg.Id
                                                      where s.Id == order.PosSessionId
                                                      select reg.WarehouseId).FirstAsync(ct);
        invoice.Void(request.Reason.Trim(), now);
        var returnType = await lookups.MovementTypeAsync(MovementTypeCodes.SaleReturn, ct);
        var cost = 0m;
        foreach (var line in order.Lines.Where(l => l.StockMovementId is not null))
        {
            var original = await db.Set<StockMovement>().FirstAsync(m => m.Id == line.StockMovementId, ct);
            var level = await db.Set<StockLevel>().FirstAsync(l => l.Id == original.StockLevelId, ct);
            var variant = await db.Set<ProductVariant>().FirstAsync(v => v.Id == line.VariantId, ct);
            var product = await db.Set<Product>().FirstAsync(p => p.Id == variant.ProductId, ct);
            var unit = await db.Set<UnitOfMeasure>().FirstAsync(u => u.Id == product.BaseUnitId, ct);
            var context = new MovementContext(userId, today, now, invoice.Number, $"Anulación de {invoice.Number}: {request.Reason.Trim()}");
            db.Set<StockMovement>().Add(level.Register(returnType, line.Quantity, new UnitRule(unit.Code, unit.AllowsDecimals), context));
            cost += line.Quantity * await AverageCosts.CurrentAsync(db, variant.Id, warehouseId, ct);
        }
        var tax = invoice.Lines.Sum(l => l.TaxAmount);
        await JournalPoster.PostAsync(db, invoice.TenantId, invoice.BranchId, userId, today, $"Anulación de la venta {invoice.Number}: {request.Reason.Trim()}",
        [
            new JournalLineSpec(AccountCodes.Sales, payment.Amount - tax, 0),
            new JournalLineSpec(AccountCodes.VatDebit, tax, 0),
            new JournalLineSpec(method.OpensCashDrawer ? AccountCodes.Cash : AccountCodes.Bank, 0, payment.Amount),
            new JournalLineSpec(AccountCodes.Inventory, JournalPoster.Money(cost), 0),
            new JournalLineSpec(AccountCodes.CostOfSales, 0, JournalPoster.Money(cost)),
        ], now, invoice.Id, ct);
        db.Publish(new SaleVoidedEvent(invoice.Number, invoice.BranchId, payment.Amount, request.Reason.Trim(), now));
        await db.SaveChangesAsync(ct);
        return $"✔ Factura {invoice.Number} anulada: el stock volvió y se registró el asiento inverso.";
    }
}

internal static class Cash
{
    public static async Task<(int Tickets, decimal Sales, decimal CashSales, decimal CashIn, decimal CashOut)> TotalsAsync(IMinvDbContext db, Guid sessionId,
        CancellationToken ct)
    {
        var payments = await (from p in db.Set<Payment>()
                              join m in db.Set<PaymentMethod>() on p.PaymentMethodId equals m.Id
                              join i in db.Set<Invoice>() on p.InvoiceId equals i.Id
                              where p.PosSessionId == sessionId && i.Status == InvoiceStatus.Issued
                              select new { p.Amount, m.OpensCashDrawer }).ToListAsync(ct);
        var cash = await db.Set<CashMovement>().Where(c => c.PosSessionId == sessionId).Select(c => new { c.Direction, c.Amount }).ToListAsync(ct);
        return (payments.Count, payments.Sum(p => p.Amount), payments.Where(p => p.OpensCashDrawer).Sum(p => p.Amount),
            cash.Where(c => c.Direction == CashDirection.In).Sum(c => c.Amount), cash.Where(c => c.Direction == CashDirection.Out).Sum(c => c.Amount));
    }
}

/// <summary>Posiciones para las salidas cuando el producto todavía no tiene existencia en el almacén.</summary>
internal static class PurchasingBins
{
    public static Task<Guid> BinForAsync(IMinvDbContext db, Guid variantId, Guid warehouseId, IQueryable<Guid> bins, CancellationToken ct) =>
        Purchasing.Receipts.BinForAsync(db, variantId, warehouseId, bins, ct);
}
