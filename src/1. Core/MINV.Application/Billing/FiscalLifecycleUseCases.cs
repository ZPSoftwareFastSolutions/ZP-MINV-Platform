using System.Globalization;
using System.Net;
using System.Text;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Sales;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Sales;

namespace MINV.Application.Billing;

// =====================================================================================================================
// V4.1 · Ciclo de vida de un documento fiscal ya emitido: verificación de estado, anulación (con o sin devolución de la
// mercadería), reversión de la anulación, re-emisión y envío por correo. Son operaciones cuyo propósito es la llamada al
// SIN (regla F-03): llaman y guardan la respuesta.
// =====================================================================================================================

// ------------------------------------------------------------------------------------------------ verificación de estado
/// <summary>Verifica el estado de un documento en el SIN por su CUF (y resuelve un documento sin respuesta).</summary>
public sealed class CheckFiscalDocumentStatusHandler(IMinvDbContext db, ISiatGateway gateway, ICurrentUser user, IClock clock,
    ISecretProtector? protector = null) : IRequestHandler<CheckFiscalDocumentStatusCommand, string>
{
    public async Task<string> Handle(CheckFiscalDocumentStatusCommand request, CancellationToken ct)
    {
        var lookups = new BillingLookups(db, protector, clock);
        var context = await lookups.ContextAsync(ct);
        var document = await LifecycleDocuments.LoadAsync(db, request.DocumentId, ct);
        var point = await db.Set<SiatPointOfSale>().FirstAsync(p => p.Id == document.PointOfSaleId, ct);
        var branch = await lookups.BranchMappingAsync(point.BranchId, ct);
        var codes = new SiatCodeManager(lookups, gateway);
        SiatReply reply;
        try
        {
            var cuis = await codes.EnsureCuisAsync(context, branch, point, false, ct);
            var cufd = await codes.EnsureCufdAsync(context, branch, point, false, ct);
            reply = await gateway.CheckDocumentStatusAsync(context.Connection, BillingLookups.PlaceOf(document), new SiatCodesForCall(cuis.Code, cufd.Code),
                LifecycleDocuments.Reference(document), document.Cuf, ct);
        }
        catch (SiatUnavailableException ex)
        {
            throw LifecycleDocuments.Unavailable(ex);
        }
        var now = clock.UtcNow;
        var registered = reply.Transaction && reply.StatusCode is SiatCodes.ReceptionValidated or SiatCodes.VoidConfirmed;
        var missing = !reply.Transaction && (reply.Has(924) || reply.Has(946));
        string outcome;
        if (document.Status == FiscalDocumentStatus.NoResponse && (registered || missing))
        {
            var reissued = await db.Set<FiscalDocument>().AnyAsync(d => d.ReplacesDocumentId == document.Id, ct);
            document.ResolveNoResponse(registered, reissued, reply.StatusCode ?? reply.Messages.FirstOrDefault()?.Code, now);
            outcome = FiscalIssuer.StatusText(document.Status);
        }
        else
        {
            if ((reply.StatusCode ?? reply.Messages.FirstOrDefault()?.Code) is { } code)
            {
                document.RecordSiatCode(code);
            }
            outcome = FiscalIssuer.StatusText(document.Status);
        }
        db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.StatusChecked, now, user.UserId, reply));
        point.RecordContact(now);
        await db.SaveChangesAsync(ct);
        var siat = registered
            ? reply.StatusCode == SiatCodes.VoidConfirmed ? "ANULADO" : "VÁLIDO"
            : missing ? "no existe en el SIN" : reply.Describe();
        return $"{SiatWorker.Title(document)} N° {document.Number}: en el SIN {siat}; en M-INV {outcome}.";
    }
}

// ------------------------------------------------------------------------------------------------ anulación
public sealed class VoidFiscalDocumentValidator : AbstractValidator<VoidFiscalDocumentCommand>
{
    public VoidFiscalDocumentValidator()
    {
        RuleFor(x => x.ReasonCode).GreaterThan(0).WithMessage("Elija el motivo de anulación del catálogo del SIN.");
        RuleFor(x => x.Note).MaximumLength(150);
    }
}

/// <summary>
/// Anula un documento en el SIN (motivo del catálogo; hasta el día 9 del mes siguiente, hora fiscal). 905/936 = anulado.
/// Sin respuesta NO se entra en contingencia (03 §5, caso D): se verifica el estado y, si ya figura anulado, se completa
/// solo en M-INV; si no, <c>fiscal.void_uncertain</c>. Con devolución de mercadería revierte la venta (stock, asiento y
/// estado) en la misma transacción. Notifica al comprador por correo si puede (F-10).
/// </summary>
public sealed class VoidFiscalDocumentHandler(IMinvDbContext db, ISiatGateway gateway, ICurrentUser user, IClock clock,
    ISecretProtector? protector = null, IMailSender? mailSender = null) : IRequestHandler<VoidFiscalDocumentCommand, string>
{
    public async Task<string> Handle(VoidFiscalDocumentCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para anular.");
        var lookups = new BillingLookups(db, protector, clock);
        var context = await lookups.ContextAsync(ct);
        var document = await LifecycleDocuments.LoadAsync(db, request.DocumentId, ct);
        document.EnsureVoidable(lookups.FiscalNow(context));
        var reason = await db.Set<SiatCatalogItem>()
                         .FirstOrDefaultAsync(i => i.Catalog == SiatCatalogNames.VoidReasons && i.Code == request.ReasonCode && i.IsCurrent, ct)
                     ?? throw new DomainException("fiscal.void_reason",
                         $"El motivo {request.ReasonCode} no está en el catálogo de motivos de anulación del SIN (sincronice los catálogos).");
        if (request.ReturnGoods)
        {
            await EnsureGoodsCanReturnAsync(document, ct);
        }
        var point = await db.Set<SiatPointOfSale>().FirstAsync(p => p.Id == document.PointOfSaleId, ct);
        var branch = await lookups.BranchMappingAsync(point.BranchId, ct);
        var codeManager = new SiatCodeManager(lookups, gateway);
        SiatCodesForCall codes;
        try
        {
            var cuis = await codeManager.EnsureCuisAsync(context, branch, point, false, ct);
            var cufd = await codeManager.EnsureCufdAsync(context, branch, point, false, ct);
            codes = new SiatCodesForCall(cuis.Code, cufd.Code);
        }
        catch (SiatUnavailableException ex)
        {
            throw LifecycleDocuments.Unavailable(ex);
        }
        await db.SaveChangesAsync(ct);   // códigos nuevos (si se pidieron) antes de anular
        var place = BillingLookups.PlaceOf(document);
        var reference = LifecycleDocuments.Reference(document);
        SiatReply reply;
        try
        {
            reply = await gateway.VoidDocumentAsync(context.Connection, place, codes, reference, document.Cuf, request.ReasonCode, ct);
        }
        catch (SiatUnavailableException ex)
        {
            // Caso D: no se entra en contingencia; antes de reintentar se verifica si ya quedó anulado
            SiatReply? status = null;
            try
            {
                status = await gateway.CheckDocumentStatusAsync(context.Connection, place, codes, reference, document.Cuf, ct);
            }
            catch (SiatUnavailableException)
            {
                // sin comunicación tampoco para verificar
            }
            if (status is { Transaction: true, StatusCode: SiatCodes.VoidConfirmed })
            {
                reply = status;
            }
            else
            {
                db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.VoidFailed, clock.UtcNow, userId,
                    description: "El SIN no respondió la anulación: " + ex.Message));
                await db.SaveChangesAsync(ct);
                throw new DomainException("fiscal.void_uncertain",
                    "El SIN no respondió la anulación y no se pudo confirmar su estado: el documento sigue válido en M-INV. Reintente en unos minutos " +
                    "(antes de volver a anular se verifica si ya quedó anulado en el SIN).");
            }
        }
        if (!reply.Has(SiatCodes.VoidConfirmed) && !reply.Has(SiatCodes.AlreadyVoided))
        {
            db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.VoidFailed, clock.UtcNow, userId, reply));
            await db.SaveChangesAsync(ct);
            throw new DomainException(reply.Has(SiatCodes.VoidOutOfTime) ? "fiscal.void_deadline" : "fiscal.void_rejected",
                $"El SIN no anuló el documento: {reply.Describe()}.");
        }
        var siatCode = reply.Has(SiatCodes.VoidConfirmed) ? SiatCodes.VoidConfirmed : SiatCodes.AlreadyVoided;
        var note = string.IsNullOrWhiteSpace(request.Note) ? string.Empty : $" ({request.Note.Trim()})";
        var (server, cannotNotify) = await BuyerMail.NotificationTargetAsync(db, mailSender, protector, document, ct);
        string goods;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                goods = await ApplyVoidAsync(document.Id, request, reason.Description + note, siatCode, reply, cannotNotify, userId, ct);
                break;
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
        var notified = await BuyerMail.NotifyAsync(db, mailSender, server, clock, userId, document.Id,
            $"Anulación de la {SiatWorker.Title(document).ToLowerInvariant()} N° {document.Number}",
            $"Le informamos que la {SiatWorker.Title(document).ToLowerInvariant()} N° {document.Number} (código de autorización / CUF {document.Cuf}) " +
            $"fue ANULADA en el Servicio de Impuestos Nacionales. Motivo: {reason.Description}.", ct);
        return $"✔ {SiatWorker.Title(document)} N° {document.Number} anulada en el SIN (motivo: {reason.Description}).{goods} {notified ?? cannotNotify}".TrimEnd();
    }

    /// <summary>Anulación local (y reversión de la venta si se pidió) sobre el documento recién leído.</summary>
    private async Task<string> ApplyVoidAsync(Guid documentId, VoidFiscalDocumentCommand request, string reason, int siatCode, SiatReply reply,
        string? cannotNotify, Guid userId, CancellationToken ct)
    {
        var document = await LifecycleDocuments.LoadAsync(db, documentId, ct);
        var now = clock.UtcNow;
        document.Void(request.ReasonCode, siatCode, now);
        var description = "Anulado en el SIN." + (cannotNotify is null ? string.Empty : " " + cannotNotify);
        string goods;
        if (request.ReturnGoods)
        {
            var invoice = await db.Set<Invoice>().Include(i => i.Lines).FirstAsync(i => i.Id == document.InvoiceId, ct);
            await SaleReverser.ReverseAsync(db, clock, userId, invoice, $"Anulación fiscal N° {document.Number}: {reason}", ct);
            description += $" Se devolvió la mercadería de la venta {invoice.Number}.";
            goods = $" La mercadería de la venta {invoice.Number} volvió al stock y se registró el asiento inverso.";
        }
        else
        {
            goods = document.Kind == FiscalDocumentKind.Invoice
                ? " La venta sigue vigente: puede emitirle un documento nuevo (re-emisión) con los datos corregidos."
                : string.Empty;
        }
        db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.Voided, now, userId, reply, description));
        await db.SaveChangesAsync(ct);
        return goods;
    }

    /// <summary>«Anular y devolver»: solo facturas de ventas vigentes y sin devoluciones parciales (se validan ANTES de
    /// anular en el SIN).</summary>
    private async Task EnsureGoodsCanReturnAsync(FiscalDocument document, CancellationToken ct)
    {
        Guard.That(document.Kind == FiscalDocumentKind.Invoice && document.InvoiceId is not null, "fiscal.void_goods",
            "Solo la factura de una venta devuelve mercadería al anularse.");
        var invoice = await db.Set<Invoice>().FirstAsync(i => i.Id == document.InvoiceId, ct);
        Guard.That(invoice.Status == InvoiceStatus.Issued, "fiscal.void_goods", $"La venta {invoice.Number} ya está anulada.");
        var returns = await db.Set<SalesReturn>().Where(r => r.InvoiceId == invoice.Id).Select(r => r.Number).ToListAsync(ct);
        Guard.That(returns.Count == 0, "sale.has_returns",
            $"La venta {invoice.Number} tiene devoluciones registradas ({string.Join(", ", returns)}): anule sin devolver mercadería o use devoluciones.");
    }
}

// ------------------------------------------------------------------------------------------------ reversión
/// <summary>Revierte la anulación (una sola vez y dentro del mismo plazo). 907/978 = revertido; si la anulación devolvió la
/// mercadería (la venta está anulada) no se revierte: la venta ya no existe.</summary>
public sealed class RevertFiscalVoidHandler(IMinvDbContext db, ISiatGateway gateway, ICurrentUser user, IClock clock,
    ISecretProtector? protector = null, IMailSender? mailSender = null) : IRequestHandler<RevertFiscalVoidCommand, string>
{
    public async Task<string> Handle(RevertFiscalVoidCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para revertir la anulación.");
        var lookups = new BillingLookups(db, protector, clock);
        var context = await lookups.ContextAsync(ct);
        var document = await LifecycleDocuments.LoadAsync(db, request.DocumentId, ct);
        document.EnsureRevertible(lookups.FiscalNow(context));
        if (document.InvoiceId is { } invoiceId)
        {
            var invoice = await db.Set<Invoice>().FirstAsync(i => i.Id == invoiceId, ct);
            Guard.That(invoice.Status == InvoiceStatus.Issued, "fiscal.revert_goods_returned",
                $"La anulación devolvió la mercadería (la venta {invoice.Number} está anulada): no se puede revertir. Si hubo un error, registre una venta nueva.");
            var other = await db.Set<FiscalDocument>().Where(d => d.InvoiceId == invoiceId && d.Id != document.Id
                                                                  && (d.Status == FiscalDocumentStatus.Pending || d.Status == FiscalDocumentStatus.Valid
                                                                      || d.Status == FiscalDocumentStatus.Offline || d.Status == FiscalDocumentStatus.InPackage))
                .Select(d => (long?)d.Number).FirstOrDefaultAsync(ct);
            Guard.That(other is null, "fiscal.revert_replaced",
                $"La venta ya tiene otro documento vigente (N° {other}): no se puede revertir la anulación de este.");
        }
        var point = await db.Set<SiatPointOfSale>().FirstAsync(p => p.Id == document.PointOfSaleId, ct);
        var branch = await lookups.BranchMappingAsync(point.BranchId, ct);
        var codeManager = new SiatCodeManager(lookups, gateway);
        SiatReply reply;
        try
        {
            var cuis = await codeManager.EnsureCuisAsync(context, branch, point, false, ct);
            var cufd = await codeManager.EnsureCufdAsync(context, branch, point, false, ct);
            await db.SaveChangesAsync(ct);
            reply = await gateway.RevertVoidAsync(context.Connection, BillingLookups.PlaceOf(document), new SiatCodesForCall(cuis.Code, cufd.Code),
                LifecycleDocuments.Reference(document), document.Cuf, ct);
        }
        catch (SiatUnavailableException ex)
        {
            throw LifecycleDocuments.Unavailable(ex);
        }
        var now = clock.UtcNow;
        if (!reply.Has(SiatCodes.RevertConfirmed) && !reply.Has(SiatCodes.RevertConfirmedAlt))
        {
            db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.RevertFailed, now, userId, reply));
            await db.SaveChangesAsync(ct);
            throw new DomainException("fiscal.revert_rejected", RevertError(reply));
        }
        var (server, cannotNotify) = await BuyerMail.NotificationTargetAsync(db, mailSender, protector, document, ct);
        document.RevertVoid(reply.Has(SiatCodes.RevertConfirmed) ? SiatCodes.RevertConfirmed : SiatCodes.RevertConfirmedAlt, now);
        db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.Reverted, now, userId, reply,
            "Anulación revertida en el SIN: el documento vuelve a ser válido y ya no se puede anular." + (cannotNotify is null ? string.Empty : " " + cannotNotify)));
        await db.SaveChangesAsync(ct);
        var notified = await BuyerMail.NotifyAsync(db, mailSender, server, clock, userId, document.Id,
            $"Reversión de la anulación de la {SiatWorker.Title(document).ToLowerInvariant()} N° {document.Number}",
            $"Le informamos que se REVIRTIÓ la anulación de la {SiatWorker.Title(document).ToLowerInvariant()} N° {document.Number} " +
            $"(código de autorización / CUF {document.Cuf}): el documento vuelve a ser válido en el Servicio de Impuestos Nacionales.", ct);
        return $"✔ Se revirtió la anulación de la {SiatWorker.Title(document).ToLowerInvariant()} N° {document.Number}: vuelve a ser válida " +
               $"(no se podrá anular otra vez). {notified ?? cannotNotify}".TrimEnd();
    }

    /// <summary>Explicación de un rechazo de la reversión (C-08).</summary>
    private static string RevertError(SiatReply reply)
    {
        var detail = reply.Describe();
        if (reply.Has(968))
        {
            return $"La anulación ya se había revertido antes: la reversión se hace una sola vez ({detail}).";
        }
        if (reply.Has(3012))
        {
            return $"La reversión está fuera de plazo (hasta el día 9 del mes siguiente a la emisión) ({detail}).";
        }
        if (reply.Has(3011))
        {
            return "El sistema todavía no está habilitado por el SIN para revertir anulaciones: termine las pruebas de reversión en el ambiente " +
                   $"piloto ({detail}).";
        }
        if (reply.Has(981))
        {
            return $"El documento no está disponible para reversión en el SIN ({detail}).";
        }
        return $"El SIN rechazó la reversión: {detail}.";
    }
}

// ------------------------------------------------------------------------------------------------ re-emisión
/// <summary>
/// Emite un documento NUEVO para la misma venta (factura) o la misma devolución (nota) después de un rechazo, una
/// observación en paquete, un descarte o una anulación sin devolución de mercadería, con los datos del comprador
/// corregidos. La venta no puede tener otro documento vigente.
/// </summary>
public sealed class ReissueFiscalDocumentHandler(IMinvDbContext db, ICurrentUser user, IClock clock, IFiscalDocumentSerializer? serializer = null)
    : IRequestHandler<ReissueFiscalDocumentCommand, FiscalDocumentRow>
{
    public async Task<FiscalDocumentRow> Handle(ReissueFiscalDocumentCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para emitir.");
        var xml = serializer ?? throw new DomainException("fiscal.no_serializer", "Este equipo no tiene el generador del XML del SIN: no puede emitir.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var original = await LifecycleDocuments.LoadAsync(db, request.DocumentId, ct);
                Guard.That(original.Status is FiscalDocumentStatus.Rejected or FiscalDocumentStatus.PackageRejected or FiscalDocumentStatus.Discarded
                        or FiscalDocumentStatus.Voided, "fiscal.reissue_state",
                    $"Solo se re-emite un documento rechazado, observado en un paquete, descartado o anulado (este está {FiscalIssuer.StatusText(original.Status)}).");
                var services = new FiscalIssueServices(db, clock, xml, userId, BillingLookups.UserCode(user));
                var replacement = original.Kind == FiscalDocumentKind.Invoice
                    ? await ReissueInvoiceAsync(services, original, request.Buyer, ct)
                    : await ReissueNoteAsync(services, original, request.Buyer, ct);
                db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(original, FiscalDocumentAction.Reissued, clock.UtcNow, userId,
                    description: $"Re-emitido como N° {replacement.Number}."));
                await db.SaveChangesAsync(ct);
                return (await IssuedDocumentRows.ForAsync(db, clock, [replacement.Id], ct))[0];
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }

    private async Task<FiscalDocument> ReissueInvoiceAsync(FiscalIssueServices services, FiscalDocument original, FiscalBuyerInput? buyerInput,
        CancellationToken ct)
    {
        var invoiceId = original.InvoiceId ?? throw new DomainException("fiscal.reissue_sale", "El documento no pertenece a una venta de M-INV.");
        var invoice = await db.Set<Invoice>().FirstAsync(i => i.Id == invoiceId, ct);
        Guard.That(invoice.Status == InvoiceStatus.Issued, "fiscal.reissue_goods_returned",
            $"La venta {invoice.Number} está anulada (se devolvió la mercadería): no se le emite otro documento.");
        await EnsureNoActiveAsync(d => d.InvoiceId == invoiceId, original.Id, ct);
        var buyer = buyerInput is null
            ? FiscalIssuer.BuyerOf(original)
            : await FiscalIssuer.ResolveBuyerAsync(db, original.TenantId, null, buyerInput, ct);
        return await FiscalIssuer.ReissueInvoiceAsync(services, original, buyer, buyerInput?.ExceptionRequested ?? original.ExceptionCode == 1, null, ct);
    }

    private async Task<FiscalDocument> ReissueNoteAsync(FiscalIssueServices services, FiscalDocument original, FiscalBuyerInput? buyerInput,
        CancellationToken ct)
    {
        Guard.That(buyerInput is null, "fiscal.note_buyer", "La nota crédito-débito lleva el comprador de la factura original: no se corrige aquí.");
        var returnId = original.SalesReturnId ?? throw new DomainException("fiscal.reissue_sale", "La nota no pertenece a una devolución de M-INV.");
        await EnsureNoActiveAsync(d => d.SalesReturnId == returnId, original.Id, ct);
        var salesReturn = await db.Set<SalesReturn>().Include(r => r.Lines).FirstAsync(r => r.Id == returnId, ct);
        var invoiceDocument = await db.Set<FiscalDocument>().Include(d => d.Lines)
                                  .Where(d => d.InvoiceId == salesReturn.InvoiceId && d.Kind == FiscalDocumentKind.Invoice
                                                                                   && d.Status == FiscalDocumentStatus.Valid)
                                  .OrderByDescending(d => d.CreatedAt).FirstOrDefaultAsync(ct)
                              ?? throw new DomainException("fiscal.note_invoice", "La factura de la venta no está válida en el SIN: no se puede emitir la nota.");
        var orderLines = await (from i in db.Set<Invoice>()
                                join l in db.Set<SalesOrderLine>() on i.SalesOrderId equals l.SalesOrderId
                                where i.Id == salesReturn.InvoiceId
                                select l).ToListAsync(ct);
        var issue = await FiscalIssuer.IssueCreditNoteAsync(services, salesReturn, invoiceDocument, orderLines, ct);
        return issue.Note ?? throw new DomainException("fiscal.note_pending", issue.Message);
    }

    private async Task EnsureNoActiveAsync(System.Linq.Expressions.Expression<Func<FiscalDocument, bool>> sameSale, Guid originalId, CancellationToken ct)
    {
        var active = await db.Set<FiscalDocument>().Where(sameSale)
            .Where(d => d.Id != originalId && (d.Status == FiscalDocumentStatus.Pending || d.Status == FiscalDocumentStatus.Valid
                                                                                       || d.Status == FiscalDocumentStatus.Offline
                                                                                       || d.Status == FiscalDocumentStatus.InPackage))
            .Select(d => (long?)d.Number).FirstOrDefaultAsync(ct);
        Guard.That(active is null, "fiscal.reissue_active", $"Ya existe un documento vigente para esta operación (N° {active}): no se emite otro.");
    }
}

// ------------------------------------------------------------------------------------------------ correo
/// <summary>Envía el XML y la representación gráfica (PDF) al correo del comprador o al indicado, y registra la entrega.</summary>
public sealed class SendFiscalDocumentEmailHandler(IMinvDbContext db, ICurrentUser user, IClock clock, ISecretProtector? protector = null,
    IMailSender? mailSender = null, IFiscalDocumentRenderer? renderer = null) : IRequestHandler<SendFiscalDocumentEmailCommand, string>
{
    public async Task<string> Handle(SendFiscalDocumentEmailCommand request, CancellationToken ct)
    {
        var document = await LifecycleDocuments.LoadAsync(db, request.DocumentId, ct);
        Guard.That(document.Status is not (FiscalDocumentStatus.Rejected or FiscalDocumentStatus.Discarded or FiscalDocumentStatus.PackageRejected
                or FiscalDocumentStatus.DuplicateToVoid or FiscalDocumentStatus.NoResponse), "mail.document_state",
            $"Este documento no se entrega al comprador ({FiscalIssuer.StatusText(document.Status)}).");
        var to = Guard.OptionalEmail(string.IsNullOrWhiteSpace(request.Email) ? document.BuyerEmail : request.Email.Trim(), "El correo")
                 ?? throw new DomainException("mail.no_recipient", "El comprador no tiene correo: indique a qué correo enviar el documento.");
        var server = await BuyerMail.ServerAsync(db, protector, ct)
                     ?? throw new DomainException("mail.not_configured",
                         "Configure y active el correo de la empresa (servidor SMTP) en Configuración › Facturación SIAT.");
        if (mailSender is null || renderer is null)
        {
            throw new DomainException("mail.unavailable", "Este equipo no puede enviar correos: el envío lo hace el servidor.");
        }
        var (ok, error) = await BuyerMail.SendDocumentAsync(db, mailSender, server, renderer, document, to, ct);
        var now = clock.UtcNow;
        db.Set<FiscalDelivery>().Add(new FiscalDelivery(document.TenantId, document.BranchId, document.Id, FiscalDeliveryChannel.Email, to, ok, error, now,
            user.UserId));
        if (ok)
        {
            db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.Delivered, now, user.UserId,
                description: $"XML y representación gráfica enviados a {to}."));
        }
        await db.SaveChangesAsync(ct);
        return ok
            ? $"✔ {SiatWorker.Title(document)} N° {document.Number} enviada a {to} (XML y PDF)."
            : throw new DomainException("mail.failed", $"No se pudo enviar el correo a {to}: {error}");
    }
}

// ------------------------------------------------------------------------------------------------ auxiliares
/// <summary>V4.1 · Lectura de documentos y errores comunes del ciclo de vida.</summary>
internal static class LifecycleDocuments
{
    public static async Task<FiscalDocument> LoadAsync(IMinvDbContext db, Guid id, CancellationToken ct) =>
        await db.Set<FiscalDocument>().Include(d => d.Lines).Include(d => d.NoteReference).FirstOrDefaultAsync(d => d.Id == id, ct)
        ?? throw new NotFoundException("El documento fiscal no existe.");

    public static SiatDocumentRef Reference(FiscalDocument document) => new(document.DocumentSector, document.DocumentType);

    public static DomainException Unavailable(SiatUnavailableException ex) =>
        new("siat.unavailable", $"No hay comunicación con el SIN: {ex.Message} Intente de nuevo en unos minutos.");
}

/// <summary>V4.1 · Correo al comprador: entrega del XML y del PDF (art. 26) y avisos de anulación y reversión (F-10).</summary>
internal static class BuyerMail
{
    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>Servidor SMTP activo de la empresa (con la contraseña descifrada SOLO en memoria), o null si no hay. V7: lo
    /// resuelve <see cref="Integration.MailServers.CompanyAsync"/>, que comparte con el despachador de la cola de correos.</summary>
    public static Task<MailServer?> ServerAsync(IMinvDbContext db, ISecretProtector? protector, CancellationToken ct) =>
        Integration.MailServers.CompanyAsync(db, protector, ct);

    /// <summary>¿Se puede avisar al comprador por correo? Devuelve el servidor o el motivo por el que hay que avisarle por otro
    /// medio (queda en la bitácora del documento).</summary>
    public static async Task<(MailServer? Server, string? CannotNotify)> NotificationTargetAsync(IMinvDbContext db, IMailSender? sender,
        ISecretProtector? protector, FiscalDocument document, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(document.BuyerEmail))
        {
            return (null, "Notifique al comprador por otro medio (código de autorización, número y motivo): no tiene correo registrado.");
        }
        MailServer? server = null;
        try
        {
            server = await ServerAsync(db, protector, ct);
        }
        catch (DomainException)
        {
            // sin la clave del correo: se notifica por otro medio
        }
        return server is null || sender is null
            ? (null, $"Notifique al comprador ({document.BuyerEmail}) por otro medio: el correo de la empresa no está disponible en este equipo.")
            : (server, null);
    }

    /// <summary>Envía un aviso al comprador y registra la entrega (lograda o fallida). Devuelve el texto para el usuario.</summary>
    public static async Task<string?> NotifyAsync(IMinvDbContext db, IMailSender? sender, MailServer? server, IClock clock, Guid? userId, Guid documentId,
        string subject, string text, CancellationToken ct)
    {
        if (server is null || sender is null)
        {
            return null;
        }
        var document = await db.Set<FiscalDocument>().FirstAsync(d => d.Id == documentId, ct);
        var to = document.BuyerEmail!;
        string? error = null;
        try
        {
            var html = $"<p>{WebUtility.HtmlEncode(text)}</p><p>{WebUtility.HtmlEncode(server.FromName)}</p>";
            await sender.SendAsync(new MailMessageSpec(server, to, subject, html, []), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = ex.Message;
        }
        var now = clock.UtcNow;
        db.Set<FiscalDelivery>().Add(new FiscalDelivery(document.TenantId, document.BranchId, document.Id, FiscalDeliveryChannel.Email, to, error is null,
            error, now, userId));
        if (error is null)
        {
            db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.Delivered, now, userId,
                description: $"Aviso al comprador enviado a {to}: {subject}."));
        }
        await db.SaveChangesAsync(ct);
        return error is null
            ? $"Se avisó al comprador ({to})."
            : $"No se pudo avisar al comprador ({to}): {error}. Notifíquele por otro medio.";
    }

    /// <summary>Entrega el documento (XML + PDF) por correo. Nunca lanza: devuelve el error para registrarlo.</summary>
    public static async Task<(bool Ok, string? Error)> SendDocumentAsync(IMinvDbContext db, IMailSender sender, MailServer server,
        IFiscalDocumentRenderer renderer, FiscalDocument document, string to, CancellationToken ct)
    {
        try
        {
            var xml = await db.Set<FiscalDocumentFile>().Where(f => f.DocumentId == document.Id).Select(f => f.Xml).FirstAsync(ct);
            var model = await FiscalPrintModelBuilder.BuildAsync(db, document.Id, ct);
            var pdf = renderer.RenderPdf(model);
            var issuer = await db.Set<SiatSettings>().Select(s => s.BusinessName).FirstOrDefaultAsync(ct) ?? server.FromName;
            var kind = document.Kind == FiscalDocumentKind.Invoice ? "factura" : "nota-credito-debito";
            var name = string.Create(CultureInfo.InvariantCulture, $"{kind}-{document.Number}");
            var title = SiatWorker.Title(document);
            var body = new StringBuilder()
                .Append("<p>Estimado cliente:</p>")
                .Append("<p>Le enviamos su ").Append(WebUtility.HtmlEncode(title.ToLowerInvariant())).Append(" N° ").Append(document.Number)
                .Append(" emitida por ").Append(WebUtility.HtmlEncode(issuer)).Append(" (código de autorización / CUF ")
                .Append(WebUtility.HtmlEncode(document.Cuf)).Append(").</p>")
                .Append("<p>Adjuntamos el archivo XML del documento fiscal digital y su representación gráfica en PDF.</p>")
                .ToString();
            await sender.SendAsync(new MailMessageSpec(server, to, $"{title} N° {document.Number} · {issuer}", body,
            [
                new MailAttachment(name + ".xml", "application/xml", Utf8.GetBytes(xml)),
                new MailAttachment(name + ".pdf", "application/pdf", pdf),
            ]), ct);
            return (true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, ex.Message);
        }
    }
}
