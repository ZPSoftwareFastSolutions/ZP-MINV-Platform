using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Pago de una factura (append-only).</summary>
public sealed class Payment : Entity, IAppendOnly
{
    private Payment()
    {
    }

    public Payment(Guid tenantId, Guid invoiceId, Guid paymentMethodId, decimal amount, string? reference, DateTimeOffset paidAt, Guid? posSessionId, Guid recordedByUserId)
        : base(tenantId)
    {
        InvoiceId = Guard.NotEmpty(invoiceId, nameof(invoiceId));
        PaymentMethodId = Guard.NotEmpty(paymentMethodId, nameof(paymentMethodId));
        Amount = Guard.Positive(amount, "El monto");
        Reference = Guard.OptionalText(reference, "La referencia", 60);
        PaidAt = paidAt.ToUniversalTime();
        PosSessionId = Guard.NotEmptyIfPresent(posSessionId, nameof(posSessionId));
        RecordedByUserId = Guard.NotEmpty(recordedByUserId, nameof(recordedByUserId));
    }

    public Guid InvoiceId { get; private set; }

    public Guid PaymentMethodId { get; private set; }

    public decimal Amount { get; private set; }

    public string? Reference { get; private set; }

    public DateTimeOffset PaidAt { get; private set; }

    public Guid? PosSessionId { get; private set; }

    public Guid RecordedByUserId { get; private set; }
}
