using MINV.Domain.Common;

namespace MINV.Domain.Purchasing;

/// <summary>Factura de proveedor.</summary>
public sealed class SupplierInvoice : Entity, IConcurrencyAware, IAggregateRoot, IBranchScoped
{
    private readonly List<SupplierInvoiceLine> _lines = new();

    private SupplierInvoice()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public SupplierInvoice(Guid tenantId, Guid branchId, Guid supplierId, string number, DateOnly invoiceDate, DateOnly? dueDate, Guid currencyId)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        SupplierId = Guard.NotEmpty(supplierId, nameof(supplierId));
        Number = Guard.Text(number, "El número", 40);
        InvoiceDate = invoiceDate;
        DueDate = dueDate;
        CurrencyId = Guard.NotEmpty(currencyId, nameof(currencyId));
        Status = SupplierInvoiceStatus.Draft;
    }

    public Guid SupplierId { get; private set; }

    public string Number { get; private set; } = string.Empty;

    public DateOnly InvoiceDate { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public Guid CurrencyId { get; private set; }

    public SupplierInvoiceStatus Status { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<SupplierInvoiceLine> Lines => _lines;

    /// <summary>Agrega una línea (solo en borrador).</summary>
    public SupplierInvoiceLine AddLine(Guid? goodsReceiptLineId, string? description, decimal quantity, decimal unitCost, Guid? taxRateId)
    {
        EnsureDraft();
        var line = new SupplierInvoiceLine(TenantId, BranchId, Id, goodsReceiptLineId, description, quantity, unitCost, taxRateId);
        _lines.Add(line);
        return line;
    }

    private void EnsureDraft() => Guard.That(Status == SupplierInvoiceStatus.Draft, "document.not_draft",
        "Solo se pueden modificar documentos en borrador.");
}
