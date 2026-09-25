using MINV.Domain.Common;

namespace MINV.Domain.Purchasing;

/// <summary>Línea de una devolución.</summary>
public sealed class PurchaseReturnLine : Entity, IBranchScoped
{
    private PurchaseReturnLine()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public PurchaseReturnLine(Guid tenantId, Guid branchId, Guid purchaseReturnId, Guid stockLevelId, Guid? goodsReceiptLineId, decimal quantity)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        PurchaseReturnId = Guard.NotEmpty(purchaseReturnId, nameof(purchaseReturnId));
        StockLevelId = Guard.NotEmpty(stockLevelId, nameof(stockLevelId));
        GoodsReceiptLineId = Guard.NotEmptyIfPresent(goodsReceiptLineId, nameof(goodsReceiptLineId));
        Quantity = Quantities.Round6(Guard.Positive(quantity, "La cantidad"));
    }

    public Guid PurchaseReturnId { get; private set; }

    public Guid StockLevelId { get; private set; }

    public Guid? GoodsReceiptLineId { get; private set; }

    public decimal Quantity { get; private set; }

    public Guid? StockMovementId { get; private set; }
}
