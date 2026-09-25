using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Inventory;
using MINV.Application.Inventory.Queries;
using MINV.Domain.Accounting;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Events;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Purchasing;
using MINV.Domain.Warehousing;

namespace MINV.Application.Purchasing;

public sealed record PurchaseOrderRow(Guid Id, string Number, string SupplierCode, string Supplier, DateOnly OrderDate, DateOnly? ExpectedDate,
    PurchaseOrderStatus Status, int Lines, decimal Total, decimal ReceivedPercent, string? Notes);

public sealed record PurchaseOrderLineRow(Guid LineId, string Sku, string Name, string Unit, decimal Quantity, decimal UnitCost, decimal Subtotal,
    decimal Received);

public sealed record PurchaseOrderDetail(PurchaseOrderRow Order, IReadOnlyList<PurchaseOrderLineRow> Lines, IReadOnlyList<string> Receipts);

/// <summary>Órdenes de compra (todas o de un estado), de la más reciente a la más antigua.</summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetPurchaseOrdersQuery(PurchaseOrderStatus? Status = null) : IRequest<IReadOnlyList<PurchaseOrderRow>>;

public sealed class GetPurchaseOrdersHandler(IMinvDbContext db) : IRequestHandler<GetPurchaseOrdersQuery, IReadOnlyList<PurchaseOrderRow>>
{
    public async Task<IReadOnlyList<PurchaseOrderRow>> Handle(GetPurchaseOrdersQuery request, CancellationToken ct)
    {
        var status = request.Status;
        var orders = await db.Set<PurchaseOrder>().Include(o => o.Lines).Where(o => status == null || o.Status == status)
            .OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Number).ToListAsync(ct);
        var suppliers = await db.Set<Supplier>().ToDictionaryAsync(s => s.Id, ct);
        var received = await Receipts.ReceivedByLineAsync(db, ct);
        return orders.Select(o => Row(o, suppliers[o.SupplierId], received)).ToList();
    }

    internal static PurchaseOrderRow Row(PurchaseOrder o, Supplier s, IReadOnlyDictionary<Guid, decimal> received)
    {
        var ordered = o.Lines.Sum(l => l.Quantity);
        var got = o.Lines.Sum(l => Math.Min(l.Quantity, received.GetValueOrDefault(l.Id)));
        return new PurchaseOrderRow(o.Id, o.Number, s.Code, s.LegalName, o.OrderDate, o.ExpectedDate, o.Status, o.Lines.Count,
            JournalPoster.Money(o.Total), ordered > 0 ? decimal.Round(got / ordered * 100, 0) : 0, o.Notes);
    }
}

[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetPurchaseOrderQuery(Guid Id) : IRequest<PurchaseOrderDetail>;

public sealed class GetPurchaseOrderHandler(IMinvDbContext db) : IRequestHandler<GetPurchaseOrderQuery, PurchaseOrderDetail>
{
    public async Task<PurchaseOrderDetail> Handle(GetPurchaseOrderQuery request, CancellationToken ct)
    {
        var order = await db.Set<PurchaseOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == request.Id, ct)
                    ?? throw new NotFoundException("La orden de compra no existe.");
        var supplier = await db.Set<Supplier>().FirstAsync(s => s.Id == order.SupplierId, ct);
        var received = await Receipts.ReceivedByLineAsync(db, ct);
        var variantIds = order.Lines.Select(l => l.VariantId).ToList();
        var names = await (from v in db.Set<ProductVariant>()
                           join p in db.Set<Product>() on v.ProductId equals p.Id
                           join u in db.Set<UnitOfMeasure>() on p.BaseUnitId equals u.Id
                           where variantIds.Contains(v.Id)
                           select new { v.Id, v.Sku, p.Name, Unit = u.Code }).ToDictionaryAsync(x => x.Id, ct);
        var receipts = await db.Set<GoodsReceipt>().Where(g => g.PurchaseOrderId == order.Id).OrderBy(g => g.ReceivedAt)
            .Select(g => g.Number).ToListAsync(ct);
        return new PurchaseOrderDetail(GetPurchaseOrdersHandler.Row(order, supplier, received),
            order.Lines.Select(l => new PurchaseOrderLineRow(l.Id, names[l.VariantId].Sku, names[l.VariantId].Name, names[l.VariantId].Unit,
                l.Quantity, l.UnitCost, JournalPoster.Money(l.Quantity * l.UnitCost), received.GetValueOrDefault(l.Id))).ToList(), receipts);
    }
}

public sealed record PurchaseLineInput(string Sku, decimal Quantity, decimal UnitCost);

/// <summary>Crea una orden de compra en borrador.</summary>
[RequiresPermission(PermissionCodes.PurchasingManage)]
public sealed record CreatePurchaseOrderCommand(string SupplierCode, DateOnly? ExpectedDate, string? Notes, IReadOnlyList<PurchaseLineInput> Lines)
    : IRequest<PurchaseOrderRow>, IAuditableRequest
{
    public object AuditDetails => new { SupplierCode, ExpectedDate, Notes, Lines = Lines.Select(l => new { l.Sku, l.Quantity, l.UnitCost }) };
}

public sealed class CreatePurchaseOrderValidator : AbstractValidator<CreatePurchaseOrderCommand>
{
    public CreatePurchaseOrderValidator()
    {
        RuleFor(x => x.SupplierCode).NotEmpty().WithMessage("Elija el proveedor.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Agregue al menos un producto a la orden.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Cada cantidad debe ser mayor que 0.");
            l.RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0).WithMessage("El costo no puede ser negativo.");
        });
    }
}

public sealed class CreatePurchaseOrderHandler(IMinvDbContext db, IClock clock) : IRequestHandler<CreatePurchaseOrderCommand, PurchaseOrderRow>
{
    public async Task<PurchaseOrderRow> Handle(CreatePurchaseOrderCommand r, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var order = await Purchases.CreateAsync(db, clock, r.SupplierCode, r.ExpectedDate, r.Notes, r.Lines, ct);
                await db.SaveChangesAsync(ct);
                var supplier = await db.Set<Supplier>().FirstAsync(s => s.Id == order.SupplierId, ct);
                return GetPurchaseOrdersHandler.Row(order, supplier, new Dictionary<Guid, decimal>());
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }
}

/// <summary>Convierte el pedido sugerido en órdenes de compra en borrador (una por proveedor).</summary>
[RequiresPermission(PermissionCodes.PurchasingManage)]
public sealed record CreateSuggestedPurchaseOrdersCommand : IRequest<IReadOnlyList<string>>, IAuditableRequest
{
    public object AuditDetails => new { Origen = "pedido sugerido" };
}

public sealed class CreateSuggestedPurchaseOrdersHandler(IMinvDbContext db, IClock clock, ISender sender)
    : IRequestHandler<CreateSuggestedPurchaseOrdersCommand, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(CreateSuggestedPurchaseOrdersCommand request, CancellationToken ct)
    {
        var view = await sender.Send(new GetStockProjectionQuery(), ct);
        var suppliers = await db.Set<Supplier>().Where(s => s.IsActive).ToListAsync(ct);
        var busy = await db.Set<PurchaseOrder>()
            .Where(o => o.Status == PurchaseOrderStatus.Draft || o.Status == PurchaseOrderStatus.Approved || o.Status == PurchaseOrderStatus.PartiallyReceived)
            .Select(o => o.SupplierId).Distinct().ToListAsync(ct);
        var numbers = new List<string>();
        foreach (var group in view.Result.Order.GroupBy(o => o.Supplier))
        {
            // Un proveedor con una orden abierta no recibe otra: primero se recibe o se anula la pendiente
            var supplier = suppliers.FirstOrDefault(s => s.LegalName == group.Key);
            if (supplier is null || busy.Contains(supplier.Id))
            {
                continue;
            }
            var order = await Purchases.CreateAsync(db, clock, supplier.Code, null, "Generada desde el pedido sugerido",
                group.Select(l => new PurchaseLineInput(l.Sku, l.QuantityToOrder, l.UnitCost)).ToList(), ct);
            numbers.Add(order.Number);
        }
        Guard.That(numbers.Count > 0, "purchase.nothing",
            "No hay órdenes nuevas que crear: el pedido sugerido está vacío o sus proveedores ya tienen una orden abierta.");
        await db.SaveChangesAsync(ct);
        return numbers;
    }
}

[RequiresPermission(PermissionCodes.PurchasingManage)]
public sealed record ApprovePurchaseOrderCommand(Guid Id) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Id };
}

public sealed class ApprovePurchaseOrderHandler(IMinvDbContext db) : IRequestHandler<ApprovePurchaseOrderCommand, string>
{
    public async Task<string> Handle(ApprovePurchaseOrderCommand request, CancellationToken ct)
    {
        var order = await db.Set<PurchaseOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == request.Id, ct)
                    ?? throw new NotFoundException("La orden de compra no existe.");
        order.Approve();
        await db.SaveChangesAsync(ct);
        return order.Number;
    }
}

[RequiresPermission(PermissionCodes.PurchasingManage)]
public sealed record CancelPurchaseOrderCommand(Guid Id) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Id };
}

public sealed class CancelPurchaseOrderHandler(IMinvDbContext db) : IRequestHandler<CancelPurchaseOrderCommand, string>
{
    public async Task<string> Handle(CancelPurchaseOrderCommand request, CancellationToken ct)
    {
        var order = await db.Set<PurchaseOrder>().FirstOrDefaultAsync(o => o.Id == request.Id, ct)
                    ?? throw new NotFoundException("La orden de compra no existe.");
        order.Cancel();
        await db.SaveChangesAsync(ct);
        return order.Number;
    }
}

/// <summary>
/// Recibe todo lo pendiente de una orden aprobada: entrada de stock (RECEPCIÓN DE COMPRA) en la posición del producto,
/// costo promedio ponderado, recepción contabilizada y asiento (Inventario a Proveedores). Todo en una transacción.
/// </summary>
[RequiresPermission(PermissionCodes.PurchasingManage)]
[RequiresPermission(PermissionCodes.MovementsRegisterWarehouse)]
public sealed record ReceivePurchaseOrderCommand(Guid Id, string? SupplierDocument = null) : IRequest<ReceiptResult>, IAuditableRequest
{
    public object AuditDetails => new { Id, SupplierDocument };
}

public sealed record ReceiptResult(string OrderNumber, string ReceiptNumber, int Lines, decimal Total, string JournalNumber);

public sealed class ReceivePurchaseOrderHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<ReceivePurchaseOrderCommand, ReceiptResult>
{
    public async Task<ReceiptResult> Handle(ReceivePurchaseOrderCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para recibir mercadería.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ReceiveAsync(request, userId, ct);
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }

    private async Task<ReceiptResult> ReceiveAsync(ReceivePurchaseOrderCommand request, Guid userId, CancellationToken ct)
    {
        var order = await db.Set<PurchaseOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == request.Id, ct)
                    ?? throw new NotFoundException("La orden de compra no existe.");
        var supplier = await db.Set<Supplier>().FirstAsync(s => s.Id == order.SupplierId, ct);
        var lookups = new InventoryLookups(db);
        var config = await lookups.ConfigAsync(ct);
        var today = clock.TodayIn(config.TimeZoneId);
        var now = clock.UtcNow;
        var type = await lookups.MovementTypeAsync(MovementTypeCodes.PurchaseReceipt, ct);
        var bins = lookups.BinIdsOf(order.WarehouseId);
        var received = await Receipts.ReceivedByLineAsync(db, ct);
        var number = await Documents.NextForBranchAsync<GoodsReceipt>(db, g => g.Number, "RC", order.BranchId, ct);
        // Arco exclusivo (ck_goods_receipts_origen): la recepción de una orden no repite el proveedor (sale de la orden)
        var receipt = new GoodsReceipt(order.TenantId, order.BranchId, number, order.Id, null, order.WarehouseId, now, userId, request.SupplierDocument);
        var total = 0m;
        foreach (var line in order.Lines)
        {
            var pending = Quantities.Round6(line.Quantity - received.GetValueOrDefault(line.Id));
            if (pending <= 0)
            {
                continue;
            }
            var variant = await db.Set<ProductVariant>().FirstAsync(v => v.Id == line.VariantId, ct);
            var product = await db.Set<Product>().FirstAsync(p => p.Id == variant.ProductId, ct);
            var unit = await db.Set<UnitOfMeasure>().FirstAsync(u => u.Id == product.BaseUnitId, ct);
            var binId = await Receipts.BinForAsync(db, variant.Id, order.WarehouseId, bins, ct);
            var batch = await lookups.BatchAsync(variant, null, ct);
            var (level, _) = await lookups.StockLevelAsync(order.TenantId, binId, batch.Id, ct);
            var onHandBefore = await AverageCosts.OnHandAsync(db, variant.Id, bins, ct);
            var averageBefore = await AverageCosts.CurrentAsync(db, variant.Id, order.WarehouseId, ct);
            var context = new MovementContext(userId, today, now, order.Number, $"Recepción {number} · {supplier.LegalName}");
            var movement = level.Register(type, pending, new UnitRule(unit.Code, unit.AllowsDecimals), context);
            db.Set<StockMovement>().Add(movement);
            receipt.AddLine(line.Id, level.Id, pending, line.UnitCost).LinkMovement(movement.Id);
            var average = AverageCosts.Weighted(onHandBefore, averageBefore, pending, line.UnitCost);
            await AverageCosts.RecordAsync(db, order.TenantId, order.BranchId, variant.Id, order.WarehouseId, now, average, movement.Id, ct);
            total += pending * line.UnitCost;
        }
        Guard.That(receipt.Lines.Count > 0, "purchase.received", $"La orden {order.Number} ya se recibió completa.");
        receipt.Post();
        db.Set<GoodsReceipt>().Add(receipt);
        order.RegisterReceipt(fullyReceived: true);
        var entry = await JournalPoster.PostAsync(db, order.TenantId, order.BranchId, userId, today, $"Compra {order.Number} · {supplier.LegalName} · recepción {number}",
            [new JournalLineSpec(AccountCodes.Inventory, total, 0), new JournalLineSpec(AccountCodes.Payables, 0, total)], now, receipt.Id, ct);
        db.Publish(new PurchaseReceivedEvent(order.Number, number, order.BranchId, supplier.Code, JournalPoster.Money(total), now));
        await db.SaveChangesAsync(ct);
        return new ReceiptResult(order.Number, number, receipt.Lines.Count, JournalPoster.Money(total), entry.Number);
    }
}

internal static class Purchases
{
    public static async Task<PurchaseOrder> CreateAsync(IMinvDbContext db, IClock clock, string supplierCode, DateOnly? expected, string? notes,
        IReadOnlyList<PurchaseLineInput> lines, CancellationToken ct)
    {
        var code = supplierCode.Trim().ToUpperInvariant();
        var supplier = await db.Set<Supplier>().FirstOrDefaultAsync(s => s.Code == code, ct) ?? throw new NotFoundException($"El proveedor {code} no existe.");
        Guard.That(supplier.IsActive, "supplier.inactive", $"El proveedor {supplier.LegalName} está inactivo.");
        var lookups = new InventoryLookups(db);
        var config = await lookups.ConfigAsync(ct);
        var warehouse = await lookups.WarehouseAsync(null, config, ct);
        var today = clock.TodayIn(config.TimeZoneId);
        var number = await Documents.NextForBranchAsync<PurchaseOrder>(db, o => o.Number, "OC", warehouse.BranchId, ct);
        var order = new PurchaseOrder(supplier.TenantId, warehouse.BranchId, number, supplier.Id, warehouse.Id, config.DefaultCurrencyId, today,
            expected ?? today.AddDays(Math.Max(1, supplier.LeadTimeDays)), notes);
        foreach (var l in lines)
        {
            var item = await lookups.VariantBySkuAsync(l.Sku, ct);
            Quantities.EnsureAllowed(l.Quantity, item.Unit.AllowsDecimals, item.Unit.UnitCode);
            var unitCost = l.UnitCost > 0 ? l.UnitCost : await AverageCosts.CurrentAsync(db, item.Variant.Id, warehouse.Id, ct);
            order.AddLine(item.Variant.Id, item.Product.BaseUnitId, l.Quantity, unitCost);
        }
        db.Set<PurchaseOrder>().Add(order);
        return order;
    }
}

internal static class Receipts
{
    /// <summary>Cantidad ya recibida por línea de orden (recepciones contabilizadas).</summary>
    public static async Task<IReadOnlyDictionary<Guid, decimal>> ReceivedByLineAsync(IMinvDbContext db, CancellationToken ct) =>
        await (from l in db.Set<GoodsReceiptLine>()
               join g in db.Set<GoodsReceipt>() on l.GoodsReceiptId equals g.Id
               where g.Status == GoodsReceiptStatus.Posted && l.PurchaseOrderLineId != null
               group l.Quantity by l.PurchaseOrderLineId!.Value into grp
               select new { grp.Key, Total = grp.Sum() }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);

    /// <summary>Posición donde entra (o sale) un producto: su posición asignada en el almacén, o la GENERAL.</summary>
    public static async Task<Guid> BinForAsync(IMinvDbContext db, Guid variantId, Guid warehouseId, IQueryable<Guid> bins, CancellationToken ct)
    {
        var assigned = await (from a in db.Set<BinAssignment>()
                              where a.VariantId == variantId && bins.Contains(a.BinId)
                              orderby a.IsPrimaryPick descending
                              select (Guid?)a.BinId).FirstOrDefaultAsync(ct);
        if (assigned is { } id)
        {
            return id;
        }
        var warehouse = await db.Set<Warehouse>().FirstAsync(w => w.Id == warehouseId, ct);
        return await db.Set<Bin>().Where(b => b.Code == warehouse.Code + "-GENERAL").Select(b => (Guid?)b.Id).FirstOrDefaultAsync(ct)
               ?? await db.Set<Bin>().Where(b => bins.Contains(b.Id)).OrderBy(b => b.Code).Select(b => b.Id).FirstAsync(ct);
    }
}
