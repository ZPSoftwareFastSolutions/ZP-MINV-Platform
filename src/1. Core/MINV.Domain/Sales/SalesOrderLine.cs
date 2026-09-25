using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Línea de un pedido de venta.</summary>
public sealed class SalesOrderLine : Entity
{
    private SalesOrderLine()
    {
    }

    public SalesOrderLine(Guid tenantId, Guid salesOrderId, Guid variantId, Guid unitId, decimal quantity, decimal unitPrice, decimal discountPercent)
        : base(tenantId)
    {
        SalesOrderId = Guard.NotEmpty(salesOrderId, nameof(salesOrderId));
        VariantId = Guard.NotEmpty(variantId, nameof(variantId));
        UnitId = Guard.NotEmpty(unitId, nameof(unitId));
        Quantity = Quantities.Round6(Guard.Positive(quantity, "La cantidad"));
        UnitPrice = Guard.NonNegative(unitPrice, "El precio");
        DiscountPercent = Guard.Percent(discountPercent, "El descuento");
    }

    public Guid SalesOrderId { get; private set; }

    public Guid VariantId { get; private set; }

    public Guid UnitId { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal DiscountPercent { get; private set; }

    public Guid? StockMovementId { get; private set; }
}
