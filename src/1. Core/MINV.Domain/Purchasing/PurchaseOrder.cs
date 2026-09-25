using MINV.Domain.Common;

namespace MINV.Domain.Purchasing;

/// <summary>Orden de compra.</summary>
/// <remarks>Origen en la V2.1: 18_PEDIDO (tblPedido) → orden de compra real.</remarks>
public sealed class PurchaseOrder : Entity, IConcurrencyAware, IAggregateRoot
{
    private readonly List<PurchaseOrderLine> _lines = new();

    private PurchaseOrder()
    {
    }

    public PurchaseOrder(Guid tenantId, string number, Guid supplierId, Guid warehouseId, Guid currencyId, DateOnly orderDate, DateOnly? expectedDate, string? notes)
        : base(tenantId)
    {
        Number = Guard.Text(number, "El número", 30);
        SupplierId = Guard.NotEmpty(supplierId, nameof(supplierId));
        WarehouseId = Guard.NotEmpty(warehouseId, nameof(warehouseId));
        CurrencyId = Guard.NotEmpty(currencyId, nameof(currencyId));
        OrderDate = orderDate;
        ExpectedDate = expectedDate;
        Notes = Guard.OptionalText(notes, "Las observaciones", 250);
        Status = PurchaseOrderStatus.Draft;
    }

    public string Number { get; private set; } = string.Empty;

    public Guid SupplierId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid CurrencyId { get; private set; }

    public DateOnly OrderDate { get; private set; }

    public DateOnly? ExpectedDate { get; private set; }

    public PurchaseOrderStatus Status { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<PurchaseOrderLine> Lines => _lines;

    /// <summary>Agrega una línea (solo en borrador).</summary>
    public PurchaseOrderLine AddLine(Guid variantId, Guid unitId, decimal quantity, decimal unitCost)
    {
        EnsureDraft();
        var line = new PurchaseOrderLine(TenantId, Id, variantId, unitId, quantity, unitCost);
        _lines.Add(line);
        return line;
    }

    private void EnsureDraft() => Guard.That(Status == PurchaseOrderStatus.Draft, "document.not_draft",
        "Solo se pueden modificar documentos en borrador.");
}
