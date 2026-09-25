using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>Conversión genérica entre unidades.</summary>
public sealed class UnitConversion : Entity
{
    private UnitConversion()
    {
    }

    public UnitConversion(Guid tenantId, Guid fromUnitId, Guid toUnitId, decimal factor)
        : base(tenantId)
    {
        FromUnitId = Guard.NotEmpty(fromUnitId, nameof(fromUnitId));
        ToUnitId = Guard.NotEmpty(toUnitId, nameof(toUnitId));
        Factor = Guard.Positive(factor, "El factor");
    }

    public Guid FromUnitId { get; private set; }

    public Guid ToUnitId { get; private set; }

    public decimal Factor { get; private set; }
}
