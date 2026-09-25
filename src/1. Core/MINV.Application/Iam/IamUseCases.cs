using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Iam;

namespace MINV.Application.Iam;

// ------------------------------------------------------------------------------------------------ login
/// <summary>Inicio de sesión cifrado (PBKDF2). Deja rastro en AccessLogs (éxito o falla) y abre una Session.</summary>
public sealed record LoginCommand(string TenantCode, string Email, string Password, string MachineName, string ClientVersion,
    string? HardwareFingerprint = null) : IRequest<LoginResult>;

public sealed record LoginResult(Guid TenantId, Guid UserId, Guid SessionId, string DisplayName, IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions, bool MustChangePassword);

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.TenantCode).NotEmpty().WithMessage("Indique la empresa.");
        RuleFor(x => x.Email).NotEmpty().WithMessage("Indique su correo.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Indique su contraseña.");
    }
}

public sealed class LoginHandler(IMinvDbContext db, ITenantContext tenant, ICurrentUser currentUser, IPasswordHasher hasher, IClock clock)
    : IRequestHandler<LoginCommand, LoginResult>
{
    private const string Generic = "Empresa, correo o contraseña incorrectos.";

    public async Task<LoginResult> Handle(LoginCommand request, CancellationToken ct)
    {
        var code = request.TenantCode.Trim().ToUpperInvariant();
        var company = await db.Set<Tenant>().FirstOrDefaultAsync(t => t.Code == code && t.IsActive, ct)
                      ?? throw new AuthenticationFailedException(Generic);
        tenant.Set(company.Id);
        var now = clock.UtcNow;
        var email = User.NormalizeEmail(request.Email);
        var device = request.HardwareFingerprint is { Length: > 0 } fp
            ? await db.Set<HardwareToken>().FirstOrDefaultAsync(h => h.Fingerprint == fp && h.RevokedAt == null, ct)
            : null;
        var user = await db.Set<User>().FirstOrDefaultAsync(u => u.Email == email, ct);
        var credential = user is null ? null : await db.Set<UserCredential>().FirstOrDefaultAsync(c => c.UserId == user.Id, ct);

        string? failure = null;
        if (user is null || credential is null)
        {
            failure = "usuario inexistente o sin credencial";
        }
        else if (!user.IsActive)
        {
            failure = "usuario inactivo";
        }
        else if (credential.IsLocked(now))
        {
            failure = "cuenta bloqueada por intentos fallidos";
        }
        else if (!hasher.Verify(request.Password, credential.PasswordHash, credential.Iterations))
        {
            failure = "contraseña incorrecta";
            credential.RegisterFailure(now);
        }

        db.Set<AccessLog>().Add(new AccessLog(company.Id, user?.Id, email, failure is null, failure, now, request.MachineName, device?.Id));
        if (failure is not null)
        {
            await db.SaveChangesAsync(ct);
            throw new AuthenticationFailedException(credential?.IsLocked(now) == true
                ? $"Cuenta bloqueada por {UserCredential.MaxFailedAttempts} intentos fallidos: espere {UserCredential.LockoutDuration.TotalMinutes} minutos."
                : Generic);
        }

        credential!.RegisterSuccess();
        var session = new Session(company.Id, user!.Id, device?.Id, now, request.MachineName, request.ClientVersion);
        db.Set<Session>().Add(session);
        await db.SaveChangesAsync(ct);

        var roles = await (from ur in db.Set<UserRole>()
                           join r in db.Set<Role>() on ur.RoleId equals r.Id
                           where ur.UserId == user.Id
                           select r.Code).ToListAsync(ct);
        var permissions = await (from ur in db.Set<UserRole>()
                                 join rp in db.Set<RolePermission>() on ur.RoleId equals rp.RoleId
                                 join p in db.Set<Permission>() on rp.PermissionId equals p.Id
                                 where ur.UserId == user.Id
                                 select p.Code).Distinct().ToListAsync(ct);
        currentUser.SignIn(user.Id, user.Email, user.DisplayName, permissions);
        return new LoginResult(company.Id, user.Id, session.Id, user.DisplayName, roles, permissions, credential.MustChangePassword);
    }
}

// ------------------------------------------------------------------------------------------------ contraseña
/// <summary>Cambio de contraseña del usuario de la sesión (obligatorio cuando el administrador la asignó con
/// «debe cambiarla»). Un intento con la contraseña actual incorrecta cuenta para el bloqueo por intentos fallidos.</summary>
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<bool>, IAuditableRequest
{
    public object AuditDetails => new { Operacion = "cambio de contraseña" };
}

public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Indique su contraseña actual.");
        RuleFor(x => x.NewPassword).NotEmpty().WithMessage("Indique la nueva contraseña.")
            .MinimumLength(8).WithMessage("La nueva contraseña debe tener al menos 8 caracteres.")
            .MaximumLength(128).WithMessage("La nueva contraseña supera 128 caracteres.")
            .Must(p => p.Any(char.IsLetter) && p.Any(char.IsDigit)).WithMessage("La nueva contraseña debe combinar letras y números.");
        RuleFor(x => x).Must(x => x.NewPassword != x.CurrentPassword).WithMessage("La nueva contraseña debe ser distinta de la actual.");
    }
}

public sealed class ChangePasswordHandler(IMinvDbContext db, ICurrentUser user, IPasswordHasher hasher, IClock clock)
    : IRequestHandler<ChangePasswordCommand, bool>
{
    public async Task<bool> Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para cambiar su contraseña.");
        var credential = await db.Set<UserCredential>().FirstOrDefaultAsync(c => c.UserId == userId, ct)
                         ?? throw new NotFoundException("Su usuario no tiene credencial: pídala al administrador.");
        var now = clock.UtcNow;
        if (!hasher.Verify(request.CurrentPassword, credential.PasswordHash, credential.Iterations))
        {
            credential.RegisterFailure(now);
            await db.SaveChangesAsync(ct);
            throw new AuthenticationFailedException("La contraseña actual no es correcta.");
        }
        credential.ChangePassword(hasher.Hash(request.NewPassword), hasher.Algorithm, hasher.Iterations, now);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

// ------------------------------------------------------------------------------------------------ cerrar sesión
/// <summary>Cierra la sesión: la marca como terminada (Sessions) y queda en la auditoría. El cliente descarta después su
/// contexto de sesión (usuario, datos y pantallas), por eso el usuario sigue identificado al auditar.</summary>
public sealed record LogoutCommand(Guid SessionId) : IRequest<bool>, IAuditableRequest
{
    public object AuditDetails => new { SessionId };
}

public sealed class LogoutHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<LogoutCommand, bool>
{
    public async Task<bool> Handle(LogoutCommand request, CancellationToken ct)
    {
        var session = await db.Set<Session>().FirstOrDefaultAsync(s => s.Id == request.SessionId, ct);
        if (session is not { IsOpen: true } || session.UserId != user.UserId)
        {
            return false;
        }
        session.End(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

// ------------------------------------------------------------------------------------------------ actividad
/// <summary>Actividad reciente (en la V2.1: 14_ACTIVIDAD): quién hizo qué, cuándo y con qué resultado.</summary>
[RequiresPermission(PermissionCodes.AuditView)]
public sealed record GetActivityQuery(int Take = 200) : IRequest<IReadOnlyList<ActivityRow>>;

public sealed record ActivityRow(DateTimeOffset OccurredAt, string? UserEmail, string? UserName, string Action, AuditOutcome Outcome,
    string? Details);

public sealed class GetActivityHandler(IMinvDbContext db) : IRequestHandler<GetActivityQuery, IReadOnlyList<ActivityRow>>
{
    public async Task<IReadOnlyList<ActivityRow>> Handle(GetActivityQuery request, CancellationToken ct) =>
        await (from a in db.Set<AuditLog>()
               join u in db.Set<User>() on a.UserId equals u.Id into users
               from u in users.DefaultIfEmpty()
               orderby a.OccurredAt descending
               select new ActivityRow(a.OccurredAt, u == null ? null : u.Email, u == null ? null : u.DisplayName, a.Action,
                   a.Outcome, a.Details)).Take(Math.Clamp(request.Take, 1, 5000)).ToListAsync(ct);
}
