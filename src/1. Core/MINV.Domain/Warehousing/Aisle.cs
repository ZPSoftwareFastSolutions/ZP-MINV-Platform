using MINV.Domain.Common;

namespace MINV.Domain.Warehousing;

/// <summary>Pasillo de una zona.</summary>
public sealed class Aisle : Entity
{
    private Aisle()
    {
    }

    public Aisle(Guid tenantId, Guid zoneId, string code)
        : base(tenantId)
    {
        ZoneId = Guard.NotEmpty(zoneId, nameof(zoneId));
        Code = Guard.Code(code, "El código del pasillo", 20);
    }

    public Guid ZoneId { get; private set; }

    public string Code { get; private set; } = string.Empty;
}
