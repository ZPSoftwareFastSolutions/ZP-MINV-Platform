using MINV.Domain.Common;
using MINV.Domain.Inventory;

namespace MINV.Domain.Catalog;

/// <summary>
/// Producto del catálogo: agregado con sus variantes (SKU), impuestos y empaques. Contiene las reglas del catálogo y
/// la evaluación del semáforo de stock (regla A-02: el dominio no es anémico).
/// </summary>
/// <remarks>Origen en la V2.1: 05_PRODUCTOS (tblProductos): SKU, Producto, Categoría, Unidad, Activo.</remarks>
public sealed class Product : Entity, IConcurrencyAware, IAggregateRoot
{
    private readonly List<ProductVariant> _variants = new();
    private readonly List<ProductTax> _taxes = new();
    private readonly List<ProductUnitConversion> _packagings = new();

    private Product()
    {
    }

    private Product(Guid tenantId, string code, string name, string? description, Guid categoryId, Guid? modelId,
        Guid baseUnitId, TrackingMode trackingMode)
        : base(tenantId)
    {
        Code = Guard.Code(code, "El código del producto", 40);
        Name = Guard.Text(name, "El nombre del producto", 150, minLength: 2);
        Description = Guard.OptionalText(description, "La descripción", 1000);
        CategoryId = Guard.NotEmpty(categoryId, nameof(categoryId));
        ModelId = Guard.NotEmptyIfPresent(modelId, nameof(modelId));
        BaseUnitId = Guard.NotEmpty(baseUnitId, nameof(baseUnitId));
        TrackingMode = Guard.Defined(trackingMode, "El control de lotes/series");
        IsActive = true;
    }

    /// <summary>Código del producto. Inmutable: como en la V2.1, un producto con movimientos no cambia de código;
    /// se crea uno nuevo y el anterior se desactiva.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid CategoryId { get; private set; }

    public Guid? ModelId { get; private set; }

    public Guid BaseUnitId { get; private set; }

    public TrackingMode TrackingMode { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<ProductVariant> Variants => _variants;

    public IReadOnlyCollection<ProductTax> Taxes => _taxes;

    public IReadOnlyCollection<ProductUnitConversion> Packagings => _packagings;

    /// <summary>Variante por defecto (la que lleva el SKU igual al código del producto).</summary>
    public ProductVariant DefaultVariant =>
        _variants.FirstOrDefault(v => v.IsDefault)
        ?? throw new DomainException("product.no_default_variant", $"El producto {Code} no tiene variante por defecto.");

    /// <summary>Crea un producto con su variante por defecto (SKU = código). Así migra cada producto de la V2.1.</summary>
    public static Product Create(Guid tenantId, string code, string name, Guid categoryId, Guid baseUnitId,
        TrackingMode trackingMode = TrackingMode.None, Guid? modelId = null, string? description = null)
    {
        var product = new Product(tenantId, code, name, description, categoryId, modelId, baseUnitId, trackingMode);
        product._variants.Add(new ProductVariant(tenantId, product.Id, product.Code, null, isDefault: true));
        return product;
    }

    public void Rename(string name) => Name = Guard.Text(name, "El nombre del producto", 150, minLength: 2);

    public void Describe(string? description) => Description = Guard.OptionalText(description, "La descripción", 1000);

    public void Recategorize(Guid categoryId) => CategoryId = Guard.NotEmpty(categoryId, nameof(categoryId));

    public void AssignModel(Guid? modelId) => ModelId = Guard.NotEmptyIfPresent(modelId, nameof(modelId));

    /// <summary>Descontinúa el producto y todas sus variantes (V2.1: <c>Activo = NO</c>). Nunca se borra.</summary>
    public void Deactivate()
    {
        IsActive = false;
        foreach (var v in _variants)
        {
            v.Deactivate();
        }
    }

    public void Activate()
    {
        IsActive = true;
        DefaultVariant.Activate();
    }

    /// <summary>Agrega una variante (p. ej. color o talla) con su propio SKU.</summary>
    public ProductVariant AddVariant(string sku, string? name)
    {
        Guard.That(IsActive, "product.inactive", $"El producto {Code} está inactivo: no admite variantes nuevas.");
        var normalized = Guard.Code(sku, "El SKU", 40);
        Guard.That(_variants.All(v => v.Sku != normalized), "product.duplicate_sku",
            $"El SKU {normalized} ya existe en el producto {Code}.");
        var variant = new ProductVariant(TenantId, Id, normalized, name, isDefault: false);
        _variants.Add(variant);
        return variant;
    }

    public ProductVariant Variant(string sku) =>
        _variants.FirstOrDefault(v => v.Sku == sku.Trim().ToUpperInvariant())
        ?? throw new DomainException("product.variant_not_found", $"El producto {Code} no tiene la variante {sku}.");

    public void AssignTax(Tax tax)
    {
        ArgumentNullException.ThrowIfNull(tax);
        EnsureSameTenant(tax, "El impuesto");
        if (_taxes.All(t => t.TaxId != tax.Id))
        {
            _taxes.Add(new ProductTax(TenantId, Id, tax.Id));
        }
    }

    public void RemoveTax(Guid taxId) => _taxes.RemoveAll(t => t.TaxId == taxId);

    /// <summary>Define (o actualiza) un empaque del producto: p. ej. CAJA = 100 unidades base.</summary>
    public ProductUnitConversion DefinePackaging(UnitOfMeasure unit, decimal factorToBase)
    {
        ArgumentNullException.ThrowIfNull(unit);
        EnsureSameTenant(unit, "La unidad");
        Guard.That(unit.Id != BaseUnitId, "product.packaging_base_unit", "El empaque no puede ser la unidad base.");
        var existing = _packagings.FirstOrDefault(p => p.UnitId == unit.Id);
        if (existing is not null)
        {
            existing.ChangeFactor(factorToBase);
            return existing;
        }
        var packaging = new ProductUnitConversion(TenantId, Id, unit.Id, factorToBase);
        _packagings.Add(packaging);
        return packaging;
    }

    /// <summary>Semáforo de stock con las reglas de la V2.1 (tblEstados).</summary>
    public StockStatusCode EvaluateStockStatus(decimal stock, decimal minimum, decimal maximum, decimal alertMargin) =>
        StockRules.Evaluate(stock, minimum, maximum, IsActive, alertMargin);
}
