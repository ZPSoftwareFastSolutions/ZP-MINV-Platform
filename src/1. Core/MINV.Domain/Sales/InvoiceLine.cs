using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Línea fiscal de una factura (impuesto aplicado).</summary>
public sealed class InvoiceLine : Entity
{
    private InvoiceLine()
    {
    }

    public InvoiceLine(Guid tenantId, Guid invoiceId, Guid salesOrderLineId, Guid? taxRateId, decimal taxAmount)
        : base(tenantId)
    {
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
