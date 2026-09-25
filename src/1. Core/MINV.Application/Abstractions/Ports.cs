using Microsoft.EntityFrameworkCore;

namespace MINV.Application.Abstractions;

/// <summary>Unidad de trabajo sobre la base de datos. La implementa MinvWriteDbContext (Infrastructure); las consultas ya
/// vienen filtradas por el tenant actual.</summary>
public interface IMinvDbContext
{
    DbSet<TEntity> Set<TEntity>() where TEntity : class;

    /// <summary>Guarda en una transacción. Traduce los conflictos de concurrencia optimista y de unicidad a
    /// <see cref="Common.ConcurrencyConflictException"/>.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Descarta lo que se está rastreando (se usa antes de reintentar tras un conflicto).</summary>
    void ClearTracking();

    /// <summary>V4 · Sucursales de la sesión (las consultas ya vienen filtradas por ellas; la ACTIVA decide el almacén
    /// de trabajo cuando un caso de uso no indica otro).</summary>
    BranchScope Branches { get; }

    /// <summary>V4 · Transacción explícita (transferencias y operaciones de varios pasos). En la base en memoria de la
    /// demostración no hay transacciones: devuelve una que no hace nada.</summary>
    Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>V4 · Publica un evento de dominio: se guarda en el outbox con el próximo SaveChanges (misma transacción).</summary>
    void Publish(Domain.Common.IDomainEvent domainEvent);
}

/// <summary>Empresa (tenant) de la sesión actual y, desde la V4, las sucursales que puede ver y operar.</summary>
public interface ITenantContext
{
    Guid TenantId { get; }

    bool IsSet { get; }

    void Set(Guid tenantId);

    /// <summary>V4 · Alcance por sucursal (lo fija el inicio de sesión o la API Key). Sin sesión: sin restricción,
    /// que es lo que usan el aprovisionamiento, la CLI y las migraciones.</summary>
    BranchScope Branches { get; }

    void SetBranches(BranchScope scope);
}

/// <summary>
/// V4 · Sucursales visibles para la sesión: todas (gerencia global: permiso <c>corporate.branches.all</c>) o las
/// asignadas al usuario (<c>warehouse.branch_users</c>), y la sucursal ACTIVA, donde se registran las operaciones que
/// no indican otra (caja, movimientos, compras).
/// </summary>
public sealed record BranchScope(bool AllBranches, IReadOnlyCollection<Guid> BranchIds, Guid? ActiveBranchId)
{
    /// <summary>Sin restricción (procesos de plataforma: aprovisionamiento, importación, datos de prueba).</summary>
    public static readonly BranchScope Unrestricted = new(true, [], null);

    public bool Allows(Guid branchId) => AllBranches || BranchIds.Contains(branchId);

    /// <summary>Cambia la sucursal activa (debe estar dentro del alcance).</summary>
    public BranchScope WithActive(Guid? branchId)
    {
        if (branchId is { } id && !Allows(id))
        {
            throw new Common.AccessDeniedException("La sucursal elegida no está entre las suyas.");
        }
        return this with { ActiveBranchId = branchId };
    }

    /// <summary>Valor de <c>minv.branch_ids</c> para las políticas RLS: <c>*</c> (todas) o la lista separada por comas.</summary>
    public string ToSessionSetting() => AllBranches ? "*" : string.Join(",", BranchIds.Order().Select(b => b.ToString()));
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

/// <summary>
/// V4 · Por dónde llegó la petición: <c>desktop</c> (escritorio con conexión directa), <c>cloud</c> (escritorio a través
/// del servidor en la nube) o <c>api</c> (API Gateway B2B, con la API Key usada). Va en la auditoría.
/// </summary>
public interface IRequestOrigin
{
    string Channel { get; }

    Guid? ApiKeyId { get; }

    void Set(string channel, Guid? apiKeyId);
}

public static class RequestChannels
{
    public const string Desktop = "desktop";
    public const string Cloud = "cloud";
    public const string Api = "api";
}

/// <summary>
/// V4 · Cifrado de secretos en reposo (AES-256-GCM con una clave maestra fuera de la base: variable de entorno o
/// bóveda). Lo usan los secretos de firma de los webhooks: si la base se filtra, no se pueden falsificar webhooks.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Id de la clave maestra vigente (para rotarla sin perder lo cifrado con la anterior).</summary>
    string CurrentKeyId { get; }

    string Protect(string secret);

    string Unprotect(string ciphertext, string keyId);
}

/// <summary>Ventas de una sucursal en un día (modelo de lectura).</summary>
public sealed record BranchSalesDay(Guid BranchId, DateOnly Day, int Tickets, decimal Revenue, decimal Tax);

/// <summary>Existencia y valor de una sucursal (modelo de lectura).</summary>
public sealed record BranchStockTotal(Guid BranchId, decimal OnHand, decimal Value);

/// <summary>
/// V4 · Modelo de LECTURA para la gerencia (OLAP): en PostgreSQL lee las vistas del esquema <c>reporting</c> (vistas
/// materializadas refrescadas cada pocos minutos, idealmente en una réplica) sin competir con las cajas; en la
/// demostración calcula lo mismo sobre la base en memoria. Ya viene filtrado por empresa y sucursales visibles.
/// </summary>
public interface IReportingReader
{
    Task<IReadOnlyList<BranchSalesDay>> DailySalesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BranchStockTotal>> StockAsync(CancellationToken cancellationToken = default);

    /// <summary>Momento del último refresco (null = datos en vivo).</summary>
    Task<DateTimeOffset?> RefreshedAtAsync(CancellationToken cancellationToken = default);
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
