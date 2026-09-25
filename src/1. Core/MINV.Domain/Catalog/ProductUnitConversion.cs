using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>Empaque propio de un producto (p. ej. CAJA = 100 UND).</summary>
public sealed class ProductUnitConversion : Entity
{
    private ProductUnitConversion()
    {
    }

    public ProductUnitConversion(Guid tenantId, Guid productId, Guid unitId, decimal factorToBase)
        : base(tenantId)
    {
        ProductId = Guard.NotEmpty(productId, nameof(productId));
        UnitId = Guard.NotEmpty(unitId, nameof(unitId));
        FactorToBase = Guard.Positive(factorToBase, "El factor");
    }

    public Guid ProductId { get; private set; }

    public Guid UnitId { get; private set; }

    public decimal FactorToBase { get; private set; }

    /// <summary>Unidades base que contiene el empaque (p. ej. 100 para CAJA x100).</summary>
    public void ChangeFactor(decimal factorToBase) => FactorToBase = Guard.Positive(factorToBase, "El factor");
}
