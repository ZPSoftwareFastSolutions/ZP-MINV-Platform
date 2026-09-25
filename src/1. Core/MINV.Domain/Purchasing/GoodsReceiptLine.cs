using MINV.Domain.Common;

namespace MINV.Domain.Purchasing;

/// <summary>Línea de una recepción.</summary>
public sealed class GoodsReceiptLine : Entity
{
    private GoodsReceiptLine()
    {
    }

    public GoodsReceiptLine(Guid tenantId, Guid goodsReceiptId, Guid? purchaseOrderLineId, Guid stockLevelId, decimal quantity, decimal unitCost)
        : base(tenantId)
    {
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
}
