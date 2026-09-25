using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Factura de venta (una por pedido).</summary>
public sealed class Invoice : Entity, IConcurrencyAware, IAggregateRoot
{
    private readonly List<InvoiceLine> _lines = new();

    private Invoice()
    {
    }

    public Invoice(Guid tenantId, string number, Guid salesOrderId, string? fiscalAuthorizationCode)
        : base(tenantId)
    {
        Number = Guard.Text(number, "El número", 40);
        SalesOrderId = Guard.NotEmpty(salesOrderId, nameof(salesOrderId));
        FiscalAuthorizationCode = Guard.OptionalText(fiscalAuthorizationCode, "El código de autorización", 100);
        Status = InvoiceStatus.Draft;
    }

    public string Number { get; private set; } = string.Empty;

    public Guid SalesOrderId { get; private set; }

    public InvoiceStatus Status { get; private set; }

    public DateTimeOffset? IssuedAt { get; private set; }

    public string? FiscalAuthorizationCode { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    public string? VoidReason { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<InvoiceLine> Lines => _lines;

    /// <summary>Agrega una línea (solo en borrador).</summary>
    public InvoiceLine AddLine(Guid salesOrderLineId, Guid? taxRateId, decimal taxAmount)
    {
        EnsureDraft();
        var line = new InvoiceLine(TenantId, Id, salesOrderLineId, taxRateId, taxAmount);
        _lines.Add(line);
        return line;
    }

    /// <summary>Emite la factura (deja de ser borrador).</summary>
    public void Issue(DateTimeOffset now)
    {
        EnsureDraft();
        Guard.That(_lines.Count > 0, "invoice.empty", "Una factura necesita al menos una línea.");
        IssuedAt = now.ToUniversalTime();
        Status = InvoiceStatus.Issued;
    }

    /// <summary>Anula una factura emitida (queda el registro; nunca se borra).</summary>
    public void Void(string reason, DateTimeOffset now)
    {
        Guard.That(Status == InvoiceStatus.Issued, "invoice.not_issued", "Solo se anula una factura emitida.");
        VoidReason = Guard.Text(reason, "El motivo de anulación", 200);
        VoidedAt = now.ToUniversalTime();
        Status = InvoiceStatus.Voided;
    }

    private void EnsureDraft() => Guard.That(Status == InvoiceStatus.Draft, "document.not_draft",
        "Solo se pueden modificar documentos en borrador.");
}
