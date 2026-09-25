using MINV.Domain.Common;

namespace MINV.Domain.Warehousing;

/// <summary>Estantería de un pasillo.</summary>
public sealed class Rack : Entity, IBranchScoped
{
    private Rack()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public Rack(Guid tenantId, Guid branchId, Guid aisleId, string code)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        AisleId = Guard.NotEmpty(aisleId, nameof(aisleId));
        Code = Guard.Code(code, "El código de la estantería", 20);
    }

    public Guid AisleId { get; private set; }

    public string Code { get; private set; } = string.Empty;
}
