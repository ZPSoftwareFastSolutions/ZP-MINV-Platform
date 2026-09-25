using System.Globalization;
using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>
/// Toma física colaborativa: cada contador registra líneas (una por existencia) y, al contabilizar, cada existencia se
/// lleva a lo contado contra el stock EXACTO de ese momento, todo o nada.
/// </summary>
/// <remarks>Origen en la V2.1: 13_CONTEO + GenerarAjustesConteo.ts (documento CF-AAAAMMDD, observación «Toma física
/// del dd/mm/aaaa: sistema S, contado C (diferencia ±D)»).</remarks>
public sealed class PhysicalCount : Entity, IConcurrencyAware, IAggregateRoot, IBranchScoped
{
    private readonly List<PhysicalCountLine> _lines = new();

    private PhysicalCount()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    private PhysicalCount(Guid tenantId, Guid branchId, string number, Guid warehouseId, DateOnly countDate, string? notes)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        Number = Guard.Text(number, "El número", 30);
        WarehouseId = Guard.NotEmpty(warehouseId, nameof(warehouseId));
        CountDate = countDate;
        Notes = Guard.OptionalText(notes, "Las observaciones", 250);
        Status = PhysicalCountStatus.Open;
    }

    public string Number { get; private set; } = string.Empty;

    public Guid WarehouseId { get; private set; }

    public DateOnly CountDate { get; private set; }

    public PhysicalCountStatus Status { get; private set; }

    public DateTimeOffset? PostedAt { get; private set; }

    public Guid? PostedByUserId { get; private set; }

    public string? Notes { get; private set; }

    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<PhysicalCountLine> Lines => _lines;

    /// <summary>Abre una toma física. Solo puede haber una abierta por almacén (índice único parcial).</summary>
    public static PhysicalCount Open(Guid tenantId, Guid branchId, Guid warehouseId, DateOnly countDate, int sequenceOfDay = 1, string? notes = null) =>
        new(tenantId, branchId, NumberFor(countDate, sequenceOfDay), warehouseId, countDate, notes);

    /// <summary>Número del documento: <c>CF-AAAAMMDD</c> (y <c>-n</c> si hay más de una toma el mismo día).</summary>
    public static string NumberFor(DateOnly date, int sequenceOfDay)
    {
        Guard.That(sequenceOfDay >= 1, "count.sequence", "La secuencia del día empieza en 1.");
        var number = "CF-" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return sequenceOfDay > 1 ? $"{number}-{sequenceOfDay}" : number;
    }

    /// <summary>Registra (o corrige) el conteo de una existencia. Varias personas pueden contar a la vez: cada línea
    /// tiene su propio token de concurrencia.</summary>
    public PhysicalCountLine RecordCount(Guid stockLevelId, decimal countedQuantity, Guid countedByUserId, DateTimeOffset countedAt)
    {
        EnsureOpen();
        var line = _lines.FirstOrDefault(l => l.StockLevelId == stockLevelId);
        if (line is null)
        {
            line = new PhysicalCountLine(TenantId, BranchId, Id, stockLevelId, countedQuantity, countedByUserId, countedAt);
            _lines.Add(line);
        }
        else
        {
            line.Recount(countedQuantity, countedByUserId, countedAt);
        }
        return line;
    }

    public void RemoveCount(Guid stockLevelId)
    {
        EnsureOpen();
        _lines.RemoveAll(l => l.StockLevelId == stockLevelId);
    }

    /// <summary>
    /// Contabiliza la toma: valida TODAS las líneas (existencia conocida, decimales, reservas) y solo entonces ajusta.
    /// Devuelve los movimientos generados (que el caso de uso guarda en una sola transacción).
    /// </summary>
    public IReadOnlyList<StockMovement> Post(IReadOnlyDictionary<Guid, StockLevel> levels,
        IReadOnlyDictionary<Guid, CountItemInfo> items, CountMovementTypes types, Guid userId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(types);
        EnsureOpen();
        Guard.That(_lines.Count > 0, "count.empty", "No hay conteos: registre las cantidades contadas.");

        var errors = new List<string>();
        foreach (var line in _lines)
        {
            if (!levels.TryGetValue(line.StockLevelId, out var level) || !items.TryGetValue(line.StockLevelId, out var info))
            {
                errors.Add($"existencia {line.StockLevelId} desconocida");
                continue;
            }
            if (!info.Unit.AllowsDecimals && !Quantities.IsWhole(line.CountedQuantity))
            {
                errors.Add($"{info.Sku} ({info.Unit.UnitCode} no admite decimales)");
            }
            else if (info.HasPriorMovements && line.CountedQuantity < level.QuantityReserved)
            {
                errors.Add($"{info.Sku} (hay {Quantities.Format(level.QuantityReserved)} reservado: libere las reservas)");
            }
        }
        if (errors.Count > 0)
        {
            throw new DomainException("count.invalid", $"{errors.Count} conteo(s) no válido(s): " +
                string.Join("; ", errors.Take(8)) + (errors.Count > 8 ? "; …" : "") + ". No se registró nada.");
        }

        var movements = new List<StockMovement>();
        var origin = "Toma física del " + CountDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        foreach (var line in _lines)
        {
            var level = levels[line.StockLevelId];
            var info = items[line.StockLevelId];
            var system = level.QuantityOnHand;
            var difference = Quantities.Round6(line.CountedQuantity - system);
            var notes = info.HasPriorMovements
                ? $"{origin}: sistema {Quantities.Format(system)}, contado {Quantities.Format(line.CountedQuantity)} " +
                  $"{info.Unit.UnitCode} (diferencia {(difference > 0 ? "+" : "")}{Quantities.Format(difference)})"
                : $"{origin}: saldo inicial contado {Quantities.Format(line.CountedQuantity)} {info.Unit.UnitCode}";
            var context = new MovementContext(userId, CountDate, now, Number, notes, CorrelationId: Id);
            var movement = level.AdjustToCount(line.CountedQuantity, types, info.HasPriorMovements, info.Unit, context);
            line.MarkPosted(system, movement?.Id);
            if (movement is not null)
            {
                movements.Add(movement);
            }
        }
        Status = PhysicalCountStatus.Posted;
        PostedAt = now.ToUniversalTime();
        PostedByUserId = Guard.NotEmpty(userId, nameof(userId));
        return movements;
    }

    public void Cancel()
    {
        EnsureOpen();
        Status = PhysicalCountStatus.Cancelled;
    }

    private void EnsureOpen() =>
        Guard.That(Status == PhysicalCountStatus.Open, "count.closed", $"La toma física {Number} ya no está abierta.");
}

/// <summary>Datos de cada existencia contada que la toma necesita para contabilizar.</summary>
public sealed record CountItemInfo(string Sku, UnitRule Unit, bool HasPriorMovements);
