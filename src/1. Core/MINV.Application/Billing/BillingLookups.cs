using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Iam;

namespace MINV.Application.Billing;

/// <summary>V4.1 · Configuración fiscal resuelta para una operación: ajustes, perfil del ambiente activo, conexión (con el
/// token descifrado SOLO en memoria) y zona horaria de la empresa.</summary>
public sealed record FiscalContext(SiatSettings Settings, SiatEnvironmentProfile Profile, SiatConnection Connection, TimeZoneInfo Zone)
{
    public int Environment => Settings.Environment;
}

/// <summary>
/// V4.1 · Búsquedas compartidas de la facturación: configuración, conexión, sucursal del Padrón, punto de venta de la
/// caja, CUIS/CUFD vigentes, hora fiscal, numeración, leyenda y bitácora. Se construye dentro de cada caso de uso (como
/// <c>InventoryLookups</c>); no guarda.
/// </summary>
public sealed class BillingLookups(IMinvDbContext db, ISecretProtector? protector, IClock clock)
{
    public IMinvDbContext Db => db;

    public IClock Clock => clock;

    // ------------------------------------------------------------------------------------------------ configuración
    public Task<SiatSettings?> SettingsAsync(CancellationToken ct) => db.Set<SiatSettings>().FirstOrDefaultAsync(ct);

    public async Task<SiatSettings> RequireSettingsAsync(CancellationToken ct) =>
        await SettingsAsync(ct) ?? throw new DomainException("siat.not_configured",
            "La facturación SIAT no está configurada: cargue el NIT, el código de sistema y el token en Configuración › Facturación SIAT.");

    /// <summary>¿Esta empresa emite documentos fiscales al vender? (configurada y activada).</summary>
    public async Task<bool> IsBillingEnabledAsync(CancellationToken ct) => (await SettingsAsync(ct))?.IsEnabled == true;

    public async Task<TimeZoneInfo> ZoneAsync(CancellationToken ct)
    {
        var zoneId = await db.Set<TenantConfig>().Select(c => c.TimeZoneId).FirstOrDefaultAsync(ct) ?? "America/La_Paz";
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("BOT", TimeSpan.FromHours(-4), "Bolivia", "Bolivia");
        }
    }

    public async Task<SiatEnvironmentProfile> ProfileAsync(int environment, CancellationToken ct) =>
        await db.Set<SiatEnvironmentProfile>().FirstOrDefaultAsync(p => p.Environment == environment, ct)
        ?? throw new DomainException("siat.no_profile",
            $"Falta la configuración de conexión del ambiente {environment} (URL de los servicios y token delegado).");

    /// <summary>Contexto completo con la conexión lista para llamar al SIN (descifra el token).</summary>
    public async Task<FiscalContext> ContextAsync(CancellationToken ct)
    {
        var settings = await RequireSettingsAsync(ct);
        var profile = await ProfileAsync(settings.Environment, ct);
        Guard.That(profile.HasToken, "siat.no_token", "Falta el token delegado del SIN para este ambiente (Portal SIAT › Token Delegado).");
        Guard.That(protector is not null, "siat.no_keys",
            "Este equipo no tiene la clave maestra de integraciones (MINV_INTEGRATION_KEYS): el envío al SIN lo hace el servidor.");
        string token;
        try
        {
            token = protector!.Unprotect(profile.TokenCiphertext!, profile.TokenKeyId!);
        }
        catch (Exception ex) when (ex is not DomainException)
        {
            throw new DomainException("siat.token_unreadable",
                "No se pudo descifrar el token del SIN con la clave maestra de este servidor: vuelva a cargarlo.");
        }
        var connection = new SiatConnection(settings.Environment, settings.Nit, settings.SystemCode, token, profile.Endpoints,
            TimeSpan.FromSeconds(profile.TimeoutSeconds));
        return new FiscalContext(settings, profile, connection, await ZoneAsync(ct));
    }

    /// <summary>Hora fiscal ahora: reloj del servidor corregido con el del SIN, en la zona de la empresa, sin zona.</summary>
    public DateTime FiscalNow(SiatSettings settings, TimeZoneInfo zone) =>
        FiscalRules.ToFiscalTime(settings.SiatNow(clock.UtcNow), zone);

    public DateTime FiscalNow(FiscalContext context) => FiscalNow(context.Settings, context.Zone);

    // ------------------------------------------------------------------------------------------------ sucursal y punto de venta
    public async Task<SiatBranch> BranchMappingAsync(Guid branchId, CancellationToken ct) =>
        await db.Set<SiatBranch>().FirstOrDefaultAsync(b => b.BranchId == branchId, ct)
        ?? throw new DomainException("siat.branch_unmapped",
            "La sucursal no tiene su código del Padrón (Configuración › Facturación SIAT › Sucursales).");

    /// <summary>Punto de venta con el que factura una caja: el vinculado a la caja o, si no hay, el punto 0 de la sucursal.</summary>
    public async Task<SiatPointOfSale> PointOfSaleForAsync(Guid branchId, Guid? posRegisterId, int environment, CancellationToken ct)
    {
        var points = db.Set<SiatPointOfSale>().Where(p => p.BranchId == branchId && p.Environment == environment && p.ClosedAt == null);
        if (posRegisterId is { } registerId && await points.FirstOrDefaultAsync(p => p.PosRegisterId == registerId, ct) is { } linked)
        {
            return linked;
        }
        return await points.FirstOrDefaultAsync(p => p.Code == 0, ct)
               ?? throw new DomainException("siat.no_point_of_sale",
                   "La sucursal no tiene punto de venta del SIN: regístrelo en Facturación › Estado SIAT.");
    }

    public Task<SiatCuis?> CurrentCuisAsync(Guid pointOfSaleId, DateTimeOffset now, CancellationToken ct) =>
        db.Set<SiatCuis>().Where(c => c.PointOfSaleId == pointOfSaleId && c.ValidUntil > now)
            .OrderByDescending(c => c.ObtainedAt).FirstOrDefaultAsync(ct);

    public Task<SiatCufd?> CurrentCufdAsync(Guid pointOfSaleId, DateTimeOffset now, CancellationToken ct) =>
        db.Set<SiatCufd>().Where(c => c.PointOfSaleId == pointOfSaleId && c.ValidUntil > now)
            .OrderByDescending(c => c.ObtainedAt).FirstOrDefaultAsync(ct);

    /// <summary>Último CUFD obtenido (vigente o no): el que se usa fuera de línea hasta 72 h.</summary>
    public Task<SiatCufd?> LatestCufdAsync(Guid pointOfSaleId, CancellationToken ct) =>
        db.Set<SiatCufd>().Where(c => c.PointOfSaleId == pointOfSaleId).OrderByDescending(c => c.ObtainedAt).FirstOrDefaultAsync(ct);

    public async Task<SiatCuis> RequireCuisAsync(Guid pointOfSaleId, DateTimeOffset now, CancellationToken ct) =>
        await CurrentCuisAsync(pointOfSaleId, now, ct) ?? throw new DomainException("siat.no_cuis",
            "El punto de venta no tiene CUIS vigente: solicítelo en Facturación › Estado SIAT.");

    /// <summary>CUIS y CUFD vigentes para una llamada de facturación.</summary>
    public async Task<(SiatCodesForCall Codes, SiatCuis Cuis, SiatCufd Cufd)> CallCodesAsync(Guid pointOfSaleId, DateTimeOffset now,
        CancellationToken ct)
    {
        var cuis = await RequireCuisAsync(pointOfSaleId, now, ct);
        var cufd = await CurrentCufdAsync(pointOfSaleId, now, ct) ?? throw new DomainException("siat.no_cufd",
            "El punto de venta no tiene CUFD vigente: solicítelo en Facturación › Estado SIAT.");
        return (new SiatCodesForCall(cuis.Code, cufd.Code), cuis, cufd);
    }

    public static SiatPlace Place(SiatBranch branch, SiatPointOfSale pointOfSale) =>
        new(branch.SiatCode, pointOfSale.Code, pointOfSale.BranchId);

    /// <summary>Lugar de un documento ya emitido: los códigos salen de su CUF (así una edición posterior del mapeo no
    /// cambia a qué sucursal y punto de venta pertenece).</summary>
    public static SiatPlace PlaceOf(FiscalDocument document) =>
        Cuf.DecodeFull(document.Cuf) is { } parts
            ? new SiatPlace(parts.BranchCode, parts.PointOfSaleCode, document.BranchId)
            : throw new DomainException("fiscal.cuf_invalid", "El CUF del documento no es válido.");

    public async Task<FiscalXmlContext> XmlContextAsync(SiatSettings settings, SiatBranch branch, SiatPointOfSale pointOfSale, SiatCufd cufd,
        CancellationToken ct)
    {
        await Task.CompletedTask;
        return new FiscalXmlContext(settings.Nit, settings.BusinessName, branch.Municipality, branch.Phone, branch.SiatCode, pointOfSale.Code,
            cufd.Code, cufd.Address);
    }

    // ------------------------------------------------------------------------------------------------ numeración y leyenda
    /// <summary>Número siguiente por (ambiente, punto de venta, documento sector). Si dos cajas numeran a la vez, el índice
    /// único rechaza el duplicado y el caso de uso reintenta (regla B-07).</summary>
    public async Task<long> NextNumberAsync(int environment, Guid pointOfSaleId, int documentSector, CancellationToken ct)
    {
        var fromDb = await db.Set<FiscalDocument>()
            .Where(d => d.Environment == environment && d.PointOfSaleId == pointOfSaleId && d.DocumentSector == documentSector)
            .MaxAsync(d => (long?)d.Number, ct) ?? 0;
        var fromLocal = db.Set<FiscalDocument>().Local
            .Where(d => d.Environment == environment && d.PointOfSaleId == pointOfSaleId && d.DocumentSector == documentSector)
            .Select(d => d.Number).DefaultIfEmpty(0).Max();
        return Math.Max(fromDb, fromLocal) + 1;
    }

    /// <summary>Leyenda de la Ley 453 al azar entre las de la actividad (cambia en cada emisión, regla de la inspección).</summary>
    public async Task<string> PickLegendAsync(string activityCode, CancellationToken ct)
    {
        var legends = await db.Set<SiatLegend>().Where(l => l.ActivityCode == activityCode && l.IsCurrent).Select(l => l.Text).ToListAsync(ct);
        if (legends.Count == 0)
        {
            legends = await db.Set<SiatLegend>().Where(l => l.IsCurrent).Select(l => l.Text).Take(50).ToListAsync(ct);
        }
        return legends.Count > 0
            ? legends[Random.Shared.Next(legends.Count)]
            : "Ley N° 453: Tienes derecho a recibir información sobre las características y contenidos de los servicios que utilices.";
    }

    /// <summary>Campo «usuario» del XML: descriptivo (parte local del correo en mayúsculas, p. ej. MSUAREZ).</summary>
    public static string UserCode(ICurrentUser user)
    {
        var email = user.Email ?? user.DisplayName ?? "SISTEMA";
        var local = email.Split('@')[0];
        var code = new string(local.Where(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-').ToArray()).ToUpperInvariant();
        return code.Length == 0 ? "SISTEMA" : code.Length > 100 ? code[..100] : code;
    }

    /// <summary>Descripción de un código de catálogo (o el código si no está sincronizado).</summary>
    public async Task<string> CatalogDescriptionAsync(string catalog, int code, CancellationToken ct) =>
        await db.Set<SiatCatalogItem>().Where(i => i.Catalog == catalog && i.Code == code).Select(i => i.Description).FirstOrDefaultAsync(ct)
        ?? code.ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------------------------------------ bitácora
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>Evento de la bitácora de un documento con la respuesta del SIN (mensajes en JSON).</summary>
    public static FiscalDocumentEvent Log(FiscalDocument document, FiscalDocumentAction action, DateTimeOffset now, Guid? userId,
        SiatReply? reply = null, string? description = null) =>
        new(document.TenantId, document.BranchId, document.Id, action, now, reply?.StatusCode ?? reply?.Messages.FirstOrDefault()?.Code,
            description ?? reply?.Describe(), reply?.ReceptionCode,
            reply is { Messages.Count: > 0 } ? JsonSerializer.Serialize(reply.Messages, JsonOptions) : null, userId);
}
