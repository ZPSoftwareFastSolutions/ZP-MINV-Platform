using MINV.Domain.Common;

namespace MINV.Domain.Warehousing;

/// <summary>Estantería de un pasillo.</summary>
public sealed class Rack : Entity
{
    private Rack()
    {
    }

    public Rack(Guid tenantId, Guid aisleId, string code)
        : base(tenantId)
    {
        AisleId = Guard.NotEmpty(aisleId, nameof(aisleId));
        Code = Guard.Code(code, "El código de la estantería", 20);
    }

    public Guid AisleId { get; private set; }

    public string Code { get; private set; } = string.Empty;
}
