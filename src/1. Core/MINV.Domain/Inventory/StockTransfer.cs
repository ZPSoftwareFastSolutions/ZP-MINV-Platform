using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>Traslado entre almacenes.</summary>
public sealed class StockTransfer : Entity, IConcurrencyAware, IAggregateRoot
{
    private readonly List<StockTransferLine> _lines = new();

    private StockTransfer()
    {
    }

    public StockTransfer(Guid tenantId, string number, Guid fromWarehouseId, Guid toWarehouseId, string? notes)
        : base(tenantId)
    {
        Number = Guard.Text(number, "El número", 30);
        FromWarehouseId = Guard.NotEmpty(fromWarehouseId, nameof(fromWarehouseId));
        ToWarehouseId = Guard.NotEmpty(toWarehouseId, nameof(toWarehouseId));
        Notes = Guard.OptionalText(notes, "Las observaciones", 250);
        Status = TransferStatus.Draft;
    }

    public string Number { get; private set; } = string.Empty;

    public Guid FromWarehouseId { get; private set; }

    public Guid ToWarehouseId { get; private set; }

    public TransferStatus Status { get; private set; }

    public DateTimeOffset? ShippedAt { get; private set; }

    public DateTimeOffset? ReceivedAt { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<StockTransferLine> Lines => _lines;

    /// <summary>Agrega una línea (solo en borrador).</summary>
    public StockTransferLine AddLine(Guid sourceStockLevelId, decimal quantity)
    {
        EnsureDraft();
        var line = new StockTransferLine(TenantId, Id, sourceStockLevelId, quantity);
        _lines.Add(line);
        return line;
    }

    private void EnsureDraft() => Guard.That(Status == TransferStatus.Draft, "document.not_draft",
        "Solo se pueden modificar documentos en borrador.");
}
