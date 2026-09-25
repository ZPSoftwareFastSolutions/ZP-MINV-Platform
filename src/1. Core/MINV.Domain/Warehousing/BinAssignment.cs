using MINV.Domain.Common;

namespace MINV.Domain.Warehousing;

/// <summary>Posición fija de picking de una variante.</summary>
/// <remarks>Origen en la V2.1: tblProductos: Ubicación.</remarks>
public sealed class BinAssignment : BaseEntity, IBranchScoped
{
    private BinAssignment()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public BinAssignment(Guid tenantId, Guid branchId, Guid binId, Guid variantId, bool isPrimaryPick)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        BinId = Guard.NotEmpty(binId, nameof(binId));
        VariantId = Guard.NotEmpty(variantId, nameof(variantId));
        IsPrimaryPick = isPrimaryPick;
    }

    public Guid BinId { get; private set; }

    public Guid VariantId { get; private set; }

    public bool IsPrimaryPick { get; private set; }
}
