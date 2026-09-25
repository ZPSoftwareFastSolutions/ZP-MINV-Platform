using MINV.Domain.Common;

namespace MINV.Domain.Warehousing;

/// <summary>Nivel (estante) de una estantería.</summary>
public sealed class Shelf : Entity, IBranchScoped
{
    private Shelf()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public Shelf(Guid tenantId, Guid branchId, Guid rackId, string code)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        RackId = Guard.NotEmpty(rackId, nameof(rackId));
        Code = Guard.Code(code, "El código del nivel", 20);
    }

    public Guid RackId { get; private set; }

    public string Code { get; private set; } = string.Empty;
}
