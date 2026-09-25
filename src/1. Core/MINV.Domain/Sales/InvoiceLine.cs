using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Línea fiscal de una factura (impuesto aplicado).</summary>
public sealed class InvoiceLine : Entity, IBranchScoped
{
    private InvoiceLine()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public InvoiceLine(Guid tenantId, Guid branchId, Guid invoiceId, Guid salesOrderLineId, Guid? taxRateId, decimal taxAmount)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        InvoiceId = Guard.NotEmpty(invoiceId, nameof(invoiceId));
        SalesOrderLineId = Guard.NotEmpty(salesOrderLineId, nameof(salesOrderLineId));
        TaxRateId = Guard.NotEmptyIfPresent(taxRateId, nameof(taxRateId));
        TaxAmount = Guard.NonNegative(taxAmount, "El impuesto");
    }

    public Guid InvoiceId { get; private set; }

    public Guid SalesOrderLineId { get; private set; }

    public Guid? TaxRateId { get; private set; }

    public decimal TaxAmount { get; private set; }
}
