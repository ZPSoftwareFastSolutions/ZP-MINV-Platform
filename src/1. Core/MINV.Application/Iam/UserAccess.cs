using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Iam;
using MINV.Domain.Warehousing;

namespace MINV.Application.Iam;

/// <summary>Sucursal visible en el selector del escritorio (y en la API).</summary>
public sealed record BranchInfo(Guid Id, string Code, string Name);

/// <summary>V4 · Alcance por sucursal que se entrega al cliente al iniciar sesión o al cambiar de sucursal.</summary>
public sealed record BranchAccess(bool AllBranches, IReadOnlyList<BranchInfo> Branches, Guid? ActiveBranchId)
{
    public BranchInfo? Active => Branches.FirstOrDefault(b => b.Id == ActiveBranchId);
}

/// <summary>
/// V4 · Permisos y alcance de un usuario, calculados en el servidor (login, sesión en la nube, API Key): los roles dan
/// los permisos; el permiso <c>corporate.branches.all</c> da todas las sucursales (gerencia global) y, si no lo tiene, el
/// usuario ve solo las asignadas en <c>warehouse.branch_users</c>. <c>branch_users</c> y <c>branches</c> son tablas de la
/// empresa (sin filtro por sucursal): se pueden leer antes de conocer el alcance.
/// </summary>
public static class UserAccess
{
    public static async Task<(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)> PermissionsAsync(IMinvDbContext db, Guid userId,
        CancellationToken ct)
    {
        var roles = await (from ur in db.Set<UserRole>()
                           join r in db.Set<Role>() on ur.RoleId equals r.Id
                           where ur.UserId == userId
                           select r.Code).ToListAsync(ct);
        var permissions = await (from ur in db.Set<UserRole>()
                                 join rp in db.Set<RolePermission>() on ur.RoleId equals rp.RoleId
                                 join p in db.Set<Permission>() on rp.PermissionId equals p.Id
                                 where ur.UserId == userId
                                 select p.Code).Distinct().ToListAsync(ct);
        return (roles, permissions);
    }

    /// <summary>Alcance del usuario. La sucursal activa preferida (la de la sesión anterior) se respeta si sigue dentro del
    /// alcance; si no, la primera asignada (o ninguna para la gerencia global sin asignaciones: vista consolidada).</summary>
    public static async Task<BranchAccess> AccessAsync(IMinvDbContext db, Guid userId, IReadOnlyCollection<string> permissions, Guid? preferredActive,
        CancellationToken ct)
    {
        var all = permissions.Contains(PermissionCodes.BranchesAll);
        var active = await db.Set<Branch>().Where(b => b.IsActive).OrderBy(b => b.Code).Select(b => new BranchInfo(b.Id, b.Code, b.Name)).ToListAsync(ct);
        var assigned = await db.Set<BranchUser>().Where(x => x.UserId == userId).Select(x => x.BranchId).ToListAsync(ct);
        var visible = all ? active : active.Where(b => assigned.Contains(b.Id)).ToList();
        if (!all && visible.Count == 0)
        {
            throw new AccessDeniedException("Su usuario no tiene sucursales asignadas: pídale al administrador que le asigne una.");
        }
        Guid? chosen = preferredActive is Guid p && visible.Any(b => b.Id == p)
            ? p
            : visible.FirstOrDefault(b => assigned.Contains(b.Id))?.Id ?? (all ? null : visible[0].Id);
        return new BranchAccess(all, visible, chosen);
    }

    public static BranchScope ToScope(this BranchAccess access) =>
        new(access.AllBranches, access.Branches.Select(b => b.Id).ToList(), access.ActiveBranchId);
}

// ------------------------------------------------------------------------------------------------ sucursal activa
/// <summary>V4 · Cambia la sucursal activa de la sesión (null = todas, solo gerencia global). A partir de aquí la caja,
/// los movimientos y las compras se registran en esa sucursal.</summary>
public sealed record SelectBranchCommand(Guid SessionId, Guid? BranchId) : IRequest<BranchAccess>, IAuditableRequest
{
    public object AuditDetails => new { SessionId, BranchId };
}

public sealed class SelectBranchHandler(IMinvDbContext db, ICurrentUser user, ITenantContext tenant) : IRequestHandler<SelectBranchCommand, BranchAccess>
{
    public async Task<BranchAccess> Handle(SelectBranchCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para elegir la sucursal.");
        var session = await db.Set<Session>().FirstOrDefaultAsync(s => s.Id == request.SessionId && s.UserId == userId, ct)
                      ?? throw new NotFoundException("La sesión no existe.");
        var access = await UserAccess.AccessAsync(db, userId, user.Permissions, null, ct);
        if (request.BranchId is Guid id)
        {
            if (access.Branches.All(b => b.Id != id))
            {
                throw new AccessDeniedException("La sucursal elegida no está entre las suyas.");
            }
        }
        else if (!access.AllBranches)
        {
            throw new AccessDeniedException("Elija una de sus sucursales (la vista de todas es solo para la gerencia).");
        }
        session.SelectBranch(request.BranchId);
        await db.SaveChangesAsync(ct);
        var chosen = access with { ActiveBranchId = request.BranchId };
        tenant.SetBranches(chosen.ToScope());
        return chosen;
    }
}
