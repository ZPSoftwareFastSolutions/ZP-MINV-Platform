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

    /// <summary>V4.1 · Tipo de documento de identidad del SIN (1 CI, 2 CEX, 3 PAS, 4 OD, 5 NIT) de <see cref="TaxId"/>
    /// (que es el NÚMERO de documento). Null = cliente sin datos de facturación.</summary>
    public int? DocumentType { get; private set; }

    /// <summary>V4.1 · Complemento del SEGIP para cédulas duplicadas (solo con CI, hasta 5 caracteres).</summary>
    public string? Complement { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    /// <summary>V4.1 · ¿Tiene lo necesario para facturar a su nombre? (tipo y número de documento).</summary>
    public bool HasFiscalIdentity => DocumentType is not null && !string.IsNullOrWhiteSpace(TaxId);

    /// <summary>V4.1 · Datos de facturación (nominatividad): tipo de documento, número y complemento, con las reglas del
    /// SIN (CI y NIT solo dígitos; complemento solo con CI).</summary>
    public void SetFiscalIdentity(int? documentType, string? documentNumber, string? complement)
    {
        if (documentType is null)
        {
            Guard.That(string.IsNullOrWhiteSpace(complement), "customer.complement", "El complemento requiere el tipo de documento CI.");
            DocumentType = null;
            Complement = null;
            TaxId = Guard.OptionalText(documentNumber, "El NIT/CI", 30);
            return;
        }
        Billing.FiscalRules.EnsureBuyerDocument(documentType.Value, documentNumber ?? string.Empty, complement);
        DocumentType = documentType;
        TaxId = documentNumber!.Trim();
        Complement = Guard.OptionalText(complement, "El complemento", 5)?.ToUpperInvariant();
    }

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
        // V4.1 · El número de documento sigue obedeciendo a su tipo (sin número no hay datos de facturación).
        if (TaxId is null)
        {
            DocumentType = null;
            Complement = null;
        }
        else if (DocumentType is { } type)
        {
            Billing.FiscalRules.EnsureBuyerDocument(type, TaxId, Complement);
        }
    }
}
