using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Inventory;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Warehousing;

namespace MINV.Application.Iam;

// ------------------------------------------------------------------------------------------------ usuarios
public sealed record UserRow(string Email, string Name, IReadOnlyList<string> Roles, bool IsActive, bool HasPassword, bool MustChangePassword,
    bool IsLocked, DateTimeOffset? LastAccess, int FailedAttempts, IReadOnlyList<string> BranchCodes);

[RequiresPermission(PermissionCodes.UsersManage)]
public sealed record GetUsersQuery : IRequest<IReadOnlyList<UserRow>>;

public sealed class GetUsersHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetUsersQuery, IReadOnlyList<UserRow>>
{
    public async Task<IReadOnlyList<UserRow>> Handle(GetUsersQuery request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var users = await db.Set<User>().OrderBy(u => u.DisplayName).ToListAsync(ct);
        var roles = (await (from ur in db.Set<UserRole>()
                            join r in db.Set<Role>() on ur.RoleId equals r.Id
                            select new { ur.UserId, r.Code }).ToListAsync(ct)).ToLookup(x => x.UserId, x => x.Code);
        var credentials = await db.Set<UserCredential>().ToDictionaryAsync(c => c.UserId, ct);
        var sessions = await db.Set<Session>().GroupBy(s => s.UserId).Select(g => new { g.Key, Last = g.Max(s => s.StartedAt) })
            .ToDictionaryAsync(x => x.Key, x => x.Last, ct);
        var branches = (await (from bu in db.Set<BranchUser>()
                               join b in db.Set<Branch>() on bu.BranchId equals b.Id
                               orderby b.Code
                               select new { bu.UserId, b.Code }).ToListAsync(ct)).ToLookup(x => x.UserId, x => x.Code);
        return users.Select(u =>
        {
            credentials.TryGetValue(u.Id, out var c);
            return new UserRow(u.Email, u.DisplayName, roles[u.Id].ToList(), u.IsActive, c is not null, c?.MustChangePassword == true,
                c?.IsLocked(now) == true, sessions.TryGetValue(u.Id, out var last) ? last : null, c?.FailedAttempts ?? 0, branches[u.Id].ToList());
        }).ToList();
    }
}

/// <summary>Crear o modificar un usuario: nombre, rol (uno), activo y, si se indica, contraseña temporal que deberá
/// cambiar al ingresar. V4: sucursales donde trabaja (códigos; null = no cambiar; al crearlo sin indicar, la sucursal activa
/// de quien lo crea).</summary>
[RequiresPermission(PermissionCodes.UsersManage)]
public sealed record SaveUserCommand(string? OriginalEmail, string Email, string Name, string RoleCode, bool IsActive, string? NewPassword,
    IReadOnlyList<string>? BranchCodes = null) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { OriginalEmail, Email, Name, RoleCode, IsActive, AsignaClave = NewPassword is { Length: > 0 }, BranchCodes };
}

public sealed class SaveUserValidator : AbstractValidator<SaveUserCommand>
{
    public SaveUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Indique un correo válido.");
        RuleFor(x => x.Name).NotEmpty().MinimumLength(2).WithMessage("Indique el nombre.").MaximumLength(120);
        RuleFor(x => x.RoleCode).NotEmpty().WithMessage("Elija el rol.");
        RuleFor(x => x.NewPassword).MinimumLength(8).WithMessage("La contraseña debe tener al menos 8 caracteres.")
            .Must(p => p!.Any(char.IsLetter) && p.Any(char.IsDigit)).WithMessage("La contraseña debe combinar letras y números.")
            .When(x => !string.IsNullOrEmpty(x.NewPassword));
        RuleFor(x => x.NewPassword).NotEmpty().When(x => string.IsNullOrWhiteSpace(x.OriginalEmail))
            .WithMessage("Asigne una contraseña temporal al usuario nuevo.");
    }
}

public sealed class SaveUserHandler(IMinvDbContext db, ICurrentUser current, IPasswordHasher hasher, IClock clock) : IRequestHandler<SaveUserCommand, string>
{
    public async Task<string> Handle(SaveUserCommand r, CancellationToken ct)
    {
        var email = User.NormalizeEmail(r.Email);
        var role = await db.Set<Role>().FirstOrDefaultAsync(x => x.Code == r.RoleCode.Trim().ToUpperInvariant(), ct)
                   ?? throw new NotFoundException($"El rol {r.RoleCode} no existe.");
        User user;
        if (string.IsNullOrWhiteSpace(r.OriginalEmail))
        {
            Guard.That(!await db.Set<User>().AnyAsync(u => u.Email == email, ct), "user.duplicate", $"Ya existe un usuario con el correo {email}.");
            user = new User(role.TenantId, email, r.Name.Trim());
            db.Set<User>().Add(user);
            if (r.BranchCodes is null)
            {
                db.Set<BranchUser>().Add(new BranchUser(user.TenantId, await BranchContext.ResolveAsync(db, null, ct), user.Id));
            }
        }
        else
        {
            var original = User.NormalizeEmail(r.OriginalEmail);
            user = await db.Set<User>().FirstOrDefaultAsync(u => u.Email == original, ct) ?? throw new NotFoundException($"El usuario {original} no existe.");
            if (email != original)
            {
                Guard.That(!await db.Set<User>().AnyAsync(u => u.Email == email, ct), "user.duplicate", $"Ya existe un usuario con el correo {email}.");
                user.ChangeEmail(email);
            }
            user.Rename(r.Name.Trim());
        }
        Guard.That(r.IsActive || user.Id != current.UserId, "user.self", "No puede desactivar su propio usuario.");
        if (r.IsActive)
        {
            user.Activate();
        }
        else
        {
            user.Deactivate();
        }
        var assigned = await db.Set<UserRole>().Where(x => x.UserId == user.Id).ToListAsync(ct);
        if (user.Id == current.UserId && role.Code != RoleCodes.Admin)
        {
            var admins = await (from ur in db.Set<UserRole>() join x in db.Set<Role>() on ur.RoleId equals x.Id where x.Code == RoleCodes.Admin select ur.UserId)
                .Distinct().CountAsync(ct);
            Guard.That(admins > 1, "user.last_admin", "Usted es el único administrador: no puede quitarse ese rol.");
        }
        db.Set<UserRole>().RemoveRange(assigned.Where(x => x.RoleId != role.Id));
        if (assigned.All(x => x.RoleId != role.Id))
        {
            db.Set<UserRole>().Add(new UserRole(user.TenantId, user.Id, role.Id));
        }
        if (r.BranchCodes is not null)
        {
            await UserBranches.AssignAsync(db, user, r.BranchCodes, ct);
        }
        if (!string.IsNullOrEmpty(r.NewPassword))
        {
            await Passwords.AssignAsync(db, hasher, clock, user, r.NewPassword, mustChange: true, ct);
        }
        await db.SaveChangesAsync(ct);
        return user.Email;
    }
}

/// <summary>El administrador asigna una contraseña temporal (y desbloquea la cuenta).</summary>
[RequiresPermission(PermissionCodes.UsersManage)]
public sealed record ResetUserPasswordCommand(string Email, string NewPassword, bool MustChange = true) : IRequest<bool>, IAuditableRequest
{
    public object AuditDetails => new { Email, MustChange };
}

public sealed class ResetUserPasswordValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordValidator() =>
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).WithMessage("La contraseña debe tener al menos 8 caracteres.")
            .Must(p => p.Any(char.IsLetter) && p.Any(char.IsDigit)).WithMessage("La contraseña debe combinar letras y números.");
}

public sealed class ResetUserPasswordHandler(IMinvDbContext db, IPasswordHasher hasher, IClock clock) : IRequestHandler<ResetUserPasswordCommand, bool>
{
    public async Task<bool> Handle(ResetUserPasswordCommand request, CancellationToken ct)
    {
        var email = User.NormalizeEmail(request.Email);
        var user = await db.Set<User>().FirstOrDefaultAsync(u => u.Email == email, ct) ?? throw new NotFoundException($"El usuario {email} no existe.");
        await Passwords.AssignAsync(db, hasher, clock, user, request.NewPassword, request.MustChange, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>V4 · Sucursales de un usuario (tabla de relación 5FN: se agregan y quitan filas, nunca se editan).</summary>
internal static class UserBranches
{
    public static async Task AssignAsync(IMinvDbContext db, User user, IReadOnlyList<string> branchCodes, CancellationToken ct)
    {
        var codes = branchCodes.Select(c => c.Trim().ToUpperInvariant()).Where(c => c.Length > 0).Distinct().ToList();
        Guard.That(codes.Count > 0, "user.branches", "Asigne al menos una sucursal al usuario.");
        var branches = await db.Set<Branch>().Where(b => codes.Contains(b.Code)).ToListAsync(ct);
        var missing = codes.Except(branches.Select(b => b.Code)).ToList();
        if (missing.Count > 0)
        {
            throw new NotFoundException($"No existe la sucursal {string.Join(", ", missing)}.");
        }
        var current = await db.Set<BranchUser>().Where(x => x.UserId == user.Id).ToListAsync(ct);
        db.Set<BranchUser>().RemoveRange(current.Where(x => branches.All(b => b.Id != x.BranchId)));
        foreach (var branch in branches.Where(b => current.All(x => x.BranchId != b.Id)))
        {
            db.Set<BranchUser>().Add(new BranchUser(user.TenantId, branch.Id, user.Id));
        }
    }
}

internal static class Passwords
{
    public static async Task AssignAsync(IMinvDbContext db, IPasswordHasher hasher, IClock clock, User user, string password, bool mustChange,
        CancellationToken ct)
    {
        var credential = await db.Set<UserCredential>().FirstOrDefaultAsync(c => c.UserId == user.Id, ct);
        var hash = hasher.Hash(password);
        if (credential is null)
        {
            db.Set<UserCredential>().Add(new UserCredential(user.TenantId, user.Id, hash, hasher.Algorithm, hasher.Iterations, clock.UtcNow, mustChange));
        }
        else
        {
            credential.ResetPassword(hash, hasher.Algorithm, hasher.Iterations, clock.UtcNow, mustChange);
        }
    }
}

// ------------------------------------------------------------------------------------------------ roles
public sealed record RoleRow(string Code, string Name, IReadOnlyList<string> Permissions, int Users);

public sealed record RolesView(IReadOnlyList<RoleRow> Roles, IReadOnlyList<(string Code, string Description)> Permissions);

/// <summary>Matriz rol × permiso (RBAC) con la cantidad de usuarios de cada rol.</summary>
[RequiresPermission(PermissionCodes.UsersManage)]
public sealed record GetRolesQuery : IRequest<RolesView>;

public sealed class GetRolesHandler(IMinvDbContext db) : IRequestHandler<GetRolesQuery, RolesView>
{
    public async Task<RolesView> Handle(GetRolesQuery request, CancellationToken ct)
    {
        var roles = await db.Set<Role>().ToListAsync(ct);
        var perms = (await (from rp in db.Set<RolePermission>()
                            join p in db.Set<Permission>() on rp.PermissionId equals p.Id
                            select new { rp.RoleId, p.Code }).ToListAsync(ct)).ToLookup(x => x.RoleId, x => x.Code);
        var users = await db.Set<UserRole>().GroupBy(x => x.RoleId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var order = RoleCodes.All.Select((r, i) => (r.Code, i)).ToDictionary(x => x.Code, x => x.i);
        return new RolesView(roles.OrderBy(r => order.GetValueOrDefault(r.Code, 99))
            .Select(r => new RoleRow(r.Code, r.Name, perms[r.Id].ToList(), users.GetValueOrDefault(r.Id))).ToList(), PermissionCodes.All);
    }
}

// ------------------------------------------------------------------------------------------------ empresa
public sealed record CompanySettings(string Code, string LegalName, string? TaxId, decimal AlertMargin, int DaysWithoutRotation, DateOnly MinBusinessDate,
    string TimeZoneId);

[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetCompanySettingsQuery : IRequest<CompanySettings>;

public sealed class GetCompanySettingsHandler(IMinvDbContext db, ITenantContext tenant) : IRequestHandler<GetCompanySettingsQuery, CompanySettings>
{
    public async Task<CompanySettings> Handle(GetCompanySettingsQuery request, CancellationToken ct)
    {
        var config = await new InventoryLookups(db).ConfigAsync(ct);
        var company = await db.Set<Tenant>().FirstAsync(t => t.Id == tenant.TenantId, ct);
        return new CompanySettings(company.Code, company.LegalName, company.TaxId, config.AlertMargin, config.DaysWithoutRotation, config.MinBusinessDate,
            config.TimeZoneId);
    }
}

/// <summary>Parámetros del semáforo (en la V2.1: cfgMargenAlerta y cfgDiasSinRotacion de 01_CONFIG).</summary>
[RequiresPermission(PermissionCodes.UsersManage)]
public sealed record UpdateCompanySettingsCommand(decimal AlertMargin, int DaysWithoutRotation) : IRequest<bool>, IAuditableRequest
{
    public object AuditDetails => new { AlertMargin, DaysWithoutRotation };
}

public sealed class UpdateCompanySettingsHandler(IMinvDbContext db) : IRequestHandler<UpdateCompanySettingsCommand, bool>
{
    public async Task<bool> Handle(UpdateCompanySettingsCommand request, CancellationToken ct)
    {
        var config = await new InventoryLookups(db).ConfigAsync(ct);
        config.Update(request.AlertMargin, request.DaysWithoutRotation, config.MinBusinessDate, config.TimeZoneId);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
