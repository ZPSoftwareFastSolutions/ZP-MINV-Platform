using MINV.Domain.Common;

namespace MINV.Domain.Purchasing;

/// <summary>Línea de una orden de compra.</summary>
public sealed class PurchaseOrderLine : Entity, IBranchScoped
{
    private PurchaseOrderLine()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public PurchaseOrderLine(Guid tenantId, Guid branchId, Guid purchaseOrderId, Guid variantId, Guid unitId, decimal quantity, decimal unitCost)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
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
