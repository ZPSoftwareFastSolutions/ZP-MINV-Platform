using MINV.Domain.Common;

namespace MINV.Domain.Iam;

/// <summary>Credencial de inicio de sesión (hash con sal, nunca la contraseña) y bloqueo por intentos fallidos.</summary>
public sealed class UserCredential : BaseEntity, IConcurrencyAware
{
    public const int MinIterations = 100_000;
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private UserCredential()
    {
    }

    public UserCredential(Guid tenantId, Guid userId, string passwordHash, string algorithm, int iterations,
        DateTimeOffset changedAt, bool mustChangePassword)
        : base(tenantId)
    {
        UserId = Guard.NotEmpty(userId, nameof(userId));
        SetHash(passwordHash, algorithm, iterations, changedAt);
        MustChangePassword = mustChangePassword;
    }

    public Guid UserId { get; private set; }

    public string PasswordHash { get; private set; } = string.Empty;

    public string Algorithm { get; private set; } = string.Empty;

    public int Iterations { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public bool MustChangePassword { get; private set; }

    public int FailedAttempts { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public uint RowVersion { get; private set; }

    public bool IsLocked(DateTimeOffset now) => LockedUntil is { } until && until > now;

    /// <summary>Tras <see cref="MaxFailedAttempts"/> intentos fallidos la cuenta se bloquea <see cref="LockoutDuration"/>.</summary>
    public void RegisterFailure(DateTimeOffset now)
    {
        FailedAttempts++;
        if (FailedAttempts >= MaxFailedAttempts)
        {
            LockedUntil = now.ToUniversalTime() + LockoutDuration;
            FailedAttempts = 0;
        }
    }

    public void RegisterSuccess()
    {
        FailedAttempts = 0;
        LockedUntil = null;
    }

    public void ChangePassword(string passwordHash, string algorithm, int iterations, DateTimeOffset now)
    {
        SetHash(passwordHash, algorithm, iterations, now);
        MustChangePassword = false;
        RegisterSuccess();
    }

    /// <summary>El administrador asigna una contraseña (temporal si <paramref name="mustChange"/>) y desbloquea la cuenta.</summary>
    public void ResetPassword(string passwordHash, string algorithm, int iterations, DateTimeOffset now, bool mustChange)
    {
        SetHash(passwordHash, algorithm, iterations, now);
        MustChangePassword = mustChange;
        RegisterSuccess();
    }

    private void SetHash(string passwordHash, string algorithm, int iterations, DateTimeOffset changedAt)
    {
        PasswordHash = Guard.Text(passwordHash, "El hash", 512);
        Algorithm = Guard.Text(algorithm, "El algoritmo", 30);
        Guard.That(iterations >= MinIterations, "credential.iterations",
            $"El hash debe usar al menos {MinIterations} iteraciones.");
        Iterations = iterations;
        ChangedAt = changedAt.ToUniversalTime();
    }
}
