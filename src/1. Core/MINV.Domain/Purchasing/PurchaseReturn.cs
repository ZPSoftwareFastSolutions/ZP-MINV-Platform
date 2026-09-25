using MINV.Domain.Common;

namespace MINV.Domain.Purchasing;

/// <summary>Devolución a proveedor.</summary>
public sealed class PurchaseReturn : Entity, IConcurrencyAware, IAggregateRoot
{
    private readonly List<PurchaseReturnLine> _lines = new();

    private PurchaseReturn()
    {
    }

    public PurchaseReturn(Guid tenantId, string number, Guid supplierId, DateOnly returnDate, string reason)
        : base(tenantId)
    {
        Number = Guard.Text(number, "El número", 30);
        SupplierId = Guard.NotEmpty(supplierId, nameof(supplierId));
        ReturnDate = returnDate;
        Reason = Guard.Text(reason, "El motivo", 200);
        Status = PurchaseReturnStatus.Draft;
    }

    public string Number { get; private set; } = string.Empty;

    public Guid SupplierId { get; private set; }

    public DateOnly ReturnDate { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public PurchaseReturnStatus Status { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<PurchaseReturnLine> Lines => _lines;

    /// <summary>Agrega una línea (solo en borrador).</summary>
    public PurchaseReturnLine AddLine(Guid stockLevelId, Guid? goodsReceiptLineId, decimal quantity)
    {
        EnsureDraft();
        var line = new PurchaseReturnLine(TenantId, Id, stockLevelId, goodsReceiptLineId, quantity);
        _lines.Add(line);
        return line;
    }

    private void EnsureDraft() => Guard.That(Status == PurchaseReturnStatus.Draft, "document.not_draft",
        "Solo se pueden modificar documentos en borrador.");
}
