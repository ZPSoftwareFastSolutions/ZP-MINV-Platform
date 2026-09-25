using MINV.Domain.Common;

namespace MINV.Domain.Warehousing;

/// <summary>Tipo de posición (picking, reserva, recepción, despacho, góndola POS…).</summary>
public sealed class LocationType : Entity
{
    private LocationType()
    {
    }

    public LocationType(Guid tenantId, string code, string name, bool allowsPicking, bool allowsReceiving, bool allowsSales)
        : base(tenantId)
    {
        Code = Guard.Code(code, "El código", 20);
        Name = Guard.Text(name, "El nombre", 60);
        AllowsPicking = allowsPicking;
        AllowsReceiving = allowsReceiving;
        AllowsSales = allowsSales;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool AllowsPicking { get; private set; }

    public bool AllowsReceiving { get; private set; }

    public bool AllowsSales { get; private set; }
}
