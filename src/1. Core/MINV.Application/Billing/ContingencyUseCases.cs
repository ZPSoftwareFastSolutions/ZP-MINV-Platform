using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Sales;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Application.Billing;

// =====================================================================================================================
// V4.1 · Contingencia (investigación 03 §5-§8): contingencia manual con talonario CAFC (energía, virus/software,
// hardware), fin de la contingencia con recuperación inmediata, recuperación forzada, paso manual a fuera de línea y
// transcripción de las facturas manuales. El registro del evento y los paquetes los hace el mismo código del trabajo
// automático (SiatWorker): CUFD nuevo → evento → paquetes → validación.
// =====================================================================================================================

// ------------------------------------------------------------------------------------------------ contingencia manual
public sealed class StartManualContingencyValidator : AbstractValidator<StartManualContingencyCommand>
{
    public StartManualContingencyValidator()
    {
        RuleFor(x => x.EventCode).GreaterThan(0).WithMessage("Elija el evento significativo del catálogo del SIN.");
        RuleFor(x => x.CafcCode).NotEmpty().WithMessage("Indique el CAFC del talonario de facturas de contingencia.").MaximumLength(50);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

/// <summary>
/// Declara una contingencia MANUAL en un punto de venta en línea: evento del catálogo por descripción (corte de energía,
/// virus o falla de software, falla de hardware), CAFC activo de la sucursal para facturas Compra Venta y CUFD del evento
/// = el vigente (o el último). Desde ahí la caja no emite: se usan las facturas del talonario y se transcriben después.
/// </summary>
public sealed class StartManualContingencyHandler(IMinvDbContext db, ICurrentUser user, IClock clock)
    : IRequestHandler<StartManualContingencyCommand, string>
{
    public async Task<string> Handle(StartManualContingencyCommand request, CancellationToken ct)
    {
        var lookups = new BillingLookups(db, null, clock);
        var settings = await lookups.RequireSettingsAsync(ct);
        var point = await ContingencyPoints.PointAsync(db, request.PointOfSaleId, ct);
        Guard.That(point.IsOnline, "siat.mode",
            $"El punto de venta {point.Code} no está en línea ({ContingencyPoints.ModeText(point.Mode)}): la contingencia manual se declara estando en línea.");
        var catalog = await db.Set<SiatCatalogItem>()
                          .FirstOrDefaultAsync(i => i.Catalog == SiatCatalogNames.SignificantEvents && i.Code == request.EventCode && i.IsCurrent, ct)
                      ?? throw new DomainException("siat.event_code",
                          $"El evento {request.EventCode} no está en el catálogo de eventos significativos del SIN (sincronice los catálogos).");
        Guard.That(FiscalIssuer.IsManualEvent(catalog.Description), "siat.event_manual",
            $"«{catalog.Description}» no es una contingencia manual: el talonario CAFC se usa por corte de energía, virus o falla de software, " +
            "o falla de hardware. Los cortes de internet se atienden fuera de línea automáticamente.");
        var cafcCode = request.CafcCode.Trim();
        var cafc = await db.Set<ContingencyCode>()
                       .FirstOrDefaultAsync(c => c.BranchId == point.BranchId && c.DocumentSector == SiatCodes.SectorPurchaseSale && c.Code == cafcCode
                                                 && c.IsActive, ct)
                   ?? throw new DomainException("siat.cafc_not_found",
                       $"El CAFC {cafcCode} no está registrado y activo para facturas Compra Venta de esta sucursal (Facturación › Contingencia).");
        var now = clock.UtcNow;
        var fiscalNow = FiscalIssuer.Milliseconds(lookups.FiscalNow(settings, await lookups.ZoneAsync(ct)));
        Guard.That(cafc.ValidUntil is null || DateOnly.FromDateTime(fiscalNow) <= cafc.ValidUntil, "siat.cafc_expired",
            $"El CAFC {cafc.Code} venció el {cafc.ValidUntil:dd/MM/yyyy}.");
        Guard.That(!await db.Set<SignificantEvent>().AnyAsync(e => e.PointOfSaleId == point.Id && e.Status == SignificantEventStatus.Open, ct),
            "siat.event_open", "El punto de venta ya tiene un evento significativo abierto.");
        var startedAt = request.StartedAt is { } start ? FiscalIssuer.Milliseconds(start) : fiscalNow;
        Guard.That(startedAt <= fiscalNow.AddMinutes(1), "siat.event_future", "El inicio de la contingencia no puede ser posterior a la hora actual.");
        Guard.That(startedAt >= fiscalNow.AddHours(-48), "siat.event_range",
            "El inicio de la contingencia no puede tener más de 48 horas: el evento debe registrarse en ese plazo.");
        var eventCufd = await lookups.CurrentCufdAsync(point.Id, now, ct) ?? await lookups.LatestCufdAsync(point.Id, ct)
                        ?? throw new DomainException("siat.no_cufd", "El punto de venta no tiene CUFD: no se puede declarar la contingencia.");
        var description = string.IsNullOrWhiteSpace(request.Description) ? catalog.Description : request.Description.Trim();
        db.Set<SignificantEvent>().Add(new SignificantEvent(point.TenantId, point.BranchId, point.Id, point.Environment, SignificantEventKind.ManualCafc,
            catalog.Code, description, startedAt, eventCufd.Id, cafc.Id, now, user.UserId));
        point.StartManualContingency(now);
        await db.SaveChangesAsync(ct);
        return $"✔ Contingencia manual en el punto de venta {point.Code} desde {startedAt:dd/MM/yyyy HH:mm}: emita las facturas del talonario CAFC " +
               $"{cafc.Code} (N° {cafc.NumberFrom} a {cafc.NumberTo}) y transcríbalas en M-INV (hasta 72 h después del fin).";
    }
}

/// <summary>
/// Declara el FIN de la contingencia (manual o fuera de línea): cierra el evento con la hora dada o la actual (nunca antes
/// de la última factura del evento), pasa el punto a «recuperando» y ejecuta ya la recuperación (CUFD nuevo → registro del
/// evento → paquetes). Sin comunicación queda recuperando y el trabajo automático reintenta.
/// </summary>
public sealed class EndContingencyHandler(IMinvDbContext db, IClock clock, SiatWorker worker) : IRequestHandler<EndContingencyCommand, string>
{
    public async Task<string> Handle(EndContingencyCommand request, CancellationToken ct)
    {
        var lookups = new BillingLookups(db, null, clock);
        var settings = await lookups.RequireSettingsAsync(ct);
        var point = await ContingencyPoints.PointAsync(db, request.PointOfSaleId, ct);
        Guard.That(point.Mode is SiatConnectionMode.ManualContingency or SiatConnectionMode.Offline or SiatConnectionMode.Recovering, "siat.mode",
            $"El punto de venta {point.Code} no está en contingencia.");
        var now = clock.UtcNow;
        var fiscalNow = FiscalIssuer.Milliseconds(lookups.FiscalNow(settings, await lookups.ZoneAsync(ct)));
        var open = await db.Set<SignificantEvent>().Where(e => e.PointOfSaleId == point.Id && e.Status == SignificantEventStatus.Open)
            .OrderByDescending(e => e.StartedAt).FirstOrDefaultAsync(ct);
        if (open is not null)
        {
            var end = request.EndedAt is { } ended ? FiscalIssuer.Milliseconds(ended) : fiscalNow;
            Guard.That(end <= fiscalNow.AddMinutes(1), "siat.event_future", "El fin de la contingencia no puede ser posterior a la hora actual.");
            var last = await db.Set<FiscalDocument>().Where(d => d.SignificantEventId == open.Id).MaxAsync(d => (DateTime?)d.IssuedAt, ct);
            Guard.That(last is null || end >= last, "siat.event_end",
                $"El fin no puede ser anterior a la última factura de la contingencia ({last:dd/MM/yyyy HH:mm:ss}).");
            open.Close(end > open.StartedAt ? end : open.StartedAt.AddMilliseconds(1));
        }
        point.StartRecovery(now);
        await db.SaveChangesAsync(ct);
        var recovery = await worker.RecoverPointAsync(point.Id, ct);
        return recovery.Recovered
            ? $"✔ Contingencia cerrada. {recovery.Message}"
            : $"Contingencia cerrada; el punto de venta queda recuperando: {recovery.Message} El trabajo automático reintentará la recuperación.";
    }
}

/// <summary>Fuerza ahora la verificación de la comunicación y la recuperación de un punto fuera de línea.</summary>
public sealed class RecoverPointOfSaleHandler(IMinvDbContext db, SiatWorker worker) : IRequestHandler<RecoverPointOfSaleCommand, string>
{
    public async Task<string> Handle(RecoverPointOfSaleCommand request, CancellationToken ct)
    {
        var point = await ContingencyPoints.PointAsync(db, request.PointOfSaleId, ct);
        var result = await worker.RecoverPointAsync(point.Id, ct);
        return result.Recovered ? "✔ " + result.Message : result.Message;
    }
}

/// <summary>Pasa un punto de venta a fuera de línea a mano (se sabe que no hay internet): evento AUTOMÁTICO del catálogo
/// (por descripción; por defecto «corte del servicio de internet») con el CUFD vigente como CUFD del evento.</summary>
public sealed class GoOfflineHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<GoOfflineCommand, string>
{
    public async Task<string> Handle(GoOfflineCommand request, CancellationToken ct)
    {
        var lookups = new BillingLookups(db, null, clock);
        var settings = await lookups.RequireSettingsAsync(ct);
        var point = await ContingencyPoints.PointAsync(db, request.PointOfSaleId, ct);
        Guard.That(point.IsOnline, "siat.mode", $"El punto de venta {point.Code} ya está en {ContingencyPoints.ModeText(point.Mode)}.");
        var now = clock.UtcNow;
        var fiscalNow = FiscalIssuer.Milliseconds(lookups.FiscalNow(settings, await lookups.ZoneAsync(ct)));
        var cufd = await lookups.CurrentCufdAsync(point.Id, now, ct)
                   ?? (await lookups.LatestCufdAsync(point.Id, ct) is { } latest && latest.IsUsableOfflineAt(now) ? latest : null)
                   ?? throw new DomainException("siat.no_cufd",
                       "El punto de venta no tiene un CUFD de las últimas 72 horas: no puede emitir fuera de línea.");
        SignificantEvent evt;
        if (request.EventCode is { } code)
        {
            var catalog = await db.Set<SiatCatalogItem>()
                              .FirstOrDefaultAsync(i => i.Catalog == SiatCatalogNames.SignificantEvents && i.Code == code && i.IsCurrent, ct)
                          ?? throw new DomainException("siat.event_code", $"El evento {code} no está en el catálogo de eventos significativos del SIN.");
            Guard.That(FiscalIssuer.IsAutomaticEvent(catalog.Description), "siat.event_offline",
                $"«{catalog.Description}» no es un evento fuera de línea (internet o acceso al SIN): para energía, software o hardware declare una contingencia manual.");
            evt = new SignificantEvent(point.TenantId, point.BranchId, point.Id, point.Environment, SignificantEventKind.Offline, catalog.Code,
                catalog.Description, fiscalNow, cufd.Id, null, now, user.UserId);
            db.Set<SignificantEvent>().Add(evt);
            point.GoOffline(now);
        }
        else
        {
            evt = await FiscalIssuer.OpenAutomaticEventAsync(db, point, cufd, FiscalIssuer.InternetEvent, FiscalIssuer.DefaultInternetEventCode, fiscalNow,
                now, user.UserId, ct);
        }
        await db.SaveChangesAsync(ct);
        return $"✔ El punto de venta {point.Code} emite fuera de línea desde {fiscalNow:dd/MM/yyyy HH:mm} ({evt.Description}). La recuperación es " +
               "automática al volver la comunicación.";
    }
}

// ------------------------------------------------------------------------------------------------ transcripción CAFC
public sealed class TranscribeManualInvoiceValidator : AbstractValidator<TranscribeManualInvoiceCommand>
{
    public TranscribeManualInvoiceValidator()
    {
        RuleFor(x => x.Number).GreaterThan(0).WithMessage("Indique el número de la factura manual.");
        RuleFor(x => x.Buyer).NotNull().WithMessage("Indique el documento del comprador de la factura manual.");
        RuleFor(x => x.PaymentMethodCode).NotEmpty().WithMessage("Elija el medio de pago.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("La factura no tiene productos.").Must(l => l.Count <= SiatCodes.MaxLinesPerDocument)
            .WithMessage("Máximo 500 líneas por factura.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.Sku).NotEmpty().WithMessage("Cada línea necesita el SKU.");
            l.RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Cada cantidad debe ser mayor que 0.");
            l.RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 100).WithMessage("El descuento va de 0 a 100 %.");
        });
    }
}

/// <summary>
/// Transcribe una factura manual del talonario CAFC emitida durante una contingencia manual: número dentro del talonario
/// y no usado, fecha dentro del evento, hasta 72 h después del fin. Registra la VENTA (canal «cafc», almacén de la caja
/// del punto o el de la sucursal, sin turno de caja) y su documento FUERA DE LÍNEA con ese número, esa fecha, el CAFC, el
/// evento y el CUFD del evento: queda para el paquete del evento.
/// </summary>
public sealed class TranscribeManualInvoiceHandler(IMinvDbContext db, ICurrentUser user, IClock clock, IFiscalDocumentSerializer? serializer = null)
    : IRequestHandler<TranscribeManualInvoiceCommand, FiscalDocumentRow>
{
    public async Task<FiscalDocumentRow> Handle(TranscribeManualInvoiceCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para transcribir.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var evt = await db.Set<SignificantEvent>().FirstOrDefaultAsync(e => e.Id == request.SignificantEventId, ct)
                          ?? throw new NotFoundException("El evento de contingencia no existe.");
                Guard.That(evt.Kind == SignificantEventKind.ManualCafc && evt.ContingencyCodeId is not null, "siat.event_kind",
                    "Solo se transcriben facturas de una contingencia manual con talonario CAFC.");
                Guard.That(evt.Status is not (SignificantEventStatus.Reconciled or SignificantEventStatus.WithObservations), "siat.event_closed",
                    "La contingencia ya se concilió con el SIN: no admite más facturas.");
                var cafc = await db.Set<ContingencyCode>().FirstAsync(c => c.Id == evt.ContingencyCodeId, ct);
                Guard.That(cafc.Contains(request.Number), "fiscal.cafc_number",
                    $"El número {request.Number} no está en el talonario CAFC {cafc.Code} (del {cafc.NumberFrom} al {cafc.NumberTo}).");
                var issuedAt = FiscalIssuer.Milliseconds(request.IssuedAt);
                Guard.That(evt.Covers(issuedAt), "fiscal.cafc_date",
                    $"La fecha {issuedAt:dd/MM/yyyy HH:mm} no está dentro de la contingencia (desde {evt.StartedAt:dd/MM/yyyy HH:mm}" +
                    (evt.EndedAt is { } end ? $" hasta {end:dd/MM/yyyy HH:mm})." : ")."));
                var lookups = new BillingLookups(db, null, clock);
                var settings = await lookups.RequireSettingsAsync(ct);
                var fiscalNow = lookups.FiscalNow(settings, await lookups.ZoneAsync(ct));
                Guard.That(issuedAt <= fiscalNow.AddMinutes(1), "fiscal.cafc_date", "La fecha de la factura manual no puede ser futura.");
                Guard.That(evt.TranscriptionDeadline is not { } deadline || fiscalNow <= deadline, "fiscal.cafc_deadline",
                    $"El plazo para transcribir venció el {evt.TranscriptionDeadline:dd/MM/yyyy HH:mm} (72 h después del fin de la contingencia).");
                var point = await db.Set<SiatPointOfSale>().FirstAsync(p => p.Id == evt.PointOfSaleId, ct);
                var used = await db.Set<FiscalDocument>().AnyAsync(d => d.Environment == point.Environment && d.PointOfSaleId == point.Id
                                                                        && d.DocumentSector == SiatCodes.SectorPurchaseSale
                                                                        && d.Number == request.Number, ct);
                Guard.That(!used, "fiscal.cafc_number_used", $"La factura N° {request.Number} ya está registrada en este punto de venta.");
                var warehouseId = await WarehouseAsync(point, ct);
                // El comprador es el cliente de la venta transcrita (si es nuevo, se registra antes para vender a su nombre)
                var buyer = await FiscalIssuer.ResolveBuyerAsync(db, point.TenantId, null, request.Buyer, ct);
                await db.SaveChangesAsync(ct);
                var sale = await SaleWriter.SellAsync(db, clock, userId,
                    new SaleOrigin(point.BranchId, warehouseId, null, SaleChannels.Cafc, point.PosRegisterId), buyer.CustomerCode,
                    request.PaymentMethodCode, request.Lines, null, $"CAFC {cafc.Code} N° {request.Number}",
                    new SaleFiscal(serializer, BillingLookups.UserCode(user), request.Buyer, null,
                        new FiscalManualEmission(request.Number, issuedAt, cafc.Code, evt.Id)), ct);
                var document = sale.Document ?? throw new DomainException("siat.not_enabled", "La facturación SIAT no está activa: no hay nada que transcribir.");
                await db.SaveChangesAsync(ct);
                return (await IssuedDocumentRows.ForAsync(db, clock, [document.Id], ct))[0];
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }

    /// <summary>Almacén de la caja del punto de venta o, si no tiene caja, el por defecto de la sucursal.</summary>
    private async Task<Guid> WarehouseAsync(SiatPointOfSale point, CancellationToken ct)
    {
        if (point.PosRegisterId is { } registerId)
        {
            return await db.Set<PosRegister>().Where(r => r.Id == registerId).Select(r => r.WarehouseId).FirstAsync(ct);
        }
        var config = await db.Set<TenantConfig>().FirstAsync(ct);
        return await db.Set<Warehouse>().Where(w => w.Id == config.DefaultWarehouseId && w.BranchId == point.BranchId).Select(w => (Guid?)w.Id)
                   .FirstOrDefaultAsync(ct)
               ?? await db.Set<Warehouse>().Where(w => w.BranchId == point.BranchId).OrderBy(w => w.Code).Select(w => (Guid?)w.Id).FirstOrDefaultAsync(ct)
               ?? throw new NotFoundException("La sucursal del punto de venta no tiene almacén.");
    }
}

/// <summary>V4.1 · Auxiliares de los comandos de contingencia.</summary>
internal static class ContingencyPoints
{
    public static async Task<SiatPointOfSale> PointAsync(IMinvDbContext db, Guid id, CancellationToken ct)
    {
        var point = await db.Set<SiatPointOfSale>().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("El punto de venta no existe.");
        Guard.That(!point.IsClosed, "siat.pos_closed", $"El punto de venta {point.Code} está cerrado en el SIN.");
        return point;
    }

    public static string ModeText(SiatConnectionMode mode) => mode switch
    {
        SiatConnectionMode.Online => "en línea",
        SiatConnectionMode.Offline => "fuera de línea",
        SiatConnectionMode.ManualContingency => "contingencia manual",
        SiatConnectionMode.Recovering => "recuperación",
        _ => mode.ToString(),
    };
}
