using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>Impuesto (las tasas vigentes están en TaxRates).</summary>
public sealed class Tax : Entity
{
    private Tax()
    {
    }

    public Tax(Guid tenantId, string code, string name)
        : base(tenantId)
    {
        Code = Guard.Code(code, "El código del impuesto", 20);
        Name = Guard.Text(name, "El nombre", 80);
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;
}
