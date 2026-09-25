using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>Motivo de ajuste (merma, daño…).</summary>
public sealed class AdjustmentReason : Entity
{
    private AdjustmentReason()
    {
    }

    public AdjustmentReason(Guid tenantId, string code, string name)
        : base(tenantId)
    {
        Code = Guard.Code(code, "El código", 20);
        Name = Guard.Text(name, "El nombre", 80);
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
