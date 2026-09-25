using MINV.Domain.Common;

namespace MINV.Domain.Purchasing;

/// <summary>Línea de una orden de compra.</summary>
public sealed class PurchaseOrderLine : Entity
{
    private PurchaseOrderLine()
    {
    }

    public PurchaseOrderLine(Guid tenantId, Guid purchaseOrderId, Guid variantId, Guid unitId, decimal quantity, decimal unitCost)
        : base(tenantId)
    {
        PurchaseOrderId = Guard.NotEmpty(purchaseOrderId, nameof(purchaseOrderId));
        VariantId = Guard.NotEmpty(variantId, nameof(variantId));
        UnitId = Guard.NotEmpty(unitId, nameof(unitId));
        Quantity = Quantities.Round6(Guard.Positive(quantity, "La cantidad"));
        UnitCost = Guard.NonNegative(unitCost, "El costo unitario");
    }

    public Guid PurchaseOrderId { get; private set; }

    public Guid VariantId { get; private set; }

    public Guid UnitId { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitCost { get; private set; }
}
