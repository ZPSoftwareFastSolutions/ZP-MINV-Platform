namespace MINV.Domain.Inventory;

/// <summary>Dominio del tipo de movimiento (en la V2.1: BODEGA → 10A, VENTAS → 10B). Se guarda como texto.</summary>
public enum MovementDomain
{
    Warehouse,
    Sales,
}

/// <summary>
/// Estado de un número de serie o IMEI. Se guarda como texto. V4.2 (regla T-02): <c>InStock</c> (en una existencia de una
/// sucursal) → <c>Sold</c> → <c>Returned</c>/<c>InRma</c> → <c>InStock</c>/<c>ReturnedToSupplier</c>/<c>Scrapped</c>;
/// <c>InStock</c> → <c>InTransit</c> → <c>InStock</c> en las transferencias. Solo <c>InStock</c> se vende; <c>InStock</c> y
/// <c>Reserved</c> (estado de la V3, sin uso en la V4.2) son las únicas que ocupan una existencia y cuentan como stock.
/// Las transiciones las hacen SOLO los métodos de <see cref="SerialNumber"/>.
/// </summary>
public enum SerialNumberStatus
{
    InStock,
    Reserved,
    Sold,
    Returned,
    Scrapped,

    /// <summary>V4.2 · Despachada en una transferencia y todavía no recibida en el destino.</summary>
    InTransit,

    /// <summary>V4.2 · En un caso de garantía (RMA): no es stock vendible.</summary>
    InRma,

    /// <summary>V4.2 · Devuelta al proveedor (reemplazo o crédito del proveedor).</summary>
    ReturnedToSupplier,
}

/// <summary>Estado de una reserva. Se guarda como texto.</summary>
public enum ReservationStatus
{
    Active,
    Consumed,
    Released,
    Expired,
}

/// <summary>Estado de un ajuste. Se guarda como texto.</summary>
public enum AdjustmentStatus
{
    Draft,
    Posted,
    Cancelled,
}

/// <summary>Estado de una toma física. Se guarda como texto.</summary>
public enum PhysicalCountStatus
{
    Open,
    Posted,
    Cancelled,
}

/// <summary>V4 · Máquina de estados de una transferencia: Pending → Dispatched (en tránsito) → Received; Pending → Cancelled.</summary>
public enum TransferStatus
{
    Pending,
    Dispatched,
    Received,
    Cancelled,
}
