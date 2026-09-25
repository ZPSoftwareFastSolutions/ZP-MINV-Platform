using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Pedido o venta (POS o back-office).</summary>
/// <remarks>Origen en la V2.1: 10B_SALIDAS (tipo SALIDA).</remarks>
public sealed class SalesOrder : Entity, IConcurrencyAware, IAggregateRoot
{
    private readonly List<SalesOrderLine> _lines = new();

    private SalesOrder()
    {
    }

    public SalesOrder(Guid tenantId, string number, Guid customerId, Guid? posSessionId, Guid? warehouseId, Guid priceListId, DateOnly orderDate)
        : base(tenantId)
    {
        Number = Guard.Text(number, "El número", 30);
        CustomerId = Guard.NotEmpty(customerId, nameof(customerId));
        PosSessionId = Guard.NotEmptyIfPresent(posSessionId, nameof(posSessionId));
        WarehouseId = Guard.NotEmptyIfPresent(warehouseId, nameof(warehouseId));
        PriceListId = Guard.NotEmpty(priceListId, nameof(priceListId));
        OrderDate = orderDate;
        Status = SalesOrderStatus.Draft;
    }

    public string Number { get; private set; } = string.Empty;

    public Guid CustomerId { get; private set; }

    public Guid? PosSessionId { get; private set; }

    public Guid? WarehouseId { get; private set; }

    public Guid PriceListId { get; private set; }

    public DateOnly OrderDate { get; private set; }

    public SalesOrderStatus Status { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<SalesOrderLine> Lines => _lines;

    /// <summary>Agrega una línea (solo en borrador).</summary>
    public SalesOrderLine AddLine(Guid variantId, Guid unitId, decimal quantity, decimal unitPrice, decimal discountPercent)
    {
        EnsureDraft();
        var line = new SalesOrderLine(TenantId, Id, variantId, unitId, quantity, unitPrice, discountPercent);
        _lines.Add(line);
        return line;
    }

    /// <summary>Total con descuentos (los precios incluyen impuestos).</summary>
    public decimal Total => decimal.Round(_lines.Sum(l => l.Amount), 2, MidpointRounding.AwayFromZero);

    /// <summary>Confirma la venta: desde aquí ya no se agregan líneas y se descuenta el stock.</summary>
    public void Confirm()
    {
        EnsureDraft();
        Guard.That(_lines.Count > 0, "sale.empty", "Agregue al menos un producto a la venta.");
        Status = SalesOrderStatus.Confirmed;
    }

    /// <summary>Todas las líneas tienen su salida de stock registrada.</summary>
    public void MarkFulfilled()
    {
        Guard.That(Status == SalesOrderStatus.Confirmed, "sale.fulfill", "Solo se despacha una venta confirmada.");
        Guard.That(_lines.All(l => l.StockMovementId is not null), "sale.movements", "Cada línea necesita su salida de stock.");
        Status = SalesOrderStatus.Fulfilled;
    }

    public void MarkInvoiced()
    {
        Guard.That(Status == SalesOrderStatus.Fulfilled, "sale.invoice", "Solo se factura una venta despachada.");
        Status = SalesOrderStatus.Invoiced;
    }

    public void Cancel()
    {
        Guard.That(Status is SalesOrderStatus.Draft or SalesOrderStatus.Confirmed, "sale.cancel",
            $"La venta {Number} ya se despachó: corríjala con una devolución.");
        Status = SalesOrderStatus.Cancelled;
    }

    private void EnsureDraft() => Guard.That(Status == SalesOrderStatus.Draft, "document.not_draft",
        "Solo se pueden modificar documentos en borrador.");
}
