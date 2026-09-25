namespace MINV.Domain.Common;

/// <summary>
/// Entidad de la plataforma (no pertenece a un inquilino): solo <c>Tenant</c> y <c>LicenseModule</c>. Son las dos
/// únicas excepciones documentadas a la regla «toda entidad tiene TenantId» (A-04): el tenant es la raíz del
/// aislamiento y los módulos comerciales son el catálogo global que se licencia a cada tenant.
/// </summary>
public abstract class PlatformEntity : IEquatable<PlatformEntity>
{
    protected PlatformEntity()
    {
    }

    protected PlatformEntity(Guid id)
    {
        Id = Guard.NotEmpty(id, nameof(id));
    }

    public Guid Id { get; protected set; }

    public bool Equals(PlatformEntity? other) =>
        other is not null && other.GetType() == GetType() && other.Id == Id && Id != Guid.Empty;

    public override bool Equals(object? obj) => obj is PlatformEntity e && Equals(e);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
