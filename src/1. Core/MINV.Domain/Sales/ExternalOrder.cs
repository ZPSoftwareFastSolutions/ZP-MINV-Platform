using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>
/// V4 · Pedido de un canal externo (e-commerce, ERP) ya registrado como venta: (canal, id externo) es único por empresa y
/// guarda el hash del contenido. Un reintento con el mismo id devuelve la venta original (idempotencia); el mismo id con
/// otro contenido se rechaza. El canal es la API Key que lo envió (no lo elige el cuerpo de la petición).
/// </summary>
public sealed class ExternalOrder : Entity, IBranchScoped, IAppendOnly
{
    private ExternalOrder()
    {
    }

    public ExternalOrder(Guid tenantId, Guid branchId, string channel, string externalId, string requestHash, Guid salesOrderId, string invoiceNumber,
        DateTimeOffset receivedAt)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        Channel = Guard.Text(channel, "El canal", 60);
        ExternalId = Guard.Text(externalId, "El id externo del pedido", 100);
        Guard.That(requestHash is { Length: 64 } && requestHash.All(char.IsAsciiHexDigit), "external_order.hash", "Hash de la petición inválido.");
        RequestHash = requestHash.ToLowerInvariant();
        SalesOrderId = Guard.NotEmpty(salesOrderId, nameof(salesOrderId));
        InvoiceNumber = Guard.Text(invoiceNumber, "El número de factura", 30);
        ReceivedAt = receivedAt.ToUniversalTime();
    }

    public Guid BranchId { get; private set; }

    public string Channel { get; private set; } = string.Empty;

    public string ExternalId { get; private set; } = string.Empty;

    /// <summary>SHA-256 del contenido normalizado del pedido.</summary>
    public string RequestHash { get; private set; } = string.Empty;

    public Guid SalesOrderId { get; private set; }

    public string InvoiceNumber { get; private set; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; private set; }
}
