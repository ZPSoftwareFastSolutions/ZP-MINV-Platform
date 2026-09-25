using MINV.Domain.Common;

namespace MINV.Domain.Accounting;

/// <summary>Tipo de cambio diario (append-only).</summary>
public sealed class ExchangeRate : Entity, IAppendOnly
{
    private ExchangeRate()
    {
    }

    public ExchangeRate(Guid tenantId, Guid fromCurrencyId, Guid toCurrencyId, DateOnly effectiveDate, decimal rate)
        : base(tenantId)
    {
        FromCurrencyId = Guard.NotEmpty(fromCurrencyId, nameof(fromCurrencyId));
        ToCurrencyId = Guard.NotEmpty(toCurrencyId, nameof(toCurrencyId));
        EffectiveDate = effectiveDate;
        Rate = Guard.Positive(rate, "La tasa");
    }

    public Guid FromCurrencyId { get; private set; }

    public Guid ToCurrencyId { get; private set; }

    public DateOnly EffectiveDate { get; private set; }

    public decimal Rate { get; private set; }
}
