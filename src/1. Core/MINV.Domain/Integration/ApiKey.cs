using MINV.Domain.Common;

namespace MINV.Domain.Integration;

/// <summary>
/// V4 · Llave de acceso al API Gateway B2B (e-commerce, ERP). Solo se guarda el prefijo público (para encontrarla) y el
/// hash SHA-256 del token completo: el token se muestra una sola vez al crearla. Actúa en nombre de un usuario dueño
/// (sus permisos, recortados por los <see cref="ApiKeyScope"/>) y puede quedar limitada a una sucursal.
/// </summary>
public sealed class ApiKey : Entity, IConcurrencyAware, IAggregateRoot
{
    public const int PrefixLength = 8;

    private readonly List<ApiKeyScope> _scopes = new();

    private ApiKey()
    {
    }

    public ApiKey(Guid tenantId, string name, string prefix, string tokenHash, Guid ownerUserId, Guid? branchId, DateTimeOffset createdAt,
        DateTimeOffset? expiresAt, IEnumerable<string> scopes)
        : base(tenantId)
    {
        Name = Guard.Text(name, "El nombre de la llave", 100);
        Guard.That(prefix is { Length: PrefixLength } && prefix.All(char.IsAsciiLetterOrDigit), "api_key.prefix",
            "El prefijo de la llave debe tener 8 caracteres alfanuméricos.");
        Prefix = prefix.ToLowerInvariant();
        Guard.That(tokenHash is { Length: 64 } && tokenHash.All(char.IsAsciiHexDigit), "api_key.hash",
            "El hash de la llave debe ser SHA-256 en hexadecimal.");
        TokenHash = tokenHash.ToLowerInvariant();
        OwnerUserId = Guard.NotEmpty(ownerUserId, nameof(ownerUserId));
        BranchId = Guard.NotEmptyIfPresent(branchId, nameof(branchId));
        CreatedAt = createdAt.ToUniversalTime();
        Guard.That(expiresAt is null || expiresAt > createdAt, "api_key.expiry", "El vencimiento debe ser posterior a la creación.");
        ExpiresAt = expiresAt?.ToUniversalTime();
        foreach (var scope in scopes.Select(s => s.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal))
        {
            Guard.That(ApiScopes.All.Any(a => a.Code == scope), "api_key.scope", $"El alcance «{scope}» no existe.");
            _scopes.Add(new ApiKeyScope(tenantId, Id, scope));
        }
        Guard.That(_scopes.Count > 0, "api_key.scopes", "Elija al menos un alcance para la llave.");
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Parte pública del token (<c>minv_&lt;prefijo&gt;_&lt;secreto&gt;</c>): única en toda la plataforma.</summary>
    public string Prefix { get; private set; } = string.Empty;

    /// <summary>SHA-256 (hex) del token completo. El token tiene 256 bits de azar: no necesita sal ni PBKDF2.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>Usuario en cuyo nombre actúa la llave (auditoría y permisos).</summary>
    public Guid OwnerUserId { get; private set; }

    /// <summary>Si se indica, la llave solo ve y opera esta sucursal.</summary>
    public Guid? BranchId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<ApiKeyScope> Scopes => _scopes;

    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);

    /// <summary>Revocación definitiva (no se borra: queda para la auditoría).</summary>
    public void Revoke(DateTimeOffset now)
    {
        Guard.That(RevokedAt is null, "api_key.revoked", "La llave ya estaba revocada.");
        RevokedAt = now.ToUniversalTime();
    }
}

/// <summary>Alcance otorgado a una llave (5FN: una fila por alcance).</summary>
public sealed class ApiKeyScope : BaseEntity
{
    private ApiKeyScope()
    {
    }

    internal ApiKeyScope(Guid tenantId, Guid apiKeyId, string scope)
        : base(tenantId)
    {
        ApiKeyId = Guard.NotEmpty(apiKeyId, nameof(apiKeyId));
        Scope = Guard.Text(scope, "El alcance", 40);
    }

    public Guid ApiKeyId { get; private set; }

    public string Scope { get; private set; } = string.Empty;
}

/// <summary>Alcances del API Gateway y el permiso de la aplicación que habilita cada uno.</summary>
public static class ApiScopes
{
    public const string CatalogRead = "catalog:read";
    public const string StockRead = "stock:read";
    public const string OrdersWrite = "orders:write";
    public const string TransfersRead = "transfers:read";
    public const string TransfersWrite = "transfers:write";
    public const string WebhooksManage = "webhooks:manage";
    public const string ReportsRead = "reports:read";

    /// <summary>Código, descripción y permisos que concede (se intersectan con los del dueño de la llave).</summary>
    public static readonly IReadOnlyList<(string Code, string Description, string[] Permissions)> All =
    [
        (CatalogRead, "Leer productos, precios, códigos de barras y sucursales", [Iam.PermissionCodes.StockView]),
        (StockRead, "Leer existencias por sucursal y consolidadas", [Iam.PermissionCodes.StockView]),
        (OrdersWrite, "Registrar pedidos de e-commerce como ventas", [Iam.PermissionCodes.PosOperate, Iam.PermissionCodes.MovementsRegisterSales, Iam.PermissionCodes.SalesView]),
        (TransfersRead, "Consultar transferencias entre sucursales", [Iam.PermissionCodes.StockView]),
        (TransfersWrite, "Crear, despachar y recibir transferencias", [Iam.PermissionCodes.TransfersManage, Iam.PermissionCodes.StockView]),
        (WebhooksManage, "Registrar y desactivar webhooks", [Iam.PermissionCodes.IntegrationManage]),
        (ReportsRead, "Leer reportes gerenciales por sucursal", [Iam.PermissionCodes.ReportsView]),
    ];

    /// <summary>Permisos efectivos: los del dueño recortados por los alcances de la llave.</summary>
    public static IReadOnlyList<string> EffectivePermissions(IEnumerable<string> scopes, IEnumerable<string> ownerPermissions)
    {
        var owner = ownerPermissions.ToHashSet(StringComparer.Ordinal);
        var granted = All.Where(a => scopes.Contains(a.Code, StringComparer.Ordinal)).SelectMany(a => a.Permissions);
        return granted.Where(owner.Contains).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }
}
