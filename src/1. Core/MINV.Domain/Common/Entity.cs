namespace MINV.Domain.Common;

/// <summary>Toda fila de negocio pertenece a un inquilino (tenant). La infraestructura filtra por él automáticamente.</summary>
public interface ITenantScoped
{
    Guid TenantId { get; }
}

/// <summary>Filas con control de concurrencia optimista: la infraestructura mapea <see cref="RowVersion"/> a
/// <c>xmin</c> de PostgreSQL y aborta la transacción si otra sesión modificó la fila entre la lectura y la escritura.</summary>
public interface IConcurrencyAware
{
    uint RowVersion { get; }
}

/// <summary>Tablas append-only (libro mayor): nunca se actualizan ni se borran; los errores se corrigen con registros
/// compensatorios. La infraestructura lo impide en EF Core y con triggers en PostgreSQL.</summary>
public interface IAppendOnly
{
}

/// <summary>Raíz de agregado: la única puerta de entrada para modificar su consistencia interna.</summary>
public interface IAggregateRoot
{
}

/// <summary>Base de toda entidad del dominio: aislamiento multi-tenant (regla A-04).</summary>
public abstract class BaseEntity : ITenantScoped
{
    protected BaseEntity()
    {
    }

    protected BaseEntity(Guid tenantId)
    {
        TenantId = Guard.NotEmpty(tenantId, nameof(tenantId));
    }

    public Guid TenantId { get; protected set; }

    /// <summary>Exige que otra entidad pertenezca al mismo inquilino (no se mezclan datos entre empresas).</summary>
    protected void EnsureSameTenant(ITenantScoped other, string what)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.TenantId != TenantId)
        {
            throw new DomainException("tenant.mismatch", $"{what} pertenece a otra empresa.");
        }
    }
}

/// <summary>Entidad con identidad propia (UUID v7 generado en el cliente: IDs sin coordinación, como en la V2.1).</summary>
public abstract class Entity : BaseEntity, IEquatable<Entity>
{
    protected Entity()
    {
    }

    protected Entity(Guid tenantId) : base(tenantId)
    {
        Id = UuidV7.NewGuid();
    }

    public Guid Id { get; protected set; }

    public bool Equals(Entity? other) =>
        other is not null && other.GetType() == GetType() && other.Id == Id && Id != Guid.Empty;

    public override bool Equals(object? obj) => obj is Entity e && Equals(e);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
