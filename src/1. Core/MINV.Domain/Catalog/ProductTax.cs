using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>Impuestos que aplican a cada producto.</summary>
public sealed class ProductTax : BaseEntity
{
    private ProductTax()
    {
    }

    public ProductTax(Guid tenantId, Guid productId, Guid taxId)
        : base(tenantId)
    {
        ProductId = Guard.NotEmpty(productId, nameof(productId));
        TaxId = Guard.NotEmpty(taxId, nameof(taxId));
    }

    public Guid ProductId { get; private set; }

    public Guid TaxId { get; private set; }
}
