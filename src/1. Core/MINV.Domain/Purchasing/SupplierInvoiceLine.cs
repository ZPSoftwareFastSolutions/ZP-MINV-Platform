using MINV.Domain.Common;

namespace MINV.Domain.Purchasing;

/// <summary>Línea de factura de proveedor.</summary>
public sealed class SupplierInvoiceLine : Entity
{
    private SupplierInvoiceLine()
    {
    }

    public SupplierInvoiceLine(Guid tenantId, Guid supplierInvoiceId, Guid? goodsReceiptLineId, string? description, decimal quantity, decimal unitCost, Guid? taxRateId)
        : base(tenantId)
    {
        SupplierInvoiceId = Guard.NotEmpty(supplierInvoiceId, nameof(supplierInvoiceId));
        GoodsReceiptLineId = Guard.NotEmptyIfPresent(goodsReceiptLineId, nameof(goodsReceiptLineId));
        Description = Guard.OptionalText(description, "La descripción", 200);
        Quantity = Quantities.Round6(Guard.Positive(quantity, "La cantidad"));
        UnitCost = Guard.NonNegative(unitCost, "El costo unitario");
        TaxRateId = Guard.NotEmptyIfPresent(taxRateId, nameof(taxRateId));
    }

    public Guid SupplierInvoiceId { get; private set; }

    public Guid? GoodsReceiptLineId { get; private set; }

    public string? Description { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitCost { get; private set; }

    public Guid? TaxRateId { get; private set; }
}
