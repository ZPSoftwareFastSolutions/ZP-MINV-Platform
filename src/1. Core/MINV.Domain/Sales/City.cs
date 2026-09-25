using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Ciudad.</summary>
public sealed class City : Entity
{
    private City()
    {
    }

    public City(Guid tenantId, Guid stateId, string name)
        : base(tenantId)
    {
        StateId = Guard.NotEmpty(stateId, nameof(stateId));
        Name = Guard.Text(name, "El nombre", 80);
    }

    public Guid StateId { get; private set; }

    public string Name { get; private set; } = string.Empty;
}
