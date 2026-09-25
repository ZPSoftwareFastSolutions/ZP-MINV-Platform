using MINV.Domain.Common;

namespace MINV.Domain.Accounting;

/// <summary>Tasa de un impuesto con vigencia.</summary>
public sealed class TaxRate : Entity
{
    private TaxRate()
    {
    }

    public TaxRate(Guid tenantId, Guid taxId, decimal rate, DateOnly validFrom, DateOnly? validTo)
        : base(tenantId)
    {
        TaxId = Guard.NotEmpty(taxId, nameof(taxId));
        Rate = Guard.Percent(rate, "La tasa");
        ValidFrom = validFrom;
        ValidTo = validTo;
    }

    public Guid TaxId { get; private set; }

    public decimal Rate { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    public DateOnly? ValidTo { get; private set; }
}
