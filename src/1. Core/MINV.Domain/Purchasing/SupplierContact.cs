using MINV.Domain.Common;

namespace MINV.Domain.Purchasing;

/// <summary>Contacto de un proveedor.</summary>
/// <remarks>Origen en la V2.1: tblProveedores: Contacto, Teléfono, Correo.</remarks>
public sealed class SupplierContact : Entity
{
    private SupplierContact()
    {
    }

    public SupplierContact(Guid tenantId, Guid supplierId, string fullName, string? phone, string? email, bool isPrimary)
        : base(tenantId)
    {
        SupplierId = Guard.NotEmpty(supplierId, nameof(supplierId));
        FullName = Guard.Text(fullName, "El nombre", 120);
        Phone = Guard.OptionalText(phone, "El teléfono", 40);
        Email = Guard.OptionalEmail(email, "El correo");
        IsPrimary = isPrimary;
    }

    public Guid SupplierId { get; private set; }

    public string FullName { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    public string? Email { get; private set; }

    public bool IsPrimary { get; private set; }
}
