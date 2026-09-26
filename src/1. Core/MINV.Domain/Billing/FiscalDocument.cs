using MINV.Domain.Common;
using MINV.Domain.Events;

namespace MINV.Domain.Billing;

/// <summary>Comprador tal como figura en el documento (instantánea legal: no cambia si cambia el cliente).</summary>
public sealed record FiscalBuyer(Guid? CustomerId, string CustomerCode, int DocumentType, string DocumentNumber, string? Complement,
    string? Name, string? Email)
{
    public static FiscalBuyer Create(Guid? customerId, string customerCode, int documentType, string documentNumber, string? complement,
        string? name, string? email)
    {
        FiscalRules.EnsureBuyerDocument(documentType, documentNumber, complement);
        return new FiscalBuyer(customerId, Guard.Text(customerCode, "El código de cliente", 100), documentType, documentNumber.Trim(),
            Guard.OptionalText(complement, "El complemento", 5), Guard.OptionalText(name, "El nombre o razón social", 500),
            Guard.OptionalEmail(email, "El correo del comprador"));
    }

    /// <summary>NIT especial 99001/99002/99003 o documento que no se valida: requiere código de excepción 1.</summary>
    public bool IsSpecial => DocumentType == SiatCodes.DocumentNit && SiatCodes.IsSpecialDocument(DocumentNumber);
}

/// <summary>Línea del documento a emitir (datos ya homologados).</summary>
public sealed record FiscalLineInput(Guid? VariantId, string ActivityCode, int SinProductCode, string ProductCode, string Description,
    decimal Quantity, int SinUnitCode, decimal UnitPrice, decimal? Discount, int? TransactionCode = null, string? SerialNumber = null,
    string? Imei = null);

/// <summary>Dónde y con qué códigos se emite: punto de venta, CUIS, CUFD (y su código de control), ambiente y tipo de emisión.</summary>
public sealed record FiscalEmission(Guid BranchId, int Environment, long Nit, Guid PointOfSaleId, int BranchCode, int PointOfSaleCode,
    Guid CuisId, Guid CufdId, string CufdControlCode, int EmissionType, DateTime IssuedAt, long Number, string Legend, string UserCode,
    Guid? SignificantEventId = null, string? Cafc = null);

/// <summary>Factura original de una nota crédito-débito.</summary>
public sealed record FiscalOriginalInvoice(Guid? DocumentId, long Number, string CufOrAuthorization, DateTime IssuedAt, decimal? DiscountShare);

/// <summary>
/// V4.1 · Documento fiscal digital (factura Compra Venta, sector 1, o nota Crédito-Débito, sector 24) en la modalidad
/// Computarizada en Línea. Agregado: sus líneas, su archivo XML y su bitácora solo cambian por sus métodos.
/// Los totales NO se guardan (se derivan de las líneas con las fórmulas del SIN).
/// </summary>
public sealed class FiscalDocument : Entity, IBranchScoped, IConcurrencyAware, IAggregateRoot, IHasDomainEvents
{
    private readonly List<FiscalDocumentLine> _lines = new();
    private readonly List<IDomainEvent> _events = new();

    private FiscalDocument()
    {
    }

    private FiscalDocument(Guid tenantId, FiscalDocumentKind kind, FiscalEmission emission, FiscalBuyer buyer, int exceptionCode,
        DateTimeOffset createdAt)
        : base(tenantId)
    {
        ArgumentNullException.ThrowIfNull(emission);
        ArgumentNullException.ThrowIfNull(buyer);
        BranchId = Guard.NotEmpty(emission.BranchId, nameof(emission.BranchId));
        Guard.That(emission.Environment is SiatCodes.EnvironmentProduction or SiatCodes.EnvironmentTest, "siat.environment",
            "El ambiente es 1 (producción) o 2 (pruebas y piloto).");
        Guard.That(emission.EmissionType is SiatCodes.EmissionOnline or SiatCodes.EmissionOffline, "fiscal.emission",
            "El tipo de emisión es 1 (en línea) o 2 (fuera de línea).");
        Guard.That(exceptionCode is 0 or 1, "fiscal.exception", "El código de excepción es 0 o 1.");
        Kind = kind;
        Environment = emission.Environment;
        PointOfSaleId = Guard.NotEmpty(emission.PointOfSaleId, nameof(emission.PointOfSaleId));
        CuisId = Guard.NotEmpty(emission.CuisId, nameof(emission.CuisId));
        CufdId = Guard.NotEmpty(emission.CufdId, nameof(emission.CufdId));
        DocumentSector = kind == FiscalDocumentKind.Invoice ? SiatCodes.SectorPurchaseSale : SiatCodes.SectorCreditDebitNote;
        DocumentType = kind == FiscalDocumentKind.Invoice ? SiatCodes.InvoiceWithTaxCredit : SiatCodes.AdjustmentDocument;
        EmissionType = emission.EmissionType;
        Guard.That(emission.Number is > 0 and <= 9_999_999_999, "fiscal.number", "El número va de 1 a 9999999999.");
        Number = emission.Number;
        IssuedAt = DateTime.SpecifyKind(emission.IssuedAt, DateTimeKind.Unspecified);
        Cuf = Billing.Cuf.Generate(new Billing.Cuf.Parts(emission.Nit, IssuedAt, emission.BranchCode, SiatCodes.ModalityComputerized,
            EmissionType, DocumentType, DocumentSector, Number, emission.PointOfSaleCode), emission.CufdControlCode);
        CustomerId = buyer.CustomerId;
        CustomerCode = buyer.CustomerCode;
        BuyerDocumentType = buyer.DocumentType;
        BuyerDocumentNumber = buyer.DocumentNumber;
        BuyerComplement = buyer.Complement;
        BuyerName = buyer.Name;
        BuyerEmail = buyer.Email;
        ExceptionCode = exceptionCode;
        Legend = Guard.Text(emission.Legend, "La leyenda", 200);
        UserCode = Guard.Text(emission.UserCode, "El usuario emisor", 100);
        SignificantEventId = Guard.NotEmptyIfPresent(emission.SignificantEventId, nameof(emission.SignificantEventId));
        Cafc = Guard.OptionalText(emission.Cafc, "El CAFC", 50);
        Guard.That(Cafc is null || EmissionType == SiatCodes.EmissionOffline, "fiscal.cafc", "Una factura con CAFC se envía fuera de línea.");
        Status = EmissionType == SiatCodes.EmissionOnline ? FiscalDocumentStatus.Pending : FiscalDocumentStatus.Offline;
        CreatedAt = createdAt;
        CurrencyCode = 1;
        ExchangeRate = 1m;
    }

    public Guid BranchId { get; private set; }

    public int Environment { get; private set; }

    public FiscalDocumentKind Kind { get; private set; }

    public Guid PointOfSaleId { get; private set; }

    public Guid CuisId { get; private set; }

    public Guid CufdId { get; private set; }

    public int DocumentSector { get; private set; }

    public int DocumentType { get; private set; }

    public int EmissionType { get; private set; }

    /// <summary>Número correlativo por (ambiente, punto de venta, documento sector).</summary>
    public long Number { get; private set; }

    public string Cuf { get; private set; } = string.Empty;

    /// <summary>Fecha y hora fiscal (hora del SIN en la zona de la empresa, con milisegundos, sin zona): fechaEmision y CUF.</summary>
    public DateTime IssuedAt { get; private set; }

    /// <summary>Venta de M-INV que factura (facturas).</summary>
    public Guid? InvoiceId { get; private set; }

    /// <summary>Devolución de M-INV que documenta (notas crédito-débito).</summary>
    public Guid? SalesReturnId { get; private set; }

    /// <summary>Documento al que reemplaza (re-emisión tras rechazo, sin respuesta o anulación por datos erróneos).</summary>
    public Guid? ReplacesDocumentId { get; private set; }

    public Guid? CustomerId { get; private set; }

    public string CustomerCode { get; private set; } = string.Empty;

    public int BuyerDocumentType { get; private set; }

    public string BuyerDocumentNumber { get; private set; } = string.Empty;

    public string? BuyerComplement { get; private set; }

    public string? BuyerName { get; private set; }

    public string? BuyerEmail { get; private set; }

    /// <summary>codigoMetodoPago homologado (facturas).</summary>
    public int? PaymentMethodCode { get; private set; }

    /// <summary>Tarjeta SIEMPRE enmascarada (4 primeros + ceros + 4 últimos).</summary>
    public string? CardNumberMasked { get; private set; }

    public int CurrencyCode { get; private set; }

    public decimal ExchangeRate { get; private set; }

    public decimal AdditionalDiscount { get; private set; }

    public decimal GiftCardAmount { get; private set; }

    public int ExceptionCode { get; private set; }

    public string? Cafc { get; private set; }

    /// <summary>Leyenda de la Ley 453 elegida al azar al emitir (queda fija para la reimpresión).</summary>
    public string Legend { get; private set; } = string.Empty;

    /// <summary>Usuario emisor descriptivo (campo «usuario» del XML).</summary>
    public string UserCode { get; private set; } = string.Empty;

    public FiscalDocumentStatus Status { get; private set; }

    /// <summary>La anulación se revirtió (una sola vez): ya no se puede volver a anular.</summary>
    public bool IsReverted { get; private set; }

    public string? ReceptionCode { get; private set; }

    public int? LastSiatCode { get; private set; }

    public Guid? SignificantEventId { get; private set; }

    public Guid? PackageId { get; private set; }

    /// <summary>Posición (1..500) dentro del paquete: el SIN informa los errores por número de archivo.</summary>
    public int? PackagePosition { get; private set; }

    public int? VoidReasonCode { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    public DateTimeOffset? RevertedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<FiscalDocumentLine> Lines => _lines;

    /// <summary>Datos de la factura original (solo notas).</summary>
    public FiscalNoteReference? NoteReference { get; private set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events;

    public void ClearDomainEvents() => _events.Clear();

    // ------------------------------------------------------------------------------------------------ totales derivados
    /// <summary>Σ subTotal de las líneas (facturas) o de las líneas originales (notas, transacción 1).</summary>
    public decimal LinesSubtotal => _lines.Where(l => l.TransactionCode is null or 1).Sum(l => l.Subtotal);

    /// <summary>montoTotal = Σ subTotal − descuentoAdicional (facturas).</summary>
    public decimal TotalAmount => Kind == FiscalDocumentKind.Invoice ? LinesSubtotal - AdditionalDiscount : ReturnedTotal;

    /// <summary>montoTotalSujetoIva = montoTotal − montoGiftCard (facturas); en notas, el monto devuelto.</summary>
    public decimal TotalSubjectToVat => Kind == FiscalDocumentKind.Invoice ? TotalAmount - GiftCardAmount : ReturnedTotal;

    /// <summary>Débito fiscal (facturas) o crédito/débito efectivo (notas): 13 % de la base.</summary>
    public decimal VatAmount => FiscalRules.Vat(TotalSubjectToVat);

    /// <summary>montoTotalOriginal de la nota = Σ subTotal de las líneas con transacción 1.</summary>
    public decimal OriginalTotal => _lines.Where(l => l.TransactionCode == 1).Sum(l => l.Subtotal);

    /// <summary>montoTotalDevuelto = Σ subTotal (transacción 2) − descuento prorrateado.</summary>
    public decimal ReturnedTotal => _lines.Where(l => l.TransactionCode == 2).Sum(l => l.Subtotal) - (NoteReference?.DiscountShare ?? 0m);

    public bool IsActive => Status is not (FiscalDocumentStatus.Voided or FiscalDocumentStatus.Rejected or FiscalDocumentStatus.Discarded
        or FiscalDocumentStatus.PackageRejected);

    // ------------------------------------------------------------------------------------------------ emisión
    /// <summary>Emite una factura Compra Venta (sector 1) para una venta de M-INV.</summary>
    public static FiscalDocument IssueInvoice(Guid tenantId, FiscalEmission emission, FiscalBuyer buyer, Guid? invoiceId,
        IReadOnlyList<FiscalLineInput> lines, int paymentMethodCode, string? cardNumber, decimal additionalDiscount, decimal giftCardAmount,
        bool exceptionRequested, DateTimeOffset createdAt, Guid? replacesDocumentId = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        Guard.That(lines.Count is >= 1 and <= SiatCodes.MaxLinesPerDocument, "fiscal.lines",
            "Una factura lleva de 1 a 500 líneas.");
        // Excepción: NIT especial, pedido expreso, o NIT fuera de línea (el SIN no lo puede validar en ese momento).
        var exception = buyer.IsSpecial || (buyer.DocumentType == SiatCodes.DocumentNit
                                             && (exceptionRequested || emission.EmissionType == SiatCodes.EmissionOffline)) ? 1 : 0;
        var doc = new FiscalDocument(tenantId, FiscalDocumentKind.Invoice, emission, buyer, exception, createdAt)
        {
            InvoiceId = Guard.NotEmptyIfPresent(invoiceId, nameof(invoiceId)),
            ReplacesDocumentId = Guard.NotEmptyIfPresent(replacesDocumentId, nameof(replacesDocumentId)),
            PaymentMethodCode = paymentMethodCode is >= 1 and <= 999 ? paymentMethodCode
                : throw new DomainException("fiscal.payment", "El método de pago SIN no es válido."),
            CardNumberMasked = string.IsNullOrWhiteSpace(cardNumber) ? null : FiscalRules.MaskCard(cardNumber),
            AdditionalDiscount = FiscalRules.Round2(Guard.NonNegative(additionalDiscount, "El descuento adicional")),
            GiftCardAmount = FiscalRules.Round2(Guard.NonNegative(giftCardAmount, "El monto de gift card")),
        };
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            Guard.That(l.TransactionCode is null, "fiscal.line_tx", "Las líneas de una factura no llevan código de transacción.");
            Guard.That(FiscalRules.HasAtMostDecimals(l.Quantity, 2) && FiscalRules.HasAtMostDecimals(l.UnitPrice, 2)
                                                                  && FiscalRules.HasAtMostDecimals(l.Discount ?? 0m, 2),
                "fiscal.decimals", $"Línea {i + 1} ({l.ProductCode}): la factura admite cantidades, precios y descuentos con 2 decimales.");
            doc._lines.Add(new FiscalDocumentLine(tenantId, doc.BranchId, doc.Id, i + 1, l));
        }
        Guard.That(doc.TotalAmount > 0, "fiscal.total", "El monto total de la factura debe ser mayor que 0.");
        Guard.That(doc.TotalSubjectToVat >= 0, "fiscal.gift_card", "La gift card no puede superar el monto total.");
        return doc;
    }

    /// <summary>Emite una nota crédito-débito (sector 24): TODAS las líneas de la factura original (transacción 1) más
    /// las devueltas (transacción 2).</summary>
    public static FiscalDocument IssueCreditNote(Guid tenantId, FiscalEmission emission, FiscalBuyer buyer, Guid? salesReturnId,
        FiscalOriginalInvoice original, IReadOnlyList<FiscalLineInput> lines, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(lines);
        Guard.That(emission.EmissionType == SiatCodes.EmissionOnline, "fiscal.note_offline",
            "Las notas crédito-débito se envían solo en línea (no tienen paquetes de contingencia).");
        Guard.That(lines.Count is >= 2 and <= SiatCodes.MaxLinesPerDocument, "fiscal.note_lines", "Una nota lleva de 2 a 500 líneas.");
        Guard.That(lines.Any(l => l.TransactionCode == 1) && lines.Any(l => l.TransactionCode == 2), "fiscal.note_lines",
            "La nota necesita las líneas de la factura original (1) y las devueltas (2).");
        Guard.That(lines.All(l => l.TransactionCode is 1 or 2), "fiscal.note_tx", "El código de transacción es 1 (original) o 2 (devuelto).");
        Guard.That(emission.IssuedAt <= FiscalRules.CreditNoteDeadline(original.IssuedAt), "fiscal.note_deadline",
            "La nota crédito-débito se emite hasta 18 meses después de la factura original.");
        Guard.That(emission.IssuedAt >= original.IssuedAt, "fiscal.note_date", "La nota no puede ser anterior a la factura original.");
        var exception = buyer.IsSpecial ? 1 : 0;
        var doc = new FiscalDocument(tenantId, FiscalDocumentKind.CreditDebitNote, emission, buyer, exception, createdAt)
        {
            SalesReturnId = Guard.NotEmptyIfPresent(salesReturnId, nameof(salesReturnId)),
        };
        doc.NoteReference = new FiscalNoteReference(tenantId, doc.BranchId, doc.Id, original);
        for (var i = 0; i < lines.Count; i++)
        {
            doc._lines.Add(new FiscalDocumentLine(tenantId, doc.BranchId, doc.Id, i + 1, lines[i]));
        }
        Guard.That(doc.OriginalTotal > 0, "fiscal.note_original", "El monto de la factura original debe ser mayor que 0.");
        Guard.That(doc.ReturnedTotal > 0, "fiscal.note_returned", "El monto devuelto debe ser mayor que 0.");
        Guard.That(doc.ReturnedTotal <= doc.OriginalTotal, "fiscal.note_exceeds", "No se puede devolver más de lo facturado.");
        return doc;
    }

    // ------------------------------------------------------------------------------------------------ respuestas del SIN
    /// <summary>908: recepción validada (en línea o sin respuesta que el SIN sí registró).</summary>
    public void Accept(string? receptionCode, int siatCode, DateTimeOffset now)
    {
        Guard.That(Status is FiscalDocumentStatus.Pending or FiscalDocumentStatus.NoResponse, "fiscal.state",
            $"Un documento {Status} no puede quedar validado en línea.");
        Status = FiscalDocumentStatus.Valid;
        ReceptionCode = receptionCode ?? ReceptionCode;
        LastSiatCode = siatCode;
        Raise(now);
    }

    /// <summary>902/904 en línea: el documento no es válido (la venta recibe uno nuevo).</summary>
    public void Reject(int siatCode)
    {
        Guard.That(Status == FiscalDocumentStatus.Pending, "fiscal.state", $"Un documento {Status} no puede rechazarse en línea.");
        Status = FiscalDocumentStatus.Rejected;
        LastSiatCode = siatCode;
    }

    /// <summary>Se perdió la respuesta al enviar en línea: queda para verificar su estado al recuperar la comunicación.</summary>
    public void MarkNoResponse()
    {
        Guard.That(Status == FiscalDocumentStatus.Pending, "fiscal.state", $"Un documento {Status} no se estaba enviando.");
        Status = FiscalDocumentStatus.NoResponse;
    }

    /// <summary>Resuelve un documento sin respuesta tras <c>verificacionEstadoFactura</c>.</summary>
    public void ResolveNoResponse(bool registeredInSiat, bool reissued, int? siatCode, DateTimeOffset now)
    {
        Guard.That(Status == FiscalDocumentStatus.NoResponse, "fiscal.state", "Solo se resuelve un documento sin respuesta.");
        LastSiatCode = siatCode ?? LastSiatCode;
        if (registeredInSiat)
        {
            Status = reissued ? FiscalDocumentStatus.DuplicateToVoid : FiscalDocumentStatus.Valid;
            if (!reissued)
            {
                Raise(now);
            }
        }
        else
        {
            Status = FiscalDocumentStatus.Discarded;
        }
    }

    /// <summary>Incluye un documento fuera de línea en un paquete de contingencia.</summary>
    public void AddToPackage(Guid packageId, int position)
    {
        Guard.That(Status == FiscalDocumentStatus.Offline, "fiscal.state", "Solo los documentos fuera de línea van en un paquete.");
        Guard.That(position is >= 1 and <= SiatCodes.MaxDocumentsPerPackage, "fiscal.package_position", "La posición va de 1 a 500.");
        PackageId = Guard.NotEmpty(packageId, nameof(packageId));
        PackagePosition = position;
        Status = FiscalDocumentStatus.InPackage;
    }

    /// <summary>El paquete se validó sin errores para este documento.</summary>
    public void ConfirmInPackage(int siatCode, DateTimeOffset now)
    {
        Guard.That(Status == FiscalDocumentStatus.InPackage, "fiscal.state", "El documento no está en un paquete.");
        Status = FiscalDocumentStatus.Valid;
        LastSiatCode = siatCode;
        Raise(now);
    }

    /// <summary>La validación del paquete observó este documento.</summary>
    public void RejectInPackage(int siatCode)
    {
        Guard.That(Status == FiscalDocumentStatus.InPackage, "fiscal.state", "El documento no está en un paquete.");
        Status = FiscalDocumentStatus.PackageRejected;
        LastSiatCode = siatCode;
    }

    /// <summary>El paquete falló en la cabecera (rechazo total): los documentos vuelven a la cola fuera de línea.</summary>
    public void ReturnToOfflineQueue()
    {
        Guard.That(Status == FiscalDocumentStatus.InPackage, "fiscal.state", "El documento no está en un paquete.");
        Status = FiscalDocumentStatus.Offline;
        PackageId = null;
        PackagePosition = null;
    }

    // ------------------------------------------------------------------------------------------------ anulación y reversión
    /// <summary>¿Se puede pedir la anulación ahora (hora fiscal)?</summary>
    public bool CanVoidAt(DateTime fiscalNow) =>
        (Status == FiscalDocumentStatus.Valid && !IsReverted || Status == FiscalDocumentStatus.DuplicateToVoid)
        && fiscalNow <= FiscalRules.VoidDeadline(IssuedAt);

    public void EnsureVoidable(DateTime fiscalNow)
    {
        Guard.That(Status is FiscalDocumentStatus.Valid or FiscalDocumentStatus.DuplicateToVoid, "fiscal.void_state",
            "Solo se anula un documento válido en el SIN.");
        Guard.That(!IsReverted, "fiscal.void_reverted", "Este documento ya tuvo una anulación revertida: no se puede volver a anular.");
        Guard.That(fiscalNow <= FiscalRules.VoidDeadline(IssuedAt), "fiscal.void_deadline",
            $"El plazo para anular venció el {FiscalRules.VoidDeadline(IssuedAt):dd/MM/yyyy} (día 9 del mes siguiente a la emisión).");
    }

    /// <summary>905 o 936: anulado en el SIN.</summary>
    public void Void(int reasonCode, int siatCode, DateTimeOffset now)
    {
        Guard.That(Status is FiscalDocumentStatus.Valid or FiscalDocumentStatus.DuplicateToVoid, "fiscal.void_state",
            "Solo se anula un documento válido en el SIN.");
        Guard.That(!IsReverted, "fiscal.void_reverted", "Este documento ya tuvo una anulación revertida.");
        VoidReasonCode = reasonCode;
        VoidedAt = now;
        LastSiatCode = siatCode;
        Status = FiscalDocumentStatus.Voided;
        _events.Add(new FiscalDocumentVoidedEvent(Id, Kind.ToString(), Number, Cuf, BranchId, reasonCode, now));
    }

    public void EnsureRevertible(DateTime fiscalNow)
    {
        Guard.That(Status == FiscalDocumentStatus.Voided, "fiscal.revert_state", "Solo se revierte un documento anulado.");
        Guard.That(!IsReverted, "fiscal.revert_twice", "La anulación se revierte una sola vez.");
        Guard.That(fiscalNow <= FiscalRules.VoidDeadline(IssuedAt), "fiscal.revert_deadline",
            $"El plazo para revertir venció el {FiscalRules.VoidDeadline(IssuedAt):dd/MM/yyyy}.");
    }

    /// <summary>907/978: la anulación se revirtió; el documento vuelve a ser válido y ya no se puede anular.</summary>
    public void RevertVoid(int siatCode, DateTimeOffset now)
    {
        Guard.That(Status == FiscalDocumentStatus.Voided && !IsReverted, "fiscal.revert_state", "Solo se revierte una vez un documento anulado.");
        Status = FiscalDocumentStatus.Valid;
        IsReverted = true;
        RevertedAt = now;
        LastSiatCode = siatCode;
    }

    /// <summary>Registra el último código del SIN (verificación de estado, errores no terminales).</summary>
    public void RecordSiatCode(int siatCode) => LastSiatCode = siatCode;

    private void Raise(DateTimeOffset now) =>
        _events.Add(new FiscalDocumentValidatedEvent(Id, Kind.ToString(), Number, Cuf, BranchId, TotalAmount, now));
}

/// <summary>V4.1 · Línea congelada de un documento fiscal (append-only: el documento emitido no cambia).</summary>
public sealed class FiscalDocumentLine : Entity, IBranchScoped, IAppendOnly
{
    private FiscalDocumentLine()
    {
    }

    internal FiscalDocumentLine(Guid tenantId, Guid branchId, Guid documentId, int lineNumber, FiscalLineInput input)
        : base(tenantId)
    {
        BranchId = branchId;
        DocumentId = documentId;
        LineNumber = lineNumber;
        VariantId = Guard.NotEmptyIfPresent(input.VariantId, nameof(input.VariantId));
        ActivityCode = Guard.Text(input.ActivityCode, "La actividad económica", 10);
        Guard.That(input.SinProductCode is >= 1 and <= 99_999_999, "fiscal.sin_product", $"Línea {lineNumber}: falta el código de producto SIN.");
        SinProductCode = input.SinProductCode;
        ProductCode = Guard.Text(input.ProductCode, "El código de producto", 50);
        Description = Guard.Text(input.Description, "La descripción", 500);
        Quantity = Guard.Positive(input.Quantity, "La cantidad");
        Guard.That(input.SinUnitCode is >= 1 and <= 999, "fiscal.sin_unit", $"Línea {lineNumber}: falta la unidad de medida SIN.");
        SinUnitCode = input.SinUnitCode;
        UnitPrice = Guard.Positive(input.UnitPrice, "El precio unitario");
        Discount = input.Discount is { } d ? Guard.NonNegative(d, "El descuento") : null;
        TransactionCode = input.TransactionCode;
        SerialNumber = Guard.OptionalText(input.SerialNumber, "El número de serie", 1500);
        Imei = Guard.OptionalText(input.Imei, "El IMEI", 1500);
        Guard.That(Subtotal > 0, "fiscal.subtotal", $"Línea {lineNumber} ({ProductCode}): el subtotal debe ser mayor que 0.");
    }

    public Guid BranchId { get; private set; }

    public Guid DocumentId { get; private set; }

    public int LineNumber { get; private set; }

    public Guid? VariantId { get; private set; }

    public string ActivityCode { get; private set; } = string.Empty;

    public int SinProductCode { get; private set; }

    public string ProductCode { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public decimal Quantity { get; private set; }

    public int SinUnitCode { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal? Discount { get; private set; }

    /// <summary>Solo notas: 1 = transacción original, 2 = devuelto.</summary>
    public int? TransactionCode { get; private set; }

    public string? SerialNumber { get; private set; }

    public string? Imei { get; private set; }

    /// <summary>subTotal = round2(cantidad × precio) − descuento (fórmula del SIN; no se guarda).</summary>
    public decimal Subtotal => FiscalRules.LineSubtotal(Quantity, UnitPrice, Discount);
}

/// <summary>V4.1 · Subtipo de las notas crédito-débito: factura original (de M-INV o transcrita) y descuento prorrateado.</summary>
public sealed class FiscalNoteReference : BaseEntity, IBranchScoped
{
    private FiscalNoteReference()
    {
    }

    internal FiscalNoteReference(Guid tenantId, Guid branchId, Guid documentId, FiscalOriginalInvoice original)
        : base(tenantId)
    {
        BranchId = branchId;
        DocumentId = documentId;
        OriginalDocumentId = Guard.NotEmptyIfPresent(original.DocumentId, nameof(original.DocumentId));
        Guard.That(original.Number is > 0 and <= 9_999_999_999, "fiscal.original_number", "El número de la factura original no es válido.");
        OriginalNumber = original.Number;
        OriginalCuf = Guard.Text(original.CufOrAuthorization, "El CUF o código de autorización de la factura original", 100);
        OriginalIssuedAt = DateTime.SpecifyKind(original.IssuedAt, DateTimeKind.Unspecified);
        DiscountShare = original.DiscountShare is { } d ? FiscalRules.Round2(Guard.NonNegative(d, "El descuento prorrateado")) : null;
    }

    public Guid DocumentId { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid? OriginalDocumentId { get; private set; }

    public long OriginalNumber { get; private set; }

    public string OriginalCuf { get; private set; } = string.Empty;

    public DateTime OriginalIssuedAt { get; private set; }

    /// <summary>montoDescuentoCreditoDebito: prorrateo del descuento adicional de la factura original.</summary>
    public decimal? DiscountShare { get; private set; }
}

/// <summary>V4.1 · XML exacto del documento (validado contra el XSD oficial) y SHA-256 del GZIP enviado (huella).</summary>
public sealed class FiscalDocumentFile : Entity, IBranchScoped, IAppendOnly
{
    private FiscalDocumentFile()
    {
    }

    public FiscalDocumentFile(Guid tenantId, Guid branchId, Guid documentId, string xml, string gzipSha256, DateTimeOffset createdAt)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        DocumentId = Guard.NotEmpty(documentId, nameof(documentId));
        Xml = Guard.Text(xml, "El XML", 5_000_000);
        GzipSha256 = Guard.Text(gzipSha256, "La huella SHA-256", 64, 64).ToLowerInvariant();
        CreatedAt = createdAt;
    }

    public Guid BranchId { get; private set; }

    public Guid DocumentId { get; private set; }

    public string Xml { get; private set; } = string.Empty;

    public string GzipSha256 { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>V4.1 · Bitácora inmutable de un documento fiscal: cada envío y respuesta del SIN y cada acción del usuario.</summary>
public sealed class FiscalDocumentEvent : Entity, IBranchScoped, IAppendOnly
{
    private FiscalDocumentEvent()
    {
    }

    public FiscalDocumentEvent(Guid tenantId, Guid branchId, Guid documentId, FiscalDocumentAction action, DateTimeOffset occurredAt,
        int? siatCode, string? description, string? receptionCode, string? messages, Guid? userId)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        DocumentId = Guard.NotEmpty(documentId, nameof(documentId));
        Action = action;
        OccurredAt = occurredAt;
        SiatCode = siatCode;
        Description = Guard.OptionalText(description is { Length: > 500 } ? description[..500] : description, "La descripción", 500);
        ReceptionCode = Guard.OptionalText(receptionCode, "El código de recepción", 100);
        Messages = messages is { Length: > 8000 } ? messages[..8000] : messages;
        UserId = Guard.NotEmptyIfPresent(userId, nameof(userId));
    }

    public Guid BranchId { get; private set; }

    public Guid DocumentId { get; private set; }

    public FiscalDocumentAction Action { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public int? SiatCode { get; private set; }

    public string? Description { get; private set; }

    public string? ReceptionCode { get; private set; }

    /// <summary>Mensajes del SIN (JSON: código, descripción, número de archivo y de detalle).</summary>
    public string? Messages { get; private set; }

    public Guid? UserId { get; private set; }
}

/// <summary>V4.1 · Entrega del documento al comprador (art. 26 RND 102100000011: XML y representación gráfica).</summary>
public sealed class FiscalDelivery : Entity, IBranchScoped, IAppendOnly
{
    private FiscalDelivery()
    {
    }

    public FiscalDelivery(Guid tenantId, Guid branchId, Guid documentId, FiscalDeliveryChannel channel, string? recipient, bool succeeded,
        string? error, DateTimeOffset occurredAt, Guid? userId)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        DocumentId = Guard.NotEmpty(documentId, nameof(documentId));
        Channel = channel;
        Recipient = Guard.OptionalText(recipient, "El destinatario", 254);
        Succeeded = succeeded;
        Error = Guard.OptionalText(error is { Length: > 500 } ? error[..500] : error, "El error", 500);
        OccurredAt = occurredAt;
        UserId = Guard.NotEmptyIfPresent(userId, nameof(userId));
    }

    public Guid BranchId { get; private set; }

    public Guid DocumentId { get; private set; }

    public FiscalDeliveryChannel Channel { get; private set; }

    public string? Recipient { get; private set; }

    public bool Succeeded { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public Guid? UserId { get; private set; }
}
