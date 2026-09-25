using MINV.Domain.Common;

namespace MINV.Domain.Purchasing;

/// <summary>Recepción de mercancía.</summary>
public sealed class GoodsReceipt : Entity, IConcurrencyAware, IAggregateRoot
{
    private readonly List<GoodsReceiptLine> _lines = new();

    private GoodsReceipt()
    {
    }

    public GoodsReceipt(Guid tenantId, string number, Guid? purchaseOrderId, Guid? supplierId, Guid warehouseId, DateTimeOffset receivedAt, Guid receivedByUserId, string? supplierDocument)
        : base(tenantId)
    {
        Number = Guard.Text(number, "El número", 30);
        PurchaseOrderId = Guard.NotEmptyIfPresent(purchaseOrderId, nameof(purchaseOrderId));
        SupplierId = Guard.NotEmptyIfPresent(supplierId, nameof(supplierId));
        WarehouseId = Guard.NotEmpty(warehouseId, nameof(warehouseId));
        ReceivedAt = receivedAt.ToUniversalTime();
        ReceivedByUserId = Guard.NotEmpty(receivedByUserId, nameof(receivedByUserId));
        SupplierDocument = Guard.OptionalText(supplierDocument, "El documento del proveedor", 40);
        Status = GoodsReceiptStatus.Draft;
    }

    public string Number { get; private set; } = string.Empty;

    public Guid? PurchaseOrderId { get; private set; }

    public Guid? SupplierId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public Guid ReceivedByUserId { get; private set; }

    public string? SupplierDocument { get; private set; }

    public GoodsReceiptStatus Status { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<GoodsReceiptLine> Lines => _lines;

    /// <summary>Agrega una línea (solo en borrador).</summary>
    public GoodsReceiptLine AddLine(Guid? purchaseOrderLineId, Guid stockLevelId, decimal quantity, decimal unitCost)
    {
        EnsureDraft();
        var line = new GoodsReceiptLine(TenantId, Id, purchaseOrderLineId, stockLevelId, quantity, unitCost);
        _lines.Add(line);
        return line;
    }

    private void EnsureDraft() => Guard.That(Status == GoodsReceiptStatus.Draft, "document.not_draft",
        "Solo se pueden modificar documentos en borrador.");
}
