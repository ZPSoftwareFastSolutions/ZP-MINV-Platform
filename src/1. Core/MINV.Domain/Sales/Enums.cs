namespace MINV.Domain.Sales;

/// <summary>Estado de un turno de caja. Se guarda como texto.</summary>
public enum PosSessionStatus
{
    Open,
    Closed,
}

/// <summary>Sentido de un movimiento de efectivo. Se guarda como texto.</summary>
public enum CashDirection
{
    In,
    Out,
}

/// <summary>Estado de un pedido de venta. Se guarda como texto.</summary>
public enum SalesOrderStatus
{
    Draft,
    Confirmed,
    Fulfilled,
    Invoiced,
    Cancelled,
}

/// <summary>Estado de una factura. Se guarda como texto.</summary>
public enum InvoiceStatus
{
    Draft,
    Issued,
    Voided,
}
