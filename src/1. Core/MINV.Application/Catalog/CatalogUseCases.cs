using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Inventory;
using MINV.Domain.Accounting;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Purchasing;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Application.Catalog;

public sealed record OptionItem(string Code, string Name);

public sealed record UnitOption(string Code, string Name, bool AllowsDecimals);

/// <summary>Listas para los combos del catálogo: categorías, unidades, proveedores e impuesto vigente.</summary>
public sealed record CatalogOptions(IReadOnlyList<OptionItem> Categories, IReadOnlyList<UnitOption> Units, IReadOnlyList<OptionItem> Suppliers,
    decimal TaxRate, string PriceListName);

[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetCatalogOptionsQuery : IRequest<CatalogOptions>;

public sealed class GetCatalogOptionsHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetCatalogOptionsQuery, CatalogOptions>
{
    public async Task<CatalogOptions> Handle(GetCatalogOptionsQuery request, CancellationToken ct)
    {
        var categories = await db.Set<Category>().OrderBy(c => c.Name).Select(c => new OptionItem(c.Code, c.Name)).ToListAsync(ct);
        var units = await db.Set<UnitOfMeasure>().OrderBy(u => u.Code).Select(u => new UnitOption(u.Code, u.Name, u.AllowsDecimals)).ToListAsync(ct);
        var suppliers = await db.Set<Supplier>().Where(s => s.IsActive).OrderBy(s => s.LegalName)
            .Select(s => new OptionItem(s.Code, s.LegalName)).ToListAsync(ct);
        var config = await new InventoryLookups(db).ConfigAsync(ct);
        var rate = await Pricing.TaxRateAsync(db, clock.TodayIn(config.TimeZoneId), ct);
        var list = await db.Set<PriceList>().Where(p => p.IsDefault).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "GENERAL";
        return new CatalogOptions(categories, units, suppliers, rate, list);
    }
}

/// <summary>Producto del catálogo (galería y administración).</summary>
public sealed record CatalogItem(Guid VariantId, string Sku, string Name, string? Description, string CategoryCode, string Category, string Unit,
    string? SupplierCode, string? Supplier, decimal SalePrice, decimal UnitCost, decimal Minimum, decimal Maximum, string? Barcode,
    bool IsActive, bool HasImage, string? BinCode);

[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetCatalogQuery : IRequest<IReadOnlyList<CatalogItem>>;

public sealed class GetCatalogHandler(IMinvDbContext db) : IRequestHandler<GetCatalogQuery, IReadOnlyList<CatalogItem>>
{
    public async Task<IReadOnlyList<CatalogItem>> Handle(GetCatalogQuery request, CancellationToken ct)
    {
        var lookups = new InventoryLookups(db);
        var warehouse = await lookups.WarehouseAsync(null, await lookups.ConfigAsync(ct), ct);
        var wid = warehouse.Id;
        var rows = await (from v in db.Set<ProductVariant>()
                          join p in db.Set<Product>() on v.ProductId equals p.Id
                          join c in db.Set<Category>() on p.CategoryId equals c.Id
                          join u in db.Set<UnitOfMeasure>() on p.BaseUnitId equals u.Id
                          orderby c.Name, p.Name
                          select new
                          {
                              v.Id, v.Sku, Name = v.Name == null ? p.Name : p.Name + " · " + v.Name, p.Description, CategoryCode = c.Code,
                              Category = c.Name, Unit = u.Code, Active = p.IsActive && v.IsActive, ProductId = p.Id,
                          }).ToListAsync(ct);
        var suppliers = (await (from ps in db.Set<ProductSupplier>()
                                join s in db.Set<Supplier>() on ps.SupplierId equals s.Id
                                where ps.IsPreferred
                                select new { ps.ProductId, s.Code, s.LegalName }).ToListAsync(ct))
            .GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.First());
        var prices = (await (from i in db.Set<PriceListItem>()
                             join l in db.Set<PriceList>() on i.PriceListId equals l.Id
                             where l.IsDefault
                             select new { i.VariantId, i.UnitPrice }).ToListAsync(ct))
            .GroupBy(x => x.VariantId).ToDictionary(g => g.Key, g => g.First().UnitPrice);
        var costs = (await db.Set<AverageCostHistory>().Where(x => x.WarehouseId == wid)
                .Select(x => new { x.VariantId, x.EffectiveAt, x.AverageCost }).ToListAsync(ct))
            .GroupBy(x => x.VariantId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.EffectiveAt).First().AverageCost);
        var policies = await db.Set<ProductStockPolicy>().Where(x => x.WarehouseId == wid)
            .ToDictionaryAsync(x => x.VariantId, x => new { x.MinQuantity, x.MaxQuantity }, ct);
        var barcodes = (await db.Set<ProductBarcode>().OrderByDescending(b => b.IsPrimary).Select(b => new { b.VariantId, b.Code }).ToListAsync(ct))
            .GroupBy(b => b.VariantId).ToDictionary(g => g.Key, g => g.First().Code);
        var images = (await db.Set<ProductImage>().Select(i => i.VariantId).ToListAsync(ct)).ToHashSet();
        var bins = (await (from a in db.Set<BinAssignment>()
                           join b in db.Set<Bin>() on a.BinId equals b.Id
                           select new { a.VariantId, b.Code, a.IsPrimaryPick }).ToListAsync(ct))
            .GroupBy(x => x.VariantId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.IsPrimaryPick).First().Code);
        return rows.Select(r =>
        {
            suppliers.TryGetValue(r.ProductId, out var s);
            policies.TryGetValue(r.Id, out var pol);
            return new CatalogItem(r.Id, r.Sku, r.Name, r.Description, r.CategoryCode, r.Category, r.Unit, s?.Code, s?.LegalName,
                prices.GetValueOrDefault(r.Id), costs.GetValueOrDefault(r.Id), pol?.MinQuantity ?? 0, pol?.MaxQuantity ?? 0,
                barcodes.GetValueOrDefault(r.Id), r.Active, images.Contains(r.Id), bins.GetValueOrDefault(r.Id));
        }).ToList();
    }
}

/// <summary>Crear o modificar un producto con su política de stock, precio, costo, proveedor preferido y código de
/// barras (en la V2.1: 05_PRODUCTOS). El SKU y la unidad no cambian después de crear el producto.</summary>
[RequiresPermission(PermissionCodes.CatalogManage)]
public sealed record SaveProductCommand(
    string? OriginalSku,
    string Sku,
    string Name,
    string? Description,
    string CategoryCode,
    string UnitCode,
    string? SupplierCode,
    decimal Minimum,
    decimal Maximum,
    decimal UnitCost,
    decimal SalePrice,
    string? Barcode,
    bool IsActive,
    string? BinCode = null) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { OriginalSku, Sku, Name, CategoryCode, UnitCode, SupplierCode, Minimum, Maximum, UnitCost, SalePrice, Barcode, IsActive, BinCode };
}

public sealed class SaveProductValidator : AbstractValidator<SaveProductCommand>
{
    public SaveProductValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().WithMessage("Indique el SKU.").MaximumLength(40);
        RuleFor(x => x.Name).NotEmpty().WithMessage("Indique el nombre.").MinimumLength(2).MaximumLength(150);
        RuleFor(x => x.CategoryCode).NotEmpty().WithMessage("Elija la categoría.");
        RuleFor(x => x.UnitCode).NotEmpty().WithMessage("Elija la unidad.");
        RuleFor(x => x.Minimum).GreaterThanOrEqualTo(0).WithMessage("El mínimo no puede ser negativo.");
        RuleFor(x => x.Maximum).GreaterThanOrEqualTo(x => x.Minimum).When(x => x.Maximum > 0)
            .WithMessage("El máximo debe ser mayor o igual al mínimo.");
        RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0).WithMessage("El costo no puede ser negativo.");
        RuleFor(x => x.SalePrice).GreaterThanOrEqualTo(0).WithMessage("El precio no puede ser negativo.");
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Barcode).MaximumLength(64);
    }
}

public sealed class SaveProductHandler(IMinvDbContext db, IClock clock) : IRequestHandler<SaveProductCommand, string>
{
    public async Task<string> Handle(SaveProductCommand r, CancellationToken ct)
    {
        var lookups = new InventoryLookups(db);
        var config = await lookups.ConfigAsync(ct);
        var warehouse = await lookups.WarehouseAsync(null, config, ct);
        var category = await db.Set<Category>().FirstOrDefaultAsync(c => c.Code == r.CategoryCode.Trim().ToUpperInvariant(), ct)
                       ?? throw new NotFoundException($"La categoría {r.CategoryCode} no existe.");
        var now = clock.UtcNow;
        Product product;
        ProductVariant variant;
        if (string.IsNullOrWhiteSpace(r.OriginalSku))
        {
            var sku = Guard.Code(r.Sku, "El SKU", 40);
            Guard.That(!await db.Set<ProductVariant>().AnyAsync(v => v.Sku == sku, ct), "product.duplicate", $"Ya existe un producto con el SKU {sku}.");
            var unit = await db.Set<UnitOfMeasure>().FirstOrDefaultAsync(u => u.Code == r.UnitCode.Trim().ToUpperInvariant(), ct)
                       ?? throw new NotFoundException($"La unidad {r.UnitCode} no existe.");
            product = Product.Create(category.TenantId, sku, r.Name.Trim(), category.Id, unit.Id, description: r.Description);
            variant = product.DefaultVariant;
            db.Set<Product>().Add(product);
            var binCode = string.IsNullOrWhiteSpace(r.BinCode) ? warehouse.Code + "-GENERAL" : r.BinCode.Trim().ToUpperInvariant();
            var bin = await db.Set<Bin>().FirstOrDefaultAsync(b => b.Code == binCode, ct);
            Guard.That(bin is not null || string.IsNullOrWhiteSpace(r.BinCode), "bin.missing", $"La posición {binCode} no existe.");
            if (bin is not null)
            {
                db.Set<BinAssignment>().Add(new BinAssignment(product.TenantId, bin.Id, variant.Id, isPrimaryPick: true));
            }
        }
        else
        {
            var item = await lookups.VariantBySkuAsync(r.OriginalSku, ct);
            product = await db.Set<Product>().Include(p => p.Variants).ThenInclude(v => v.Barcodes).FirstAsync(p => p.Id == item.Product.Id, ct);
            variant = product.Variants.First(v => v.Id == item.Variant.Id);
            product.Rename(r.Name.Trim());
            product.Describe(r.Description);
            product.Recategorize(category.Id);
            if (!string.IsNullOrWhiteSpace(r.BinCode))
            {
                // Nueva posición principal: las asignaciones anteriores se reemplazan (el stock existente no se mueve)
                var bin = await lookups.BinByCodeAsync(r.BinCode, ct);
                var assignments = await db.Set<BinAssignment>().Where(a => a.VariantId == variant.Id).ToListAsync(ct);
                if (assignments.All(a => a.BinId != bin.Id))
                {
                    db.Set<BinAssignment>().RemoveRange(assignments);
                    db.Set<BinAssignment>().Add(new BinAssignment(product.TenantId, bin.Id, variant.Id, isPrimaryPick: true));
                }
            }
        }
        if (r.IsActive)
        {
            product.Activate();
            variant.Activate();
        }
        else
        {
            product.Deactivate();
        }

        // Política de stock (mínimo / máximo) del almacén de trabajo
        var policy = await db.Set<ProductStockPolicy>().FirstOrDefaultAsync(x => x.VariantId == variant.Id && x.WarehouseId == warehouse.Id, ct);
        if (policy is null)
        {
            db.Set<ProductStockPolicy>().Add(new ProductStockPolicy(product.TenantId, variant.Id, warehouse.Id, r.Minimum, r.Maximum));
        }
        else
        {
            policy.Define(r.Minimum, r.Maximum);
        }

        // Precio de venta en la lista por defecto
        var priceList = await db.Set<PriceList>().FirstOrDefaultAsync(l => l.IsDefault, ct)
                        ?? throw new NotFoundException("La empresa no tiene lista de precios por defecto.");
        var price = await db.Set<PriceListItem>().FirstOrDefaultAsync(i => i.PriceListId == priceList.Id && i.VariantId == variant.Id, ct);
        if (price is null)
        {
            db.Set<PriceListItem>().Add(new PriceListItem(product.TenantId, priceList.Id, variant.Id, r.SalePrice));
        }
        else
        {
            price.ChangePrice(r.SalePrice);
        }

        // Costo: cada cambio es una fila nueva del historial de costo promedio (append-only)
        var currentCost = await AverageCosts.CurrentAsync(db, variant.Id, warehouse.Id, ct);
        if (r.UnitCost != currentCost && r.UnitCost > 0)
        {
            db.Set<AverageCostHistory>().Add(new AverageCostHistory(product.TenantId, variant.Id, warehouse.Id, now, r.UnitCost, null));
        }

        // Proveedor preferido
        if (!string.IsNullOrWhiteSpace(r.SupplierCode))
        {
            var code = r.SupplierCode.Trim().ToUpperInvariant();
            var supplier = await db.Set<Supplier>().FirstOrDefaultAsync(s => s.Code == code, ct)
                           ?? throw new NotFoundException($"El proveedor {code} no existe.");
            var links = await db.Set<ProductSupplier>().Where(x => x.ProductId == product.Id).ToListAsync(ct);
            foreach (var link in links)
            {
                link.SetPreferred(link.SupplierId == supplier.Id);
            }
            if (links.All(l => l.SupplierId != supplier.Id))
            {
                db.Set<ProductSupplier>().Add(new ProductSupplier(product.TenantId, product.Id, supplier.Id, null, supplier.LeadTimeDays, true));
            }
        }

        // Código de barras (EAN-13 si es válido; si no, código interno)
        if (!string.IsNullOrWhiteSpace(r.Barcode) && variant.Barcodes.All(b => b.Code != r.Barcode.Trim()))
        {
            var code = r.Barcode.Trim();
            Guard.That(!await db.Set<ProductBarcode>().AnyAsync(b => b.Code == code, ct), "barcode.duplicate", $"El código {code} ya pertenece a otro producto.");
            var typeCode = code.Length == 13 && code.All(char.IsDigit) && BarcodeRules.IsValidGtin(code) ? "EAN13" : "INTERNO";
            var type = await db.Set<BarcodeType>().FirstAsync(t => t.Code == typeCode, ct);
            db.Set<ProductBarcode>().Add(variant.AddBarcode(type, code, isPrimary: variant.Barcodes.Count == 0));
        }
        await db.SaveChangesAsync(ct);
        return variant.Sku;
    }
}

/// <summary>Imágenes de los productos (todas o las pedidas) para la galería y el punto de venta.</summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetProductImagesQuery(IReadOnlyList<Guid>? VariantIds = null) : IRequest<IReadOnlyList<ProductImageData>>;

public sealed record ProductImageData(Guid VariantId, string Sku, byte[] Content, string ContentType);

public sealed class GetProductImagesHandler(IMinvDbContext db) : IRequestHandler<GetProductImagesQuery, IReadOnlyList<ProductImageData>>
{
    public async Task<IReadOnlyList<ProductImageData>> Handle(GetProductImagesQuery request, CancellationToken ct)
    {
        var ids = request.VariantIds;
        return await (from i in db.Set<ProductImage>()
                      join v in db.Set<ProductVariant>() on i.VariantId equals v.Id
                      where ids == null || ids.Contains(i.VariantId)
                      select new ProductImageData(i.VariantId, v.Sku, i.Content, i.ContentType)).ToListAsync(ct);
    }
}

/// <summary>Pone o reemplaza la imagen de un producto (PNG o JPEG de hasta 1 MB).</summary>
[RequiresPermission(PermissionCodes.CatalogManage)]
public sealed record SetProductImageCommand(string Sku, byte[] Content, string ContentType, string? FileName) : IRequest<bool>, IAuditableRequest
{
    public object AuditDetails => new { Sku, Bytes = Content.Length, ContentType, FileName };
}

public sealed class SetProductImageHandler(IMinvDbContext db) : IRequestHandler<SetProductImageCommand, bool>
{
    public async Task<bool> Handle(SetProductImageCommand request, CancellationToken ct)
    {
        var item = await new InventoryLookups(db).VariantBySkuAsync(request.Sku, ct);
        var image = await db.Set<ProductImage>().FirstOrDefaultAsync(i => i.VariantId == item.Variant.Id, ct);
        if (image is null)
        {
            db.Set<ProductImage>().Add(new ProductImage(item.Variant.TenantId, item.Variant.Id, request.Content, request.ContentType, request.FileName));
        }
        else
        {
            image.Replace(request.Content, request.ContentType, request.FileName);
        }
        await db.SaveChangesAsync(ct);
        return true;
    }
}

[RequiresPermission(PermissionCodes.CatalogManage)]
public sealed record RemoveProductImageCommand(string Sku) : IRequest<bool>, IAuditableRequest
{
    public object AuditDetails => new { Sku };
}

public sealed class RemoveProductImageHandler(IMinvDbContext db) : IRequestHandler<RemoveProductImageCommand, bool>
{
    public async Task<bool> Handle(RemoveProductImageCommand request, CancellationToken ct)
    {
        var item = await new InventoryLookups(db).VariantBySkuAsync(request.Sku, ct);
        var image = await db.Set<ProductImage>().FirstOrDefaultAsync(i => i.VariantId == item.Variant.Id, ct);
        if (image is null)
        {
            return false;
        }
        db.Set<ProductImage>().Remove(image);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>Crea una categoría (raíz) del catálogo.</summary>
[RequiresPermission(PermissionCodes.CatalogManage)]
public sealed record SaveCategoryCommand(string Code, string Name) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Code, Name };
}

public sealed class SaveCategoryHandler(IMinvDbContext db, ITenantContext tenant) : IRequestHandler<SaveCategoryCommand, string>
{
    public async Task<string> Handle(SaveCategoryCommand request, CancellationToken ct)
    {
        var code = Guard.Code(request.Code, "El código de la categoría", 20);
        var existing = await db.Set<Category>().FirstOrDefaultAsync(c => c.Code == code, ct);
        if (existing is not null)
        {
            existing.Rename(request.Name.Trim());
        }
        else
        {
            var category = new Category(tenant.TenantId, code, request.Name.Trim());
            db.Set<Category>().Add(category);
            db.Set<CategoryHierarchy>().AddRange(CategoryTree.ForNewCategory(category, []));
        }
        await db.SaveChangesAsync(ct);
        return code;
    }
}

/// <summary>Precio, impuesto y moneda que usan el catálogo y el punto de venta.</summary>
public static class Pricing
{
    /// <summary>Tasa del IVA vigente en la fecha (porcentaje; 0 si no hay).</summary>
    public static async Task<decimal> TaxRateAsync(IMinvDbContext db, DateOnly date, CancellationToken ct) =>
        await (from r in db.Set<TaxRate>()
               join t in db.Set<Tax>() on r.TaxId equals t.Id
               where t.Code == "IVA" && r.ValidFrom <= date && (r.ValidTo == null || r.ValidTo >= date)
               orderby r.ValidFrom descending
               select (decimal?)r.Rate).FirstOrDefaultAsync(ct) ?? 0m;

    /// <summary>Impuesto incluido en un importe (los precios de venta incluyen IVA).</summary>
    public static decimal IncludedTax(decimal amount, decimal ratePercent) =>
        ratePercent <= 0 ? 0 : JournalPoster.Money(amount * ratePercent / (100 + ratePercent));
}
