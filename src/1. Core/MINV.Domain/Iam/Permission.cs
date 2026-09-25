using MINV.Domain.Common;

namespace MINV.Domain.Iam;

/// <summary>Permiso granular (p. ej. inventory.movements.register).</summary>
public sealed class Permission : Entity
{
    private Permission()
    {
    }

    public Permission(Guid tenantId, string code, string description)
        : base(tenantId)
    {
        Code = Guard.Text(code, "El código del permiso", 80).ToLowerInvariant();
        Description = Guard.Text(description, "La descripción", 200);
    }

    public string Code { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;
}
