using MINV.Domain.Common;

namespace MINV.Domain.Accounting;

/// <summary>Regla de aplicación de un impuesto por categoría de cliente y sucursal.</summary>
public sealed class TaxRule : Entity
{
    private TaxRule()
    {
    }

    public TaxRule(Guid tenantId, Guid taxId, Guid? customerCategoryId, Guid? branchId, bool isExempt, int priority)
        : base(tenantId)
    {
        TaxId = Guard.NotEmpty(taxId, nameof(taxId));
        CustomerCategoryId = Guard.NotEmptyIfPresent(customerCategoryId, nameof(customerCategoryId));
        BranchId = Guard.NotEmptyIfPresent(branchId, nameof(branchId));
        IsExempt = isExempt;
        Priority = Guard.NonNegative(priority, "La prioridad");
    }

    public Guid TaxId { get; private set; }

    public Guid? CustomerCategoryId { get; private set; }

    public Guid? BranchId { get; private set; }

    public bool IsExempt { get; private set; }

    public int Priority { get; private set; }
}
