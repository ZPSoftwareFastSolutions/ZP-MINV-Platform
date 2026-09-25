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

/// <summary>
/// V4 · Fila que pertenece a UNA sucursal (inventario, documentos, cajas, asientos). La infraestructura la filtra según
/// el alcance del usuario (sucursales asignadas o todas para la gerencia global), valida que las filas nuevas caigan
/// dentro de ese alcance y PostgreSQL lo refuerza con Row Level Security restrictiva. <c>BranchId</c> es redundancia
/// controlada (como <c>TenantId</c>): las FK compuestas (tenant_id, branch_id, padre) impiden que un hijo tenga otra
/// sucursal que su padre.
/// </summary>
public interface IBranchScoped : ITenantScoped
{
    Guid BranchId { get; }
}

/// <summary>V4 · Documento entre dos sucursales (transferencias): lo ven ambas.</summary>
public interface IInterBranch : ITenantScoped
{
    Guid FromBranchId { get; }

    Guid ToBranchId { get; }
}

/// <summary>V4 · Hecho del negocio que interesa fuera del agregado (webhooks, integraciones). El dominio solo lo
/// describe; la infraestructura lo guarda en el outbox en la MISMA transacción que el cambio que lo produjo.</summary>
public interface IDomainEvent
{
    /// <summary>Nombre estable del evento para integraciones (p. ej. <c>transfer.dispatched</c>).</summary>
    string EventType { get; }

    DateTimeOffset OccurredAt { get; }

    /// <summary>Sucursal donde ocurrió (null si es de toda la empresa).</summary>
    Guid? BranchId { get; }
}

/// <summary>Agregado que acumula eventos de dominio hasta que se guardan.</summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
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
