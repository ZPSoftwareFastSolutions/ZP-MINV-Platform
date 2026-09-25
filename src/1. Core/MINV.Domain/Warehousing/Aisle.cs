using MINV.Domain.Common;

namespace MINV.Domain.Warehousing;

/// <summary>Pasillo de una zona.</summary>
public sealed class Aisle : Entity, IBranchScoped
{
    private Aisle()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public Aisle(Guid tenantId, Guid branchId, Guid zoneId, string code)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        ZoneId = Guard.NotEmpty(zoneId, nameof(zoneId));
        Code = Guard.Code(code, "El código del pasillo", 20);
    }

    public Guid ZoneId { get; private set; }

    public string Code { get; private set; } = string.Empty;
}
