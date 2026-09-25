namespace MINV.Domain.Inventory;

/// <summary>Dominio del tipo de movimiento (en la V2.1: BODEGA → 10A, VENTAS → 10B). Se guarda como texto.</summary>
public enum MovementDomain
{
    Warehouse,
    Sales,
}

/// <summary>Estado de un número de serie. Se guarda como texto.</summary>
public enum SerialNumberStatus
{
    InStock,
    Reserved,
    Sold,
    Returned,
    Scrapped,
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
