using MINV.Domain.Common;

namespace MINV.Domain.Accounting;

/// <summary>Moneda (ISO 4217).</summary>
public sealed class Currency : Entity
{
    private Currency()
    {
    }

    public Currency(Guid tenantId, string code, string name, string symbol, short decimalPlaces)
        : base(tenantId)
    {
        Code = Guard.Code(code, "El código ISO", 3);
        Name = Guard.Text(name, "El nombre", 60);
        Symbol = Guard.Text(symbol, "El símbolo", 8);
        DecimalPlaces = decimalPlaces;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Symbol { get; private set; } = string.Empty;

    public short DecimalPlaces { get; private set; }
}
