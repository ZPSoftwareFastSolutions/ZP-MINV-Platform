using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using MINV.Infrastructure.Integration;

namespace MINV.ApiGateway.Security;

/// <summary>
/// V4 · Esquema de autenticación «ApiKey»: lee la llave de <c>Authorization: Bearer minv_…</c> o de <c>X-Api-Key</c>, la
/// valida con <see cref="ApiKeyAuthenticator"/> (que además deja listo el contexto de la petición: empresa, dueño con
/// permisos recortados, sucursales y canal api) y emite un principal con un claim <c>scope</c> por alcance. Una llave
/// inválida, vencida o revocada produce 401 sin detalle (no se revela si el prefijo existe).
/// </summary>
public sealed class ApiKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string ScopeClaim = "scope";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(token))
        {
            var header = Request.Headers.Authorization.ToString();
            token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : string.Empty;
        }
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }
        var principal = await Context.RequestServices.GetRequiredService<ApiKeyAuthenticator>().AuthenticateAsync(token, Context.RequestAborted);
        if (principal is null)
        {
            return AuthenticateResult.Fail("API Key inválida, vencida o revocada.");
        }
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, principal.OwnerUserId.ToString()),
            new(ClaimTypes.Name, principal.KeyName),
            new("api_key_id", principal.ApiKeyId.ToString()),
            new("api_key_prefix", principal.Prefix),
            new("tenant_id", principal.TenantId.ToString()),
        };
        claims.AddRange(principal.Scopes.Select(s => new Claim(ScopeClaim, s)));
        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer realm=\"M-INV\", error=\"invalid_token\"";
        return Response.WriteAsJsonAsync(new
        {
            type = "https://httpstatuses.io/401",
            title = "No autenticado",
            status = 401,
            detail = "Envíe una API Key válida en Authorization: Bearer minv_… o en X-Api-Key.",
        });
    }
}
