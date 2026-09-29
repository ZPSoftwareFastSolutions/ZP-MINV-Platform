namespace MINV.Application.Common;

/// <summary>El caso de uso exige que el usuario tenga este permiso (RBAC).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}

/// <summary>El caso de uso solo está disponible si la empresa tiene licenciado el módulo comercial.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequiresModuleAttribute(string moduleCode) : Attribute
{
    public string ModuleCode { get; } = moduleCode;
}

/// <summary>Comando que deja rastro en la auditoría inmutable (AuditLogs), también cuando es rechazado.</summary>
public interface IAuditableRequest
{
    /// <summary>Datos del comando para la auditoría (nunca contraseñas).</summary>
    object AuditDetails { get; }
}

/// <summary>
/// V7 · Respuesta de un comando que lleva datos personales (contacto o datos para la factura de quien reserva, reglas S-06
/// y P-05): la auditoría guarda <see cref="AuditResult"/> en vez de la respuesta completa. Se implementa de forma EXPLÍCITA
/// para que no viaje en el JSON de la respuesta.
/// </summary>
public interface IAuditableResponse
{
    /// <summary>La respuesta para la auditoría, con los datos personales enmascarados.</summary>
    object AuditResult { get; }
}
