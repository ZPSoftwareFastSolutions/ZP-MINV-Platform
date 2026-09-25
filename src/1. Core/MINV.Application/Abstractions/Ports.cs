using Microsoft.EntityFrameworkCore;

namespace MINV.Application.Abstractions;

/// <summary>Unidad de trabajo sobre la base de datos. La implementa MINVDbContext (Infrastructure); las consultas ya
/// vienen filtradas por el tenant actual.</summary>
public interface IMinvDbContext
{
    DbSet<TEntity> Set<TEntity>() where TEntity : class;

    /// <summary>Guarda en una transacción. Traduce los conflictos de concurrencia optimista y de unicidad a
    /// <see cref="Common.ConcurrencyConflictException"/>.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Descarta lo que se está rastreando (se usa antes de reintentar tras un conflicto).</summary>
    void ClearTracking();
}

/// <summary>Empresa (tenant) de la sesión actual.</summary>
public interface ITenantContext
{
    Guid TenantId { get; }

    bool IsSet { get; }

    void Set(Guid tenantId);
}

/// <summary>Usuario autenticado de la sesión actual.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    string? Email { get; }

    string? DisplayName { get; }

    IReadOnlyCollection<string> Permissions { get; }

    bool IsAuthenticated => UserId is not null;

    bool HasPermission(string permission) => Permissions.Contains(permission);

    void SignIn(Guid userId, string email, string displayName, IReadOnlyCollection<string> permissions);

    void SignOut();
}

/// <summary>Reloj del sistema (inyectable para pruebas).</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>Fecha de hoy en la zona horaria de la empresa (IANA, p. ej. America/La_Paz).</summary>
    DateOnly TodayIn(string timeZoneId);
}

/// <summary>Hash de contraseñas (PBKDF2-SHA256 en Infrastructure).</summary>
public interface IPasswordHasher
{
    string Algorithm { get; }

    int Iterations { get; }

    string Hash(string password);

    bool Verify(string password, string hash, int iterations);
}

/// <summary>Módulos comerciales licenciados de la empresa actual.</summary>
public interface ILicenseService
{
    Task<bool> IsModuleActiveAsync(string moduleCode, CancellationToken cancellationToken = default);
}

/// <summary>Registro de auditoría independiente de la transacción del caso de uso: un intento rechazado también se
/// audita aunque su transacción se deshaga (en la V2.1: 14_ACTIVIDAD, regla C-14).</summary>
public interface IAuditTrail
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

public sealed record AuditEntry(string Action, Domain.Iam.AuditOutcome Outcome, string Details, Guid CorrelationId,
    string? EntityType = null, Guid? EntityId = null);
