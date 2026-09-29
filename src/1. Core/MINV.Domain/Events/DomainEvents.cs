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

/// <summary>V4.1 · Documento fiscal válido en el SIN (908 en línea o validado en un paquete).</summary>
public sealed record FiscalDocumentValidatedEvent(Guid DocumentId, string Kind, long Number, string Cuf, Guid BranchIdOfDocument, decimal Total,
    DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt, BranchIdOfDocument)
{
    public override string EventType => IntegrationEvents.FiscalDocumentValidated;
}

/// <summary>V4.1 · Documento fiscal anulado en el SIN.</summary>
public sealed record FiscalDocumentVoidedEvent(Guid DocumentId, string Kind, long Number, string Cuf, Guid BranchIdOfDocument, int ReasonCode,
    DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt, BranchIdOfDocument)
{
    public override string EventType => IntegrationEvents.FiscalDocumentVoided;
}

/// <summary>V4.1 · Devolución de mercadería de un cliente (con nota crédito-débito si la venta estaba facturada).</summary>
public sealed record SaleReturnedEvent(string ReturnNumber, string InvoiceNumber, Guid BranchIdOfReturn, decimal Refund,
    IReadOnlyList<SaleEventLine> Lines, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt, BranchIdOfReturn)
{
    public override string EventType => IntegrationEvents.SaleReturned;
}

/// <summary>V6 · Línea de un armado en un evento (variante, ranura, cantidad y precio cotizado; nunca datos del contacto).
/// V7: <paramref name="Slot"/> es null en la línea sin ranura de un carrito.</summary>
public sealed record PcBuildEventLine(Guid VariantId, string? Slot, int Quantity, decimal QuotedUnitPrice);

/// <summary>V6 · Un armado (cotización) reservó el stock de sus piezas (desde la tienda web o el escritorio). V7: los tres
/// eventos <c>pcbuild.*</c> llevan <c>Kind</c> (campo nuevo, regla B-08): <c>Build</c> (armado) o <c>Cart</c> (carrito).</summary>
public sealed record PcBuildReservedEvent(Guid PcBuildId, string Number, Guid BranchIdOfBuild, string Channel, decimal Total,
    DateTimeOffset ReservedUntil, IReadOnlyList<PcBuildEventLine> Lines, DateTimeOffset OccurredAt, string Kind = PcBuildEventKinds.Build)
    : DomainEvent(OccurredAt, BranchIdOfBuild)
{
    public override string EventType => IntegrationEvents.PcBuildReserved;
}

/// <summary>V6 · La reserva de un armado se liberó (el cliente desistió, el vendedor la liberó o venció): el stock volvió.</summary>
public sealed record PcBuildReleasedEvent(Guid PcBuildId, string Number, Guid BranchIdOfBuild, string Channel, decimal Total, string Reason,
    bool Expired, IReadOnlyList<PcBuildEventLine> Lines, DateTimeOffset OccurredAt, string Kind = PcBuildEventKinds.Build)
    : DomainEvent(OccurredAt, BranchIdOfBuild)
{
    public override string EventType => IntegrationEvents.PcBuildReleased;
}

/// <summary>V6 · Un armado se vendió en la caja (si estaba reservado, la reserva se consumió).</summary>
public sealed record PcBuildSoldEvent(Guid PcBuildId, string Number, Guid BranchIdOfBuild, string Channel, decimal Total, Guid InvoiceId,
    bool WasReserved, IReadOnlyList<PcBuildEventLine> Lines, DateTimeOffset OccurredAt, string Kind = PcBuildEventKinds.Build)
    : DomainEvent(OccurredAt, BranchIdOfBuild)
{
    public override string EventType => IntegrationEvents.PcBuildSold;
}

/// <summary>V7 · Valores de <c>Kind</c> en los eventos <c>pcbuild.*</c> (los nombres de <c>PcBuildKind</c>).</summary>
public static class PcBuildEventKinds
{
    public const string Build = "Build";
    public const string Cart = "Cart";
}
