using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Sales;

namespace MINV.Application.Billing;

/// <summary>V4.1 · Servicios con los que se emite un documento fiscal. La emisión NO guarda ni llama al SIN (regla F-03):
/// agrega el documento, su XML validado y su bitácora al contexto; el caso de uso guarda con la venta.</summary>
public sealed record FiscalIssueServices(IMinvDbContext Db, IClock Clock, IFiscalDocumentSerializer Serializer, Guid? UserId, string UserCode);

/// <summary>V4.1 · Factura manual del talonario CAFC que se transcribe: su número, su fecha fiscal (dentro del evento), el
/// CAFC y el evento de contingencia manual.</summary>
public sealed record FiscalManualEmission(long Number, DateTime IssuedAt, string Cafc, Guid SignificantEventId);

/// <summary>V4.1 · Línea vendida que se factura (línea del pedido de M-INV con su producto).</summary>
public sealed record FiscalSaleLine(SalesOrderLine Line, ProductVariant Variant, Product Product);

/// <summary>V4.1 · Venta de M-INV que se factura: sucursal, caja (null en la API), factura interna, cliente, medio de pago,
/// líneas y datos de facturación del comprador que capturó la caja.</summary>
public sealed record FiscalSale(Guid BranchId, Guid? PosRegisterId, Invoice Invoice, Customer Customer, PaymentMethod PaymentMethod,
    IReadOnlyList<FiscalSaleLine> Lines, FiscalBuyerInput? Buyer, string? CardNumber);

/// <summary>V4.1 · Resultado de intentar emitir la nota crédito-débito de una devolución: la nota, o por qué queda pendiente.</summary>
public sealed record CreditNoteIssue(FiscalDocument? Note, string Message);

/// <summary>
/// V4.1 · Emisión de documentos fiscales (factura Compra Venta, sector 1, y nota Crédito-Débito, sector 24) en la
/// modalidad Computarizada en Línea. La usan la caja y la API (dentro de la transacción de la venta), la re-emisión, la
/// transcripción de facturas manuales CAFC, el despacho (re-emisión fuera de línea) y las devoluciones. Decide el tipo de
/// emisión según el MODO del punto de venta: en línea (CUIS y CUFD vigentes), fuera de línea (evento abierto y su CUFD) o
/// contingencia manual (no se emite: talonario CAFC). Nunca llama al SIN y nunca guarda.
/// </summary>
public static class FiscalIssuer
{
    /// <summary>Descripción (sin tildes) del evento automático por corte de internet.</summary>
    public const string InternetEvent = "CORTE DEL SERVICIO DE INTERNET";

    /// <summary>Descripción (sin tildes, inicio) del evento automático por inaccesibilidad al servicio web del SIN.</summary>
    public const string WebServiceEvent = "INACCESIBILIDAD AL SERVICIO WEB";

    /// <summary>Código por defecto si el catálogo de eventos no está sincronizado (numeración de la Etapa V: 1 internet,
    /// 2 inaccesibilidad). Solo se usa sin catálogo: la regla F-07 manda el catálogo por descripción.</summary>
    public const int DefaultInternetEventCode = 1;

    public const int DefaultWebServiceEventCode = 2;

    /// <summary>Motivos de anulación por descripción (catálogo MOTIVOS_ANULACION).</summary>
    public const string VoidReasonInvoice = "FACTURA MAL EMITIDA";

    public const string VoidReasonNote = "NOTA DE CREDITO-DEBITO MAL EMITIDA";

    // ================================================================================================ ventas
    /// <summary>
    /// Emite la factura de una venta de M-INV si la empresa factura (si no, devuelve null). Comprador: los datos capturados
    /// por la caja o la identidad fiscal del cliente; homologación obligatoria de productos, unidades y medio de pago;
    /// cantidades y precios con hasta 2 decimales; el total fiscal es el total cobrado (F-06). Con
    /// <paramref name="manual"/> transcribe una factura del talonario CAFC (número, fecha, CAFC y evento dados).
    /// </summary>
    public static async Task<FiscalDocument?> IssueForSaleAsync(FiscalIssueServices services, FiscalSale sale, FiscalManualEmission? manual,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(sale);
        var lookups = new BillingLookups(services.Db, null, services.Clock);
        if (!await lookups.IsBillingEnabledAsync(ct))
        {
            return null;
        }
        var settings = await lookups.RequireSettingsAsync(ct);
        var buyer = await ResolveBuyerAsync(services.Db, sale.Customer.TenantId, sale.Customer, sale.Buyer, ct);
        var (lines, paymentCode) = await HomologateAsync(services.Db, sale.Lines, sale.PaymentMethod, ct);
        SiatPointOfSale point;
        if (manual is not null)
        {
            var evt = await services.Db.Set<SignificantEvent>().FirstOrDefaultAsync(e => e.Id == manual.SignificantEventId, ct)
                      ?? throw new DomainException("siat.event_not_found", "El evento de contingencia no existe.");
            point = await services.Db.Set<SiatPointOfSale>().FirstAsync(p => p.Id == evt.PointOfSaleId, ct);
        }
        else
        {
            point = await lookups.PointOfSaleForAsync(sale.BranchId, sale.PosRegisterId, settings.Environment, ct);
        }
        var draft = new InvoiceDraft(buyer, sale.Invoice.Id, lines, paymentCode, sale.CardNumber, 0m, 0m, sale.Buyer?.ExceptionRequested ?? false, null);
        return await IssueInvoiceAsync(services, lookups, settings, point, draft, manual, null, ct);
    }

    /// <summary>
    /// Documento NUEVO que reemplaza a otro de la misma venta (rechazo, sin respuesta, anulación por datos erróneos): las
    /// mismas líneas congeladas, el mismo medio de pago y el comprador indicado (el mismo o el corregido). Con
    /// <paramref name="offlineEvent"/> se emite fuera de línea con el CUFD de ese evento (flujo C-07 del despacho).
    /// </summary>
    public static async Task<FiscalDocument> ReissueInvoiceAsync(FiscalIssueServices services, FiscalDocument original, FiscalBuyer buyer,
        bool exceptionRequested, SignificantEvent? offlineEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(original);
        Guard.That(original.Kind == FiscalDocumentKind.Invoice, "fiscal.reissue_kind", "Solo se re-emiten facturas: la nota se emite desde la devolución.");
        var lookups = new BillingLookups(services.Db, null, services.Clock);
        var settings = await lookups.RequireSettingsAsync(ct);
        var point = await services.Db.Set<SiatPointOfSale>().FirstAsync(p => p.Id == original.PointOfSaleId, ct);
        var lines = original.Lines.OrderBy(l => l.LineNumber)
            .Select(l => new FiscalLineInput(l.VariantId, l.ActivityCode, l.SinProductCode, l.ProductCode, l.Description, l.Quantity, l.SinUnitCode,
                l.UnitPrice, l.Discount, null, l.SerialNumber, l.Imei))
            .ToList();
        var draft = new InvoiceDraft(buyer, original.InvoiceId, lines,
            original.PaymentMethodCode ?? throw new DomainException("fiscal.payment", "La factura original no tiene método de pago."),
            original.CardNumberMasked, original.AdditionalDiscount, original.GiftCardAmount, exceptionRequested, original.Id);
        return await IssueInvoiceAsync(services, lookups, settings, point, draft, null, offlineEvent, ct);
    }

    /// <summary>Comprador congelado de un documento ya emitido (para re-emitir con los mismos datos).</summary>
    public static FiscalBuyer BuyerOf(FiscalDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new FiscalBuyer(document.CustomerId, document.CustomerCode, document.BuyerDocumentType, document.BuyerDocumentNumber,
            document.BuyerComplement, document.BuyerName, document.BuyerEmail);
    }

    // ================================================================================================ notas crédito-débito
    /// <summary>
    /// Nota crédito-débito (sector 24) de una devolución sobre una factura VÁLIDA: TODAS las líneas de la factura original
    /// como transacción 1 (copiadas del detalle congelado) y las devueltas como transacción 2 (mismo precio, descuento
    /// proporcional redondeado), comprador de la factura, emisión en línea con el CUIS y el CUFD vigentes del punto de la
    /// factura. Si la factura aún no es válida o el punto no está en línea, la nota queda pendiente (regla F-09: las notas
    /// no se emiten fuera de línea) y la emite el trabajo automático.
    /// </summary>
    public static async Task<CreditNoteIssue> IssueCreditNoteAsync(FiscalIssueServices services, SalesReturn salesReturn, FiscalDocument invoiceDocument,
        IReadOnlyList<SalesOrderLine> orderLines, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(salesReturn);
        ArgumentNullException.ThrowIfNull(invoiceDocument);
        ArgumentNullException.ThrowIfNull(orderLines);
        if (invoiceDocument.Status != FiscalDocumentStatus.Valid)
        {
            return new CreditNoteIssue(null,
                $"La factura {invoiceDocument.Number} todavía no está validada por el SIN ({StatusText(invoiceDocument.Status)}): la nota crédito-débito se emitirá automáticamente cuando lo esté.");
        }
        var db = services.Db;
        var lookups = new BillingLookups(db, null, services.Clock);
        var settings = await lookups.RequireSettingsAsync(ct);
        var point = await db.Set<SiatPointOfSale>().FirstAsync(p => p.Id == invoiceDocument.PointOfSaleId, ct);
        if (!point.IsOnline)
        {
            return new CreditNoteIssue(null,
                "El punto de venta está fuera de línea: la nota crédito-débito se emitirá automáticamente al volver la comunicación con el SIN.");
        }
        var now = services.Clock.UtcNow;
        var cuis = await lookups.CurrentCuisAsync(point.Id, now, ct);
        var cufd = await lookups.CurrentCufdAsync(point.Id, now, ct);
        if (cuis is null || cufd is null)
        {
            return new CreditNoteIssue(null,
                "El punto de venta no tiene CUIS o CUFD vigente: la nota crédito-débito se emitirá automáticamente al renovarlos.");
        }
        var zone = await lookups.ZoneAsync(ct);
        var fiscalNow = Milliseconds(lookups.FiscalNow(settings, zone));
        var branch = await lookups.BranchMappingAsync(point.BranchId, ct);
        var original = invoiceDocument.Lines.OrderBy(l => l.LineNumber).ToList();
        var lines = original.Select(l => new FiscalLineInput(l.VariantId, l.ActivityCode, l.SinProductCode, l.ProductCode, l.Description, l.Quantity,
            l.SinUnitCode, l.UnitPrice, l.Discount, 1, l.SerialNumber, l.Imei)).ToList();
        var used = new HashSet<int>();
        foreach (var returned in salesReturn.Lines)
        {
            var orderLine = orderLines.FirstOrDefault(l => l.Id == returned.SalesOrderLineId)
                            ?? throw new DomainException("return.line", "La línea devuelta no pertenece a la venta.");
            var source = original.FirstOrDefault(l => !used.Contains(l.LineNumber) && l.VariantId == orderLine.VariantId && l.Quantity == orderLine.Quantity
                                                      && l.UnitPrice == orderLine.UnitPrice)
                         ?? original.FirstOrDefault(l => !used.Contains(l.LineNumber) && l.VariantId == orderLine.VariantId)
                         ?? throw new DomainException("fiscal.note_line",
                             "Una línea devuelta no figura en la factura del SIN de la venta: no se puede armar la nota crédito-débito.");
            used.Add(source.LineNumber);
            var discount = source.Discount is { } d && d > 0 ? FiscalRules.Round2(d * returned.Quantity / source.Quantity) : 0m;
            lines.Add(new FiscalLineInput(source.VariantId, source.ActivityCode, source.SinProductCode, source.ProductCode, source.Description,
                returned.Quantity, source.SinUnitCode, source.UnitPrice, discount > 0 ? discount : null, 2));
        }
        var number = await lookups.NextNumberAsync(settings.Environment, point.Id, SiatCodes.SectorCreditDebitNote, ct);
        var legend = await lookups.PickLegendAsync(original[0].ActivityCode, ct);
        var emission = new FiscalEmission(point.BranchId, settings.Environment, settings.Nit, point.Id, branch.SiatCode, point.Code, cuis.Id, cufd.Id,
            cufd.ControlCode, SiatCodes.EmissionOnline, fiscalNow, number, legend, services.UserCode);
        var note = FiscalDocument.IssueCreditNote(settings.TenantId, emission, BuyerOf(invoiceDocument), salesReturn.Id,
            new FiscalOriginalInvoice(invoiceDocument.Id, invoiceDocument.Number, invoiceDocument.Cuf, invoiceDocument.IssuedAt, null), lines, now);
        await AttachAsync(services, lookups, settings, branch, point, cufd, note,
            $"Nota crédito-débito emitida en línea por la devolución {salesReturn.Number} (factura {invoiceDocument.Number}).", ct);
        return new CreditNoteIssue(note, $"Nota crédito-débito N° {note.Number} emitida (pendiente de envío al SIN).");
    }

    // ================================================================================================ comprador
    /// <summary>
    /// Comprador del documento: los datos capturados (si el documento es de un cliente existente se usa ese; si no, se
    /// crea el cliente con un código derivado del tipo y número y se guarda su identidad fiscal) o, sin datos capturados,
    /// la identidad fiscal del cliente de la venta. Sin ninguno: <c>fiscal.buyer_required</c> (nominatividad, II-1).
    /// </summary>
    public static async Task<FiscalBuyer> ResolveBuyerAsync(IMinvDbContext db, Guid tenantId, Customer? saleCustomer, FiscalBuyerInput? input,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (input is null)
        {
            if (saleCustomer is not { HasFiscalIdentity: true })
            {
                throw new DomainException("fiscal.buyer_required",
                    "Para facturar indique el documento del comprador (CI o NIT); para ventas menores sin documento use el NIT especial 99003." +
                    (saleCustomer is null ? string.Empty : $" El cliente {saleCustomer.Code} no tiene datos de facturación."));
            }
            return FiscalBuyer.Create(saleCustomer.Id, saleCustomer.Code, saleCustomer.DocumentType!.Value, saleCustomer.TaxId!, saleCustomer.Complement,
                saleCustomer.Name, saleCustomer.Email);
        }
        var number = (input.DocumentNumber ?? string.Empty).Trim();
        var complement = string.IsNullOrWhiteSpace(input.Complement) ? null : input.Complement.Trim().ToUpperInvariant();
        FiscalRules.EnsureBuyerDocument(input.DocumentType, number, complement);
        var customer = await FindCustomerAsync(db, input.DocumentType, number, complement, ct);
        var name = string.IsNullOrWhiteSpace(input.Name) ? null : input.Name.Trim();
        var email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim();
        if (customer is null)
        {
            var category = await DefaultCategoryAsync(db, ct);
            var code = await NewCustomerCodeAsync(db, input.DocumentType, number, complement, ct);
            customer = new Customer(tenantId, code, Truncate(name ?? DefaultBuyerName(input.DocumentType, number), 150), null, email, null, category);
            customer.SetFiscalIdentity(input.DocumentType, number, complement);
            db.Set<Customer>().Add(customer);
        }
        else if (customer.DocumentType is null)
        {
            customer.SetFiscalIdentity(input.DocumentType, number, complement);
        }
        return FiscalBuyer.Create(customer.Id, customer.Code, input.DocumentType, number, complement, name ?? customer.Name, email ?? customer.Email);
    }

    private static async Task<Customer?> FindCustomerAsync(IMinvDbContext db, int type, string number, string? complement, CancellationToken ct)
    {
        var set = db.Set<Customer>();
        var local = set.Local.FirstOrDefault(c => c.DocumentType == type && c.TaxId == number && c.Complement == complement);
        if (local is not null)
        {
            return local;
        }
        return await set.FirstOrDefaultAsync(c => c.DocumentType == type && c.TaxId == number && c.Complement == complement, ct)
               // Cliente anterior a la V4.1 con ese número pero sin tipo de documento: se completa su identidad fiscal
               ?? await set.Where(c => c.DocumentType == null && c.TaxId == number && c.IsActive).OrderBy(c => c.Code).FirstOrDefaultAsync(ct);
    }

    private static async Task<Guid> DefaultCategoryAsync(IMinvDbContext db, CancellationToken ct)
    {
        var categories = db.Set<CustomerCategory>();
        return await categories.Where(c => c.Code == "GENERAL").Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct)
               ?? await (from c in db.Set<Customer>()
                         where c.Code == "CF"
                         select (Guid?)c.CustomerCategoryId).FirstOrDefaultAsync(ct)
               ?? await categories.OrderBy(c => c.Code).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct)
               ?? throw new DomainException("customer.category", "La empresa no tiene categorías de clientes: cree una antes de facturar a clientes nuevos.");
    }

    /// <summary>Código del cliente nuevo: tipo + número (+ complemento), p. ej. «CI-4567890» o «NIT-1003579028», único.</summary>
    private static async Task<string> NewCustomerCodeAsync(IMinvDbContext db, int type, string number, string? complement, CancellationToken ct)
    {
        var prefix = type switch
        {
            SiatCodes.DocumentCi => "CI",
            SiatCodes.DocumentCex => "CEX",
            SiatCodes.DocumentPassport => "PAS",
            SiatCodes.DocumentNit => "NIT",
            _ => "OD",
        };
        var body = new string(number.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        if (complement is not null)
        {
            body += "-" + new string(complement.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        }
        body = body.Length == 0 ? "SN" : body;
        var baseCode = prefix + "-" + body;
        baseCode = baseCode.Length > 20 ? baseCode[..20] : baseCode;
        var code = baseCode;
        for (var n = 2; await CodeTakenAsync(db, code, ct); n++)
        {
            var suffix = "-" + n.ToString(CultureInfo.InvariantCulture);
            code = (baseCode.Length + suffix.Length > 20 ? baseCode[..(20 - suffix.Length)] : baseCode) + suffix;
        }
        return code;
    }

    private static async Task<bool> CodeTakenAsync(IMinvDbContext db, string code, CancellationToken ct) =>
        db.Set<Customer>().Local.Any(c => c.Code == code) || await db.Set<Customer>().AnyAsync(c => c.Code == code, ct);

    private static string DefaultBuyerName(int type, string number) => number switch
    {
        SiatCodes.SpecialMinorSales => "VENTAS MENORES DEL DIA",
        SiatCodes.SpecialTaxControl => "CONTROL TRIBUTARIO",
        SiatCodes.SpecialConsulates => "CONSULADOS Y EMBAJADAS",
        _ => type == SiatCodes.DocumentNit ? $"NIT {number}" : $"Cliente {number}",
    };

    // ================================================================================================ homologación
    /// <summary>Líneas homologadas de la venta y código SIN del medio de pago. Sin homologación de un producto, una unidad o
    /// el medio de pago, la venta se rechaza con la lista de lo que falta (regla F-07).</summary>
    private static async Task<(List<FiscalLineInput> Lines, int PaymentCode)> HomologateAsync(IMinvDbContext db, IReadOnlyList<FiscalSaleLine> lines,
        PaymentMethod method, CancellationToken ct)
    {
        Guard.That(lines.Count > 0, "fiscal.lines", "La venta no tiene líneas para facturar.");
        var productIds = lines.Select(l => l.Product.Id).Distinct().ToList();
        var unitIds = lines.Select(l => l.Line.UnitId).Distinct().ToList();
        var products = await db.Set<ProductSiatCode>().Where(p => productIds.Contains(p.ProductId)).ToListAsync(ct);
        var units = await db.Set<UnitSiatCode>().Where(u => unitIds.Contains(u.UnitId)).ToListAsync(ct);
        var payment = await db.Set<PaymentMethodSiatCode>().Where(p => p.PaymentMethodId == method.Id).Select(p => (int?)p.SinPaymentMethodCode)
            .FirstOrDefaultAsync(ct);
        var missing = new List<string>();
        var missingProducts = lines.Where(l => products.All(p => p.ProductId != l.Product.Id)).Select(l => l.Variant.Sku).Distinct().ToList();
        if (missingProducts.Count > 0)
        {
            missing.Add((missingProducts.Count == 1 ? "producto " : "productos ") + string.Join(", ", missingProducts));
        }
        var missingUnitIds = unitIds.Where(id => units.All(u => u.UnitId != id)).ToList();
        if (missingUnitIds.Count > 0)
        {
            var names = await db.Set<UnitOfMeasure>().Where(u => missingUnitIds.Contains(u.Id)).Select(u => u.Code).ToListAsync(ct);
            missing.Add((names.Count == 1 ? "unidad " : "unidades ") + string.Join(", ", names.Order(StringComparer.Ordinal)));
        }
        if (payment is null)
        {
            missing.Add($"medio de pago {method.Name}");
        }
        if (missing.Count > 0)
        {
            throw new DomainException("fiscal.not_homologated",
                $"Falta homologar con los catálogos del SIN: {string.Join("; ", missing)}. Complételo en Facturación › Homologación antes de facturar.");
        }
        var result = new List<FiscalLineInput>();
        foreach (var (line, variant, product) in lines)
        {
            Guard.That(FiscalRules.HasAtMostDecimals(line.Quantity, 2), "fiscal.quantity_decimals",
                $"{variant.Sku}: la factura admite cantidades con hasta 2 decimales (se vendió {Quantities.Format(line.Quantity)}). Ajuste la cantidad.");
            Guard.That(FiscalRules.HasAtMostDecimals(line.UnitPrice, 2), "fiscal.price_decimals",
                $"{variant.Sku}: el precio de lista {line.UnitPrice.ToString(CultureInfo.InvariantCulture)} tiene más de 2 decimales: corríjalo en la lista de precios.");
            var code = products.First(p => p.ProductId == product.Id);
            var unit = units.First(u => u.UnitId == line.UnitId);
            // descuento = round2(cantidad × precio) − importe cobrado: así el total fiscal es exactamente el total cobrado (F-06)
            var discount = FiscalRules.Round2(line.Quantity * line.UnitPrice) - line.Amount;
            var description = variant.Name is { Length: > 0 } variantName ? $"{product.Name} · {variantName}" : product.Name;
            result.Add(new FiscalLineInput(variant.Id, code.ActivityCode, code.SinProductCode, variant.Sku, Truncate(description, 500), line.Quantity,
                unit.SinUnitCode, line.UnitPrice, discount > 0 ? discount : null));
        }
        return (result, payment!.Value);
    }

    // ================================================================================================ emisión (núcleo)
    /// <summary>Datos de la factura a emitir (ya homologados).</summary>
    private sealed record InvoiceDraft(FiscalBuyer Buyer, Guid? InvoiceId, IReadOnlyList<FiscalLineInput> Lines, int PaymentMethodCode, string? CardNumber,
        decimal AdditionalDiscount, decimal GiftCardAmount, bool ExceptionRequested, Guid? ReplacesDocumentId);

    /// <summary>Cómo se emite: códigos y tipo de emisión (y el evento si es fuera de línea).</summary>
    private sealed record EmissionPlan(Guid CuisId, SiatCufd Cufd, int EmissionType, SignificantEvent? Event);

    private static async Task<FiscalDocument> IssueInvoiceAsync(FiscalIssueServices services, BillingLookups lookups, SiatSettings settings,
        SiatPointOfSale point, InvoiceDraft draft, FiscalManualEmission? manual, SignificantEvent? offlineEvent, CancellationToken ct)
    {
        var now = services.Clock.UtcNow;
        var zone = await lookups.ZoneAsync(ct);
        var fiscalNow = Milliseconds(lookups.FiscalNow(settings, zone));
        var branch = await lookups.BranchMappingAsync(point.BranchId, ct);
        EmissionPlan plan;
        long number;
        DateTime issuedAt;
        string description;
        if (manual is not null)
        {
            var evt = await services.Db.Set<SignificantEvent>().FirstAsync(e => e.Id == manual.SignificantEventId, ct);
            var cufd = await services.Db.Set<SiatCufd>().FirstAsync(c => c.Id == evt.EventCufdId, ct);
            plan = new EmissionPlan(cufd.CuisId, cufd, SiatCodes.EmissionOffline, evt);
            number = manual.Number;
            issuedAt = Milliseconds(manual.IssuedAt);
            description = $"Factura manual transcrita del talonario CAFC {manual.Cafc} (evento {evt.Description}).";
        }
        else
        {
            plan = await PlanAsync(services, lookups, point, now, fiscalNow, offlineEvent, ct);
            number = await lookups.NextNumberAsync(settings.Environment, point.Id, SiatCodes.SectorPurchaseSale, ct);
            issuedAt = fiscalNow;
            description = plan.EmissionType == SiatCodes.EmissionOnline
                ? $"Emitida en línea (punto de venta {point.Code})."
                : $"Emitida fuera de línea (punto de venta {point.Code}, evento {plan.Event!.Description}, CUFD del evento).";
        }
        if (draft.ReplacesDocumentId is not null)
        {
            description += " Reemplaza a un documento anterior de la misma venta.";
        }
        var legend = await lookups.PickLegendAsync(draft.Lines[0].ActivityCode, ct);
        var emission = new FiscalEmission(point.BranchId, settings.Environment, settings.Nit, point.Id, branch.SiatCode, point.Code, plan.CuisId,
            plan.Cufd.Id, plan.Cufd.ControlCode, plan.EmissionType, issuedAt, number, legend, services.UserCode, plan.Event?.Id, manual?.Cafc);
        var document = FiscalDocument.IssueInvoice(settings.TenantId, emission, draft.Buyer, draft.InvoiceId, draft.Lines, draft.PaymentMethodCode,
            draft.CardNumber, draft.AdditionalDiscount, draft.GiftCardAmount, draft.ExceptionRequested, now, draft.ReplacesDocumentId);
        await AttachAsync(services, lookups, settings, branch, point, plan.Cufd, document, description, ct);
        return document;
    }

    /// <summary>
    /// Tipo de emisión según el modo del punto: con un evento dado, fuera de línea con su CUFD; en contingencia manual no se
    /// emite (talonario CAFC); fuera de línea o recuperando, con el evento abierto (o uno automático nuevo); en línea con
    /// CUIS y CUFD vigentes y, si el CUFD venció pero el último es usable (≤ 72 h), abre un evento automático y emite fuera
    /// de línea. Sin ningún CUFD usable: <c>siat.no_cufd</c>.
    /// </summary>
    private static async Task<EmissionPlan> PlanAsync(FiscalIssueServices services, BillingLookups lookups, SiatPointOfSale point, DateTimeOffset now,
        DateTime fiscalNow, SignificantEvent? offlineEvent, CancellationToken ct)
    {
        var db = services.Db;
        if (offlineEvent is not null)
        {
            var eventCufd = await db.Set<SiatCufd>().FirstAsync(c => c.Id == offlineEvent.EventCufdId, ct);
            return new EmissionPlan(eventCufd.CuisId, eventCufd, SiatCodes.EmissionOffline, offlineEvent);
        }
        if (point.Mode == SiatConnectionMode.ManualContingency)
        {
            throw new DomainException("fiscal.manual_contingency",
                "Contingencia manual: emita la factura del talonario CAFC y transcríbala después (Facturación › Contingencia).");
        }
        if (point.EmitsOffline)
        {
            var open = await OpenOfflineEventAsync(db, point.Id, ct);
            if (open is null)
            {
                var latest = await lookups.LatestCufdAsync(point.Id, ct);
                if (latest is null || !latest.IsUsableOfflineAt(now))
                {
                    throw NoCufd();
                }
                open = await OpenAutomaticEventAsync(db, point, latest, WebServiceEvent, DefaultWebServiceEventCode, fiscalNow, now, services.UserId, ct);
            }
            var cufd = await db.Set<SiatCufd>().FirstAsync(c => c.Id == open.EventCufdId, ct);
            if (!cufd.IsUsableOfflineAt(now))
            {
                throw new DomainException("siat.no_cufd",
                    "El CUFD del evento fuera de línea superó las 72 horas: no se puede facturar hasta recuperar la comunicación con el SIN.");
            }
            return new EmissionPlan(cufd.CuisId, cufd, SiatCodes.EmissionOffline, open);
        }
        var cuis = await lookups.RequireCuisAsync(point.Id, now, ct);
        var current = await lookups.CurrentCufdAsync(point.Id, now, ct);
        if (current is not null)
        {
            return new EmissionPlan(cuis.Id, current, SiatCodes.EmissionOnline, null);
        }
        var last = await lookups.LatestCufdAsync(point.Id, ct);
        if (last is null || !last.IsUsableOfflineAt(now))
        {
            throw NoCufd();
        }
        // El CUFD del día no se pudo renovar: fuera de línea con el último (vigencia ampliada a 72 h, caso E de 03 §5)
        var evt = await OpenAutomaticEventAsync(db, point, last, WebServiceEvent, DefaultWebServiceEventCode, fiscalNow, now, services.UserId, ct);
        return new EmissionPlan(last.CuisId, last, SiatCodes.EmissionOffline, evt);
    }

    private static DomainException NoCufd() => new("siat.no_cufd",
        "El punto de venta no tiene un CUFD utilizable (vigente o de las últimas 72 horas): solicítelo en Facturación › Estado SIAT.");

    /// <summary>XML (validado contra el XSD), archivo con la huella del GZIP y bitácora «Emitido»; agrega todo al contexto.</summary>
    private static async Task AttachAsync(FiscalIssueServices services, BillingLookups lookups, SiatSettings settings, SiatBranch branch,
        SiatPointOfSale point, SiatCufd cufd, FiscalDocument document, string description, CancellationToken ct)
    {
        var xml = services.Serializer.BuildXml(document, await lookups.XmlContextAsync(settings, branch, point, cufd, ct));
        var now = services.Clock.UtcNow;
        var db = services.Db;
        db.Set<FiscalDocument>().Add(document);
        db.Set<FiscalDocumentFile>().Add(new FiscalDocumentFile(document.TenantId, document.BranchId, document.Id, xml,
            services.Serializer.Sha256Hex(services.Serializer.Gzip(xml)), now));
        db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.Issued, now, services.UserId, description: description));
    }

    // ================================================================================================ eventos significativos
    /// <summary>Evento fuera de línea ABIERTO de un punto de venta (el que usan las facturas emitidas sin comunicación).</summary>
    public static async Task<SignificantEvent?> OpenOfflineEventAsync(IMinvDbContext db, Guid pointOfSaleId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Set<SignificantEvent>().Local.FirstOrDefault(e => e.PointOfSaleId == pointOfSaleId && e.Kind == SignificantEventKind.Offline
                                                                    && e.Status == SignificantEventStatus.Open)
               ?? await db.Set<SignificantEvent>().Where(e => e.PointOfSaleId == pointOfSaleId && e.Kind == SignificantEventKind.Offline
                                                             && e.Status == SignificantEventStatus.Open)
                   .OrderByDescending(e => e.StartedAt).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Abre un evento fuera de línea AUTOMÁTICO (código del catálogo elegido por descripción, regla F-07) con el CUFD dado
    /// como CUFD del evento, y pasa el punto de venta a fuera de línea. No llama al SIN: el evento se registra al recuperar.
    /// </summary>
    public static async Task<SignificantEvent> OpenAutomaticEventAsync(IMinvDbContext db, SiatPointOfSale point, SiatCufd eventCufd,
        string descriptionKey, int fallbackCode, DateTime fiscalNow, DateTimeOffset now, Guid? userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(eventCufd);
        var (code, description) = await EventFromCatalogAsync(db, descriptionKey, fallbackCode, ct);
        var evt = new SignificantEvent(point.TenantId, point.BranchId, point.Id, point.Environment, SignificantEventKind.Offline, code, description,
            Milliseconds(fiscalNow), eventCufd.Id, null, now, userId);
        db.Set<SignificantEvent>().Add(evt);
        point.GoOffline(now);
        return evt;
    }

    /// <summary>Código y descripción del catálogo «Eventos Significativos» cuya descripción contiene el texto (sin tildes ni
    /// mayúsculas); sin catálogo sincronizado, el código por defecto con el texto buscado.</summary>
    public static async Task<(int Code, string Description)> EventFromCatalogAsync(IMinvDbContext db, string descriptionKey, int fallbackCode,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var items = await db.Set<SiatCatalogItem>().Where(i => i.Catalog == SiatCatalogNames.SignificantEvents && i.IsCurrent)
            .OrderBy(i => i.Code).ToListAsync(ct);
        var key = Plain(descriptionKey);
        var match = items.FirstOrDefault(i => Plain(i.Description).Contains(key, StringComparison.Ordinal));
        return match is not null ? (match.Code, match.Description) : (fallbackCode, descriptionKey);
    }

    /// <summary>Código del catálogo «Motivos de Anulación» por descripción (p. ej. «FACTURA MAL EMITIDA»), o null.</summary>
    public static async Task<int?> VoidReasonFromCatalogAsync(IMinvDbContext db, string descriptionKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var items = await db.Set<SiatCatalogItem>().Where(i => i.Catalog == SiatCatalogNames.VoidReasons && i.IsCurrent).OrderBy(i => i.Code)
            .ToListAsync(ct);
        var key = Plain(descriptionKey);
        return items.FirstOrDefault(i => Plain(i.Description) == key)?.Code
               ?? items.FirstOrDefault(i => Plain(i.Description).Contains(key, StringComparison.Ordinal))?.Code;
    }

    /// <summary>¿La descripción del catálogo corresponde a una contingencia MANUAL (energía, virus/software, hardware)?</summary>
    public static bool IsManualEvent(string description)
    {
        var text = Plain(description);
        return text.Contains("ENERGIA", StringComparison.Ordinal) || text.Contains("VIRUS", StringComparison.Ordinal)
                                                                   || text.Contains("SOFTWARE", StringComparison.Ordinal)
                                                                   || text.Contains("HARDWARE", StringComparison.Ordinal);
    }

    /// <summary>¿La descripción corresponde a un evento fuera de línea AUTOMÁTICO (internet o acceso al servicio del SIN)?</summary>
    public static bool IsAutomaticEvent(string description)
    {
        var text = Plain(description);
        return !IsManualEvent(description)
               && (text.Contains("INTERNET", StringComparison.Ordinal) || text.Contains("INACCESIBILIDAD", StringComparison.Ordinal));
    }

    // ================================================================================================ auxiliares
    /// <summary>Hora fiscal truncada a milisegundos (así viaja en el CUF, el XML y los eventos).</summary>
    public static DateTime Milliseconds(DateTime value) =>
        DateTime.SpecifyKind(value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMillisecond)), DateTimeKind.Unspecified);

    /// <summary>Texto en mayúsculas y sin tildes (para comparar descripciones de catálogos).</summary>
    public static string Plain(string? text)
    {
        var decomposed = (text ?? string.Empty).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        {
            sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant().Trim();
    }

    /// <summary>Estado de un documento en palabras (mensajes para el usuario).</summary>
    public static string StatusText(FiscalDocumentStatus status) => status switch
    {
        FiscalDocumentStatus.Pending => "pendiente de envío",
        FiscalDocumentStatus.Valid => "válido",
        FiscalDocumentStatus.Rejected => "rechazado",
        FiscalDocumentStatus.NoResponse => "sin respuesta del SIN",
        FiscalDocumentStatus.Offline => "emitido fuera de línea",
        FiscalDocumentStatus.InPackage => "en un paquete por validar",
        FiscalDocumentStatus.PackageRejected => "observado en el paquete",
        FiscalDocumentStatus.DuplicateToVoid => "duplicado por anular",
        FiscalDocumentStatus.Voided => "anulado",
        FiscalDocumentStatus.Discarded => "descartado",
        _ => status.ToString(),
    };

    private static string Truncate(string value, int max) => value.Length > max ? value[..max] : value;
}
