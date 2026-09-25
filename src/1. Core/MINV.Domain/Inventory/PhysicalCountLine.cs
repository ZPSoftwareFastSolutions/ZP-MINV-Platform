using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>Conteo de una existencia dentro de una toma física.</summary>
/// <remarks>Origen en la V2.1: tblConteo (Conteo, Sistema, Diferencia).</remarks>
public sealed class PhysicalCountLine : Entity, IConcurrencyAware
{
    private PhysicalCountLine()
    {
    }

    internal PhysicalCountLine(Guid tenantId, Guid physicalCountId, Guid stockLevelId, decimal countedQuantity,
        Guid countedByUserId, DateTimeOffset countedAt)
        : base(tenantId)
    {
        PhysicalCountId = Guard.NotEmpty(physicalCountId, nameof(physicalCountId));
        StockLevelId = Guard.NotEmpty(stockLevelId, nameof(stockLevelId));
        Recount(countedQuantity, countedByUserId, countedAt);
    }

    public Guid PhysicalCountId { get; private set; }

    public Guid StockLevelId { get; private set; }

    public decimal CountedQuantity { get; private set; }

    public Guid CountedByUserId { get; private set; }

    public DateTimeOffset CountedAt { get; private set; }

    /// <summary>Existencia exacta al contabilizar (hecho histórico: el sistema cambia después).</summary>
    public decimal? SystemQuantityAtPosting { get; private set; }

    public Guid? StockMovementId { get; private set; }

    public uint RowVersion { get; private set; }

    public decimal? Difference => SystemQuantityAtPosting is { } system ? Quantities.Round6(CountedQuantity - system) : null;

    internal void Recount(decimal countedQuantity, Guid countedByUserId, DateTimeOffset countedAt)
    {
        CountedQuantity = Quantities.Round6(Guard.NonNegative(countedQuantity, "La cantidad contada"));
        CountedByUserId = Guard.NotEmpty(countedByUserId, nameof(countedByUserId));
        CountedAt = countedAt.ToUniversalTime();
    }

    internal void MarkPosted(decimal systemQuantity, Guid? stockMovementId)
    {
        SystemQuantityAtPosting = Quantities.Round6(systemQuantity);
        StockMovementId = stockMovementId;
    }
}
