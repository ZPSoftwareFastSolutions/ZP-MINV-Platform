using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>Número de serie (trazabilidad 1 a 1).</summary>
public sealed class SerialNumber : Entity, IConcurrencyAware
{
    private SerialNumber()
    {
    }

    public SerialNumber(Guid tenantId, Guid batchId, string serial, Guid? stockLevelId)
        : base(tenantId)
    {
        BatchId = Guard.NotEmpty(batchId, nameof(batchId));
        Serial = Guard.Text(serial, "La serie", 80);
        StockLevelId = Guard.NotEmptyIfPresent(stockLevelId, nameof(stockLevelId));
        Status = SerialNumberStatus.InStock;
    }

    public Guid BatchId { get; private set; }

    public string Serial { get; private set; } = string.Empty;

    public Guid? StockLevelId { get; private set; }

    public SerialNumberStatus Status { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    /// <summary>Cambia el estado y la ubicación de la serie (una serie vendida o dada de baja no tiene ubicación).</summary>
    public void MoveTo(SerialNumberStatus status, Guid? stockLevelId)
    {
        Status = Guard.Defined(status, "El estado");
        StockLevelId = status is SerialNumberStatus.Sold or SerialNumberStatus.Scrapped
            ? null
            : Guard.NotEmptyIfPresent(stockLevelId, nameof(stockLevelId));
    }
}
