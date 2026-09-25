using MINV.Domain.Common;

namespace MINV.Domain.Purchasing;

/// <summary>Línea de una devolución.</summary>
public sealed class PurchaseReturnLine : Entity
{
    private PurchaseReturnLine()
    {
    }

    public PurchaseReturnLine(Guid tenantId, Guid purchaseReturnId, Guid stockLevelId, Guid? goodsReceiptLineId, decimal quantity)
        : base(tenantId)
    {
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
