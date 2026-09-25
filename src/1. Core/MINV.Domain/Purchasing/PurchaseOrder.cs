using MINV.Domain.Common;

namespace MINV.Domain.Purchasing;

/// <summary>Orden de compra.</summary>
/// <remarks>Origen en la V2.1: 18_PEDIDO (tblPedido) → orden de compra real.</remarks>
public sealed class PurchaseOrder : Entity, IConcurrencyAware, IAggregateRoot, IBranchScoped
{
    private readonly List<PurchaseOrderLine> _lines = new();

    private PurchaseOrder()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public PurchaseOrder(Guid tenantId, Guid branchId, string number, Guid supplierId, Guid warehouseId, Guid currencyId, DateOnly orderDate, DateOnly? expectedDate, string? notes)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
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
        var line = new PurchaseOrderLine(TenantId, BranchId, Id, variantId, unitId, quantity, unitCost);
        _lines.Add(line);
        return line;
    }

    public decimal Total => Quantities.Round6(_lines.Sum(l => l.Quantity * l.UnitCost));

    /// <summary>Aprueba la orden (ya se puede enviar al proveedor y recibir).</summary>
    public void Approve()
    {
        EnsureDraft();
        Guard.That(_lines.Count > 0, "purchase.empty", "Una orden de compra necesita al menos una línea.");
        Status = PurchaseOrderStatus.Approved;
    }

    /// <summary>Anula la orden si todavía no se recibió nada.</summary>
    public void Cancel()
    {
        Guard.That(Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Approved, "purchase.cancel",
            $"La orden {Number} ya tiene recepciones o está cerrada: no se puede anular.");
        Status = PurchaseOrderStatus.Cancelled;
    }

    /// <summary>Registra una recepción: la orden queda recibida en todo o en parte según lo pendiente.</summary>
    public void RegisterReceipt(bool fullyReceived)
    {
        Guard.That(Status is PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived, "purchase.receive",
            $"La orden {Number} debe estar aprobada para recibirla.");
        Status = fullyReceived ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
    }

    private void EnsureDraft() => Guard.That(Status == PurchaseOrderStatus.Draft, "document.not_draft",
        "Solo se pueden modificar documentos en borrador.");
}
