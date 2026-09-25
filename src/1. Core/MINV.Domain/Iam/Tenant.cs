using MINV.Domain.Common;

namespace MINV.Domain.Iam;

/// <summary>Inquilino (empresa cliente): raíz del aislamiento multi-tenant. Única tabla de negocio sin TenantId.</summary>
/// <remarks>Origen en la V2.1: 01_CONFIG (cfgEmpresa, cfgNIT).</remarks>
public sealed class Tenant : PlatformEntity, IAggregateRoot
{
    private Tenant()
    {
    }

    public Tenant(string code, string legalName, string? taxId)
        : base(UuidV7.NewGuid())
    {
        Code = Guard.Code(code, "El código de la empresa", 20);
        LegalName = Guard.Text(legalName, "La razón social", 150);
        TaxId = Guard.OptionalText(taxId, "El NIT", 30);
        IsActive = true;
    }

    /// <summary>Código corto con el que se inicia sesión (p. ej. DEMO).</summary>
    public string Code { get; private set; } = string.Empty;

    public string LegalName { get; private set; } = string.Empty;

    public string? TaxId { get; private set; }

    public bool IsActive { get; private set; }

    public void Rename(string legalName, string? taxId)
    {
        LegalName = Guard.Text(legalName, "La razón social", 150);
        TaxId = Guard.OptionalText(taxId, "El NIT", 30);
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
