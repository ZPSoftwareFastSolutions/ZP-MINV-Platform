using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>Documento de ajuste de inventario.</summary>
public sealed class StockAdjustment : Entity, IConcurrencyAware, IAggregateRoot, IBranchScoped
{
    private readonly List<StockAdjustmentLine> _lines = new();

    private StockAdjustment()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public StockAdjustment(Guid tenantId, Guid branchId, string number, Guid warehouseId, Guid adjustmentReasonId, string? notes)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        Number = Guard.Text(number, "El número", 30);
        WarehouseId = Guard.NotEmpty(warehouseId, nameof(warehouseId));
        AdjustmentReasonId = Guard.NotEmpty(adjustmentReasonId, nameof(adjustmentReasonId));
        Notes = Guard.OptionalText(notes, "Las observaciones", 250);
        Status = AdjustmentStatus.Draft;
    }

    public string Number { get; private set; } = string.Empty;

    public Guid WarehouseId { get; private set; }

    public Guid AdjustmentReasonId { get; private set; }

    public AdjustmentStatus Status { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset? PostedAt { get; private set; }

    public Guid? PostedByUserId { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<StockAdjustmentLine> Lines => _lines;

    /// <summary>Agrega una línea (solo en borrador).</summary>
    public StockAdjustmentLine AddLine(Guid stockLevelId, Guid movementTypeId, decimal quantity)
    {
        EnsureDraft();
        var line = new StockAdjustmentLine(TenantId, BranchId, Id, stockLevelId, movementTypeId, quantity);
        _lines.Add(line);
        return line;
    }

    private void EnsureDraft() => Guard.That(Status == AdjustmentStatus.Draft, "document.not_draft",
        "Solo se pueden modificar documentos en borrador.");
}
