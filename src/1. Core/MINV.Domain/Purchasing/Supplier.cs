using MINV.Domain.Common;

namespace MINV.Domain.Purchasing;

/// <summary>Proveedor.</summary>
/// <remarks>Origen en la V2.1: 04_PROVEEDORES (tblProveedores): Proveedor, NIT, DiasEntrega.</remarks>
public sealed class Supplier : Entity
{
    private Supplier()
    {
    }

    public Supplier(Guid tenantId, string code, string legalName, string? taxId, int leadTimeDays)
        : base(tenantId)
    {
        Code = Guard.Code(code, "El código del proveedor", 20);
        LegalName = Guard.Text(legalName, "La razón social", 150);
        TaxId = Guard.OptionalText(taxId, "El NIT", 30);
        LeadTimeDays = Guard.NonNegative(leadTimeDays, "Los días de entrega");
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;

    public string LegalName { get; private set; } = string.Empty;

    public string? TaxId { get; private set; }

    public int LeadTimeDays { get; private set; }

    public bool IsActive { get; private set; }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
