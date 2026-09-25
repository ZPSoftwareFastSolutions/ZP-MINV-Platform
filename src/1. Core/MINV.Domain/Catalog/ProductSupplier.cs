using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>Proveedores de cada producto (uno preferido).</summary>
/// <remarks>Origen en la V2.1: tblProductos: Proveedor.</remarks>
public sealed class ProductSupplier : BaseEntity
{
    private ProductSupplier()
    {
    }

    public ProductSupplier(Guid tenantId, Guid productId, Guid supplierId, string? supplierSku, int? leadTimeDays, bool isPreferred)
        : base(tenantId)
    {
        ProductId = Guard.NotEmpty(productId, nameof(productId));
        SupplierId = Guard.NotEmpty(supplierId, nameof(supplierId));
        SupplierSku = Guard.OptionalText(supplierSku, "El código del proveedor", 60);
        Guard.That(leadTimeDays is null || leadTimeDays >= 0, "guard.non_negative", "Los días de entrega no puede ser negativo.");
        LeadTimeDays = leadTimeDays;
        IsPreferred = isPreferred;
    }

    public Guid ProductId { get; private set; }

    public Guid SupplierId { get; private set; }

    public string? SupplierSku { get; private set; }

    public int? LeadTimeDays { get; private set; }

    public bool IsPreferred { get; private set; }

    public void SetPreferred(bool isPreferred) => IsPreferred = isPreferred;
}
