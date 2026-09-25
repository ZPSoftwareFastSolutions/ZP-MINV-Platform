using MINV.Domain.Common;

namespace MINV.Domain.Warehousing;

/// <summary>Posición fija de picking de una variante.</summary>
/// <remarks>Origen en la V2.1: tblProductos: Ubicación.</remarks>
public sealed class BinAssignment : BaseEntity
{
    private BinAssignment()
    {
    }

    public BinAssignment(Guid tenantId, Guid binId, Guid variantId, bool isPrimaryPick)
        : base(tenantId)
    {
        BinId = Guard.NotEmpty(binId, nameof(binId));
        VariantId = Guard.NotEmpty(variantId, nameof(variantId));
        IsPrimaryPick = isPrimaryPick;
    }

    public Guid BinId { get; private set; }

    public Guid VariantId { get; private set; }

    public bool IsPrimaryPick { get; private set; }
}
