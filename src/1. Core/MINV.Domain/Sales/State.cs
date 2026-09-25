using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Departamento o estado.</summary>
public sealed class State : Entity
{
    private State()
    {
    }

    public State(Guid tenantId, Guid countryId, string code, string name)
        : base(tenantId)
    {
        CountryId = Guard.NotEmpty(countryId, nameof(countryId));
        Code = Guard.Code(code, "El código", 10);
        Name = Guard.Text(name, "El nombre", 80);
    }

    public Guid CountryId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;
}
