using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Infrastructure.Persistence;

namespace MINV.Infrastructure.Services;

public sealed class TenantContext : ITenantContext
{
    public Guid TenantId { get; private set; }

    public bool IsSet => TenantId != Guid.Empty;

    /// <summary>V4 · Alcance por sucursal. <c>minv.branch_ids</c> se fija al abrir cada conexión (TenantSessionInterceptor):
    /// los casos de uso cambian el alcance antes de su SaveChanges, que abre una conexión nueva.</summary>
    public BranchScope Branches { get; private set; } = BranchScope.Unrestricted;

    public void Set(Guid tenantId) => TenantId = Guard.NotEmpty(tenantId, nameof(tenantId));

    public void SetBranches(BranchScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Guard.That(scope.AllBranches || scope.BranchIds.Count > 0, "branch.scope_empty", "El usuario no tiene ninguna sucursal asignada.");
        Guard.That(scope.ActiveBranchId is null || scope.Allows(scope.ActiveBranchId.Value), "branch.active_outside",
            "La sucursal activa no está entre las permitidas.");
        Branches = scope;
    }
}

public sealed class CurrentUser : ICurrentUser
{
    private IReadOnlyCollection<string> _permissions = [];

    public Guid? UserId { get; private set; }

    public string? Email { get; private set; }

    public string? DisplayName { get; private set; }

    public IReadOnlyCollection<string> Permissions => _permissions;

    public void SignIn(Guid userId, string email, string displayName, IReadOnlyCollection<string> permissions)
    {
        UserId = userId;
        Email = email;
        DisplayName = displayName;
        _permissions = permissions.ToHashSet(StringComparer.Ordinal);
    }

    public void SignOut()
    {
        UserId = null;
        Email = null;
        DisplayName = null;
        _permissions = [];
    }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly TodayIn(string timeZoneId) => DateIn(UtcNow, timeZoneId);

    internal static DateOnly DateIn(DateTimeOffset instant, string timeZoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }
}

/// <summary>
/// Reloj del modo demostración: avanza como el del sistema, pero desplazado para que «hoy» sea el día en que la V2.1
/// calculó su última instantánea. Así la demostración muestra las mismas alertas, cobertura y pedido que el libro.
/// </summary>
public sealed class DemoClock : IClock
{
    private TimeSpan _offset;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow + _offset;

    public DateOnly TodayIn(string timeZoneId) => SystemClock.DateIn(UtcNow, timeZoneId);

    /// <summary>Desde ahora el reloj marca <paramref name="instant"/> y sigue corriendo.</summary>
    public void StartAt(DateTimeOffset instant) => _offset = instant - DateTimeOffset.UtcNow;
}

/// <summary>PBKDF2-HMAC-SHA256 con sal aleatoria de 16 bytes y 600.000 iteraciones (recomendación OWASP 2023).
/// Formato: <c>base64(sal):base64(hash)</c>; el algoritmo y las iteraciones van en columnas propias.</summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Algorithm => "PBKDF2-SHA256";

    public int Iterations => 600_000;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        Guard.That(password.Length >= 8, "password.length", "La contraseña debe tener al menos 8 caracteres.");
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
    }

    public bool Verify(string password, string hash, int iterations)
    {
        var parts = hash.Split(':');
        if (parts.Length != 2 || string.IsNullOrEmpty(password))
        {
            return false;
        }
        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

/// <summary>Módulos licenciados del tenant actual (TenantModules activos y vigentes).</summary>
public sealed class LicenseService(MinvWriteDbContext db, IClock clock) : ILicenseService
{
    public async Task<bool> IsModuleActiveAsync(string moduleCode, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        return await (from tm in db.TenantModules
                      join m in db.Modules on tm.ModuleId equals m.Id
                      where m.Code == moduleCode && tm.IsActive && (tm.ExpiresAt == null || tm.ExpiresAt > now)
                      select tm).AnyAsync(cancellationToken);
    }
}

/// <summary>
/// Auditoría en un contexto propio: se guarda aunque la transacción del caso de uso falle o se deshaga, y un error al
/// auditar nunca interrumpe la operación (regla C-14 heredada de la V2.1).
/// </summary>
public sealed class AuditTrail(IDbContextFactory<MinvWriteDbContext> factory, ITenantContext tenant, ICurrentUser user, IRequestOrigin origin,
    IClock clock) : IAuditTrail
{
    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        if (!tenant.IsSet)
        {
            return;
        }
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            db.AuditLogs.Add(new AuditLog(tenant.TenantId, user.UserId, clock.UtcNow, entry.Action, entry.Outcome,
                entry.EntityType, entry.EntityId, entry.Details, entry.CorrelationId, null)
                .WithOrigin(origin.Channel, origin.ApiKeyId, tenant.Branches.ActiveBranchId));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Trace.TraceWarning("M-INV · no se pudo auditar {0}: {1}", entry.Action, ex.Message);
        }
    }
}
