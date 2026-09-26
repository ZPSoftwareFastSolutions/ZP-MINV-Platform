using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>
/// V4.1 · Devolución (total o parcial) de una venta: el stock vuelve con movimientos DEVOLUCIÓN DE CLIENTE, se
/// reembolsa al cliente y, si la venta estaba facturada en el SIAT, se emite una nota crédito-débito (sector 24).
/// El importe a reembolsar no se guarda: sale de las líneas del pedido original (precio y descuento de la venta).
/// </summary>
public sealed class SalesReturn : Entity, IBranchScoped, IConcurrencyAware, IAggregateRoot
{
    private readonly List<SalesReturnLine> _lines = new();

    private SalesReturn()
    {
    }

    public SalesReturn(Guid tenantId, Guid branchId, string number, Guid invoiceId, Guid customerId, string reason, Guid refundPaymentMethodId,
        Guid? posSessionId, Guid userId, DateTimeOffset returnedAt)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        Number = Guard.Text(number, "El número de devolución", 40);
        InvoiceId = Guard.NotEmpty(invoiceId, nameof(invoiceId));
        CustomerId = Guard.NotEmpty(customerId, nameof(customerId));
        Reason = Guard.Text(reason, "El motivo de la devolución", 200);
        RefundPaymentMethodId = Guard.NotEmpty(refundPaymentMethodId, nameof(refundPaymentMethodId));
        PosSessionId = Guard.NotEmptyIfPresent(posSessionId, nameof(posSessionId));
        UserId = Guard.NotEmpty(userId, nameof(userId));
        ReturnedAt = returnedAt;
    }

    public Guid BranchId { get; private set; }

    public string Number { get; private set; } = string.Empty;

    /// <summary>Venta (factura de M-INV) que se devuelve.</summary>
    public Guid InvoiceId { get; private set; }

    public Guid CustomerId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    /// <summary>Medio con el que se reembolsa (efectivo: sale de la caja del turno).</summary>
    public Guid RefundPaymentMethodId { get; private set; }

    public Guid? PosSessionId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset ReturnedAt { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<SalesReturnLine> Lines => _lines;

    /// <summary>Agrega una línea devuelta; <paramref name="alreadyReturned"/> es lo devuelto antes de esa misma línea.</summary>
    public SalesReturnLine AddLine(SalesOrderLine orderLine, decimal quantity, decimal alreadyReturned)
    {
        ArgumentNullException.ThrowIfNull(orderLine);
        EnsureSameTenant(orderLine, "La línea de la venta");
        Guard.That(orderLine.BranchId == BranchId, "return.branch", "La devolución se registra en la sucursal de la venta.");
        Guard.That(_lines.All(l => l.SalesOrderLineId != orderLine.Id), "return.duplicate_line", "La línea ya está en la devolución.");
        var q = Quantities.Round6(Guard.Positive(quantity, "La cantidad devuelta"));
        Guard.That(alreadyReturned + q <= orderLine.Quantity, "return.exceeds",
            $"No se puede devolver más de lo vendido: vendido {Quantities.Format(orderLine.Quantity)}, " +
            $"ya devuelto {Quantities.Format(alreadyReturned)}.");
        var line = new SalesReturnLine(TenantId, BranchId, Id, orderLine.Id, orderLine.VariantId, q);
        _lines.Add(line);
        return line;
    }

    /// <summary>Importe a reembolsar: cantidad devuelta × precio × (1 − descuento), redondeado a centavos por línea.</summary>
    public static decimal RefundOf(SalesOrderLine orderLine, decimal quantity) =>
        decimal.Round(quantity * orderLine.UnitPrice * (1 - orderLine.DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero);
}

/// <summary>V4.1 · Línea de una devolución (enlaza el movimiento de stock que la repuso).</summary>
public sealed class SalesReturnLine : Entity, IBranchScoped
{
    private SalesReturnLine()
    {
    }

    internal SalesReturnLine(Guid tenantId, Guid branchId, Guid salesReturnId, Guid salesOrderLineId, Guid variantId, decimal quantity)
        : base(tenantId)
    {
        BranchId = branchId;
        SalesReturnId = salesReturnId;
        SalesOrderLineId = salesOrderLineId;
        VariantId = variantId;
        Quantity = quantity;
    }

    public Guid BranchId { get; private set; }

    public Guid SalesReturnId { get; private set; }

    public Guid SalesOrderLineId { get; private set; }

    public Guid VariantId { get; private set; }

    public decimal Quantity { get; private set; }

    public Guid? StockMovementId { get; private set; }

    public void LinkMovement(Guid stockMovementId) => StockMovementId = Guard.NotEmpty(stockMovementId, nameof(stockMovementId));
}
