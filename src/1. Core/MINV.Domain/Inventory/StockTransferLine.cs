using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>Línea de un traslado.</summary>
public sealed class StockTransferLine : Entity
{
    private StockTransferLine()
    {
    }

    public StockTransferLine(Guid tenantId, Guid stockTransferId, Guid sourceStockLevelId, decimal quantity)
        : base(tenantId)
    {
        StockTransferId = Guard.NotEmpty(stockTransferId, nameof(stockTransferId));
        SourceStockLevelId = Guard.NotEmpty(sourceStockLevelId, nameof(sourceStockLevelId));
        Quantity = Quantities.Round6(Guard.Positive(quantity, "La cantidad"));
    }

    public Guid StockTransferId { get; private set; }

    public Guid SourceStockLevelId { get; private set; }

    public Guid? DestinationStockLevelId { get; private set; }

    public decimal Quantity { get; private set; }

    public Guid? OutboundMovementId { get; private set; }

    public Guid? InboundMovementId { get; private set; }
}
