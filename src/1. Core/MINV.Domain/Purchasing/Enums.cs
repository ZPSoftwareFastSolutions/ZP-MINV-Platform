namespace MINV.Domain.Purchasing;

/// <summary>Estado de una orden de compra. Se guarda como texto.</summary>
public enum PurchaseOrderStatus
{
    Draft,
    Approved,
    PartiallyReceived,
    Received,
    Cancelled,
}

/// <summary>Estado de una recepción. Se guarda como texto.</summary>
public enum GoodsReceiptStatus
{
    Draft,
    Posted,
}

/// <summary>Estado de una factura de proveedor. Se guarda como texto.</summary>
public enum SupplierInvoiceStatus
{
    Draft,
    Posted,
    Paid,
    Cancelled,
}

/// <summary>Estado de una devolución. Se guarda como texto.</summary>
public enum PurchaseReturnStatus
{
    Draft,
    Posted,
}
