using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>Variante vendible de un producto: lleva el SKU, los códigos de barras y los valores de atributo.</summary>
/// <remarks>Origen en la V2.1: tblProductos[SKU]. La V2.1 no tenía variantes: cada producto migra con una variante por
/// defecto cuyo SKU es el de la V2.1.</remarks>
public sealed class ProductVariant : Entity
{
    private readonly List<ProductBarcode> _barcodes = new();
    private readonly List<ProductVariantAttribute> _attributeValues = new();

    private ProductVariant()
    {
    }

    internal ProductVariant(Guid tenantId, Guid productId, string sku, string? name, bool isDefault)
        : base(tenantId)
    {
        ProductId = Guard.NotEmpty(productId, nameof(productId));
        Sku = Guard.Code(sku, "El SKU", 40);
        Name = Guard.OptionalText(name, "El nombre de la variante", 150);
        IsDefault = isDefault;
        IsActive = true;
    }

    public Guid ProductId { get; private set; }

    /// <summary>SKU único por empresa (inmutable).</summary>
    public string Sku { get; private set; } = string.Empty;

    public string? Name { get; private set; }

    public bool IsDefault { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<ProductBarcode> Barcodes => _barcodes;

    public IReadOnlyCollection<ProductVariantAttribute> AttributeValues => _attributeValues;

    public void Rename(string? name) => Name = Guard.OptionalText(name, "El nombre de la variante", 150);

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>Agrega un código de barras validado según su simbología (longitud y dígito de control GTIN).
    /// El primero queda como principal.</summary>
    public ProductBarcode AddBarcode(BarcodeType type, string code, Guid? unitId = null, bool isPrimary = false)
    {
        ArgumentNullException.ThrowIfNull(type);
        EnsureSameTenant(type, "El tipo de código de barras");
        var normalized = BarcodeRules.Validate(type, code);
        Guard.That(_barcodes.All(b => b.Code != normalized), "barcode.duplicate",
            $"El código de barras {normalized} ya está asignado a {Sku}.");
        var primary = isPrimary || _barcodes.Count == 0;
        if (primary)
        {
            foreach (var b in _barcodes)
            {
                b.MarkPrimary(false);
            }
        }
        var barcode = new ProductBarcode(TenantId, Id, type.Id, normalized, unitId, primary);
        _barcodes.Add(barcode);
        return barcode;
    }

    public void RemoveBarcode(string code)
    {
        var removed = _barcodes.RemoveAll(b => b.Code == code.Trim());
        if (removed > 0 && _barcodes.Count > 0 && _barcodes.All(b => !b.IsPrimary))
        {
            _barcodes[0].MarkPrimary(true);
        }
    }

    /// <summary>Asigna un valor de atributo. Regla: un solo valor por atributo (p. ej. un solo color); también la
    /// exige un trigger en PostgreSQL. <paramref name="assigned"/> son los valores ya asignados a esta variante.</summary>
    public void AssignAttributeValue(CatalogAttributeValue value, IEnumerable<CatalogAttributeValue> assigned)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(assigned);
        EnsureSameTenant(value, "El valor de atributo");
        if (_attributeValues.Any(a => a.AttributeValueId == value.Id))
        {
            return;
        }
        Guard.That(assigned.All(a => a.AttributeId != value.AttributeId || a.Id == value.Id), "variant.attribute_duplicate",
            $"La variante {Sku} ya tiene un valor para ese atributo: quítelo antes de asignar otro.");
        _attributeValues.Add(new ProductVariantAttribute(TenantId, Id, value.Id));
    }

    public void RemoveAttributeValue(Guid attributeValueId) =>
        _attributeValues.RemoveAll(a => a.AttributeValueId == attributeValueId);
}
