using System.Globalization;
using System.Text;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Inventory;
using MINV.Application.Sales;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Application.Integration;

// ================================================================================================ pedidos externos
public sealed record ExternalOrderResult(string ExternalId, string OrderNumber, string InvoiceNumber, string BranchCode, decimal Total, decimal Tax,
    DateTimeOffset IssuedAt, bool Replayed);

/// <summary>
/// V4 · Pedido del e-commerce (o de un ERP) registrado como venta cobrada en el almacén de una sucursal: mismo flujo que la
/// caja (poka-yoke, factura con IVA, pago, asiento y evento <c>sale.completed</c>) pero sin turno de caja. IDEMPOTENTE:
/// el par (canal = API Key, id externo) es único; repetir el mismo pedido devuelve la venta original (<c>Replayed</c>) y
/// el mismo id con otro contenido se rechaza (422) en lugar de vender dos veces.
/// </summary>
[RequiresModule(LicenseModuleCodes.ApiIntegrations)]
[RequiresPermission(PermissionCodes.PosOperate)]
[RequiresPermission(PermissionCodes.MovementsRegisterSales)]
public sealed record CreateExternalOrderCommand(string ExternalId, string CustomerCode, string PaymentMethodCode, IReadOnlyList<SaleLineInput> Lines,
    string? PaymentReference = null, string? WarehouseCode = null) : IRequest<ExternalOrderResult>, IAuditableRequest
{
    public object AuditDetails => new { ExternalId, CustomerCode, PaymentMethodCode, Lines, PaymentReference, WarehouseCode };
}

public sealed class CreateExternalOrderValidator : AbstractValidator<CreateExternalOrderCommand>
{
    public CreateExternalOrderValidator()
    {
        RuleFor(x => x.ExternalId).NotEmpty().WithMessage("Indique el id del pedido en su sistema (externalId).").MaximumLength(100);
        RuleFor(x => x.CustomerCode).NotEmpty().WithMessage("Indique el cliente.");
        RuleFor(x => x.PaymentMethodCode).NotEmpty().WithMessage("Indique el medio de pago.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("El pedido no tiene productos.").Must(l => l.Count <= 200).WithMessage("Máximo 200 líneas por pedido.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.Sku).NotEmpty().WithMessage("Cada línea necesita el SKU.");
            l.RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Cada cantidad debe ser mayor que 0.");
            l.RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 100).WithMessage("El descuento va de 0 a 100 %.");
        });
    }
}

public sealed class CreateExternalOrderHandler(IMinvDbContext db, ICurrentUser user, IRequestOrigin origin, IClock clock)
    : IRequestHandler<CreateExternalOrderCommand, ExternalOrderResult>
{
    public async Task<ExternalOrderResult> Handle(CreateExternalOrderCommand r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("La petición no está autenticada.");
        var channel = origin.ApiKeyId is { } key ? $"api-{key:N}" : origin.Channel;
        var externalId = r.ExternalId.Trim();
        var hash = RequestHash(r);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var existing = await db.Set<ExternalOrder>().FirstOrDefaultAsync(x => x.Channel == channel && x.ExternalId == externalId, ct);
                if (existing is not null)
                {
                    if (existing.RequestHash != hash)
                    {
                        throw new IdempotencyConflictException(
                            $"El pedido {externalId} ya se registró con otro contenido (factura {existing.InvoiceNumber}): use otro externalId.");
                    }
                    return await ReplayAsync(existing, ct);
                }
                var lookups = new InventoryLookups(db);
                var config = await lookups.ConfigAsync(ct);
                var warehouse = await lookups.WarehouseAsync(r.WarehouseCode, config, ct);
                if (!db.Branches.Allows(warehouse.BranchId))
                {
                    throw new AccessDeniedException($"El almacén {warehouse.Code} no es de las sucursales de esta llave.");
                }
                var (result, order) = await SaleWriter.SellAsync(db, clock, userId, new SaleOrigin(warehouse.BranchId, warehouse.Id, null, SaleChannels.Api),
                    r.CustomerCode, r.PaymentMethodCode, r.Lines, null, r.PaymentReference, ct);
                db.Set<ExternalOrder>().Add(new ExternalOrder(order.TenantId, warehouse.BranchId, channel, externalId, hash, order.Id, result.InvoiceNumber,
                    clock.UtcNow));
                await db.SaveChangesAsync(ct);
                var branchCode = await db.Set<Branch>().Where(b => b.Id == warehouse.BranchId).Select(b => b.Code).FirstAsync(ct);
                return new ExternalOrderResult(externalId, result.OrderNumber, result.InvoiceNumber, branchCode, result.Total, result.Tax, result.IssuedAt,
                    false);
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                // Dos reintentos simultáneos del mismo pedido: el segundo choca con el índice único y aquí encuentra el primero
                db.ClearTracking();
            }
        }
    }

    private async Task<ExternalOrderResult> ReplayAsync(ExternalOrder existing, CancellationToken ct)
    {
        var order = await db.Set<SalesOrder>().Include(o => o.Lines).FirstAsync(o => o.Id == existing.SalesOrderId, ct);
        var invoice = await db.Set<Invoice>().Include(i => i.Lines).FirstAsync(i => i.Number == existing.InvoiceNumber, ct);
        var branchCode = await db.Set<Branch>().Where(b => b.Id == existing.BranchId).Select(b => b.Code).FirstAsync(ct);
        return new ExternalOrderResult(existing.ExternalId, order.Number, invoice.Number, branchCode, order.Total,
            JournalPoster.Money(invoice.Lines.Sum(l => l.TaxAmount)), invoice.IssuedAt ?? existing.ReceivedAt, true);
    }

    /// <summary>SHA-256 del contenido normalizado (orden de líneas y formato numérico invariables).</summary>
    public static string RequestHash(CreateExternalOrderCommand r)
    {
        var text = new StringBuilder()
            .Append(r.CustomerCode.Trim().ToUpperInvariant()).Append('|')
            .Append(r.PaymentMethodCode.Trim().ToUpperInvariant()).Append('|')
            .Append(r.PaymentReference?.Trim()).Append('|')
            .Append(r.WarehouseCode?.Trim().ToUpperInvariant()).Append('|');
        foreach (var line in r.Lines.OrderBy(l => l.Sku.Trim().ToUpperInvariant(), StringComparer.Ordinal))
        {
            text.Append(line.Sku.Trim().ToUpperInvariant()).Append(':')
                .Append(Quantities.Round6(line.Quantity).ToString("0.######", CultureInfo.InvariantCulture)).Append(':')
                .Append(line.DiscountPercent.ToString("0.######", CultureInfo.InvariantCulture)).Append(';');
        }
        return ApiKeyTokens.Hash(text.ToString());
    }
}

/// <summary>V4 · Pedido externo ya registrado por el canal de la llave (el mismo resultado que devolvió el POST).</summary>
[RequiresModule(LicenseModuleCodes.ApiIntegrations)]
[RequiresPermission(PermissionCodes.SalesView)]
public sealed record GetExternalOrderQuery(string ExternalId) : IRequest<ExternalOrderResult>;

public sealed class GetExternalOrderHandler(IMinvDbContext db, IRequestOrigin origin) : IRequestHandler<GetExternalOrderQuery, ExternalOrderResult>
{
    public async Task<ExternalOrderResult> Handle(GetExternalOrderQuery request, CancellationToken ct)
    {
        var channel = origin.ApiKeyId is { } key ? $"api-{key:N}" : origin.Channel;
        var externalId = request.ExternalId.Trim();
        var order = await db.Set<ExternalOrder>().FirstOrDefaultAsync(x => x.Channel == channel && x.ExternalId == externalId, ct)
                    ?? throw new NotFoundException($"El pedido {externalId} no existe para esta integración.");
        var sales = await db.Set<SalesOrder>().Include(o => o.Lines).FirstAsync(o => o.Id == order.SalesOrderId, ct);
        var invoice = await db.Set<Invoice>().Include(i => i.Lines).FirstAsync(i => i.Number == order.InvoiceNumber, ct);
        var branchCode = await db.Set<Branch>().Where(b => b.Id == order.BranchId).Select(b => b.Code).FirstAsync(ct);
        return new ExternalOrderResult(order.ExternalId, sales.Number, invoice.Number, branchCode, sales.Total,
            JournalPoster.Money(invoice.Lines.Sum(l => l.TaxAmount)), invoice.IssuedAt ?? order.ReceivedAt, true);
    }
}

// ================================================================================================ consultas del API
public sealed record Page<T>(int PageNumber, int PageSize, int Total, IReadOnlyList<T> Items);

public sealed record ApiProduct(string Sku, string Name, string Category, string Unit, decimal Price, IReadOnlyList<string> Barcodes, bool IsActive);

/// <summary>V4 · Catálogo paginado para el e-commerce (máximo 500 por página): SKU, precio de la lista por defecto y
/// códigos de barras. El catálogo es global de la empresa.</summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetApiCatalogQuery(int PageNumber = 1, int PageSize = 100, string? Search = null) : IRequest<Page<ApiProduct>>;

public sealed class GetApiCatalogHandler(IMinvDbContext db) : IRequestHandler<GetApiCatalogQuery, Page<ApiProduct>>
{
    public const int MaxPageSize = 500;

    public async Task<Page<ApiProduct>> Handle(GetApiCatalogQuery request, CancellationToken ct)
    {
        var size = Math.Clamp(request.PageSize, 1, MaxPageSize);
        var page = Math.Max(1, request.PageNumber);
        var priceList = await db.Set<PriceList>().Where(l => l.IsDefault).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);
        var query = from v in db.Set<ProductVariant>()
                    join p in db.Set<Product>() on v.ProductId equals p.Id
                    join c in db.Set<Category>() on p.CategoryId equals c.Id
                    join u in db.Set<UnitOfMeasure>() on p.BaseUnitId equals u.Id
                    select new { v, p, Category = c.Name, Unit = u.Code };
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToUpperInvariant();
            query = query.Where(x => x.v.Sku.Contains(search) || x.p.Name.ToUpper().Contains(search));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.v.Sku).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var ids = rows.Select(x => x.v.Id).ToList();
        var prices = await db.Set<PriceListItem>().Where(i => i.PriceListId == priceList && ids.Contains(i.VariantId))
            .ToDictionaryAsync(i => i.VariantId, i => i.UnitPrice, ct);
        var barcodes = (await db.Set<ProductBarcode>().Where(b => ids.Contains(b.VariantId)).Select(b => new { b.VariantId, b.Code }).ToListAsync(ct))
            .ToLookup(b => b.VariantId, b => b.Code);
        return new Page<ApiProduct>(page, size, total, rows.Select(x => new ApiProduct(x.v.Sku, x.p.Name, x.Category, x.Unit,
            prices.GetValueOrDefault(x.v.Id), barcodes[x.v.Id].ToList(), x.p.IsActive && x.v.IsActive)).ToList());
    }
}

public sealed record ApiStock(string Sku, string BranchCode, string WarehouseCode, decimal OnHand, decimal Reserved, decimal Available);

/// <summary>V4 · Existencias por sucursal y almacén (solo las sucursales de la llave), paginadas.</summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetApiStockQuery(string? BranchCode = null, string? Sku = null, int PageNumber = 1, int PageSize = 100) : IRequest<Page<ApiStock>>;

public sealed class GetApiStockHandler(IMinvDbContext db) : IRequestHandler<GetApiStockQuery, Page<ApiStock>>
{
    public async Task<Page<ApiStock>> Handle(GetApiStockQuery request, CancellationToken ct)
    {
        var size = Math.Clamp(request.PageSize, 1, GetApiCatalogHandler.MaxPageSize);
        var page = Math.Max(1, request.PageNumber);
        var levels = from l in db.Set<StockLevel>()
                     join b in db.Set<Batch>() on l.BatchId equals b.Id
                     join v in db.Set<ProductVariant>() on b.VariantId equals v.Id
                     join bin in db.Set<Bin>() on l.BinId equals bin.Id
                     join s in db.Set<Shelf>() on bin.ShelfId equals s.Id
                     join r in db.Set<Rack>() on s.RackId equals r.Id
                     join a in db.Set<Aisle>() on r.AisleId equals a.Id
                     join z in db.Set<Zone>() on a.ZoneId equals z.Id
                     join w in db.Set<Warehouse>() on z.WarehouseId equals w.Id
                     join br in db.Set<Branch>() on w.BranchId equals br.Id
                     select new { v.Sku, Branch = br.Code, Warehouse = w.Code, l.QuantityOnHand, l.QuantityReserved };
        if (!string.IsNullOrWhiteSpace(request.BranchCode))
        {
            var code = request.BranchCode.Trim().ToUpperInvariant();
            levels = levels.Where(x => x.Branch == code);
        }
        if (!string.IsNullOrWhiteSpace(request.Sku))
        {
            var sku = request.Sku.Trim().ToUpperInvariant();
            levels = levels.Where(x => x.Sku == sku);
        }
        var grouped = levels.GroupBy(x => new { x.Sku, x.Branch, x.Warehouse })
            .Select(g => new { g.Key.Sku, g.Key.Branch, g.Key.Warehouse, OnHand = g.Sum(x => x.QuantityOnHand), Reserved = g.Sum(x => x.QuantityReserved) });
        var total = await grouped.CountAsync(ct);
        var rows = await grouped.OrderBy(x => x.Sku).ThenBy(x => x.Branch).ThenBy(x => x.Warehouse).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new Page<ApiStock>(page, size, total, rows.Select(x => new ApiStock(x.Sku, x.Branch, x.Warehouse, x.OnHand, x.Reserved,
            Quantities.Round6(x.OnHand - x.Reserved))).ToList());
    }
}
