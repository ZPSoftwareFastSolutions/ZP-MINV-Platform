using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>Código de barras de una variante (relación 1:N: EAN, UPC, internos, de empaque).</summary>
public sealed class ProductBarcode : Entity
{
    private ProductBarcode()
    {
    }

    internal ProductBarcode(Guid tenantId, Guid variantId, Guid barcodeTypeId, string code, Guid? unitId, bool isPrimary)
        : base(tenantId)
    {
        VariantId = Guard.NotEmpty(variantId, nameof(variantId));
        BarcodeTypeId = Guard.NotEmpty(barcodeTypeId, nameof(barcodeTypeId));
        Code = Guard.Text(code, "El código de barras", 64);
        UnitId = Guard.NotEmptyIfPresent(unitId, nameof(unitId));
        IsPrimary = isPrimary;
    }

    public Guid VariantId { get; private set; }

    public Guid BarcodeTypeId { get; private set; }

    /// <summary>Código único por empresa: el escáner del POS lo resuelve a una sola variante.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Unidad del empaque que identifica (null = unidad base).</summary>
    public Guid? UnitId { get; private set; }

    public bool IsPrimary { get; private set; }

    internal void MarkPrimary(bool value) => IsPrimary = value;
}
