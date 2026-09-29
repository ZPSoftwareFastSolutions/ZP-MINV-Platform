using System.Text.RegularExpressions;

namespace MINV.CloudServer;

/// <summary>
/// V7 · Configuración de la sesión web (<c>Minv:Web</c>, regla P-02): la web (tienda con cuentas de cliente y panel del
/// personal) entra al servidor en la nube por <c>/api/v1/web</c> con una cookie <c>HttpOnly</c>. Apagada por defecto: sin
/// <see cref="Enabled"/> las rutas no existen (404) y el servidor atiende solo al escritorio.
/// </summary>
public sealed partial class WebSettings
{
    public const string Section = "Minv:Web";

    public const string DefaultCookieName = "minv_session";

    /// <summary>Publica las rutas <c>/api/v1/web/*</c> (falso por defecto).</summary>
    public bool Enabled { get; set; }

    /// <summary>Código de la empresa de la web (p. ej. TECHZONE). La empresa NUNCA llega en el cuerpo de una petición.</summary>
    public string? TenantCode { get; set; }

    /// <summary>Sucursal de las cuentas de cliente; sin valor, la del almacén principal de la empresa.</summary>
    public string? BranchCode { get; set; }

    /// <summary>Registros de cuentas por hora y por IP (5).</summary>
    public int RegistrationsPerHour { get; set; } = 5;

    /// <summary>Nombre de la cookie de la sesión (<c>minv_session</c>).</summary>
    public string CookieName { get; set; } = DefaultCookieName;

    /// <summary>Con <see cref="Enabled"/> pero sin empresa, las rutas responden 503 (la sesión web no está configurada).</summary>
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(TenantCode);

    /// <summary>Empresa de la web en mayúsculas y sin espacios (como se guarda el código).</summary>
    public string Tenant => (TenantCode ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Registros por hora efectivos (de 1 a 1000).</summary>
    public int EffectiveRegistrationsPerHour => Math.Clamp(RegistrationsPerHour, 1, 1000);

    /// <summary>Deja la configuración lista para usarse: un nombre de cookie inválido no arranca el servidor (se enviaría
    /// una cabecera <c>Set-Cookie</c> mal formada).</summary>
    public WebSettings Validated()
    {
        CookieName = string.IsNullOrWhiteSpace(CookieName) ? DefaultCookieName : CookieName.Trim();
        if (!CookieNamePattern().IsMatch(CookieName))
        {
            throw new InvalidOperationException(
                $"{Section}:CookieName admite de 1 a 64 letras, números, guion y guion bajo (valor recibido: «{CookieName}»).");
        }
        return this;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex CookieNamePattern();
}
