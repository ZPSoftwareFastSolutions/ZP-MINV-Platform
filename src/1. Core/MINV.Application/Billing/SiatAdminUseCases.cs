using System.Globalization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Application.Billing;

// =====================================================================================================================
// V4.1 · Administración de la facturación SIAT: configuración (empresa, conexión por ambiente, sucursales del Padrón,
// correo), puntos de venta, CUIS, CUFD, preparación diaria, sincronización de catálogos, verificación de comunicación y
// de NIT. Son casos de uso cuyo PROPÓSITO es la llamada al SIN (regla F-03): llaman por ISiatGateway y guardan la
// respuesta. Sin comunicación → DomainException «siat.unavailable» (el rechazo del token, «siat.token_rejected», pasa tal cual).
// =====================================================================================================================

/// <summary>V4.1 · Búsquedas y mensajes comunes de la administración SIAT.</summary>
internal static class SiatAdminSupport
{
    /// <summary>Nombre del punto 0 («no corresponde»: la sucursal misma, sin punto de venta del SIN).</summary>
    public const string PointZeroName = "Sin punto de venta";

    public static string EnvironmentName(int environment) =>
        environment == SiatCodes.EnvironmentProduction ? "producción" : "pruebas y piloto";

    public static DomainException Unavailable(SiatUnavailableException ex) =>
        new("siat.unavailable", "No hay comunicación con el SIN: " + ex.Message);

    public static DomainException NoKeys() =>
        new("siat.no_keys", "Este equipo no tiene la clave maestra de integraciones (MINV_INTEGRATION_KEYS) para cifrar secretos: " +
                            "cargue el token y la contraseña del correo desde el servidor en la nube (o configure la clave en este equipo).");

    public static string Local(DateTimeOffset instant, TimeZoneInfo zone, string format = "dd/MM/yyyy HH:mm") =>
        TimeZoneInfo.ConvertTime(instant, zone).ToString(format, CultureInfo.InvariantCulture);

    public static async Task<Branch> BranchAsync(IMinvDbContext db, string branchCode, CancellationToken ct)
    {
        var code = (branchCode ?? string.Empty).Trim().ToUpperInvariant();
        return await db.Set<Branch>().FirstOrDefaultAsync(b => b.Code == code, ct)
               ?? throw new NotFoundException($"La sucursal {code} no existe.");
    }

    public static async Task<SiatPointOfSale> PointAsync(IMinvDbContext db, Guid pointOfSaleId, CancellationToken ct) =>
        await db.Set<SiatPointOfSale>().FirstOrDefaultAsync(p => p.Id == pointOfSaleId, ct)
        ?? throw new NotFoundException("El punto de venta del SIN no existe (o es de una sucursal que no es suya).");

    /// <summary>Punto 0 de la sucursal en el ambiente (lo crea, sin guardar, si falta).</summary>
    public static async Task<SiatPointOfSale> PointZeroAsync(IMinvDbContext db, Guid tenantId, Guid branchId, int environment, DateTimeOffset now,
        CancellationToken ct)
    {
        var zero = db.Set<SiatPointOfSale>().Local.FirstOrDefault(p => p.BranchId == branchId && p.Environment == environment && p.Code == 0
                                                                       && p.ClosedAt == null)
                   ?? await db.Set<SiatPointOfSale>().FirstOrDefaultAsync(p => p.BranchId == branchId && p.Environment == environment && p.Code == 0
                                                                               && p.ClosedAt == null, ct);
        if (zero is null)
        {
            zero = new SiatPointOfSale(tenantId, branchId, environment, 0, 0, PointZeroName, null, null, now);
            db.Set<SiatPointOfSale>().Add(zero);
        }
        return zero;
    }

    /// <summary>Caja de M-INV de la sucursal por su código (null si no se indica).</summary>
    public static async Task<Guid?> RegisterIdAsync(IMinvDbContext db, Guid branchId, string? registerCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(registerCode))
        {
            return null;
        }
        var code = registerCode.Trim().ToUpperInvariant();
        return await db.Set<PosRegister>().Where(r => r.BranchId == branchId && r.Code == code).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct)
               ?? throw new NotFoundException($"La caja {code} no existe en esa sucursal.");
    }

    /// <summary>Una caja factura con UN punto de venta por ambiente (índice único): se desvincula de los demás y se guarda
    /// antes de vincularla al nuevo, para que la base no vea dos puntos con la misma caja.</summary>
    public static async Task UnlinkRegisterAsync(IMinvDbContext db, Guid registerId, int environment, Guid? keepPointId, CancellationToken ct)
    {
        var linked = await db.Set<SiatPointOfSale>()
            .Where(p => p.PosRegisterId == registerId && p.Environment == environment && p.Id != keepPointId).ToListAsync(ct);
        foreach (var point in linked)
        {
            point.LinkRegister(null);
        }
        if (linked.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>El punto de venta es del ambiente activo (nada del ambiente de pruebas se usa en producción, regla F-13).</summary>
    public static void EnsureEnvironment(SiatPointOfSale point, FiscalContext context) =>
        Guard.That(point.Environment == context.Environment, "siat.environment_mismatch",
            $"El punto de venta es del ambiente {point.Environment} y la facturación está en el ambiente {context.Environment} " +
            $"({EnvironmentName(context.Environment)}).");

    public static string Protect(ISecretProtector? protector, string secret, out string keyId)
    {
        if (protector is null)
        {
            throw NoKeys();
        }
        try
        {
            var ciphertext = protector.Protect(secret);
            keyId = protector.CurrentKeyId;
            return ciphertext;
        }
        catch (Exception ex) when (ex is not DomainException and not OperationCanceledException)
        {
            throw NoKeys();
        }
    }
}

// --------------------------------------------------------------------------------------------------- configuración
public sealed class GetSiatSettingsHandler(IMinvDbContext db, ILicenseService licenses) : IRequestHandler<GetSiatSettingsQuery, SiatSettingsView>
{
    public async Task<SiatSettingsView> Handle(GetSiatSettingsQuery request, CancellationToken ct)
    {
        var settings = await db.Set<SiatSettings>().AsNoTracking().FirstOrDefaultAsync(ct);
        var profiles = await db.Set<SiatEnvironmentProfile>().AsNoTracking().OrderBy(p => p.Environment).ToListAsync(ct);
        var mappings = await db.Set<SiatBranch>().AsNoTracking().ToDictionaryAsync(b => b.BranchId, ct);
        var branches = await db.Set<Branch>().AsNoTracking().OrderBy(b => b.Code).ToListAsync(ct);
        var mail = await db.Set<MailSettings>().AsNoTracking().FirstOrDefaultAsync(ct);
        return new SiatSettingsView(settings is not null, settings?.Nit, settings?.BusinessName, settings?.SystemCode,
            settings?.Environment ?? SiatCodes.EnvironmentTest, settings?.IsEnabled == true, settings?.OnlineLegend ?? SiatSettings.DefaultOnlineLegend,
            settings?.OfflineLegend ?? SiatSettings.DefaultOfflineLegend, settings?.ClockSyncedAt, settings?.ClockOffsetMs ?? 0,
            profiles.Select(p => new SiatProfileView(p.Environment, p.Endpoints, p.QrBaseUrl, p.TimeoutSeconds, p.HasToken, p.TokenValidUntil,
                p.TokenUpdatedAt)).ToList(),
            branches.Select(b => mappings.TryGetValue(b.Id, out var m)
                ? new SiatBranchView(b.Id, b.Code, b.Name, m.SiatCode, m.Municipality, m.Phone)
                : new SiatBranchView(b.Id, b.Code, b.Name, null, null, null)).ToList(),
            mail is null ? null : new MailSettingsView(mail.Host, mail.Port, mail.UseSsl, mail.UserName, mail.PasswordCiphertext is not null,
                mail.FromAddress, mail.FromName, mail.IsEnabled),
            await licenses.IsModuleActiveAsync(LicenseModuleCodes.FiscalSiat, ct));
    }
}

public sealed class SaveSiatSettingsValidator : AbstractValidator<SaveSiatSettingsCommand>
{
    public SaveSiatSettingsValidator()
    {
        RuleFor(x => x.Nit).InclusiveBetween(1, 9_999_999_999_999).WithMessage("El NIT tiene entre 1 y 13 dígitos.");
        RuleFor(x => x.BusinessName).NotEmpty().WithMessage("Indique la razón social tal como figura en el Padrón.").MaximumLength(200);
        RuleFor(x => x.SystemCode).NotEmpty().WithMessage("Indique el código de sistema que asignó el SIN.").MaximumLength(50);
        RuleFor(x => x.Environment).InclusiveBetween(1, 2).WithMessage("El ambiente es 1 (producción) o 2 (pruebas y piloto).");
    }
}

public sealed class SaveSiatSettingsHandler(IMinvDbContext db, ITenantContext tenant) : IRequestHandler<SaveSiatSettingsCommand, string>
{
    public async Task<string> Handle(SaveSiatSettingsCommand r, CancellationToken ct)
    {
        var online = string.IsNullOrWhiteSpace(r.OnlineLegend) ? SiatSettings.DefaultOnlineLegend : r.OnlineLegend.Trim();
        var offline = string.IsNullOrWhiteSpace(r.OfflineLegend) ? SiatSettings.DefaultOfflineLegend : r.OfflineLegend.Trim();
        var settings = await db.Set<SiatSettings>().FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new SiatSettings(tenant.TenantId, r.Nit, r.BusinessName.Trim(), r.SystemCode.Trim(), r.Environment);
            db.Set<SiatSettings>().Add(settings);
        }
        settings.Update(r.Nit, r.BusinessName.Trim(), r.SystemCode.Trim(), r.Environment, online, offline);
        if (r.Enabled)
        {
            var profile = await db.Set<SiatEnvironmentProfile>().FirstOrDefaultAsync(p => p.Environment == r.Environment, ct);
            Guard.That(profile is { HasToken: true }, "siat.no_token",
                $"Para activar la facturación falta la conexión del ambiente {r.Environment} con su token delegado (Portal SIAT › Token Delegado).");
            Guard.That(await db.Set<SiatBranch>().AnyAsync(b => b.SiatCode == 0, ct), "siat.no_head_office",
                "Para activar la facturación falta mapear la casa matriz: una sucursal con el código 0 del Padrón.");
            settings.Enable();
        }
        else
        {
            settings.Disable();
        }
        await db.SaveChangesAsync(ct);
        return $"✔ Facturación SIAT {(settings.IsEnabled ? "ACTIVADA" : "guardada (desactivada: las ventas salen sin documento fiscal)")} · " +
               $"NIT {settings.Nit} · ambiente {settings.Environment} ({SiatAdminSupport.EnvironmentName(settings.Environment)}).";
    }
}

public sealed class SaveSiatProfileValidator : AbstractValidator<SaveSiatProfileCommand>
{
    public SaveSiatProfileValidator()
    {
        RuleFor(x => x.Environment).InclusiveBetween(1, 2).WithMessage("El ambiente es 1 (producción) o 2 (pruebas y piloto).");
        RuleFor(x => x.Endpoints).NotNull().WithMessage("Indique las URL de los servicios del SIN.");
        RuleFor(x => x.QrBaseUrl).NotEmpty().WithMessage("Indique la URL base de la consulta por QR.");
        RuleFor(x => x.TimeoutSeconds).InclusiveBetween(3, 120).WithMessage("El tiempo de espera va de 3 a 120 segundos.");
    }
}

public sealed class SaveSiatProfileHandler(IMinvDbContext db, ITenantContext tenant, IClock clock, ISecretProtector? protector = null)
    : IRequestHandler<SaveSiatProfileCommand, string>
{
    public async Task<string> Handle(SaveSiatProfileCommand r, CancellationToken ct)
    {
        var profile = await db.Set<SiatEnvironmentProfile>().FirstOrDefaultAsync(p => p.Environment == r.Environment, ct);
        if (profile is null)
        {
            profile = new SiatEnvironmentProfile(tenant.TenantId, r.Environment, r.Endpoints, r.QrBaseUrl.Trim(), r.TimeoutSeconds);
            db.Set<SiatEnvironmentProfile>().Add(profile);
        }
        else
        {
            profile.UpdateEndpoints(r.Endpoints, r.QrBaseUrl.Trim(), r.TimeoutSeconds);
        }
        var tokenChanged = r.NewToken is not null;
        if (r.NewToken is not null)
        {
            // El token se cifra aquí y NUNCA se devuelve, se audita ni se registra (regla F-12)
            var token = Guard.Text(r.NewToken.Trim(), "El token delegado", 8000, 10);
            var ciphertext = SiatAdminSupport.Protect(protector, token, out var keyId);
            profile.SetToken(ciphertext, keyId, r.TokenValidUntil, clock.UtcNow);
        }
        else if (profile.HasToken && r.TokenValidUntil != profile.TokenValidUntil)
        {
            profile.SetToken(profile.TokenCiphertext!, profile.TokenKeyId!, r.TokenValidUntil, profile.TokenUpdatedAt ?? clock.UtcNow);
        }
        await db.SaveChangesAsync(ct);
        return $"✔ Conexión del ambiente {r.Environment} ({SiatAdminSupport.EnvironmentName(r.Environment)}) guardada" +
               (tokenChanged ? " con el token delegado nuevo (cifrado)" : profile.HasToken ? " (se conserva el token guardado)" : " (todavía sin token)") +
               (profile.TokenValidUntil is { } until ? $" · token vigente hasta {until:dd/MM/yyyy}." : ".");
    }
}

public sealed class SaveSiatBranchValidator : AbstractValidator<SaveSiatBranchCommand>
{
    public SaveSiatBranchValidator()
    {
        RuleFor(x => x.BranchCode).NotEmpty().WithMessage("Elija la sucursal de M-INV.");
        RuleFor(x => x.SiatCode).InclusiveBetween(0, 9999).WithMessage("El código de sucursal del Padrón va de 0 (casa matriz) a 9999.");
        RuleFor(x => x.Municipality).NotEmpty().WithMessage("Indique el municipio que va en la factura.").MaximumLength(25);
        RuleFor(x => x.Phone).MaximumLength(25);
    }
}

public sealed class SaveSiatBranchHandler(IMinvDbContext db, ITenantContext tenant) : IRequestHandler<SaveSiatBranchCommand, string>
{
    public async Task<string> Handle(SaveSiatBranchCommand r, CancellationToken ct)
    {
        var branch = await SiatAdminSupport.BranchAsync(db, r.BranchCode, ct);
        Guard.That(!await db.Set<SiatBranch>().AnyAsync(b => b.SiatCode == r.SiatCode && b.BranchId != branch.Id, ct), "siat.branch_code_taken",
            $"El código {r.SiatCode} del Padrón ya está asignado a otra sucursal: cada sucursal tiene su propio código.");
        var mapping = await db.Set<SiatBranch>().FirstOrDefaultAsync(b => b.BranchId == branch.Id, ct);
        if (mapping is null)
        {
            db.Set<SiatBranch>().Add(new SiatBranch(tenant.TenantId, branch.Id, r.SiatCode, r.Municipality.Trim(), r.Phone?.Trim()));
        }
        else
        {
            // Los puntos de venta quedaron registrados en el SIN bajo el código anterior: no se «mueven» de sucursal
            Guard.That(mapping.SiatCode == r.SiatCode || !await db.Set<SiatPointOfSale>().AnyAsync(p => p.BranchId == branch.Id && p.Code > 0
                                                                                                       && p.ClosedAt == null, ct),
                "siat.branch_code_locked",
                $"La sucursal {branch.Code} ya tiene puntos de venta registrados en el SIN con el código {mapping.SiatCode}: ciérrelos antes de cambiarlo.");
            mapping.Update(r.SiatCode, r.Municipality.Trim(), r.Phone?.Trim());
        }
        await db.SaveChangesAsync(ct);
        return $"✔ Sucursal {branch.Code} · {(r.SiatCode == 0 ? "casa matriz (código 0)" : $"sucursal {r.SiatCode}")} del Padrón · {r.Municipality.Trim()}.";
    }
}

public sealed class SaveMailSettingsValidator : AbstractValidator<SaveMailSettingsCommand>
{
    public SaveMailSettingsValidator()
    {
        RuleFor(x => x.Host).NotEmpty().WithMessage("Indique el servidor SMTP.").MaximumLength(200);
        RuleFor(x => x.Port).InclusiveBetween(1, 65535).WithMessage("El puerto va de 1 a 65535.");
        RuleFor(x => x.FromAddress).NotEmpty().WithMessage("Indique el correo remitente.");
        RuleFor(x => x.FromName).NotEmpty().WithMessage("Indique el nombre del remitente.").MaximumLength(100);
    }
}

public sealed class SaveMailSettingsHandler(IMinvDbContext db, ITenantContext tenant, ISecretProtector? protector = null)
    : IRequestHandler<SaveMailSettingsCommand, string>
{
    public async Task<string> Handle(SaveMailSettingsCommand r, CancellationToken ct)
    {
        var mail = await db.Set<MailSettings>().FirstOrDefaultAsync(ct);
        if (mail is null)
        {
            mail = new MailSettings(tenant.TenantId, r.Host.Trim(), r.Port, r.UseSsl, r.UserName?.Trim(), r.FromAddress.Trim(), r.FromName.Trim());
            db.Set<MailSettings>().Add(mail);
        }
        else
        {
            mail.Update(r.Host.Trim(), r.Port, r.UseSsl, r.UserName?.Trim(), r.FromAddress.Trim(), r.FromName.Trim());
        }
        if (!string.IsNullOrEmpty(r.NewPassword))
        {
            var ciphertext = SiatAdminSupport.Protect(protector, r.NewPassword, out var keyId);
            mail.SetPassword(ciphertext, keyId);
        }
        if (r.Enabled)
        {
            mail.Enable();
        }
        else
        {
            mail.Disable();
        }
        await db.SaveChangesAsync(ct);
        return $"✔ Correo de la empresa guardado ({mail.Host}:{mail.Port}{(mail.UseSsl ? ", TLS" : string.Empty)})" +
               (mail.IsEnabled ? ": los documentos fiscales se envían a los compradores." : ": el envío por correo está desactivado.");
    }
}

// --------------------------------------------------------------------------------------------------- puntos de venta
public sealed class RegisterSiatPointOfSaleValidator : AbstractValidator<RegisterSiatPointOfSaleCommand>
{
    public RegisterSiatPointOfSaleValidator()
    {
        RuleFor(x => x.BranchCode).NotEmpty().WithMessage("Elija la sucursal.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("El nombre del punto de venta es obligatorio (el SIN lo rechaza vacío: 948).").MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(200);
        RuleFor(x => x.TypeCode).InclusiveBetween(1, 99).WithMessage("Elija el tipo de punto de venta (5 = cajeros).");
    }
}

public sealed class RegisterSiatPointOfSaleHandler(IMinvDbContext db, ITenantContext tenant, IClock clock, ISiatGateway gateway,
    ISecretProtector? protector = null) : IRequestHandler<RegisterSiatPointOfSaleCommand, SiatPointOfSaleStatus>
{
    public async Task<SiatPointOfSaleStatus> Handle(RegisterSiatPointOfSaleCommand r, CancellationToken ct)
    {
        var lookups = new BillingLookups(db, protector, clock);
        var context = await lookups.ContextAsync(ct);
        var codes = new SiatCodeManager(lookups, gateway);
        var branch = await SiatAdminSupport.BranchAsync(db, r.BranchCode, ct);
        var mapping = await lookups.BranchMappingAsync(branch.Id, ct);
        var registerId = await SiatAdminSupport.RegisterIdAsync(db, branch.Id, r.RegisterCode, ct);
        var now = clock.UtcNow;
        SiatPointOfSale point;
        try
        {
            // registroPuntoVenta va con el CUIS de la sucursal SIN punto de venta (punto 0), que se crea si falta
            var zero = await SiatAdminSupport.PointZeroAsync(db, tenant.TenantId, branch.Id, context.Environment, now, ct);
            var zeroCuis = await codes.EnsureCuisAsync(context, mapping, zero, false, ct);
            var name = r.Name.Trim();
            var reply = await gateway.RegisterPointOfSaleAsync(context.Connection, new SiatPlace(mapping.SiatCode, 0, branch.Id), zeroCuis.Code, r.TypeCode,
                name, string.IsNullOrWhiteSpace(r.Description) ? name : r.Description.Trim(), ct);
            zero.RecordContact(now);
            if (reply.Code is not { } code || code <= 0)
            {
                await db.SaveChangesAsync(ct);   // el CUIS del punto 0 ya es un hecho del SIN: se conserva
                throw new DomainException("siat.pos_rejected", "El SIN no registró el punto de venta: " + SiatCodeManager.Describe(reply.Messages));
            }
            Guard.That(!await db.Set<SiatPointOfSale>().AnyAsync(p => p.BranchId == branch.Id && p.Environment == context.Environment && p.Code == code, ct),
                "siat.pos_duplicate", $"El SIN devolvió el punto de venta {code}, que ya existe en M-INV para esta sucursal.");
            if (registerId is { } id)
            {
                await SiatAdminSupport.UnlinkRegisterAsync(db, id, context.Environment, null, ct);
            }
            point = new SiatPointOfSale(tenant.TenantId, branch.Id, context.Environment, code, r.TypeCode, name, r.Description?.Trim(), registerId, now);
            db.Set<SiatPointOfSale>().Add(point);
            await db.SaveChangesAsync(ct);   // el código que asignó el SIN no se puede perder aunque falle lo que sigue
        }
        catch (SiatUnavailableException ex)
        {
            throw SiatAdminSupport.Unavailable(ex);
        }
        try
        {
            await codes.EnsureCuisAsync(context, mapping, point, false, ct);
            await codes.EnsureCufdAsync(context, mapping, point, false, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (SiatUnavailableException ex)
        {
            point.RecordFailure(ex.Message, now);
            await db.SaveChangesAsync(ct);
            throw new DomainException("siat.unavailable",
                $"El punto de venta {point.Code} quedó registrado, pero no hay comunicación con el SIN para pedir su CUIS y CUFD: {ex.Message} " +
                "(se pedirán en «Preparar SIAT» o en el mantenimiento automático).");
        }
        var zone = context.Zone;
        return (await SiatStatusBuilder.PointsAsync(db, context.Environment, zone, point.Id, clock.UtcNow, ct)).Single();
    }
}

public sealed class LinkPointOfSaleRegisterHandler(IMinvDbContext db) : IRequestHandler<LinkPointOfSaleRegisterCommand, string>
{
    public async Task<string> Handle(LinkPointOfSaleRegisterCommand r, CancellationToken ct)
    {
        var point = await SiatAdminSupport.PointAsync(db, r.PointOfSaleId, ct);
        Guard.That(!point.IsClosed, "siat.pos_closed", "El punto de venta está cerrado en el SIN.");
        var registerId = await SiatAdminSupport.RegisterIdAsync(db, point.BranchId, r.RegisterCode, ct);
        if (registerId is { } id)
        {
            await SiatAdminSupport.UnlinkRegisterAsync(db, id, point.Environment, point.Id, ct);
        }
        point.LinkRegister(registerId);
        await db.SaveChangesAsync(ct);
        return registerId is null
            ? $"✔ El punto de venta {point.Code} quedó sin caja (lo usan la oficina y la API)."
            : $"✔ La caja {r.RegisterCode!.Trim().ToUpperInvariant()} factura con el punto de venta {point.Code} · {point.Name}.";
    }
}

public sealed class CloseSiatPointOfSaleHandler(IMinvDbContext db, IClock clock, ISiatGateway gateway, ISecretProtector? protector = null)
    : IRequestHandler<CloseSiatPointOfSaleCommand, string>
{
    public async Task<string> Handle(CloseSiatPointOfSaleCommand r, CancellationToken ct)
    {
        var point = await SiatAdminSupport.PointAsync(db, r.PointOfSaleId, ct);
        Guard.That(point.Code > 0, "siat.pos_zero", "El punto 0 (sin punto de venta) no se cierra: representa a la sucursal misma.");
        Guard.That(!point.IsClosed, "siat.pos_closed", "El punto de venta ya está cerrado en el SIN.");
        var open = await db.Set<FiscalDocument>().CountAsync(d => d.PointOfSaleId == point.Id
                                                                  && (d.Status == FiscalDocumentStatus.Pending || d.Status == FiscalDocumentStatus.NoResponse
                                                                      || d.Status == FiscalDocumentStatus.Offline || d.Status == FiscalDocumentStatus.InPackage
                                                                      || d.Status == FiscalDocumentStatus.DuplicateToVoid), ct);
        Guard.That(open == 0, "siat.pos_pending_documents",
            $"El punto de venta tiene {open} documento(s) sin resolver con el SIN (pendientes, fuera de línea o duplicados): resuélvalos antes del cierre.");
        var lookups = new BillingLookups(db, protector, clock);
        var context = await lookups.ContextAsync(ct);
        SiatAdminSupport.EnsureEnvironment(point, context);
        var mapping = await lookups.BranchMappingAsync(point.BranchId, ct);
        var now = clock.UtcNow;
        var cuis = await lookups.CurrentCuisAsync(point.Id, now, ct);
        if (cuis is null)
        {
            var zero = await db.Set<SiatPointOfSale>().FirstOrDefaultAsync(p => p.BranchId == point.BranchId && p.Environment == point.Environment
                                                                                  && p.Code == 0 && p.ClosedAt == null, ct);
            cuis = zero is null ? null : await lookups.CurrentCuisAsync(zero.Id, now, ct);
        }
        if (cuis is null)
        {
            throw new DomainException("siat.no_cuis", "No hay CUIS vigente del punto ni de la sucursal para pedir el cierre: solicite uno primero.");
        }
        SiatReply reply;
        try
        {
            reply = await gateway.ClosePointOfSaleAsync(context.Connection, BillingLookups.Place(mapping, point), cuis.Code, ct);
        }
        catch (SiatUnavailableException ex)
        {
            throw SiatAdminSupport.Unavailable(ex);
        }
        if (!reply.Transaction)
        {
            throw new DomainException("siat.pos_close_rejected", "El SIN no cerró el punto de venta: " + reply.Describe());
        }
        point.Close(now);
        point.LinkRegister(null);
        await db.SaveChangesAsync(ct);
        return $"✔ Punto de venta {point.Code} · {point.Name} cerrado DEFINITIVAMENTE en el SIN (su número no se vuelve a usar).";
    }
}

// --------------------------------------------------------------------------------------------------- CUIS y CUFD
public sealed class RequestCuisHandler(IMinvDbContext db, IClock clock, ISiatGateway gateway, ISecretProtector? protector = null)
    : IRequestHandler<RequestCuisCommand, string>
{
    public async Task<string> Handle(RequestCuisCommand r, CancellationToken ct)
    {
        var point = await SiatAdminSupport.PointAsync(db, r.PointOfSaleId, ct);
        Guard.That(!point.IsClosed, "siat.pos_closed", "El punto de venta está cerrado en el SIN.");
        var lookups = new BillingLookups(db, protector, clock);
        var context = await lookups.ContextAsync(ct);
        SiatAdminSupport.EnsureEnvironment(point, context);
        var mapping = await lookups.BranchMappingAsync(point.BranchId, ct);
        var codes = new SiatCodeManager(lookups, gateway);
        var before = await lookups.CurrentCuisAsync(point.Id, clock.UtcNow, ct);
        SiatCuis cuis;
        SiatCufd? cufd = null;
        try
        {
            cuis = await codes.EnsureCuisAsync(context, mapping, point, true, ct);
            if (before is null || before.Id != cuis.Id)
            {
                // Con el CUIS renovado se debe pedir de inmediato un CUFD nuevo (V23 1.0.30)
                cufd = await codes.EnsureCufdAsync(context, mapping, point, true, ct);
            }
        }
        catch (SiatUnavailableException ex)
        {
            throw SiatAdminSupport.Unavailable(ex);
        }
        await db.SaveChangesAsync(ct);
        var until = SiatAdminSupport.Local(cuis.ValidUntil, context.Zone, "dd/MM/yyyy");
        return cufd is null
            ? $"✔ El SIN mantuvo el CUIS vigente del punto {point.Code} (hasta {until}); se renueva desde 5 días antes del vencimiento."
            : $"✔ CUIS nuevo del punto {point.Code} vigente hasta {until} · CUFD nuevo hasta {SiatAdminSupport.Local(cufd.ValidUntil, context.Zone)}.";
    }
}

public sealed class RequestCufdHandler(IMinvDbContext db, IClock clock, ISiatGateway gateway, ISecretProtector? protector = null)
    : IRequestHandler<RequestCufdCommand, string>
{
    public async Task<string> Handle(RequestCufdCommand r, CancellationToken ct)
    {
        var point = await SiatAdminSupport.PointAsync(db, r.PointOfSaleId, ct);
        Guard.That(!point.IsClosed, "siat.pos_closed", "El punto de venta está cerrado en el SIN.");
        var lookups = new BillingLookups(db, protector, clock);
        var context = await lookups.ContextAsync(ct);
        SiatAdminSupport.EnsureEnvironment(point, context);
        var mapping = await lookups.BranchMappingAsync(point.BranchId, ct);
        SiatCufd cufd;
        try
        {
            cufd = await new SiatCodeManager(lookups, gateway).EnsureCufdAsync(context, mapping, point, true, ct);
        }
        catch (SiatUnavailableException ex)
        {
            throw SiatAdminSupport.Unavailable(ex);
        }
        await db.SaveChangesAsync(ct);
        return $"✔ CUFD nuevo del punto {point.Code} vigente hasta {SiatAdminSupport.Local(cufd.ValidUntil, context.Zone)} · dirección del Padrón: {cufd.Address}.";
    }
}

public sealed class PrepareSiatHandler(IMinvDbContext db, IClock clock, ISiatGateway gateway, ICurrentUser user, ISecretProtector? protector = null)
    : IRequestHandler<PrepareSiatCommand, SiatMaintenanceResult>
{
    public async Task<SiatMaintenanceResult> Handle(PrepareSiatCommand request, CancellationToken ct)
    {
        var lookups = new BillingLookups(db, protector, clock);
        var context = await lookups.ContextAsync(ct);
        var result = await SiatDailyMaintenance.RunAsync(lookups, new SiatCodeManager(lookups, gateway), context, true, user.UserId, ct);
        await db.SaveChangesAsync(ct);
        return result;
    }
}

public sealed class SyncSiatCatalogsHandler(IMinvDbContext db, IClock clock, ISiatGateway gateway, ICurrentUser user, ISecretProtector? protector = null)
    : IRequestHandler<SyncSiatCatalogsCommand, SiatSyncResult>
{
    public async Task<SiatSyncResult> Handle(SyncSiatCatalogsCommand r, CancellationToken ct)
    {
        string? catalog = null;
        if (!string.IsNullOrWhiteSpace(r.Catalog))
        {
            catalog = r.Catalog.Trim().ToUpperInvariant();
            Guard.That(SiatCatalogNames.All.Contains(catalog), "siat.catalog_unknown",
                $"El catálogo «{r.Catalog}» no es uno de los 18 servicios de sincronización del SIN.");
        }
        var lookups = new BillingLookups(db, protector, clock);
        var context = await lookups.ContextAsync(ct);
        // Esquema centralizado: se sincroniza una sola vez con la casa matriz sin punto de venta (investigación 04 §3.1)
        var head = await db.Set<SiatBranch>().FirstOrDefaultAsync(b => b.SiatCode == 0, ct)
                   ?? throw new DomainException("siat.no_head_office", "Mapee la casa matriz (código 0 del Padrón) antes de sincronizar los catálogos.");
        var now = clock.UtcNow;
        var zero = await SiatAdminSupport.PointZeroAsync(db, context.Settings.TenantId, head.BranchId, context.Environment, now, ct);
        var codes = new SiatCodeManager(lookups, gateway);
        SiatSyncResult result;
        try
        {
            var cuis = await codes.EnsureCuisAsync(context, head, zero, false, ct);
            result = await codes.SyncCatalogsAsync(context, BillingLookups.Place(head, zero), cuis.Code, catalog, user.UserId, ct);
            zero.RecordContact(now);
        }
        catch (SiatUnavailableException ex)
        {
            throw SiatAdminSupport.Unavailable(ex);
        }
        await db.SaveChangesAsync(ct);
        return result;
    }
}

// --------------------------------------------------------------------------------------------------- comunicación y NIT
public sealed class CheckSiatCommunicationHandler(IMinvDbContext db, IClock clock, ISiatGateway gateway, ISecretProtector? protector = null)
    : IRequestHandler<CheckSiatCommunicationCommand, string>
{
    public async Task<string> Handle(CheckSiatCommunicationCommand r, CancellationToken ct)
    {
        var lookups = new BillingLookups(db, protector, clock);
        var context = await lookups.ContextAsync(ct);
        List<SiatPointOfSale> points = r.PointOfSaleId is { } id
            ? [await SiatAdminSupport.PointAsync(db, id, ct)]
            : await db.Set<SiatPointOfSale>().Where(p => p.Environment == context.Environment && p.ClosedAt == null).ToListAsync(ct);
        var now = clock.UtcNow;
        SiatReply reply;
        try
        {
            reply = await gateway.CheckCommunicationAsync(context.Connection, SiatResource.PurchaseSale, ct);
        }
        catch (SiatUnavailableException ex)
        {
            foreach (var point in points)
            {
                point.RecordFailure(ex.Message, now);
            }
            await db.SaveChangesAsync(ct);
            throw SiatAdminSupport.Unavailable(ex);
        }
        if (!reply.Transaction && !reply.Has(SiatCodes.CommunicationOk))
        {
            throw new DomainException("siat.communication_rejected", "El SIN respondió sin confirmar la comunicación: " + reply.Describe());
        }
        foreach (var point in points)
        {
            point.RecordContact(now);
        }
        await db.SaveChangesAsync(ct);
        return $"✔ Comunicación exitosa con el SIN ({reply.Describe()}) · ambiente {context.Environment} · {points.Count} punto(s) de venta al día.";
    }
}

public sealed class VerifyNitHandler(IMinvDbContext db, IClock clock, ISiatGateway gateway, ICurrentUser user, ISecretProtector? protector = null)
    : IRequestHandler<VerifyNitCommand, NitCheckResult>
{
    public const string NotVerified = "No se pudo verificar: el comprobante saldrá con código de excepción";

    public async Task<NitCheckResult> Handle(VerifyNitCommand r, CancellationToken ct)
    {
        Guard.That(r.Nit is > 0 and <= 9_999_999_999_999, "siat.nit", "El NIT a verificar tiene entre 1 y 13 dígitos.");
        var lookups = new BillingLookups(db, protector, clock);
        var context = await lookups.ContextAsync(ct);
        Guid? customerId = null;
        if (!string.IsNullOrWhiteSpace(r.CustomerCode))
        {
            var code = r.CustomerCode.Trim().ToUpperInvariant();
            customerId = await db.Set<Customer>().Where(c => c.Code == code).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct)
                         ?? throw new NotFoundException($"El cliente {code} no existe.");
        }
        // Lugar: la sucursal ACTIVA de la sesión (si está en el Padrón) o la casa matriz
        var active = db.Branches.ActiveBranchId;
        var mapping = (active is { } a ? await db.Set<SiatBranch>().FirstOrDefaultAsync(b => b.BranchId == a, ct) : null)
                      ?? await db.Set<SiatBranch>().FirstOrDefaultAsync(b => b.SiatCode == 0, ct)
                      ?? throw new DomainException("siat.branch_unmapped", "Ninguna sucursal tiene su código del Padrón (Configuración › Facturación SIAT).");
        var now = clock.UtcNow;
        var zero = await db.Set<SiatPointOfSale>().FirstOrDefaultAsync(p => p.BranchId == mapping.BranchId && p.Environment == context.Environment
                                                                              && p.Code == 0 && p.ClosedAt == null, ct)
                   ?? throw new DomainException("siat.no_point_of_sale", "La sucursal no tiene su punto 0 del SIN: ejecute «Preparar SIAT».");
        var cuis = await lookups.RequireCuisAsync(zero.Id, now, ct);
        SiatNitReply reply;
        try
        {
            reply = await gateway.VerifyNitAsync(context.Connection, new SiatPlace(mapping.SiatCode, 0, mapping.BranchId), cuis.Code, r.Nit, ct);
        }
        catch (SiatUnavailableException)
        {
            // Sin comunicación la venta no se bloquea: el comprobante lleva codigoExcepcion = 1 (regla F-08)
            return new NitCheckResult(r.Nit, false, null, NotVerified, false);
        }
        zero.RecordContact(now);
        var siatCode = reply.Code ?? reply.Messages.FirstOrDefault()?.Code;
        if (siatCode is null)
        {
            await db.SaveChangesAsync(ct);
            return new NitCheckResult(r.Nit, false, null, $"{NotVerified} (el SIN no devolvió el estado del NIT).", false);
        }
        var description = reply.Description is { Length: > 0 } d ? d
            : reply.Messages.FirstOrDefault(m => m.Code == siatCode)?.Description
              ?? await lookups.CatalogDescriptionAsync(SiatCatalogNames.ServiceMessages, siatCode.Value, ct);
        db.Set<CustomerNitCheck>().Add(new CustomerNitCheck(context.Settings.TenantId, customerId, r.Nit, siatCode.Value, reply.IsValid, description, now,
            user.UserId));
        await db.SaveChangesAsync(ct);
        return new NitCheckResult(r.Nit, reply.IsValid, siatCode, description, true);
    }
}
