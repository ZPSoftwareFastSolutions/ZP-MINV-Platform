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
// V4.1 · Consulta de documentos fiscales (búsqueda, detalle con la bitácora del SIN, XML y entregas), representación
// gráfica (modelo, PDF y rollo) y registro de la entrega al comprador.
// =====================================================================================================================

/// <summary>V4.1 · Qué estados cuentan como documento EMITIDO (entra al libro de ventas, a los totales del día y al resumen).</summary>
internal static class FiscalIssuedStatus
{
    /// <summary>Válido o en camino de serlo (pendiente en línea, fuera de línea o dentro de un paquete). Rechazado, descartado,
    /// sin respuesta (la venta se re-emitió), duplicado por anular y anulado NO cuentan.</summary>
    public static bool IsIssued(FiscalDocumentStatus status) =>
        status is FiscalDocumentStatus.Valid or FiscalDocumentStatus.Pending or FiscalDocumentStatus.Offline or FiscalDocumentStatus.InPackage;

    /// <summary>Hora fiscal ahora (con el reloj del SIN si la empresa lo sincronizó).</summary>
    public static async Task<DateTime> FiscalNowAsync(IMinvDbContext db, IClock clock, CancellationToken ct)
    {
        var lookups = new BillingLookups(db, null, clock);
        var zone = await lookups.ZoneAsync(ct);
        var settings = await db.Set<SiatSettings>().AsNoTracking().FirstOrDefaultAsync(ct);
        return settings is null ? FiscalRules.ToFiscalTime(clock.UtcNow, zone) : lookups.FiscalNow(settings, zone);
    }

    /// <summary>Documento del comprador como se imprime: número y, si hay, complemento («5115889-1A»).</summary>
    public static string BuyerDocument(FiscalDocument d) =>
        string.IsNullOrWhiteSpace(d.BuyerComplement) ? d.BuyerDocumentNumber : $"{d.BuyerDocumentNumber}-{d.BuyerComplement}";
}

/// <summary>V4.1 · Filas de documentos fiscales con las acciones permitidas AHORA (hora fiscal).</summary>
internal static class FiscalDocumentListing
{
    public static async Task<IReadOnlyList<FiscalDocumentRow>> BuildAsync(IMinvDbContext db, IReadOnlyList<FiscalDocument> documents, DateTime fiscalNow,
        CancellationToken ct)
    {
        if (documents.Count == 0)
        {
            return [];
        }
        var branches = await db.Set<Branch>().AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        var pointIds = documents.Select(d => d.PointOfSaleId).Distinct().ToList();
        var points = await db.Set<SiatPointOfSale>().AsNoTracking().Where(p => pointIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
        // Número de la factura de M-INV: la venta (facturas) o, en las notas, la venta devuelta o la de la factura original
        var returnIds = documents.Where(d => d.SalesReturnId is not null).Select(d => d.SalesReturnId!.Value).Distinct().ToList();
        var returns = await db.Set<SalesReturn>().AsNoTracking().Where(r => returnIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.InvoiceId, ct);
        var originalIds = documents.Where(d => d.NoteReference?.OriginalDocumentId is not null).Select(d => d.NoteReference!.OriginalDocumentId!.Value)
            .Distinct().ToList();
        var originals = await db.Set<FiscalDocument>().AsNoTracking().Where(d => originalIds.Contains(d.Id) && d.InvoiceId != null)
            .ToDictionaryAsync(d => d.Id, d => d.InvoiceId!.Value, ct);
        var invoiceIds = documents.Where(d => d.InvoiceId is not null).Select(d => d.InvoiceId!.Value).Concat(returns.Values).Concat(originals.Values)
            .Distinct().ToList();
        var invoices = await db.Set<Invoice>().AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Number, ct);
        return documents.Select(d =>
        {
            var saleInvoiceId = d.InvoiceId
                                ?? (d.SalesReturnId is { } r && returns.TryGetValue(r, out var returned) ? (Guid?)returned : null)
                                ?? (d.NoteReference?.OriginalDocumentId is { } o && originals.TryGetValue(o, out var original) ? (Guid?)original : null);
            return Row(d, branches.GetValueOrDefault(d.BranchId, "?"), points.GetValueOrDefault(d.PointOfSaleId),
                saleInvoiceId is { } i ? invoices.GetValueOrDefault(i) : null, fiscalNow);
        }).ToList();
    }

    public static FiscalDocumentRow Row(FiscalDocument d, string branchCode, int pointOfSaleCode, string? saleNumber, DateTime fiscalNow)
    {
        var canRevert = d.Status == FiscalDocumentStatus.Voided && !d.IsReverted && fiscalNow <= FiscalRules.VoidDeadline(d.IssuedAt);
        var canCreditNote = d.Kind == FiscalDocumentKind.Invoice && d.Status == FiscalDocumentStatus.Valid && d.InvoiceId is not null
                            && fiscalNow <= FiscalRules.CreditNoteDeadline(d.IssuedAt);
        return new FiscalDocumentRow(d.Id, d.Kind, d.Number, d.Cuf, d.IssuedAt, d.BranchId, branchCode, pointOfSaleCode, d.BuyerName ?? "S/N",
            FiscalIssuedStatus.BuyerDocument(d), d.TotalAmount, d.Status, d.IsReverted, d.EmissionType, saleNumber, d.LastSiatCode,
            d.CanVoidAt(fiscalNow), canRevert, canCreditNote, FiscalRules.VoidDeadline(d.IssuedAt));
    }
}

public sealed class GetFiscalDocumentsHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetFiscalDocumentsQuery, IReadOnlyList<FiscalDocumentRow>>
{
    /// <summary>Tope de filas de una búsqueda (el rango de fechas acota; la pantalla pagina sobre esto).</summary>
    public const int MaxRows = 5000;

    public async Task<IReadOnlyList<FiscalDocumentRow>> Handle(GetFiscalDocumentsQuery r, CancellationToken ct)
    {
        Guard.That(r.To >= r.From, "range.invalid", "La fecha final no puede ser anterior a la inicial.");
        var start = r.From.ToDateTime(TimeOnly.MinValue);
        var end = r.To.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var query = db.Set<FiscalDocument>().AsNoTracking().Include(d => d.Lines).Include(d => d.NoteReference)
            .Where(d => d.IssuedAt >= start && d.IssuedAt < end);
        if (r.Status is { } status)
        {
            query = query.Where(d => d.Status == status);
        }
        if (r.Kind is { } kind)
        {
            query = query.Where(d => d.Kind == kind);
        }
        if (!string.IsNullOrWhiteSpace(r.Search))
        {
            // Por número del documento, CUF, número de documento del comprador o nombre
            var text = r.Search.Trim();
            var upper = text.ToUpperInvariant();
            var isNumber = long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number);
            query = query.Where(d => (isNumber && d.Number == number) || d.Cuf.StartsWith(upper) || d.BuyerDocumentNumber.Contains(text)
                                     || (d.BuyerName != null && d.BuyerName.ToUpper().Contains(upper)));
        }
        var documents = await query.OrderByDescending(d => d.IssuedAt).ThenByDescending(d => d.Number).Take(MaxRows).ToListAsync(ct);
        return await FiscalDocumentListing.BuildAsync(db, documents, await FiscalIssuedStatus.FiscalNowAsync(db, clock, ct), ct);
    }
}

public sealed class GetFiscalDocumentHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetFiscalDocumentQuery, FiscalDocumentDetail>
{
    public async Task<FiscalDocumentDetail> Handle(GetFiscalDocumentQuery r, CancellationToken ct)
    {
        var document = await db.Set<FiscalDocument>().AsNoTracking().Include(d => d.Lines).Include(d => d.NoteReference)
                           .FirstOrDefaultAsync(d => d.Id == r.DocumentId, ct)
                       ?? throw new NotFoundException("El documento fiscal no existe (o es de una sucursal que no es suya).");
        var row = (await FiscalDocumentListing.BuildAsync(db, [document], await FiscalIssuedStatus.FiscalNowAsync(db, clock, ct), ct))[0];
        var unitCodes = document.Lines.Select(l => l.SinUnitCode).Distinct().ToList();
        var units = await FiscalCatalogText.DescriptionsAsync(db, SiatCatalogNames.UnitsOfMeasure, unitCodes, ct);
        var lines = document.Lines.OrderBy(l => l.LineNumber).Select(l => new FiscalDocumentLineView(l.LineNumber, l.ProductCode, l.Description, l.Quantity,
            l.SinUnitCode, units.GetValueOrDefault(l.SinUnitCode, l.SinUnitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)), l.UnitPrice,
            l.Discount ?? 0m, l.Subtotal, l.ActivityCode, l.SinProductCode, l.TransactionCode, Tech.TechPrint.Serials(l.SerialNumber, l.Imei))).ToList();
        var events = await db.Set<FiscalDocumentEvent>().AsNoTracking().Where(e => e.DocumentId == document.Id).OrderBy(e => e.OccurredAt).ToListAsync(ct);
        var deliveries = await db.Set<FiscalDelivery>().AsNoTracking().Where(e => e.DocumentId == document.Id).OrderBy(e => e.OccurredAt).ToListAsync(ct);
        var userIds = events.Select(e => e.UserId).OfType<Guid>().Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var file = await db.Set<FiscalDocumentFile>().AsNoTracking().Where(f => f.DocumentId == document.Id).OrderByDescending(f => f.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var replacedBy = await db.Set<FiscalDocument>().AsNoTracking().Where(d => d.ReplacesDocumentId == document.Id).OrderByDescending(d => d.CreatedAt)
            .Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct);
        string? payment = null;
        if (document.PaymentMethodCode is { } method)
        {
            payment = (await FiscalCatalogText.DescriptionsAsync(db, SiatCatalogNames.PaymentMethods, [method], ct)).GetValueOrDefault(method)
                      ?? method.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        string? voidReason = null;
        if (document.VoidReasonCode is { } reason)
        {
            voidReason = (await FiscalCatalogText.DescriptionsAsync(db, SiatCatalogNames.VoidReasons, [reason], ct)).GetValueOrDefault(reason)
                         ?? $"Motivo {reason}";
        }
        var original = document.NoteReference is { } note ? new FiscalPrintOriginal(note.OriginalNumber, note.OriginalCuf, note.OriginalIssuedAt) : null;
        return new FiscalDocumentDetail(row, lines,
            events.Select(e => new FiscalDocumentEventView(e.OccurredAt, e.Action, e.SiatCode, e.Description, e.ReceptionCode, e.Messages,
                e.UserId is { } u ? users.GetValueOrDefault(u) : null)).ToList(),
            deliveries.Select(x => new FiscalDeliveryView(x.OccurredAt, x.Channel, x.Recipient, x.Succeeded, x.Error)).ToList(),
            file?.Xml ?? string.Empty, document.ReceptionCode, document.Cafc, document.ExceptionCode, payment, document.CardNumberMasked, document.Legend,
            voidReason, original, document.ReplacesDocumentId, replacedBy, document.VatAmount, document.BuyerEmail);
    }
}

/// <summary>V4.1 · Descripciones de las paramétricas sincronizadas (unidad de medida, método de pago, motivo de anulación…).</summary>
internal static class FiscalCatalogText
{
    public static async Task<Dictionary<int, string>> DescriptionsAsync(IMinvDbContext db, string catalog, IReadOnlyCollection<int> codes,
        CancellationToken ct) =>
        codes.Count == 0
            ? new Dictionary<int, string>()
            : await db.Set<SiatCatalogItem>().AsNoTracking().Where(i => i.Catalog == catalog && codes.Contains(i.Code))
                .ToDictionaryAsync(i => i.Code, i => i.Description, ct);
}

public sealed class GetFiscalPrintModelHandler(IMinvDbContext db) : IRequestHandler<GetFiscalPrintModelQuery, FiscalPrintModel>
{
    public Task<FiscalPrintModel> Handle(GetFiscalPrintModelQuery request, CancellationToken ct) =>
        FiscalPrintModelBuilder.BuildAsync(db, request.DocumentId, ct);
}

/// <summary>V4.1 · Representación gráfica: PDF (hoja carta, QR t=2) o rollo ESC/POS (QR t=1). El PDF está en todos los
/// anfitriones; el rollo solo donde hay impresora ESC/POS (escritorio con hardware).</summary>
public sealed class RenderFiscalDocumentHandler(IMinvDbContext db, IFiscalDocumentRenderer? pdf = null, IFiscalRollRenderer? roll = null)
    : IRequestHandler<RenderFiscalDocumentQuery, FiscalFile>
{
    public async Task<FiscalFile> Handle(RenderFiscalDocumentQuery r, CancellationToken ct)
    {
        var model = await FiscalPrintModelBuilder.BuildAsync(db, r.DocumentId, ct);
        var branchCode = await (from d in db.Set<FiscalDocument>()
                                join b in db.Set<Branch>() on d.BranchId equals b.Id
                                where d.Id == r.DocumentId
                                select b.Code).FirstOrDefaultAsync(ct) ?? "SUC";
        var name = $"{(model.Original is null ? "Factura" : "NotaCreditoDebito")}-{model.Number}-{branchCode}";
        switch (r.Format)
        {
            case FiscalDeliveryChannel.Pdf:
                if (pdf is null)
                {
                    throw new DomainException("billing.no_pdf_renderer", "Este equipo no tiene el generador de PDF de la representación gráfica.");
                }
                return new FiscalFile(name + ".pdf", "application/pdf", pdf.RenderPdf(model));
            case FiscalDeliveryChannel.Print:
                if (roll is null)
                {
                    throw new DomainException("billing.no_roll_printer",
                        "Este equipo no tiene configurada la impresión en rollo (ESC/POS): descargue el PDF o imprima desde la caja.");
                }
                Guard.That(r.Columns is >= 32 and <= 64, "billing.roll_columns", "El rollo tiene de 32 a 64 columnas (58 mm: 32; 80 mm: 48).");
                // En rollo el QR lleva t=1 (tamaño de previsualización del SIN: 1 = rollo, 2 = media hoja)
                return new FiscalFile(name + ".escpos", "application/octet-stream",
                    roll.RenderRoll(model with { QrUrl = FiscalPrintModelBuilder.WithQrSize(model.QrUrl, 1) }, r.Columns));
            default:
                throw new DomainException("billing.render_format", "El correo no es un formato de impresión: use «Enviar por correo» (XML + PDF).");
        }
    }
}

public sealed class RecordFiscalDeliveryHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<RecordFiscalDeliveryCommand, string>
{
    public async Task<string> Handle(RecordFiscalDeliveryCommand r, CancellationToken ct)
    {
        var document = await db.Set<FiscalDocument>().FirstOrDefaultAsync(d => d.Id == r.DocumentId, ct)
                       ?? throw new NotFoundException("El documento fiscal no existe (o es de una sucursal que no es suya).");
        var recipient = r.Channel == FiscalDeliveryChannel.Email ? Guard.Email(r.Recipient, "El correo del comprador")
            : Guard.OptionalText(r.Recipient?.Trim(), "El destinatario", 254);
        var now = clock.UtcNow;
        db.Set<FiscalDelivery>().Add(new FiscalDelivery(document.TenantId, document.BranchId, document.Id, r.Channel, recipient, true, null, now, user.UserId));
        var what = r.Channel switch
        {
            FiscalDeliveryChannel.Print => "Impreso",
            FiscalDeliveryChannel.Pdf => "Entregado en PDF",
            _ => "Enviado por correo",
        };
        db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document,
            r.Channel == FiscalDeliveryChannel.Print ? FiscalDocumentAction.Printed : FiscalDocumentAction.Delivered, now, user.UserId,
            description: recipient is null ? what : $"{what} a {recipient}"));
        await db.SaveChangesAsync(ct);
        return $"✔ {what}: {(document.Kind == FiscalDocumentKind.Invoice ? "factura" : "nota")} N° {document.Number} (queda la constancia de la entrega).";
    }
}
