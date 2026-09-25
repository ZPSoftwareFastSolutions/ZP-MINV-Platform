using MINV.Domain.Common;

namespace MINV.Domain.Iam;

/// <summary>Persona que opera el sistema. Su identidad es el correo (único por empresa, siempre en minúsculas).</summary>
/// <remarks>Origen en la V2.1: 02_USUARIOS (tblUsuarios): Correo, Nombre, Activo.</remarks>
public sealed class User : Entity, IConcurrencyAware, IAggregateRoot
{
    private User()
    {
    }

    public User(Guid tenantId, string email, string displayName)
        : base(tenantId)
    {
        Email = Guard.Email(email, "El correo");
        DisplayName = Guard.Text(displayName, "El nombre", 120, minLength: 2);
        IsActive = true;
    }

    public string Email { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public void Rename(string displayName) => DisplayName = Guard.Text(displayName, "El nombre", 120, minLength: 2);

    public void ChangeEmail(string email) => Email = Guard.Email(email, "El correo");

    /// <summary>Retiro sin borrar (la fila se conserva para la auditoría), como <c>Activo = NO</c> en la V2.1.</summary>
    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
