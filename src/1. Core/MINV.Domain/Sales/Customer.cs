using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Cliente.</summary>
public sealed class Customer : Entity, IConcurrencyAware, IAggregateRoot
{
    private Customer()
    {
    }

    public Customer(Guid tenantId, string code, string name, string? taxId, string? email, string? phone, Guid customerCategoryId)
        : base(tenantId)
    {
        Code = Guard.Code(code, "El código del cliente", 20);
        Name = Guard.Text(name, "El nombre", 150);
        TaxId = Guard.OptionalText(taxId, "El NIT/CI", 30);
        Email = Guard.OptionalEmail(email, "El correo");
        Phone = Guard.OptionalText(phone, "El teléfono", 40);
        CustomerCategoryId = Guard.NotEmpty(customerCategoryId, nameof(customerCategoryId));
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? TaxId { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public Guid CustomerCategoryId { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>Actualiza los datos del cliente (el código no cambia: identifica al cliente en los documentos).</summary>
    public void Update(string name, string? taxId, string? email, string? phone, Guid customerCategoryId)
    {
        Name = Guard.Text(name, "El nombre", 150);
        TaxId = Guard.OptionalText(taxId, "El NIT/CI", 30);
        Email = Guard.OptionalEmail(email, "El correo");
        Phone = Guard.OptionalText(phone, "El teléfono", 40);
        CustomerCategoryId = Guard.NotEmpty(customerCategoryId, nameof(customerCategoryId));
    }
}
