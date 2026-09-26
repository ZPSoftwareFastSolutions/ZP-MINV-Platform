using MINV.Domain.Catalog;
using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>
/// Número de serie o IMEI de UNA unidad (trazabilidad 1 a 1). V4.2 (reglas T-02 y T-03): la serie es de una variante
/// (única por empresa, variante y serie), entra al stock con su lote y su existencia, y su estado cambia SOLO con los
/// métodos de negocio de esta clase (recibir, vender, devolver, RMA, transferir, devolver al proveedor, dar de baja). Cada
/// cambio valida el estado de origen (códigos <c>serial.*</c>) y agrega su fila a la bitácora append-only
/// <see cref="SerialEvent"/> (<see cref="History"/>), que el método también devuelve.
/// Las existencias siguen saliendo de los movimientos (A-05): la serie es trazabilidad, no reemplaza al kardex; el caso de
/// uso registra el movimiento y llama al método de la serie en la misma transacción. En cada sucursal, las series en
/// stock de una variante serializada coinciden con su stock (vista <c>inventory.v_serial_breaches</c>).
/// </summary>
public sealed class SerialNumber : Entity, IConcurrencyAware, IAggregateRoot
{
    public const int MaxLength = 80;

    private readonly List<SerialEvent> _history = new();

    private SerialNumber()
    {
    }

    private SerialNumber(Guid tenantId, Guid variantId, SerialKind kind, string serial, DateTimeOffset receivedAt)
        : base(tenantId)
    {
        VariantId = Guard.NotEmpty(variantId, nameof(variantId));
        Kind = Guard.Defined(kind, "El tipo de serie");
        Serial = Normalize(kind, serial);
        ReceivedAt = receivedAt.ToUniversalTime();
    }

    /// <summary>Variante de la unidad (la serie es única por empresa, variante y serie).</summary>
    public Guid VariantId { get; private set; }

    /// <summary>Lote de la unidad: el de la existencia donde está o donde estuvo por última vez (siempre de su variante).</summary>
    public Guid BatchId { get; private set; }

    /// <summary>Serie del fabricante o IMEI (solo dígitos).</summary>
    public SerialKind Kind { get; private set; }

    public string Serial { get; private set; } = string.Empty;

    /// <summary>Existencia (posición + lote, en una sucursal) donde está la unidad; null si no está en stock.</summary>
    public Guid? StockLevelId { get; private set; }

    public SerialNumberStatus Status { get; private set; }

    /// <summary>Primer ingreso al stock de la empresa.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL): dos cajas no pueden vender la misma unidad.</summary>
    public uint RowVersion { get; private set; }

    /// <summary>Bitácora de la unidad (solo las filas agregadas en esta sesión si no se cargó).</summary>
    public IReadOnlyCollection<SerialEvent> History => _history;

    /// <summary>Ocupa una existencia y cuenta como stock (<c>InStock</c> o el <c>Reserved</c> de la V3).</summary>
    public bool IsOnHand => Status is SerialNumberStatus.InStock or SerialNumberStatus.Reserved;

    /// <summary>Se puede vender, transferir o entregar como reposición.</summary>
    public bool IsAvailable => Status == SerialNumberStatus.InStock;

    /// <summary>
    /// Serie o IMEI normalizado: el IMEI sin espacios ni guiones, con 15 dígitos y dígito de Luhn correcto; la serie sin
    /// espacios en los extremos, en mayúsculas, sin espacios ni separadores internos (coma o punto y coma: la factura del SIN
    /// lista las series separadas por coma) y de hasta 80 caracteres.
    /// </summary>
    public static string Normalize(SerialKind kind, string? value)
    {
        if (kind == SerialKind.Imei)
        {
            var digits = Imei.Normalize(value);
            Guard.That(Imei.IsValid(digits), "serial.imei_invalid",
                $"El IMEI «{value?.Trim()}» no es válido: debe tener 15 dígitos y el dígito verificador (Luhn) correcto.");
            return digits;
        }
        var serial = Guard.Text(value, "La serie", MaxLength).ToUpperInvariant();
        Guard.That(serial.All(c => !char.IsWhiteSpace(c) && !char.IsControl(c) && c is not (',' or ';')), "serial.format",
            $"La serie «{serial}» no puede tener espacios, comas ni punto y coma.");
        return serial;
    }

    /// <summary>
    /// Ingreso de una unidad nueva al stock (recepción de compra, saldo inicial, ajuste positivo): queda <c>InStock</c> en la
    /// existencia <paramref name="level"/> (del lote <paramref name="batch"/> y de la sucursal del hecho).
    /// </summary>
    public static SerialNumber Receive(SerialKind kind, string serial, Batch batch, StockLevel level, SerialContext context)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(context);
        var unit = new SerialNumber(batch.TenantId, batch.VariantId, kind, serial, context.OccurredAt);
        unit.EnterStock(level, batch, context);
        unit.Status = SerialNumberStatus.InStock;
        unit.Log(SerialEventAction.Received, context);
        return unit;
    }

    /// <summary>Venta: la unidad sale de su existencia (en la sucursal de la venta).</summary>
    public SerialEvent Sell(StockLevel from, SerialContext context)
    {
        EnsureStatus("vender", SerialNumberStatus.InStock, SerialNumberStatus.Reserved);
        LeaveStock(from, context);
        Status = SerialNumberStatus.Sold;
        return Log(SerialEventAction.Sold, context);
    }

    /// <summary>Reposición por garantía: una unidad del stock se entrega al cliente en lugar de la defectuosa (sale con el
    /// movimiento de reposición por garantía).</summary>
    public SerialEvent IssueAsReplacement(StockLevel from, SerialContext context)
    {
        EnsureStatus("entregar como reposición", SerialNumberStatus.InStock);
        LeaveStock(from, context);
        Status = SerialNumberStatus.Sold;
        return Log(SerialEventAction.ReplacementIssued, context);
    }

    /// <summary>
    /// Devolución del cliente: con existencia (<paramref name="to"/> y <paramref name="batch"/>) vuelve al stock vendible;
    /// sin existencia queda <c>Returned</c> (a revisión: luego se repone, va a RMA, al proveedor o se da de baja).
    /// </summary>
    public SerialEvent Return(StockLevel? to, Batch? batch, SerialContext context)
    {
        EnsureStatus("devolver", SerialNumberStatus.Sold);
        if (to is not null)
        {
            EnterStock(to, batch ?? throw new ArgumentNullException(nameof(batch)), context);
            Status = SerialNumberStatus.InStock;
        }
        else
        {
            Status = SerialNumberStatus.Returned;
        }
        return Log(SerialEventAction.Returned, context);
    }

    /// <summary>Vuelve al stock vendible una unidad devuelta, reparada en un RMA o que el proveedor mandó de vuelta.</summary>
    public SerialEvent Restock(StockLevel to, Batch batch, SerialContext context)
    {
        EnsureStatus("reponer al stock", SerialNumberStatus.Returned, SerialNumberStatus.InRma, SerialNumberStatus.ReturnedToSupplier);
        EnterStock(to, batch, context);
        Status = SerialNumberStatus.InStock;
        return Log(SerialEventAction.Restocked, context);
    }

    /// <summary>Entra a un caso de garantía (RMA): una unidad vendida (o devuelta) que el cliente trae con una falla.</summary>
    public SerialEvent SendToRma(SerialContext context)
    {
        EnsureStatus("recibir en garantía", SerialNumberStatus.Sold, SerialNumberStatus.Returned);
        Status = SerialNumberStatus.InRma;
        return Log(SerialEventAction.RmaReceived, context);
    }

    /// <summary>Dentro del RMA: se envía al proveedor o al servicio técnico para reparación (sigue en RMA).</summary>
    public SerialEvent SendToSupplier(SerialContext context)
    {
        EnsureStatus("enviar al proveedor", SerialNumberStatus.InRma);
        return Log(SerialEventAction.SentToSupplier, context);
    }

    /// <summary>Dentro del RMA: quedó reparada (sigue en RMA hasta entregarla).</summary>
    public SerialEvent MarkRepaired(SerialContext context)
    {
        EnsureStatus("marcar como reparada", SerialNumberStatus.InRma);
        return Log(SerialEventAction.Repaired, context);
    }

    /// <summary>Dentro del RMA: el cliente recibió otra unidad; esta sigue en RMA para devolverla al proveedor o darla de baja.</summary>
    public SerialEvent MarkReplaced(SerialContext context)
    {
        EnsureStatus("marcar como reemplazada", SerialNumberStatus.InRma);
        return Log(SerialEventAction.Replaced, context);
    }

    /// <summary>Cierre del RMA: la unidad vuelve a su dueño (reparada o con la garantía rechazada).</summary>
    public SerialEvent ReturnFromRma(SerialContext context)
    {
        EnsureStatus("entregar al cliente", SerialNumberStatus.InRma);
        Status = SerialNumberStatus.Sold;
        return Log(SerialEventAction.ReturnedToCustomer, context);
    }

    /// <summary>Devolución al proveedor: desde el stock (con la existencia de salida) o desde una devolución o un RMA.</summary>
    public SerialEvent ReturnToSupplier(StockLevel? from, SerialContext context)
    {
        EnsureStatus("devolver al proveedor", SerialNumberStatus.InStock, SerialNumberStatus.Reserved, SerialNumberStatus.Returned,
            SerialNumberStatus.InRma);
        LeaveStockIfOnHand(from, context);
        Status = SerialNumberStatus.ReturnedToSupplier;
        return Log(SerialEventAction.ReturnedToSupplier, context);
    }

    /// <summary>Baja definitiva (daño, pérdida, faltante en una transferencia): desde el stock (con la existencia de salida),
    /// en tránsito, devuelta o en RMA.</summary>
    public SerialEvent Scrap(StockLevel? from, SerialContext context)
    {
        EnsureStatus("dar de baja", SerialNumberStatus.InStock, SerialNumberStatus.Reserved, SerialNumberStatus.InTransit,
            SerialNumberStatus.Returned, SerialNumberStatus.InRma);
        LeaveStockIfOnHand(from, context);
        Status = SerialNumberStatus.Scrapped;
        return Log(SerialEventAction.Scrapped, context);
    }

    /// <summary>Despacho de una transferencia: sale de la existencia del origen y queda en tránsito.</summary>
    public SerialEvent TransferOut(StockLevel from, SerialContext context)
    {
        EnsureStatus("despachar", SerialNumberStatus.InStock);
        LeaveStock(from, context);
        Status = SerialNumberStatus.InTransit;
        return Log(SerialEventAction.TransferDispatched, context);
    }

    /// <summary>Recepción de una transferencia: entra a la existencia del destino.</summary>
    public SerialEvent TransferIn(StockLevel to, Batch batch, SerialContext context)
    {
        EnsureStatus("recibir en la transferencia", SerialNumberStatus.InTransit);
        EnterStock(to, batch, context);
        Status = SerialNumberStatus.InStock;
        return Log(SerialEventAction.TransferReceived, context);
    }

    public static string Describe(SerialNumberStatus status) => status switch
    {
        SerialNumberStatus.InStock => "en stock",
        SerialNumberStatus.Reserved => "reservada",
        SerialNumberStatus.Sold => "vendida",
        SerialNumberStatus.Returned => "devuelta por el cliente",
        SerialNumberStatus.Scrapped => "dada de baja",
        SerialNumberStatus.InTransit => "en tránsito",
        SerialNumberStatus.InRma => "en garantía (RMA)",
        SerialNumberStatus.ReturnedToSupplier => "devuelta al proveedor",
        _ => status.ToString(),
    };

    private void EnsureStatus(string action, params SerialNumberStatus[] allowed) =>
        Guard.That(allowed.Contains(Status), "serial.state", $"No se puede {action} la serie {Serial}: está {Describe(Status)}.");

    private void EnterStock(StockLevel to, Batch batch, SerialContext context)
    {
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(context);
        EnsureSameTenant(to, "La existencia");
        EnsureSameTenant(batch, "El lote");
        Guard.That(batch.VariantId == VariantId, "serial.variant", $"La serie {Serial} es de otro producto que el lote {batch.LotNumber}.");
        Guard.That(to.BatchId == batch.Id, "serial.batch", $"La existencia de destino de la serie {Serial} no es del lote {batch.LotNumber}.");
        Guard.That(to.BranchId == context.BranchId, "serial.branch", $"La serie {Serial} solo puede entrar al stock de la sucursal de la operación.");
        StockLevelId = to.Id;
        BatchId = batch.Id;
    }

    private void LeaveStock(StockLevel from, SerialContext context)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(context);
        EnsureSameTenant(from, "La existencia");
        Guard.That(StockLevelId == from.Id, "serial.location", $"La serie {Serial} no está en esa posición y lote.");
        Guard.That(from.BranchId == context.BranchId, "serial.branch", $"La serie {Serial} está en otra sucursal.");
        StockLevelId = null;
    }

    private void LeaveStockIfOnHand(StockLevel? from, SerialContext context)
    {
        if (IsOnHand)
        {
            LeaveStock(from ?? throw new DomainException("serial.location", $"Indique la existencia de la que sale la serie {Serial}."), context);
        }
    }

    private SerialEvent Log(SerialEventAction action, SerialContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var row = new SerialEvent(TenantId, Id, action, context.BranchId, context.DocumentNumber, context.Note, context.UserId, context.OccurredAt);
        _history.Add(row);
        return row;
    }
}
