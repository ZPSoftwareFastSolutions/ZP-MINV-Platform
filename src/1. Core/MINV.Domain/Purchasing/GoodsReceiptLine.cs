using MINV.Domain.Common;

namespace MINV.Domain.Purchasing;

/// <summary>Línea de una recepción.</summary>
public sealed class GoodsReceiptLine : Entity, IBranchScoped
{
    private GoodsReceiptLine()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public GoodsReceiptLine(Guid tenantId, Guid branchId, Guid goodsReceiptId, Guid? purchaseOrderLineId, Guid stockLevelId, decimal quantity, decimal unitCost)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        GoodsReceiptId = Guard.NotEmpty(goodsReceiptId, nameof(goodsReceiptId));
        PurchaseOrderLineId = Guard.NotEmptyIfPresent(purchaseOrderLineId, nameof(purchaseOrderLineId));
        StockLevelId = Guard.NotEmpty(stockLevelId, nameof(stockLevelId));
        Quantity = Quantities.Round6(Guard.Positive(quantity, "La cantidad"));
        UnitCost = Guard.NonNegative(unitCost, "El costo unitario");
    }

    public Guid GoodsReceiptId { get; private set; }

    public Guid? PurchaseOrderLineId { get; private set; }

    public Guid StockLevelId { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitCost { get; private set; }

    public Guid? StockMovementId { get; private set; }

    public void LinkMovement(Guid stockMovementId) => StockMovementId = Guard.NotEmpty(stockMovementId, nameof(stockMovementId));
}
