using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Integration;
using MINV.Domain.Warehousing;

namespace MINV.Application.Integration;

// ================================================================================================ API Keys
public sealed record ApiKeyRow(Guid Id, string Name, string Prefix, string Owner, string? Branch, IReadOnlyList<string> Scopes, DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt, DateTimeOffset? RevokedAt, DateTimeOffset? LastUsedAt, bool IsUsable);

/// <summary>V4 · Llaves del API Gateway (nunca devuelve el token: solo el prefijo).</summary>
[RequiresPermission(PermissionCodes.IntegrationManage)]
public sealed record GetApiKeysQuery : IRequest<IReadOnlyList<ApiKeyRow>>;

public sealed class GetApiKeysHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetApiKeysQuery, IReadOnlyList<ApiKeyRow>>
{
    public async Task<IReadOnlyList<ApiKeyRow>> Handle(GetApiKeysQuery request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var keys = await db.Set<ApiKey>().Include(k => k.Scopes).OrderByDescending(k => k.CreatedAt).ToListAsync(ct);
        var users = await db.Set<User>().ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        var branches = await db.Set<Branch>().ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        var ids = keys.Select(k => k.Id).ToList();
        var used = await db.Set<AuditLog>().Where(a => a.ApiKeyId != null && ids.Contains(a.ApiKeyId.Value))
            .GroupBy(a => a.ApiKeyId!.Value).Select(g => new { g.Key, Last = g.Max(a => a.OccurredAt) }).ToDictionaryAsync(x => x.Key, x => x.Last, ct);
        return keys.Select(k => new ApiKeyRow(k.Id, k.Name, k.Prefix, users.GetValueOrDefault(k.OwnerUserId, "?"),
            k.BranchId is { } b ? branches.GetValueOrDefault(b) : null, k.Scopes.Select(s => s.Scope).Order().ToList(), k.CreatedAt, k.ExpiresAt, k.RevokedAt,
            used.TryGetValue(k.Id, out var last) ? last : null, k.IsUsable(now))).ToList();
    }
}

public sealed record CreatedApiKey(Guid Id, string Name, string Prefix, string Token, IReadOnlyList<string> EffectivePermissions);

/// <summary>
/// V4 · Crea una API Key para una integración (e-commerce, ERP). Actúa en nombre de quien la crea, con sus permisos
/// recortados por los alcances elegidos, y puede limitarse a una sucursal del alcance. El token completo se devuelve UNA
/// sola vez (en la base queda el hash SHA-256).
/// </summary>
[RequiresModule(LicenseModuleCodes.ApiIntegrations)]
[RequiresPermission(PermissionCodes.IntegrationManage)]
public sealed record CreateApiKeyCommand(string Name, IReadOnlyList<string> Scopes, string? BranchCode = null, int? ExpiresInDays = null)
    : IRequest<CreatedApiKey>, IAuditableRequest
{
    public object AuditDetails => new { Name, Scopes, BranchCode, ExpiresInDays };
}

public sealed class CreateApiKeyValidator : AbstractValidator<CreateApiKeyCommand>
{
    public CreateApiKeyValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Indique para qué es la llave (p. ej. «Tienda en línea»).").MaximumLength(100);
        RuleFor(x => x.Scopes).NotEmpty().WithMessage("Elija al menos un alcance.");
        RuleFor(x => x.ExpiresInDays).InclusiveBetween(1, 730).When(x => x.ExpiresInDays is not null)
            .WithMessage("El vencimiento va de 1 a 730 días.");
    }
}

public sealed class CreateApiKeyHandler(IMinvDbContext db, ICurrentUser user, ITenantContext tenant, IClock clock)
    : IRequestHandler<CreateApiKeyCommand, CreatedApiKey>
{
    public async Task<CreatedApiKey> Handle(CreateApiKeyCommand r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para crear llaves.");
        Guid? branchId = null;
        if (!string.IsNullOrWhiteSpace(r.BranchCode))
        {
            var code = r.BranchCode.Trim().ToUpperInvariant();
            branchId = await db.Set<Branch>().Where(b => b.Code == code).Select(b => (Guid?)b.Id).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException($"La sucursal {code} no existe.");
            if (!db.Branches.Allows(branchId.Value))
            {
                throw new AccessDeniedException("No puede crear llaves para una sucursal que no es suya.");
            }
        }
        var effective = ApiScopes.EffectivePermissions(r.Scopes.Select(s => s.Trim().ToLowerInvariant()), user.Permissions);
        Guard.That(effective.Count > 0, "api_key.no_permissions", "Su rol no tiene ninguno de los permisos que piden esos alcances.");
        var now = clock.UtcNow;
        var generated = ApiKeyTokens.Generate();
        var key = new ApiKey(tenant.TenantId, r.Name.Trim(), generated.Prefix, generated.Hash, userId, branchId, now,
            r.ExpiresInDays is { } days ? now.AddDays(days) : null, r.Scopes);
        db.Set<ApiKey>().Add(key);
        await db.SaveChangesAsync(ct);
        return new CreatedApiKey(key.Id, key.Name, key.Prefix, generated.Token, effective);
    }
}

/// <summary>V4 · Revoca una llave (queda registrada: nunca se borra).</summary>
[RequiresPermission(PermissionCodes.IntegrationManage)]
public sealed record RevokeApiKeyCommand(Guid Id) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Id };
}

public sealed class RevokeApiKeyHandler(IMinvDbContext db, IClock clock) : IRequestHandler<RevokeApiKeyCommand, string>
{
    public async Task<string> Handle(RevokeApiKeyCommand request, CancellationToken ct)
    {
        var key = await db.Set<ApiKey>().FirstOrDefaultAsync(k => k.Id == request.Id, ct) ?? throw new NotFoundException("La llave no existe.");
        key.Revoke(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return $"✔ Llave {key.Name} ({key.Prefix}) revocada: el gateway la rechaza desde ahora.";
    }
}

// ================================================================================================ webhooks
public sealed record WebhookRow(Guid Id, string Url, string? Description, IReadOnlyList<string> Events, string? Branch, bool IsActive,
    DateTimeOffset CreatedAt, int SecretVersion, int Delivered, int Failed, DateTimeOffset? LastAttemptAt, string? LastError);

[RequiresPermission(PermissionCodes.IntegrationManage)]
public sealed record GetWebhooksQuery : IRequest<IReadOnlyList<WebhookRow>>;

public sealed class GetWebhooksHandler(IMinvDbContext db) : IRequestHandler<GetWebhooksQuery, IReadOnlyList<WebhookRow>>
{
    public async Task<IReadOnlyList<WebhookRow>> Handle(GetWebhooksQuery request, CancellationToken ct)
    {
        var endpoints = (await db.Set<WebhookEndpoint>().Include(e => e.Events).OrderBy(e => e.CreatedAt).ToListAsync(ct))
            .Where(e => Webhooks.Visible(db, e)).ToList();
        var branches = await db.Set<Branch>().ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        var stats = await db.Set<WebhookDelivery>().GroupBy(d => d.EndpointId).Select(g => new
        {
            g.Key,
            Delivered = g.Count(d => d.Succeeded),
            Failed = g.Count(d => !d.Succeeded),
            Last = g.Max(d => d.AttemptedAt),
        }).ToDictionaryAsync(x => x.Key, ct);
        var lastErrors = await (from d in db.Set<WebhookDelivery>()
                                where !d.Succeeded && d.AttemptedAt == db.Set<WebhookDelivery>().Where(x => x.EndpointId == d.EndpointId).Max(x => x.AttemptedAt)
                                select new { d.EndpointId, d.Error, d.StatusCode }).ToListAsync(ct);
        return endpoints.Select(e =>
        {
            stats.TryGetValue(e.Id, out var s);
            var error = lastErrors.FirstOrDefault(x => x.EndpointId == e.Id);
            return new WebhookRow(e.Id, e.Url, e.Description, e.Events.Select(x => x.EventType).Order().ToList(),
                e.BranchId is { } b ? branches.GetValueOrDefault(b) : null, e.IsActive, e.CreatedAt, e.SecretVersion, s?.Delivered ?? 0, s?.Failed ?? 0,
                s?.Last, error is null ? null : $"{error.StatusCode?.ToString() ?? "sin respuesta"}: {error.Error}");
        }).ToList();
    }
}

public sealed record CreatedWebhook(Guid Id, string Url, string Secret, string Message);

/// <summary>
/// V4 · Registra un webhook: la plataforma enviará por POST firmado (HMAC-SHA256, cabecera <c>X-MINV-Signature</c>) los
/// eventos elegidos. El secreto de firma se genera al azar, se guarda cifrado y se muestra UNA sola vez.
/// </summary>
[RequiresModule(LicenseModuleCodes.ApiIntegrations)]
[RequiresPermission(PermissionCodes.IntegrationManage)]
public sealed record CreateWebhookCommand(string Url, IReadOnlyList<string> Events, string? Description = null, string? BranchCode = null)
    : IRequest<CreatedWebhook>, IAuditableRequest
{
    public object AuditDetails => new { Url, Events, Description, BranchCode };
}

public sealed class CreateWebhookValidator : AbstractValidator<CreateWebhookCommand>
{
    public CreateWebhookValidator()
    {
        RuleFor(x => x.Url).NotEmpty().WithMessage("Indique la URL https del webhook.").MaximumLength(500);
        RuleFor(x => x.Events).NotEmpty().WithMessage("Elija al menos un evento.");
        RuleFor(x => x.Description).MaximumLength(200);
    }
}

public sealed class CreateWebhookHandler(IMinvDbContext db, ICurrentUser user, ITenantContext tenant, IRequestOrigin origin, ISecretProtector protector,
    IClock clock) : IRequestHandler<CreateWebhookCommand, CreatedWebhook>
{
    public const int MaxEndpoints = 20;

    public async Task<CreatedWebhook> Handle(CreateWebhookCommand r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para registrar webhooks.");
        Guard.That(await db.Set<WebhookEndpoint>().CountAsync(e => e.IsActive, ct) < MaxEndpoints, "webhook.limit",
            $"La empresa ya tiene {MaxEndpoints} webhooks activos: desactive alguno antes de registrar otro.");
        Guid? branchId = null;
        if (!string.IsNullOrWhiteSpace(r.BranchCode))
        {
            var code = r.BranchCode.Trim().ToUpperInvariant();
            branchId = await db.Set<Branch>().Where(b => b.Code == code).Select(b => (Guid?)b.Id).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException($"La sucursal {code} no existe.");
            if (!db.Branches.Allows(branchId.Value))
            {
                throw new AccessDeniedException("No puede suscribirse a los eventos de una sucursal que no es suya.");
            }
        }
        else if (!db.Branches.AllBranches)
        {
            // Una sesión (o llave) limitada a sucursales no puede recibir eventos de toda la empresa
            branchId = db.Branches.ActiveBranchId ?? db.Branches.BranchIds.First();
        }
        var secret = ApiKeyTokens.NewWebhookSecret();
        var endpoint = new WebhookEndpoint(tenant.TenantId, r.Url, r.Description?.Trim(), r.Events, userId, origin.ApiKeyId, branchId,
            protector.Protect(secret), protector.CurrentKeyId, clock.UtcNow);
        db.Set<WebhookEndpoint>().Add(endpoint);
        await db.SaveChangesAsync(ct);
        return new CreatedWebhook(endpoint.Id, endpoint.Url, secret,
            "✔ Webhook registrado. Guarde el secreto: se muestra una sola vez y sirve para verificar la firma X-MINV-Signature.");
    }
}

/// <summary>V4 · Rota el secreto de firma: el anterior sigue firmando (doble firma) durante 24 horas.</summary>
[RequiresPermission(PermissionCodes.IntegrationManage)]
public sealed record RotateWebhookSecretCommand(Guid Id) : IRequest<CreatedWebhook>, IAuditableRequest
{
    public object AuditDetails => new { Id };
}

public sealed class RotateWebhookSecretHandler(IMinvDbContext db, ISecretProtector protector, IClock clock)
    : IRequestHandler<RotateWebhookSecretCommand, CreatedWebhook>
{
    public async Task<CreatedWebhook> Handle(RotateWebhookSecretCommand request, CancellationToken ct)
    {
        var endpoint = await Webhooks.FindAsync(db, request.Id, ct);
        var secret = ApiKeyTokens.NewWebhookSecret();
        endpoint.RotateSecret(protector.Protect(secret), protector.CurrentKeyId, clock.UtcNow, TimeSpan.FromHours(24));
        await db.SaveChangesAsync(ct);
        return new CreatedWebhook(endpoint.Id, endpoint.Url, secret, "✔ Secreto rotado: durante 24 horas cada entrega lleva las dos firmas.");
    }
}

/// <summary>V4 · Desactiva un webhook (deja de recibir eventos; su historial de entregas se conserva).</summary>
[RequiresPermission(PermissionCodes.IntegrationManage)]
public sealed record DisableWebhookCommand(Guid Id) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Id };
}

public sealed class DisableWebhookHandler(IMinvDbContext db, IClock clock) : IRequestHandler<DisableWebhookCommand, string>
{
    public async Task<string> Handle(DisableWebhookCommand request, CancellationToken ct)
    {
        var endpoint = await Webhooks.FindAsync(db, request.Id, ct);
        endpoint.Disable(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return $"✔ Webhook {endpoint.Url} desactivado.";
    }
}

public sealed record WebhookDeliveryRow(DateTimeOffset AttemptedAt, string Url, string EventType, int Attempt, int? StatusCode, bool Succeeded, string? Error,
    int DurationMs);

/// <summary>V4 · Últimas entregas de webhooks (para diagnosticar una integración).</summary>
[RequiresPermission(PermissionCodes.IntegrationManage)]
public sealed record GetWebhookDeliveriesQuery(Guid? EndpointId = null, int Take = 200) : IRequest<IReadOnlyList<WebhookDeliveryRow>>;

public sealed class GetWebhookDeliveriesHandler(IMinvDbContext db) : IRequestHandler<GetWebhookDeliveriesQuery, IReadOnlyList<WebhookDeliveryRow>>
{
    public async Task<IReadOnlyList<WebhookDeliveryRow>> Handle(GetWebhookDeliveriesQuery request, CancellationToken ct)
    {
        var scope = db.Branches;
        var branchIds = scope.BranchIds.ToList();
        var all = scope.AllBranches;
        return await (from d in db.Set<WebhookDelivery>()
                      join e in db.Set<WebhookEndpoint>() on d.EndpointId equals e.Id
                      join o in db.Set<OutboxEvent>() on d.OutboxEventId equals o.Id
                      where (request.EndpointId == null || d.EndpointId == request.EndpointId)
                            && (all || (e.BranchId != null && branchIds.Contains(e.BranchId.Value)))
                      orderby d.AttemptedAt descending
                      select new WebhookDeliveryRow(d.AttemptedAt, e.Url, o.EventType, d.Attempt, d.StatusCode, d.Succeeded, d.Error, d.DurationMs))
            .Take(Math.Clamp(request.Take, 1, 1000)).ToListAsync(ct);
    }
}

/// <summary>
/// V4 · Los webhooks son de la empresa, pero una sesión (o llave) limitada a sucursales solo ve y administra los webhooks
/// de SUS sucursales: los de toda la empresa (sin sucursal) son de la gerencia global.
/// </summary>
internal static class Webhooks
{
    public static bool Visible(IMinvDbContext db, WebhookEndpoint endpoint) =>
        db.Branches.AllBranches || endpoint.BranchId is Guid b && db.Branches.Allows(b);

    public static async Task<WebhookEndpoint> FindAsync(IMinvDbContext db, Guid id, CancellationToken ct)
    {
        var endpoint = await db.Set<WebhookEndpoint>().FirstOrDefaultAsync(e => e.Id == id, ct);
        return endpoint is not null && Visible(db, endpoint) ? endpoint : throw new NotFoundException("El webhook no existe o no es de sus sucursales.");
    }
}

/// <summary>Catálogos para la pantalla de integraciones (alcances y eventos disponibles).</summary>
public sealed record IntegrationCatalog(IReadOnlyList<(string Code, string Description)> Scopes, IReadOnlyList<(string Code, string Description)> Events);

[RequiresPermission(PermissionCodes.IntegrationManage)]
public sealed record GetIntegrationCatalogQuery : IRequest<IntegrationCatalog>;

public sealed class GetIntegrationCatalogHandler : IRequestHandler<GetIntegrationCatalogQuery, IntegrationCatalog>
{
    public Task<IntegrationCatalog> Handle(GetIntegrationCatalogQuery request, CancellationToken ct) =>
        Task.FromResult(new IntegrationCatalog(ApiScopes.All.Select(s => (s.Code, s.Description)).ToList(), IntegrationEvents.All));
}
