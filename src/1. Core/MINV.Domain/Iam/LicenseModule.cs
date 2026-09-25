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

    public static readonly Guid DataEngineId = new("01920000-0000-7000-8000-000000000001");
    public static readonly Guid DesktopClientId = new("01920000-0000-7000-8000-000000000002");
    public static readonly Guid PosHardwareId = new("01920000-0000-7000-8000-000000000003");
    public static readonly Guid RbacId = new("01920000-0000-7000-8000-000000000004");
    public static readonly Guid SlaSupportId = new("01920000-0000-7000-8000-000000000005");
}
