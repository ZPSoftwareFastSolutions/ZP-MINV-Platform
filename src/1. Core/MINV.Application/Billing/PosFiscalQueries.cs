using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Sales;

namespace MINV.Application.Billing;

// =====================================================================================================================
// V4.1 · Consultas de la caja: comprador por documento (¿ya compró antes? ¿su NIT está verificado?) y estado fiscal del
// punto de venta del usuario (¿factura? ¿en línea o fuera de línea? ¿qué falta?).
// =====================================================================================================================

public sealed class FindFiscalBuyerHandler(IMinvDbContext db) : IRequestHandler<FindFiscalBuyerQuery, FiscalBuyerLookup>
{
    public async Task<FiscalBuyerLookup> Handle(FindFiscalBuyerQuery r, CancellationToken ct)
    {
        FiscalRules.EnsureBuyerDocument(r.DocumentType, r.DocumentNumber, r.Complement);
        var number = r.DocumentNumber.Trim();
        var complement = string.IsNullOrWhiteSpace(r.Complement) ? null : r.Complement.Trim().ToUpperInvariant();
        var type = r.DocumentType;
        var customer = await db.Set<Customer>().AsNoTracking()
                           .Where(c => c.IsActive && c.TaxId == number && c.DocumentType == type && c.Complement == complement)
                           .OrderBy(c => c.Code).FirstOrDefaultAsync(ct)
                       // Clientes de antes de la V4.1: tienen el número (NIT/CI) pero todavía no el tipo de documento
                       ?? await db.Set<Customer>().AsNoTracking()
                           .Where(c => c.IsActive && c.TaxId == number && c.DocumentType == null && complement == null)
                           .OrderBy(c => c.Code).FirstOrDefaultAsync(ct);
        bool? nitValid = null;
        if (type == SiatCodes.DocumentNit && long.TryParse(number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
                out var nit))
        {
            nitValid = await db.Set<CustomerNitCheck>().AsNoTracking().Where(x => x.Nit == nit).OrderByDescending(x => x.CheckedAt)
                .Select(x => (bool?)x.IsValid).FirstOrDefaultAsync(ct);
        }
        return new FiscalBuyerLookup(customer is not null, customer?.Code, customer?.Name, customer?.Email, type, number, complement, nitValid);
    }
}

public sealed class GetPosFiscalStateHandler(IMinvDbContext db, ICurrentUser user, IClock clock, ILicenseService licenses)
    : IRequestHandler<GetPosFiscalStateQuery, PosFiscalState>
{
    public async Task<PosFiscalState> Handle(GetPosFiscalStateQuery request, CancellationToken ct)
    {
        var documentTypes = await db.Set<SiatCatalogItem>().AsNoTracking()
            .Where(i => i.Catalog == SiatCatalogNames.IdentityDocumentTypes && i.IsCurrent).OrderBy(i => i.Code)
            .Select(i => new SiatCatalogItemView(i.Catalog, i.Code, i.Description, i.IsCurrent)).ToListAsync(ct);
        var pending = await HomologationCounts.PendingProductsAsync(db, ct);
        PosFiscalState State(bool enabled, bool ready, string message, SiatPointOfSale? point = null) =>
            new(enabled, ready, message + (enabled && pending > 0 ? $" · {pending} producto(s) sin homologar no se pueden facturar." : string.Empty),
                point?.Mode, point?.Code, documentTypes, pending);

        if (!await licenses.IsModuleActiveAsync(LicenseModuleCodes.FiscalSiat, ct))
        {
            return State(false, false, "La empresa no tiene licenciado el módulo Facturación SIAT: las ventas salen sin documento fiscal.");
        }
        var settings = await db.Set<SiatSettings>().AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            return State(false, false, "La facturación SIAT no está configurada (Configuración › Facturación SIAT): las ventas salen sin documento fiscal.");
        }
        if (!settings.IsEnabled)
        {
            return State(false, false, "La facturación SIAT está desactivada: las ventas se registran sin documento fiscal.");
        }
        var userId = user.UserId;
        var session = await db.Set<PosSession>().AsNoTracking().Where(s => s.Status == PosSessionStatus.Open && s.OpenedByUserId == userId)
            .OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync(ct);
        if (session is null)
        {
            return State(true, false, "Abra un turno de caja para facturar.");
        }
        var lookups = new BillingLookups(db, null, clock);
        if (!await db.Set<SiatBranch>().AnyAsync(b => b.BranchId == session.BranchId, ct))
        {
            return State(true, false, "La sucursal de la caja no tiene su código del Padrón (Configuración › Facturación SIAT › Sucursales).");
        }
        SiatPointOfSale point;
        try
        {
            point = await lookups.PointOfSaleForAsync(session.BranchId, session.PosRegisterId, settings.Environment, ct);
        }
        catch (DomainException ex)
        {
            return State(true, false, ex.Message);
        }
        var zone = await lookups.ZoneAsync(ct);
        var now = clock.UtcNow;
        if (await lookups.CurrentCuisAsync(point.Id, now, ct) is null)
        {
            return State(true, false, $"El punto de venta {point.Code} no tiene CUIS vigente: solicítelo en Facturación › Estado SIAT.", point);
        }
        if (point.Mode == SiatConnectionMode.ManualContingency)
        {
            return State(true, false,
                $"Contingencia manual declarada desde {SiatAdminSupport.Local(point.ModeSince, zone)}: emita facturas del talonario CAFC y transcríbalas al terminar.",
                point);
        }
        var cufd = await lookups.CurrentCufdAsync(point.Id, now, ct);
        if (cufd is null)
        {
            var latest = await lookups.LatestCufdAsync(point.Id, ct);
            if (latest is not null && latest.IsUsableOfflineAt(now))
            {
                return State(true, true,
                    $"⚠ El CUFD del punto de venta {point.Code} venció: se factura fuera de línea con el último CUFD hasta el " +
                    $"{SiatAdminSupport.Local(latest.ObtainedAt.Add(FiscalRules.CufdExtendedValidity), zone)} (pida uno nuevo en Estado SIAT).", point);
            }
            return State(true, false, $"El punto de venta {point.Code} no tiene CUFD del día: pídalo en Facturación › Estado SIAT («Preparar SIAT»).", point);
        }
        return point.Mode switch
        {
            SiatConnectionMode.Online => State(true, true,
                $"✔ Facturación en línea · punto de venta {point.Code} · CUFD vigente hasta {SiatAdminSupport.Local(cufd.ValidUntil, zone)}.", point),
            SiatConnectionMode.Offline => State(true, true,
                $"⚠ Fuera de línea desde {SiatAdminSupport.Local(point.ModeSince, zone)}: se factura con tipo de emisión 2 y se envía al volver la comunicación.",
                point),
            _ => State(true, true, "⚠ Recuperando la comunicación con el SIN: se sigue facturando fuera de línea hasta terminar el envío de los paquetes.", point),
        };
    }
}
