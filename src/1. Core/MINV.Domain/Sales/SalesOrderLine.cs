using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Línea de un pedido de venta.</summary>
public sealed class SalesOrderLine : Entity, IBranchScoped
{
    private SalesOrderLine()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public SalesOrderLine(Guid tenantId, Guid branchId, Guid salesOrderId, Guid variantId, Guid unitId, decimal quantity, decimal unitPrice, decimal discountPercent)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
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

    /// <summary>Importe de la línea: cantidad × precio − descuento (redondeado a centavos).</summary>
    public decimal Amount => decimal.Round(Quantity * UnitPrice * (1 - DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero);

    public void LinkMovement(Guid stockMovementId) => StockMovementId = Guard.NotEmpty(stockMovementId, nameof(stockMovementId));
}
