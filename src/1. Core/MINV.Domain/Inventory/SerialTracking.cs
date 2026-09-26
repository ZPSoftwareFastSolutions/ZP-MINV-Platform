using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>V4.2 · Qué le pasó a una unidad serializada. Se guarda como texto.</summary>
public enum SerialEventAction
{
    /// <summary>Ingresó al stock por primera vez (recepción de compra, saldo inicial, ajuste positivo).</summary>
    Received,

    /// <summary>Vendida a un cliente.</summary>
    Sold,

    /// <summary>Devuelta por el cliente (a una existencia o a revisión).</summary>
    Returned,

    TransferDispatched,
    TransferReceived,

    /// <summary>Entró a un caso de garantía (RMA).</summary>
    RmaReceived,

    /// <summary>Enviada al proveedor o al servicio técnico para reparación (sigue en RMA).</summary>
    SentToSupplier,

    /// <summary>Reparada dentro del caso RMA.</summary>
    Repaired,

    /// <summary>El cliente recibió otra unidad en su lugar (esta sigue en RMA hasta devolverla al proveedor).</summary>
    Replaced,

    /// <summary>Unidad nueva entregada como reposición de una garantía (sale del stock).</summary>
    ReplacementIssued,

    ReturnedToSupplier,
    Scrapped,
    Adjusted,

    /// <summary>V4.2 · Volvió a una existencia vendible después de una devolución, un RMA o del proveedor.</summary>
    Restocked,

    /// <summary>V4.2 · Entregada de vuelta a su dueño al cerrar el RMA (reparada o con la garantía rechazada).</summary>
    ReturnedToCustomer,
}

/// <summary>V4.2 · Dónde, quién y con qué documento ocurre un hecho de una serie (va en su fila de bitácora).</summary>
public sealed record SerialContext(Guid BranchId, Guid UserId, DateTimeOffset OccurredAt, string? DocumentNumber = null, string? Note = null);

/// <summary>
/// V4.2 · IMEI (International Mobile Equipment Identity): 15 dígitos, el último es el dígito verificador de Luhn sobre los
/// 14 anteriores. Se acepta con espacios o guiones y se guarda solo con dígitos.
/// </summary>
public static class Imei
{
    public const int Length = 15;

    /// <summary>Quita espacios y guiones (35-209900-176148-1 → 352099001761481).</summary>
    public static string Normalize(string? value) =>
        new((value ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

    public static bool IsValid(string? value)
    {
        var digits = Normalize(value);
        return digits.Length == Length && digits.All(char.IsAsciiDigit) && CheckDigit(digits[..^1]) == digits[^1] - '0';
    }

    /// <summary>Dígito verificador de Luhn de los 14 primeros dígitos (se duplica cada segundo dígito desde la izquierda).</summary>
    public static int CheckDigit(string first14)
    {
        Guard.That(first14.Length == Length - 1 && first14.All(char.IsAsciiDigit), "serial.imei_invalid", "Se esperaban 14 dígitos.");
        var sum = 0;
        for (var i = 0; i < first14.Length; i++)
        {
            var d = first14[i] - '0';
            if (i % 2 == 1)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }
            sum += d;
        }
        return (10 - sum % 10) % 10;
    }
}

/// <summary>
/// V4.2 · Bitácora inmutable de una serie o IMEI: cada ingreso, venta, devolución, transferencia, caso de garantía,
/// reemplazo o baja con su documento y usuario. Es la trazabilidad que pide una tienda de tecnología (y la policía ante
/// un equipo robado).
/// </summary>
public sealed class SerialEvent : Entity, IAppendOnly
{
    private SerialEvent()
    {
    }

    public SerialEvent(Guid tenantId, Guid serialNumberId, SerialEventAction action, Guid? branchId, string? documentNumber, string? note,
        Guid? userId, DateTimeOffset occurredAt)
        : base(tenantId)
    {
        SerialNumberId = Guard.NotEmpty(serialNumberId, nameof(serialNumberId));
        Action = Guard.Defined(action, "La acción");
        BranchId = Guard.NotEmptyIfPresent(branchId, nameof(branchId));
        DocumentNumber = Guard.OptionalText(documentNumber, "El documento", 40);
        Note = Guard.OptionalText(note is { Length: > 300 } ? note[..300] : note, "La nota", 300);
        UserId = Guard.NotEmptyIfPresent(userId, nameof(userId));
        OccurredAt = occurredAt.ToUniversalTime();
    }

    public Guid SerialNumberId { get; private set; }

    public SerialEventAction Action { get; private set; }

    /// <summary>Sucursal donde ocurrió (dato del hecho; la serie en sí es de la empresa y viaja entre sucursales).</summary>
    public Guid? BranchId { get; private set; }

    public string? DocumentNumber { get; private set; }

    public string? Note { get; private set; }

    public Guid? UserId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>V4.2 · Unidades (series) vendidas en una línea de venta: salen en el ticket, en la factura del SIN
/// (numeroSerie/numeroImei) y en la garantía.</summary>
public sealed class SalesOrderLineSerial : Entity, IBranchScoped, IAppendOnly
{
    private SalesOrderLineSerial()
    {
    }

    public SalesOrderLineSerial(Guid tenantId, Guid branchId, Guid salesOrderLineId, Guid serialNumberId)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        SalesOrderLineId = Guard.NotEmpty(salesOrderLineId, nameof(salesOrderLineId));
        SerialNumberId = Guard.NotEmpty(serialNumberId, nameof(serialNumberId));
    }

    public Guid BranchId { get; private set; }

    public Guid SalesOrderLineId { get; private set; }

    public Guid SerialNumberId { get; private set; }
}

/// <summary>V4.2 · Unidades devueltas en una línea de devolución.</summary>
public sealed class SalesReturnLineSerial : Entity, IBranchScoped, IAppendOnly
{
    private SalesReturnLineSerial()
    {
    }

    public SalesReturnLineSerial(Guid tenantId, Guid branchId, Guid salesReturnLineId, Guid serialNumberId)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        SalesReturnLineId = Guard.NotEmpty(salesReturnLineId, nameof(salesReturnLineId));
        SerialNumberId = Guard.NotEmpty(serialNumberId, nameof(serialNumberId));
    }

    public Guid BranchId { get; private set; }

    public Guid SalesReturnLineId { get; private set; }

    public Guid SerialNumberId { get; private set; }
}

/// <summary>V4.2 · Unidades que viajan en una línea de transferencia (las ven origen y destino).</summary>
public sealed class StockTransferLineSerial : Entity, IInterBranch, IAppendOnly
{
    private StockTransferLineSerial()
    {
    }

    public StockTransferLineSerial(Guid tenantId, Guid fromBranchId, Guid toBranchId, Guid stockTransferLineId, Guid serialNumberId)
        : base(tenantId)
    {
        FromBranchId = Guard.NotEmpty(fromBranchId, nameof(fromBranchId));
        ToBranchId = Guard.NotEmpty(toBranchId, nameof(toBranchId));
        StockTransferLineId = Guard.NotEmpty(stockTransferLineId, nameof(stockTransferLineId));
        SerialNumberId = Guard.NotEmpty(serialNumberId, nameof(serialNumberId));
    }

    public Guid FromBranchId { get; private set; }

    public Guid ToBranchId { get; private set; }

    public Guid StockTransferLineId { get; private set; }

    public Guid SerialNumberId { get; private set; }
}
