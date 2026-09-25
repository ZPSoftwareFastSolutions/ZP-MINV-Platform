using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Medio de pago (efectivo, tarjeta, QR…).</summary>
public sealed class PaymentMethod : Entity
{
    private PaymentMethod()
    {
    }

    public PaymentMethod(Guid tenantId, string code, string name, bool requiresReference, bool opensCashDrawer)
        : base(tenantId)
    {
        Code = Guard.Code(code, "El código", 20);
        Name = Guard.Text(name, "El nombre", 60);
        RequiresReference = requiresReference;
        OpensCashDrawer = opensCashDrawer;
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool RequiresReference { get; private set; }

    public bool OpensCashDrawer { get; private set; }

    public bool IsActive { get; private set; }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
