using MINV.Domain.Common;
using MINV.Domain.Events;

namespace MINV.Domain.Inventory;

/// <summary>
/// V4 · Transferencia de mercadería entre almacenes de sucursales (máquina de estados atómica pero diferida):
/// <c>Pending</c> (solicitada, con líneas) → <c>Dispatched</c> (salió del origen con TRASLADO (SALIDA): la mercadería
/// queda EN TRÁNSITO) → <c>Received</c> (entró al destino con TRASLADO (ENTRADA); los faltantes quedan como registros
/// compensatorios <see cref="StockTransferDiscrepancy"/>). Una pendiente se puede anular. Cada transición queda en la
/// bitácora append-only <see cref="StockTransferEvent"/> y produce un evento de dominio (webhooks).
/// Conservación: Σ salidas = Σ entradas + Σ faltantes + en tránsito.
/// </summary>
public sealed class StockTransfer : Entity, IInterBranch, IConcurrencyAware, IAggregateRoot, IHasDomainEvents
{
    private readonly List<StockTransferLine> _lines = new();
    private readonly List<StockTransferEvent> _history = new();
    private readonly List<IDomainEvent> _events = new();

    private StockTransfer()
    {
    }

    public StockTransfer(Guid tenantId, string number, Guid fromBranchId, Guid fromWarehouseId, Guid toBranchId, Guid toWarehouseId,
        Guid requestedByUserId, DateTimeOffset requestedAt, string? notes)
        : base(tenantId)
    {
        Number = Guard.Text(number, "El número", 30);
        FromBranchId = Guard.NotEmpty(fromBranchId, nameof(fromBranchId));
        FromWarehouseId = Guard.NotEmpty(fromWarehouseId, nameof(fromWarehouseId));
        ToBranchId = Guard.NotEmpty(toBranchId, nameof(toBranchId));
        ToWarehouseId = Guard.NotEmpty(toWarehouseId, nameof(toWarehouseId));
        Guard.That(fromWarehouseId != toWarehouseId, "transfer.same_warehouse", "El almacén de origen y el de destino deben ser distintos.");
        RequestedByUserId = Guard.NotEmpty(requestedByUserId, nameof(requestedByUserId));
        RequestedAt = requestedAt.ToUniversalTime();
        Notes = Guard.OptionalText(notes, "Las observaciones", 250);
        Status = TransferStatus.Pending;
        Log(TransferStatus.Pending, requestedByUserId, requestedAt, "Solicitada");
    }

    public string Number { get; private set; } = string.Empty;

    public Guid FromBranchId { get; private set; }

    public Guid FromWarehouseId { get; private set; }

    public Guid ToBranchId { get; private set; }

    public Guid ToWarehouseId { get; private set; }

    /// <summary>Estado materializado (redundancia documentada: la bitácora guarda cada transición).</summary>
    public TransferStatus Status { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset? DispatchedAt { get; private set; }

    public DateTimeOffset? ReceivedAt { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin): dos sucursales que despachan o reciben la misma transferencia a
    /// la vez no pueden hacerlo dos veces; la más lenta recibe un conflicto.</summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<StockTransferLine> Lines => _lines;

    public IReadOnlyCollection<StockTransferEvent> History => _history;

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events;

    public void ClearDomainEvents() => _events.Clear();

    /// <summary>Agrega una línea (solo pendiente; una por variante).</summary>
    public StockTransferLine AddLine(Guid variantId, decimal quantity)
    {
        EnsureStatus(TransferStatus.Pending, "agregar productos");
        Guard.That(_lines.All(l => l.VariantId != variantId), "transfer.duplicate_line", "El producto ya está en la transferencia.");
        var line = new StockTransferLine(TenantId, FromBranchId, ToBranchId, Id, variantId, quantity);
        _lines.Add(line);
        return line;
    }

    /// <summary>
    /// Despacho: cada línea sale COMPLETA del origen (una o varias salidas, de las posiciones con disponible) al costo
    /// promedio del origen. Los movimientos los registra la existencia (el poka-yoke impide el negativo).
    /// </summary>
    public void Dispatch(IReadOnlyList<LineDispatch> dispatches, Guid userId, DateTimeOffset now)
    {
        EnsureStatus(TransferStatus.Pending, "despachar");
        Guard.That(_lines.Count > 0, "transfer.empty", "La transferencia no tiene productos.");
        foreach (var line in _lines)
        {
            var mine = dispatches.Where(d => d.LineId == line.Id).ToList();
            Guard.That(mine.Count > 0, "transfer.line_not_dispatched", "Cada producto debe despacharse completo.");
            var quantity = Quantities.Round6(mine.Sum(d => d.Movement.Quantity));
            Guard.That(quantity == line.Quantity, "transfer.partial_dispatch",
                $"Se despacharían {quantity} y la línea pide {line.Quantity}: el despacho debe ser completo.");
            foreach (var d in mine)
            {
                Guard.That(d.Movement.BranchId == FromBranchId, "transfer.movement_branch", "La salida debe registrarse en la sucursal de origen.");
                line.Link(d.Movement.Id, TransferDirection.Out, FromBranchId);
            }
            foreach (var batch in mine.GroupBy(d => d.Movement.BatchId))
            {
                line.Ship(batch.Key, Quantities.Round6(batch.Sum(d => d.Movement.Quantity)));
            }
            Guard.That(mine.All(d => d.UnitCost == mine[0].UnitCost), "transfer.unit_cost", "Una línea se despacha a un único costo promedio.");
            line.SetUnitCost(mine[0].UnitCost);
        }
        Status = TransferStatus.Dispatched;
        DispatchedAt = now.ToUniversalTime();
        Log(TransferStatus.Dispatched, userId, now, "Despachada: mercadería en tránsito");
        _events.Add(new TransferDispatchedEvent(Id, Number, FromBranchId, ToBranchId,
            _lines.Select(l => new TransferEventLine(l.VariantId, l.Quantity, l.UnitCost ?? 0)).ToList(),
            _lines.Sum(l => l.Quantity * (l.UnitCost ?? 0)), now));
    }

    /// <summary>
    /// Recepción (de una vez, todas las líneas): lo recibido entra al destino; si llega menos de lo despachado, la
    /// diferencia exige motivo y queda como faltante (delta compensatorio, append-only). Nunca más de lo despachado.
    /// </summary>
    public IReadOnlyList<StockTransferDiscrepancy> Receive(IReadOnlyList<LineReceipt> receipts, Guid userId, DateTimeOffset now)
    {
        EnsureStatus(TransferStatus.Dispatched, "recibir");
        var discrepancies = new List<StockTransferDiscrepancy>();
        foreach (var line in _lines)
        {
            var receipt = receipts.FirstOrDefault(r => r.LineId == line.Id)
                          ?? throw new DomainException("transfer.line_not_received", "Indique lo recibido de cada producto.");
            var received = Quantities.Round6(receipt.Movements.Sum(m => m.Quantity));
            Guard.That(received <= line.Quantity, "transfer.over_receipt",
                $"Se recibirían {received} y solo se despacharon {line.Quantity}: no se puede recibir de más.");
            foreach (var movement in receipt.Movements)
            {
                Guard.That(movement.BranchId == ToBranchId, "transfer.movement_branch", "La entrada debe registrarse en la sucursal de destino.");
                line.Link(movement.Id, TransferDirection.In, ToBranchId);
            }
            foreach (var batch in receipt.Movements.GroupBy(m => m.BatchId))
            {
                var shipped = line.Batches.Where(b => b.BatchId == batch.Key).Sum(b => b.Quantity);
                Guard.That(Quantities.Round6(batch.Sum(m => m.Quantity)) <= shipped, "transfer.batch_over_receipt",
                    "Se recibiría de un lote más de lo que viajó en el manifiesto.");
            }
            var shortage = Quantities.Round6(line.Quantity - received);
            if (shortage > 0)
            {
                Guard.That(!string.IsNullOrWhiteSpace(receipt.ShortageReason), "transfer.shortage_reason",
                    "Indique el motivo del faltante (se registra como merma en tránsito).");
                var discrepancy = new StockTransferDiscrepancy(TenantId, FromBranchId, ToBranchId, line.Id, shortage, receipt.ShortageReason!, userId, now);
                line.AddDiscrepancy(discrepancy);
                discrepancies.Add(discrepancy);
                _events.Add(new TransferDiscrepancyRecordedEvent(Id, Number, ToBranchId, line.VariantId, shortage, receipt.ShortageReason!.Trim(), now));
            }
        }
        Status = TransferStatus.Received;
        ReceivedAt = now.ToUniversalTime();
        var totalShortage = discrepancies.Sum(d => d.Quantity);
        Log(TransferStatus.Received, userId, now, totalShortage > 0 ? $"Recibida con faltantes ({totalShortage})" : "Recibida completa");
        _events.Add(new TransferReceivedEvent(Id, Number, FromBranchId, ToBranchId,
            _lines.Select(l => new TransferEventLine(l.VariantId, l.ReceivedQuantity, l.UnitCost ?? 0)).ToList(), totalShortage, now));
        return discrepancies;
    }

    /// <summary>Anulación (solo pendiente: una despachada ya movió stock y debe recibirse).</summary>
    public void Cancel(Guid userId, DateTimeOffset now, string reason)
    {
        EnsureStatus(TransferStatus.Pending, "anular");
        Guard.That(!string.IsNullOrWhiteSpace(reason), "transfer.cancel_reason", "Indique el motivo de la anulación.");
        Status = TransferStatus.Cancelled;
        Log(TransferStatus.Cancelled, userId, now, "Anulada: " + reason.Trim());
    }

    /// <summary>Cantidad en tránsito de una línea (solo mientras está despachada).</summary>
    public decimal InTransit(StockTransferLine line) => Status == TransferStatus.Dispatched ? line.Quantity : 0;

    private void Log(TransferStatus status, Guid userId, DateTimeOffset at, string detail) =>
        _history.Add(new StockTransferEvent(TenantId, FromBranchId, ToBranchId, Id, status, userId, at, detail));

    private void EnsureStatus(TransferStatus expected, string action) =>
        Guard.That(Status == expected, "transfer.invalid_state",
            $"No se puede {action} la transferencia {Number}: está {TransferStatuses.Label(Status)}.");
}

/// <summary>Salida o entrada ya registrada (la crea <see cref="StockLevel.Register"/>) que se vincula a una línea, con el
/// lote de su existencia.</summary>
public sealed record TransferMovement(Guid Id, Guid BranchId, Guid BatchId, decimal Quantity);

/// <summary>Despacho de una línea: una salida (puede haber varias por línea si sale de varias posiciones) y el costo.</summary>
public sealed record LineDispatch(Guid LineId, TransferMovement Movement, decimal UnitCost);

/// <summary>Recepción de una línea: entradas registradas (cero si todo se perdió) y motivo del faltante.</summary>
public sealed record LineReceipt(Guid LineId, IReadOnlyList<TransferMovement> Movements, string? ShortageReason);

public enum TransferDirection
{
    Out,
    In,
}

public static class TransferStatuses
{
    public static string Label(TransferStatus status) => status switch
    {
        TransferStatus.Pending => "pendiente",
        TransferStatus.Dispatched => "despachada (en tránsito)",
        TransferStatus.Received => "recibida",
        _ => "anulada",
    };
}
