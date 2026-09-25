using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Iam;
using MINV.Application.Integration;
using MINV.Domain.Iam;
using MINV.Domain.Integration;
using MINV.Infrastructure.Persistence;

namespace MINV.Infrastructure.Integration;

/// <summary>Identidad de una petición autenticada con API Key.</summary>
public sealed record ApiPrincipal(Guid ApiKeyId, Guid TenantId, Guid OwnerUserId, string KeyName, string Prefix, IReadOnlyList<string> Scopes,
    IReadOnlyList<string> Permissions, BranchScope Branches);

/// <summary>
/// V4 · Autentica una API Key y deja el contexto de la petición (empresa, usuario dueño con permisos recortados por los
/// alcances, sucursales y canal <c>api</c>) listo para la tubería de MediatR. El prefijo se busca con la función SECURITY
/// DEFINER <c>integration.resolve_api_key</c> (antes de conocer la empresa, RLS no dejaría ver la fila); el hash se compara
/// en tiempo constante. Cualquier falla devuelve null (el gateway responde 401 sin decir por qué).
/// </summary>
public sealed class ApiKeyAuthenticator(MinvWriteDbContext db, ITenantContext tenant, ICurrentUser user, IRequestOrigin origin, IClock clock)
{
    private sealed record KeyRow(Guid ApiKeyId, Guid TenantId, string TokenHash, Guid OwnerUserId, Guid? BranchId, DateTimeOffset? ExpiresAt,
        DateTimeOffset? RevokedAt);

    public async Task<ApiPrincipal?> AuthenticateAsync(string? token, CancellationToken ct)
    {
        var prefix = ApiKeyTokens.PrefixOf(token);
        if (prefix is null)
        {
            return null;
        }
        var row = await FindAsync(prefix, ct);
        var now = clock.UtcNow;
        if (row is null || !ApiKeyTokens.Matches(token!, row.TokenHash) || row.RevokedAt is not null || row.ExpiresAt <= now)
        {
            return null;
        }
        tenant.Set(row.TenantId);
        tenant.SetBranches(new BranchScope(false, row.BranchId is Guid b ? [b] : [Guid.Empty], row.BranchId));
        var key = await db.ApiKeys.AsNoTracking().Include(k => k.Scopes).FirstOrDefaultAsync(k => k.Id == row.ApiKeyId, ct);
        var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == row.OwnerUserId && u.IsActive, ct);
        if (key is null || owner is null)
        {
            return null;
        }
        var (_, ownerPermissions) = await UserAccess.PermissionsAsync(db, owner.Id, ct);
        var scopes = key.Scopes.Select(s => s.Scope).ToList();
        var effective = ApiScopes.EffectivePermissions(scopes, ownerPermissions);
        BranchScope branches;
        try
        {
            var access = await UserAccess.AccessAsync(db, owner.Id, ownerPermissions, row.BranchId, ct);
            if (row.BranchId is Guid only)
            {
                if (access.Branches.All(x => x.Id != only))
                {
                    return null;   // el dueño ya no trabaja en la sucursal de la llave
                }
                branches = new BranchScope(false, [only], only);
            }
            else
            {
                branches = access.ToScope();
            }
        }
        catch (Application.Common.AccessDeniedException)
        {
            return null;
        }
        user.SignIn(owner.Id, owner.Email, $"{key.Name} (API)", effective);
        tenant.SetBranches(branches);
        origin.Set(RequestChannels.Api, key.Id);
        return new ApiPrincipal(key.Id, key.TenantId, owner.Id, key.Name, key.Prefix, scopes, effective, branches);
    }

    private async Task<KeyRow?> FindAsync(string prefix, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            return await db.Database.SqlQuery<KeyRow>(
                    $"SELECT api_key_id AS \"ApiKeyId\", tenant_id AS \"TenantId\", token_hash AS \"TokenHash\", owner_user_id AS \"OwnerUserId\", branch_id AS \"BranchId\", expires_at AS \"ExpiresAt\", revoked_at AS \"RevokedAt\" FROM integration.resolve_api_key({prefix})")
                .FirstOrDefaultAsync(ct);
        }
        // Demostración en memoria (sin RLS): búsqueda de plataforma por el prefijo, que es único en toda la base
        return await db.ApiKeys.IgnoreQueryFilters().AsNoTracking().Where(k => k.Prefix == prefix)
            .Select(k => new KeyRow(k.Id, k.TenantId, k.TokenHash, k.OwnerUserId, k.BranchId, k.ExpiresAt, k.RevokedAt)).FirstOrDefaultAsync(ct);
    }
}

/// <summary>Sesión del servidor en la nube resuelta a partir de su token.</summary>
public sealed record CloudPrincipal(Guid SessionId, Guid TenantId, Guid UserId, BranchAccess Access, IReadOnlyList<string> Permissions);

/// <summary>
/// V4 · Autentica el token de sesión del escritorio en modo nube: busca la sesión por el hash del token
/// (<c>iam.resolve_session</c>, SECURITY DEFINER), exige que esté abierta y vigente (vencimiento deslizante), recalcula
/// permisos y alcance EN EL SERVIDOR (el escritorio no decide qué ve) y deja el contexto con el canal <c>cloud</c>.
/// </summary>
public sealed class CloudSessionAuthenticator(MinvWriteDbContext db, ITenantContext tenant, ICurrentUser user, IRequestOrigin origin, IClock clock)
{
    public static readonly TimeSpan SlidingExpiration = TimeSpan.FromHours(12);

    private sealed record SessionRow(Guid SessionId, Guid TenantId, Guid UserId, Guid? ActiveBranchId, DateTimeOffset? ExpiresAt, DateTimeOffset? EndedAt);

    public async Task<CloudPrincipal?> AuthenticateAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith("mses_", StringComparison.Ordinal) || token.Length > 200)
        {
            return null;
        }
        var hash = ApiKeyTokens.Hash(token);
        var row = await FindAsync(hash, ct);
        var now = clock.UtcNow;
        if (row is null || row.EndedAt is not null || row.ExpiresAt is null || row.ExpiresAt <= now)
        {
            return null;
        }
        tenant.Set(row.TenantId);
        tenant.SetBranches(new BranchScope(false, [Guid.Empty], null));   // nada visible hasta calcular el alcance
        var account = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == row.UserId && u.IsActive, ct);
        if (account is null)
        {
            return null;
        }
        var (_, permissions) = await UserAccess.PermissionsAsync(db, account.Id, ct);
        BranchAccess access;
        try
        {
            access = await UserAccess.AccessAsync(db, account.Id, permissions, row.ActiveBranchId, ct);
        }
        catch (Application.Common.AccessDeniedException)
        {
            return null;
        }
        if (row.ActiveBranchId is null && access.AllBranches)
        {
            access = access with { ActiveBranchId = null };   // gerencia global en vista consolidada
        }
        user.SignIn(account.Id, account.Email, account.DisplayName, permissions);
        tenant.SetBranches(access.ToScope());
        origin.Set(RequestChannels.Cloud, null);
        // Vencimiento deslizante: se renueva a lo sumo una vez por minuto
        if (row.ExpiresAt - now < SlidingExpiration - TimeSpan.FromMinutes(1))
        {
            var session = await db.Sessions.FirstAsync(s => s.Id == row.SessionId, ct);
            session.Extend(now + SlidingExpiration);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        return new CloudPrincipal(row.SessionId, row.TenantId, account.Id, access, permissions);
    }

    private async Task<SessionRow?> FindAsync(string hash, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            return await db.Database.SqlQuery<SessionRow>(
                    $"SELECT session_id AS \"SessionId\", tenant_id AS \"TenantId\", user_id AS \"UserId\", active_branch_id AS \"ActiveBranchId\", expires_at AS \"ExpiresAt\", ended_at AS \"EndedAt\" FROM iam.resolve_session({hash})")
                .FirstOrDefaultAsync(ct);
        }
        return await db.Sessions.IgnoreQueryFilters().AsNoTracking().Where(s => s.TokenHash == hash)
            .Select(s => new SessionRow(s.Id, s.TenantId, s.UserId, s.ActiveBranchId, s.ExpiresAt, s.EndedAt)).FirstOrDefaultAsync(ct);
    }
}
