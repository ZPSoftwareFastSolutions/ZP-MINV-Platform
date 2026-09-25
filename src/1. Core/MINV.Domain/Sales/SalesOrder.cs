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

    private void EnsureDraft() => Guard.That(Status == SalesOrderStatus.Draft, "document.not_draft",
        "Solo se pueden modificar documentos en borrador.");
}
