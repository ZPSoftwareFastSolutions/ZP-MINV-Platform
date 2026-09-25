using MINV.Domain.Common;

namespace MINV.Domain.Iam;

/// <summary>Rol de acceso (RBAC).</summary>
/// <remarks>Origen en la V2.1: 02_USUARIOS: Rol (ADMIN, BODEGA, VENTAS, CONSULTA).</remarks>
public sealed class Role : Entity
{
    private Role()
    {
    }

    public Role(Guid tenantId, string code, string name, bool isSystem)
        : base(tenantId)
    {
        Code = Guard.Code(code, "El código del rol", 30);
        Name = Guard.Text(name, "El nombre", 80);
        IsSystem = isSystem;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsSystem { get; private set; }
}
