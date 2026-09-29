using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MINV.Application.Abstractions;
using MINV.Application.Iam;
using MINV.Domain.Iam;
using MINV.Infrastructure.Persistence;

namespace MINV.ApiGateway.Security;

/// <summary>
/// V6 · Configuración de la tienda web pública (<c>Minv:Storefront</c>): empresa y sucursal que atiende la tienda, orígenes
/// permitidos (CORS), vigencia de las reservas y límites por IP. Sin <see cref="TenantCode"/> las rutas <c>/storefront/v1</c>
/// responden 503 (la tienda no está configurada).
/// </summary>
public sealed class StorefrontSettings
{
    public const string Section = "Minv:Storefront";

    public bool Enabled { get; set; } = true;

    /// <summary>Código de la empresa cuya tienda se publica (p. ej. TECHZONE).</summary>
    public string? TenantCode { get; set; }

    /// <summary>Sucursal cuyo stock ve la tienda (por defecto, la del almacén principal: la casa matriz).</summary>
    public string? BranchCode { get; set; }

    /// <summary>Orígenes del catálogo web autorizados por CORS (p. ej. http://localhost:5173).</summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>Horas que dura una reserva web que no indica los días para recogerla (48).</summary>
    public int ReservationHours { get; set; } = 48;

    /// <summary>V7 · Tope en horas de una reserva web (72): quien reserva puede pedir de 1 a 3 días mientras quepan en el tope.</summary>
    public int MaxReservationHours { get; set; } = 72;

    /// <summary>Lecturas por minuto y por IP (300).</summary>
    public int ReadsPerMinute { get; set; } = 300;

    /// <summary>Reservas (y cancelaciones) por minuto y por IP (10).</summary>
    public int ReservationsPerMinute { get; set; } = 10;

    /// <summary>Cada cuántos minutos se cierran las reservas vencidas (5).</summary>
    public int ExpiryMinutes { get; set; } = 5;

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(TenantCode);
}

/// <summary>Identidad de la tienda web en una petición: la empresa, el usuario técnico y la sucursal de la tienda.</summary>
public sealed record StorefrontPrincipal(Guid TenantId, string TenantCode, Guid UserId, string Email, Guid BranchId, string BranchCode,
    IReadOnlyList<string> Permissions);

/// <summary>
/// V6 · Construye el principal técnico de la tienda (regla S-02) SIN llave: la empresa de <see cref="StorefrontSettings.TenantCode"/>,
/// su usuario con el rol <c>TIENDA_WEB</c> (permisos recalculados en cada petición desde la base) y la sucursal de la tienda
/// (<see cref="StorefrontSettings.BranchCode"/> o la del almacén principal). Deja el contexto listo para la tubería de MediatR
/// (empresa, usuario, alcance de UNA sucursal y canal <c>storefront</c>). Cualquier falta devuelve null (el gateway responde 503).
/// </summary>
public sealed class StorefrontAuthenticator(MinvWriteDbContext db, ITenantContext tenant, ICurrentUser user, IRequestOrigin origin,
    IOptionsMonitor<StorefrontSettings> settings)
{
    public async Task<StorefrontPrincipal?> AuthenticateAsync(CancellationToken ct)
    {
        var options = settings.CurrentValue;
        if (!options.IsConfigured)
        {
            return null;
        }
        var code = options.TenantCode!.Trim().ToUpperInvariant();
        var company = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Code == code && t.IsActive, ct);
        if (company is null)
        {
            return null;
        }
        tenant.Set(company.Id);
        tenant.SetBranches(new BranchScope(false, [Guid.Empty], null));   // nada de sucursal visible hasta resolver la tienda
        var technical = await (from ur in db.UserRoles
                               join r in db.Roles on ur.RoleId equals r.Id
                               join u in db.Users on ur.UserId equals u.Id
                               where r.Code == RoleCodes.Storefront && u.IsActive
                               orderby u.Email
                               select u).AsNoTracking().FirstOrDefaultAsync(ct);
        if (technical is null)
        {
            return null;
        }
        var branch = await ResolveBranchAsync(options.BranchCode, ct);
        if (branch is null)
        {
            return null;
        }
        var (_, permissions) = await UserAccess.PermissionsAsync(db, technical.Id, ct);
        user.SignIn(technical.Id, technical.Email, technical.DisplayName, permissions);
        tenant.SetBranches(new BranchScope(false, [branch.Value.Id], branch.Value.Id));
        origin.Set(RequestChannels.Storefront, null);
        return new StorefrontPrincipal(company.Id, company.Code, technical.Id, technical.Email, branch.Value.Id, branch.Value.Code, permissions);
    }

    /// <summary>La sucursal configurada; si no, la del almacén principal de la empresa; si no, la primera activa.</summary>
    private async Task<(Guid Id, string Code)?> ResolveBranchAsync(string? branchCode, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(branchCode))
        {
            var code = branchCode.Trim().ToUpperInvariant();
            var configured = await db.Branches.AsNoTracking().Where(b => b.Code == code && b.IsActive).Select(b => new { b.Id, b.Code }).FirstOrDefaultAsync(ct);
            return configured is null ? null : (configured.Id, configured.Code);
        }
        var main = await (from c in db.TenantConfigs
                          join w in db.Warehouses on c.DefaultWarehouseId equals w.Id
                          join b in db.Branches on w.BranchId equals b.Id
                          where b.IsActive
                          select new { b.Id, b.Code }).AsNoTracking().FirstOrDefaultAsync(ct)
                   ?? await db.Branches.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Code).Select(b => new { b.Id, b.Code }).FirstOrDefaultAsync(ct);
        return main is null ? null : (main.Id, main.Code);
    }
}

/// <summary>
/// V6 · Esquema de autenticación «Storefront» de las rutas públicas <c>/storefront/v1</c>: no lee ninguna credencial de la
/// petición; el principal es siempre el usuario técnico de la tienda configurada (<see cref="StorefrontAuthenticator"/>). Si la
/// tienda no está configurada (empresa, usuario técnico o sucursal), la respuesta es 503 con un aviso claro.
/// </summary>
public sealed class StorefrontAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Storefront";
    public const string PolicyName = "storefront";
    public const string ChannelClaim = "channel";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var principal = await Context.RequestServices.GetRequiredService<StorefrontAuthenticator>().AuthenticateAsync(Context.RequestAborted);
        if (principal is null)
        {
            return AuthenticateResult.Fail("La tienda web no está configurada en este servidor.");
        }
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, principal.UserId.ToString()),
            new Claim(ClaimTypes.Name, principal.Email),
            new Claim("tenant_id", principal.TenantId.ToString()),
            new Claim("tenant_code", principal.TenantCode),
            new Claim("branch_id", principal.BranchId.ToString()),
            new Claim("branch_code", principal.BranchCode),
            new Claim(ChannelClaim, RequestChannels.Storefront),
        ], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return Response.WriteAsJsonAsync(new
        {
            type = "https://minv.example/errores/storefront_unavailable",
            title = "Tienda web no disponible",
            status = 503,
            detail = "La tienda web no está configurada en este servidor (Minv:Storefront:TenantCode, usuario técnico TIENDA_WEB y sucursal).",
        });
    }
}
