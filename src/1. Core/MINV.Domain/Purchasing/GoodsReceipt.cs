using MINV.Domain.Common;

namespace MINV.Domain.Purchasing;

/// <summary>Recepción de mercancía.</summary>
public sealed class GoodsReceipt : Entity, IConcurrencyAware, IAggregateRoot, IBranchScoped
{
    private readonly List<GoodsReceiptLine> _lines = new();

    private GoodsReceipt()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public GoodsReceipt(Guid tenantId, Guid branchId, string number, Guid? purchaseOrderId, Guid? supplierId, Guid warehouseId, DateTimeOffset receivedAt, Guid receivedByUserId, string? supplierDocument)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
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
        var line = new GoodsReceiptLine(TenantId, BranchId, Id, purchaseOrderLineId, stockLevelId, quantity, unitCost);
        _lines.Add(line);
        return line;
    }

    /// <summary>Contabiliza la recepción: cada línea ya tiene su movimiento de entrada.</summary>
    public void Post()
    {
        EnsureDraft();
        Guard.That(_lines.Count > 0, "receipt.empty", "Una recepción necesita al menos una línea.");
        Guard.That(_lines.All(l => l.StockMovementId is not null), "receipt.movements", "Cada línea recibida necesita su movimiento de stock.");
        Status = GoodsReceiptStatus.Posted;
    }

    private void EnsureDraft() => Guard.That(Status == GoodsReceiptStatus.Draft, "document.not_draft",
        "Solo se pueden modificar documentos en borrador.");
}
