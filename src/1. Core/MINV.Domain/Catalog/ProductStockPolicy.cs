using MINV.Domain.Common;
using MINV.Domain.Inventory;

namespace MINV.Domain.Catalog;

/// <summary>Mínimo y máximo de una variante en un almacén: alimentan el semáforo y el pedido sugerido.</summary>
/// <remarks>Origen en la V2.1: tblProductos (StockMin, StockMax).</remarks>
public sealed class ProductStockPolicy : Entity, IConcurrencyAware, IBranchScoped
{
    private ProductStockPolicy()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public ProductStockPolicy(Guid tenantId, Guid branchId, Guid variantId, Guid warehouseId, decimal minQuantity, decimal maxQuantity)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        VariantId = Guard.NotEmpty(variantId, nameof(variantId));
        WarehouseId = Guard.NotEmpty(warehouseId, nameof(warehouseId));
        Define(minQuantity, maxQuantity);
    }

    public Guid VariantId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public decimal MinQuantity { get; private set; }

    /// <summary>0 = sin máximo (entonces el tope de reposición es 2 × mínimo, como en la V2.1).</summary>
    public decimal MaxQuantity { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>Cantidad objetivo al reponer: el máximo o, si no hay, 2 × mínimo.</summary>
    public decimal ReorderTarget => StockRules.ReorderTarget(MinQuantity, MaxQuantity);

    public void Define(decimal minQuantity, decimal maxQuantity)
    {
        var min = Quantities.Round6(Guard.NonNegative(minQuantity, "El mínimo"));
        var max = Quantities.Round6(Guard.NonNegative(maxQuantity, "El máximo"));
        Guard.That(max == 0 || max >= min, "policy.range",
            "El máximo debe ser 0 (sin máximo) o mayor o igual que el mínimo.");
        MinQuantity = min;
        MaxQuantity = max;
    }

    public StockStatusCode Evaluate(decimal stock, bool productActive, decimal alertMargin) =>
        StockRules.Evaluate(stock, MinQuantity, MaxQuantity, productActive, alertMargin);

    /// <summary>Cantidad a pedir para llegar al tope (V2.1: 18_PEDIDO, columna APedir).</summary>
    public decimal SuggestedOrderQuantity(decimal stock) =>
        Quantities.Round6(Math.Max(0, ReorderTarget - Math.Max(0, stock)));
}
