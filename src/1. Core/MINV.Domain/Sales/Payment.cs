using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Pago de una factura (append-only).</summary>
public sealed class Payment : Entity, IAppendOnly, IBranchScoped
{
    private Payment()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public Payment(Guid tenantId, Guid branchId, Guid invoiceId, Guid paymentMethodId, decimal amount, string? reference, DateTimeOffset paidAt, Guid? posSessionId, Guid recordedByUserId)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
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
