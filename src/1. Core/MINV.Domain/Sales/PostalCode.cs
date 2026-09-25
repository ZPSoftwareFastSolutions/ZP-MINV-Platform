using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Código postal de una ciudad («S/N» para zonas sin código).</summary>
public sealed class PostalCode : Entity
{
    private PostalCode()
    {
    }

    public PostalCode(Guid tenantId, Guid cityId, string code)
        : base(tenantId)
    {
        CityId = Guard.NotEmpty(cityId, nameof(cityId));
        Code = Guard.Text(code, "El código postal", 12);
    }

    public Guid CityId { get; private set; }

    public string Code { get; private set; } = string.Empty;
}
