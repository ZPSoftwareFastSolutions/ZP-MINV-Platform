using MINV.Domain.Common;

namespace MINV.Domain.Warehousing;

/// <summary>Nivel (estante) de una estantería.</summary>
public sealed class Shelf : Entity
{
    private Shelf()
    {
    }

    public Shelf(Guid tenantId, Guid rackId, string code)
        : base(tenantId)
    {
        RackId = Guard.NotEmpty(rackId, nameof(rackId));
        Code = Guard.Code(code, "El código del nivel", 20);
    }

    public Guid RackId { get; private set; }

    public string Code { get; private set; } = string.Empty;
}
