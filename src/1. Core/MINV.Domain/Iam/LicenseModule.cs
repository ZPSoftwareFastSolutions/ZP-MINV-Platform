using MINV.Domain.Common;

namespace MINV.Domain.Iam;

/// <summary>
/// Módulo comercial licenciable (matriz de valor B2B de la V3): precio de implantación y mensualidad en bolivianos.
/// Los módulos activos de cada empresa están en <see cref="TenantModule"/> y habilitan funciones del sistema.
/// </summary>
public sealed class LicenseModule : PlatformEntity
{
    private LicenseModule()
    {
    }

    public LicenseModule(Guid id, string code, string name, string? description, decimal setupPriceBs, decimal monthlyFeeBs)
        : base(id)
    {
        Code = Guard.Code(code, "El código del módulo", 40);
        Name = Guard.Text(name, "El nombre", 100);
        Description = Guard.OptionalText(description, "La descripción", 400);
        SetupPriceBs = Guard.NonNegative(setupPriceBs, "El precio de implantación");
        MonthlyFeeBs = Guard.NonNegative(monthlyFeeBs, "La mensualidad");
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public decimal SetupPriceBs { get; private set; }

    public decimal MonthlyFeeBs { get; private set; }

    /// <summary>Catálogo comercial de la V3 (identificadores fijos: se siembran con la migración inicial).</summary>
    public static IReadOnlyList<LicenseModule> Catalog() =>
    [
        new(LicenseModuleCodes.DataEngineId, LicenseModuleCodes.DataEngine, "Migración de motor de datos",
            "Transición a PostgreSQL local: elimina la corrupción de archivos.", 8500m, 0m),
        new(LicenseModuleCodes.DesktopClientId, LicenseModuleCodes.DesktopClient, "Cliente de escritorio nativo",
            "Interfaz compilada: 100.000+ productos sin latencia.", 6000m, 0m),
        new(LicenseModuleCodes.PosHardwareId, LicenseModuleCodes.PosHardware, "Módulo POS y hardware",
            "Cajas, escáneres e impresoras ESC/POS (COM/USB/red).", 4500m, 0m),
        new(LicenseModuleCodes.RbacId, LicenseModuleCodes.Rbac, "Control de acceso (RBAC)",
            "Login cifrado y trazabilidad inmutable por operador.", 3000m, 0m),
        new(LicenseModuleCodes.SlaSupportId, LicenseModuleCodes.SlaSupport, "SLA de soporte",
            "Respaldo, telemetría y actualizaciones de seguridad.", 0m, 800m),
        // V4 · Enterprise Cloud
        new(LicenseModuleCodes.CloudHaId, LicenseModuleCodes.CloudHa, "Infraestructura Cloud HA",
            "PostgreSQL gestionado en la nube (AWS/DigitalOcean) con respaldos PITR, réplica y SLA 99,9 %.", 12000m, 1500m),
        new(LicenseModuleCodes.MultiBranchId, LicenseModuleCodes.MultiBranch, "Topología multi-sucursal",
            "Inventario aislado por sucursal (BranchId) y transferencias con mercancía en tránsito.", 8000m, 500m),
        new(LicenseModuleCodes.ApiIntegrationsId, LicenseModuleCodes.ApiIntegrations, "Integraciones API (B2B)",
            "API Gateway con API Keys y webhooks para e-commerce (Shopify) y ERP contable.", 6000m, 400m),
        new(LicenseModuleCodes.GlobalAuditId, LicenseModuleCodes.GlobalAudit, "Auditoría global (réplicas de lectura)",
            "Modelo de lectura desnormalizado para gerencia sin cargar las cajas POS.", 5000m, 300m),
    ];
}

/// <summary>Códigos e identificadores estables de los módulos comerciales.</summary>
public static class LicenseModuleCodes
{
    public const string DataEngine = "DATA_ENGINE";
    public const string DesktopClient = "DESKTOP_CLIENT";
    public const string PosHardware = "POS_HARDWARE";
    public const string Rbac = "RBAC";
    public const string SlaSupport = "SLA_SUPPORT";
    public const string CloudHa = "CLOUD_HA";
    public const string MultiBranch = "MULTI_BRANCH";
    public const string ApiIntegrations = "API_INTEGRATIONS";
    public const string GlobalAudit = "GLOBAL_AUDIT";

    public static readonly Guid DataEngineId = new("01920000-0000-7000-8000-000000000001");
    public static readonly Guid DesktopClientId = new("01920000-0000-7000-8000-000000000002");
    public static readonly Guid PosHardwareId = new("01920000-0000-7000-8000-000000000003");
    public static readonly Guid RbacId = new("01920000-0000-7000-8000-000000000004");
    public static readonly Guid SlaSupportId = new("01920000-0000-7000-8000-000000000005");
    public static readonly Guid CloudHaId = new("01920000-0000-7000-8000-000000000006");
    public static readonly Guid MultiBranchId = new("01920000-0000-7000-8000-000000000007");
    public static readonly Guid ApiIntegrationsId = new("01920000-0000-7000-8000-000000000008");
    public static readonly Guid GlobalAuditId = new("01920000-0000-7000-8000-000000000009");
}
