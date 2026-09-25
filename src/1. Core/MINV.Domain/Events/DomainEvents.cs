using MINV.Domain.Common;
using MINV.Domain.Integration;

namespace MINV.Domain.Events;

/// <summary>Base de los eventos de dominio (registro inmutable con fecha y sucursal).</summary>
public abstract record DomainEvent(DateTimeOffset OccurredAt, Guid? BranchId) : IDomainEvent
{
    public abstract string EventType { get; }
}

/// <summary>Línea de un evento de transferencia (lo que viaja en el webhook).</summary>
public sealed record TransferEventLine(Guid VariantId, decimal Quantity, decimal UnitCost);

/// <summary>La mercadería salió del origen: queda en tránsito hacia el destino.</summary>
public sealed record TransferDispatchedEvent(Guid TransferId, string Number, Guid FromBranchId, Guid ToBranchId,
    IReadOnlyList<TransferEventLine> Lines, decimal TotalCost, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt, FromBranchId)
{
    public override string EventType => IntegrationEvents.TransferDispatched;
}

/// <summary>La sucursal destino recibió la transferencia (con o sin faltantes).</summary>
public sealed record TransferReceivedEvent(Guid TransferId, string Number, Guid FromBranchId, Guid ToBranchId,
    IReadOnlyList<TransferEventLine> Lines, decimal Shortage, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt, ToBranchId)
{
    public override string EventType => IntegrationEvents.TransferReceived;
}

/// <summary>Se registró un faltante (delta compensatorio) al recibir una transferencia.</summary>
public sealed record TransferDiscrepancyRecordedEvent(Guid TransferId, string Number, Guid ToBranchId, Guid VariantId, decimal Quantity,
    string Reason, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt, ToBranchId)
{
    public override string EventType => IntegrationEvents.TransferDiscrepancy;
}

/// <summary>Venta cobrada (POS o e-commerce).</summary>
public sealed record SaleCompletedEvent(string InvoiceNumber, string OrderNumber, Guid BranchIdOfSale, string CustomerCode, decimal Total,
    decimal Tax, string Channel, IReadOnlyList<SaleEventLine> Lines, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt, BranchIdOfSale)
{
    public override string EventType => IntegrationEvents.SaleCompleted;
}

public sealed record SaleEventLine(string Sku, decimal Quantity, decimal UnitPrice, decimal DiscountPercent);

/// <summary>Venta anulada.</summary>
public sealed record SaleVoidedEvent(string InvoiceNumber, Guid BranchIdOfSale, decimal Total, string Reason, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt, BranchIdOfSale)
{
    public override string EventType => IntegrationEvents.SaleVoided;
}

/// <summary>Recepción de mercadería de un proveedor.</summary>
public sealed record PurchaseReceivedEvent(string OrderNumber, string ReceiptNumber, Guid BranchIdOfReceipt, string SupplierCode, decimal Total,
    DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt, BranchIdOfReceipt)
{
    public override string EventType => IntegrationEvents.PurchaseReceived;
}
