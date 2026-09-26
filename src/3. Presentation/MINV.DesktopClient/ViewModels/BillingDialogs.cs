using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using System.Xml.Linq;
using MINV.Application.Billing;
using MINV.Application.Sales;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Billing;

namespace MINV.DesktopClient.ViewModels;

// =====================================================================================================================
// V4.1 · Formularios modales de la facturación (anular, devolución con nota crédito-débito, re-emitir, XML, contingencia
// manual, punto de venta, CAFC, transcripción de facturas manuales, homologación y factura del proveedor). Cada uno arma
// el caso de uso y muestra el error del dominio dentro del propio formulario. Plantillas: Views/BillingForms.xaml.
// =====================================================================================================================

/// <summary>Hora fiscal (zona de la empresa) y lectura de fechas escritas por una persona.</summary>
public static class BillingClock
{
    /// <summary>Ahora en la zona horaria de la empresa (la hora fiscal sale de ahí; el reloj del SIN la corrige al emitir).</summary>
    public static DateTime Now(AppServices app)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(app.Session.Workspace.TimeZoneId);
            return TimeZoneInfo.ConvertTime(app.Now, zone).DateTime;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or InvalidOperationException)
        {
            return app.Now.ToLocalTime().DateTime;
        }
    }

    public static DateOnly Today(AppServices app) => DateOnly.FromDateTime(Now(app));

    /// <summary>«25/09/2026 10:30» o «25/09/2026».</summary>
    public static bool TryParse(string? text, out DateTime value) =>
        DateTime.TryParseExact((text ?? string.Empty).Trim(), ["dd/MM/yyyy HH:mm", "d/M/yyyy HH:mm", "dd/MM/yyyy H:mm", "d/M/yyyy H:mm", "dd/MM/yyyy", "d/M/yyyy"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    public static string Text(DateTime value) => value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}

// --------------------------------------------------------------------------------------------------- anular
/// <summary>Anulación ante el SIN: motivo del catálogo MOTIVOS_ANULACION, «devolver la mercadería», plazo visible y
/// confirmación expresa.</summary>
public sealed class VoidFiscalDialog : FormDialog
{
    private readonly AppServices _app;
    private readonly FiscalDocumentDetail _detail;
    private Choice<int>? _reason;
    private bool _returnGoods;
    private bool _confirmed;
    private string _note = string.Empty;

    public VoidFiscalDialog(AppServices app, FiscalDocumentDetail detail, IReadOnlyList<SiatCatalogItemView> reasons)
        : base($"Anular {(detail.Row.Kind == FiscalDocumentKind.Invoice ? "la factura" : "la nota")} N° {detail.Row.Number}", "Anular ante el SIN",
            Glyphs.Error, isDanger: true)
    {
        _app = app;
        _detail = detail;
        Reasons = reasons.Where(r => r.IsCurrent).Select(r => new Choice<int>($"{r.Code} · {Fmt.SentenceCase(r.Description)}", r.Code)).ToList();
        var preferred = detail.Row.Kind == FiscalDocumentKind.Invoice ? FiscalIssuer.VoidReasonInvoice : FiscalIssuer.VoidReasonNote;
        _reason = Reasons.FirstOrDefault(r => r.Label.Contains(Fmt.SentenceCase(preferred), StringComparison.OrdinalIgnoreCase)) ?? Reasons.FirstOrDefault();
    }

    public override string Subtitle => $"{_detail.Row.BuyerName} · {_detail.Row.BuyerDocument} · {Fmt.Money(_detail.Row.Total)}";

    public IReadOnlyList<Choice<int>> Reasons { get; }

    public bool HasReasons => Reasons.Count > 0;

    public Choice<int>? Reason { get => _reason; set => Set(ref _reason, value); }

    /// <summary>Solo las facturas de una venta de M-INV pueden devolver la mercadería.</summary>
    public bool CanReturnGoods => _detail.Row.Kind == FiscalDocumentKind.Invoice && _detail.Row.SaleNumber is not null;

    public bool ReturnGoods { get => _returnGoods; set => Set(ref _returnGoods, value); }

    public bool Confirmed { get => _confirmed; set => Set(ref _confirmed, value); }

    public string Note { get => _note; set => Set(ref _note, value ?? string.Empty); }

    public string DeadlineText =>
        $"Se puede anular hasta el {_detail.Row.VoidDeadline:dd/MM/yyyy} a las 23:59 (día 9 del mes siguiente a la emisión). El comprador recibe el aviso por correo.";

    public string ConfirmLabel => $"Confirmo que anulo el documento N° {_detail.Row.Number} ante el SIN.";

    public string ReturnGoodsHelp => "Marque si el cliente devuelve TODO: vuelve el stock, se registra el reembolso y el asiento inverso. " +
                                     "Sin marcar, la venta sigue y puede re-emitir la factura con los datos corregidos.";

    public string? ResultMessage { get; private set; }

    protected override bool CanConfirm() => _confirmed && _reason is not null;

    protected override async Task<bool> SubmitAsync()
    {
        ResultMessage = await _app.SendAsync(new VoidFiscalDocumentCommand(_detail.Row.Id, _reason!.Value, CanReturnGoods && _returnGoods,
            string.IsNullOrWhiteSpace(_note) ? null : _note.Trim()));
        return true;
    }
}

// --------------------------------------------------------------------------------------------------- devolución
/// <summary>Línea devolvible (vendido − devuelto) con la cantidad a devolver.</summary>
public sealed class ReturnLineItem : ObservableObject
{
    private readonly Action _changed;
    private string _quantity = string.Empty;

    public ReturnLineItem(ReturnableLine line, Action changed)
    {
        Line = line;
        _changed = changed;
    }

    public ReturnableLine Line { get; }

    public decimal Available => Line.Sold - Line.Returned;

    public string AvailableText => $"Vendido {Fmt.Qty(Line.Sold)} · devuelto {Fmt.Qty(Line.Returned)} · quedan {Fmt.Qty(Available)} {Line.Unit}";

    public string PriceText => Fmt.Money(Line.UnitPrice) + (Line.DiscountPercent > 0 ? $" · desc. {Line.DiscountPercent:0.#} %" : string.Empty);

    public bool CanReturn => Available > 0;

    public string Quantity
    {
        get => _quantity;
        set
        {
            if (Set(ref _quantity, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasError));
                _changed();
            }
        }
    }

    public decimal Parsed => Numbers.TryParse(_quantity, out var q, emptyIsZero: true) ? q : -1;

    public bool HasError => Parsed < 0 || Parsed > Available;

    public decimal Refund => Parsed > 0 ? decimal.Round(Parsed * Line.UnitPrice * (1 - Line.DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero) : 0m;
}

/// <summary>Devolución parcial o total de una venta: stock de vuelta, reembolso, asiento y, si la venta tiene factura
/// válida, nota crédito-débito (sector 24).</summary>
public sealed class SalesReturnDialog : FormDialog
{
    private readonly AppServices _app;
    private readonly string _invoice;
    private PosOption? _method;
    private string _reason = string.Empty;

    private SalesReturnDialog(AppServices app, string invoiceNumber, IReadOnlyList<ReturnableLine> lines, IReadOnlyList<PosOption> methods)
        : base($"Devolución de la venta {invoiceNumber}", "Registrar devolución", Glyphs.Undo, width: 640)
    {
        _app = app;
        _invoice = invoiceNumber;
        Lines = lines.Select(l => new ReturnLineItem(l, () => OnPropertiesChanged(nameof(RefundText), nameof(ReturnsSomething)))).ToList();
        Methods = methods;
        _method = methods.FirstOrDefault(m => m.Code == "EFECTIVO") ?? methods.FirstOrDefault();
        ReturnAll = new RelayCommand(() =>
        {
            foreach (var line in Lines.Where(l => l.CanReturn))
            {
                line.Quantity = Numbers.Plain(line.Available);
            }
        });
    }

    public override string Subtitle =>
        "Vuelve el stock (devolución de cliente), se registra el reembolso y, si la venta tiene factura válida, se emite la nota crédito-débito.";

    public IReadOnlyList<ReturnLineItem> Lines { get; }

    public IReadOnlyList<PosOption> Methods { get; }

    public PosOption? Method { get => _method; set => Set(ref _method, value); }

    public IReadOnlyList<string> Reasons { get; } = ["Producto con falla", "Cambio de producto", "Error en el despacho", "El cliente desistió de la compra"];

    public string Reason { get => _reason; set => Set(ref _reason, value ?? string.Empty); }

    public RelayCommand ReturnAll { get; }

    public bool ReturnsSomething => Lines.Any(l => l.Parsed > 0);

    public string RefundText => $"Reembolso estimado: {Fmt.Money(Lines.Sum(l => l.Refund))}";

    public SalesReturnResult? Result { get; private set; }

    /// <summary>Lee lo que todavía se puede devolver y los medios de pago y abre el formulario. Null si se canceló.</summary>
    public static async Task<SalesReturnResult?> OpenAsync(AppServices app, string invoiceNumber)
    {
        var lines = await app.SendAsync(new GetReturnableLinesQuery(invoiceNumber));
        if (lines.All(l => l.Sold - l.Returned <= 0))
        {
            app.Notify.Info("Nada por devolver", $"Todo lo vendido en {invoiceNumber} ya se devolvió.");
            return null;
        }
        var state = await app.SendAsync(new GetPosStateQuery());
        var dialog = new SalesReturnDialog(app, invoiceNumber, lines, state.PaymentMethods);
        return await app.Dialogs.ShowAsync(dialog) ? dialog.Result : null;
    }

    protected override bool CanConfirm() => ReturnsSomething && _method is not null && _reason.Trim().Length > 0;

    protected override async Task<bool> SubmitAsync()
    {
        if (Lines.FirstOrDefault(l => l.HasError) is { } wrong)
        {
            Error = $"Revise la cantidad de {wrong.Line.Name}: puede devolver de 0 a {Fmt.Qty(wrong.Available)}.";
            return false;
        }
        var inputs = Lines.Where(l => l.Parsed > 0).Select(l => new ReturnLineInput(l.Line.Sku, l.Parsed)).ToList();
        Result = await _app.SendAsync(new CreateSalesReturnCommand(_invoice, _reason.Trim(), _method!.Code, inputs));
        return true;
    }
}

// --------------------------------------------------------------------------------------------------- re-emitir
/// <summary>Documento nuevo para la misma venta (tras un rechazo o una anulación por datos del comprador erróneos), con
/// los datos del comprador editables (las notas conservan el comprador de la factura original).</summary>
public sealed class ReissueDialog : FormDialog
{
    private readonly AppServices _app;
    private readonly FiscalDocumentDetail _detail;

    public ReissueDialog(AppServices app, FiscalDocumentDetail detail, IReadOnlyList<SiatCatalogItemView>? documentTypes)
        : base($"Re-emitir {(detail.Row.Kind == FiscalDocumentKind.Invoice ? "la factura" : "la nota")} N° {detail.Row.Number}", "Emitir documento nuevo",
            Glyphs.Invoice, width: 600)
    {
        _app = app;
        _detail = detail;
        Buyer = new BuyerForm(app, documentTypes);
        var (number, complement) = SplitDocument(detail.Row.BuyerDocument);
        Buyer.Prefill(DocumentTypeFromXml(detail.Xml) ?? SiatCodes.DocumentCi, number, complement, detail.Row.BuyerName, detail.BuyerEmail);
    }

    public override string Subtitle => IsNote
        ? "La nota se emite otra vez con el comprador de la factura original (número y CUF nuevos)."
        : "Se emite una factura nueva (número y CUF nuevos) con las mismas líneas y el mismo medio de pago. Corrija los datos del comprador si hace falta.";

    public bool IsNote => _detail.Row.Kind == FiscalDocumentKind.CreditDebitNote;

    public bool CanEditBuyer => !IsNote;

    public BuyerForm Buyer { get; }

    public FiscalDocumentRow? Result { get; private set; }

    public string? DispatchMessage { get; private set; }

    protected override async Task<bool> SubmitAsync()
    {
        if (!IsNote && Buyer.Check() is { } problem)
        {
            Error = problem;
            return false;
        }
        Result = await _app.SendAsync(new ReissueFiscalDocumentCommand(_detail.Row.Id, IsNote ? null : Buyer.ToInput()));
        try
        {
            // Se envía ya al SIN (si no hay comunicación, el trabajo automático lo hace después)
            var sent = await _app.SendAsync(new DispatchFiscalDocumentsCommand(Result.Id));
            DispatchMessage = sent.Documents.FirstOrDefault(d => d.Id == Result.Id) is { } row
                ? $"Documento N° {row.Number}: {FiscalText.Status(row.Status, row.IsReverted).ToLower(Fmt.Culture)}."
                : sent.Messages.FirstOrDefault();
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            DispatchMessage = "Quedó pendiente de envío: " + AppServices.Describe(ex);
        }
        return true;
    }

    /// <summary>«5115889-1A» → («5115889», «1A»).</summary>
    public static (string Number, string? Complement) SplitDocument(string document)
    {
        var dash = document.IndexOf('-', StringComparison.Ordinal);
        return dash > 0 ? (document[..dash], document[(dash + 1)..]) : (document, null);
    }

    /// <summary>Tipo de documento del comprador tal como se envió al SIN (elemento codigoTipoDocumentoIdentidad del XML).</summary>
    public static int? DocumentTypeFromXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }
        try
        {
            var value = XDocument.Parse(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == "codigoTipoDocumentoIdentidad")?.Value;
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var type) ? type : null;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}

// --------------------------------------------------------------------------------------------------- XML
/// <summary>El XML exacto que se envió al SIN (monoespaciado, con copiar).</summary>
public sealed class XmlDialog : FormDialog
{
    public XmlDialog(string title, string xml)
        : base(title, "Cerrar", Glyphs.Code, width: 860)
    {
        Xml = Pretty(xml);
        Copy = new RelayCommand(() =>
        {
            Copied = ClipboardText.TrySet(xml) ? "✔ XML copiado al portapapeles" : "No se pudo copiar: intente de nuevo.";
            OnPropertyChanged(nameof(Copied));
        });
    }

    public override string Subtitle => "El documento tal como se revisó y se envió al SIN (modalidad computarizada: sin firma digital). Puede copiarlo para soporte o para el contador.";

    public override bool ShowConfirm => false;

    public string Xml { get; }

    public RelayCommand Copy { get; }

    public string? Copied { get; private set; }

    protected override Task<bool> SubmitAsync() => Task.FromResult(true);

    private static string Pretty(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return "(Este documento no tiene XML guardado.)";
        }
        try
        {
            return XDocument.Parse(xml).ToString();
        }
        catch (System.Xml.XmlException)
        {
            return xml;
        }
    }
}

// --------------------------------------------------------------------------------------------------- contingencia manual
/// <summary>Contingencia manual (corte de energía, virus o falla de software, falla de hardware): evento del catálogo, CAFC
/// del talonario de la sucursal y hora de inicio.</summary>
public sealed class ManualContingencyDialog : FormDialog
{
    private readonly AppServices _app;
    private readonly SiatPointOfSaleStatus _point;
    private Choice<int>? _event;
    private Choice<string>? _cafc;
    private string _start;
    private string _description = string.Empty;

    public ManualContingencyDialog(AppServices app, SiatPointOfSaleStatus point, IReadOnlyList<SiatCatalogItemView> events, IReadOnlyList<ContingencyCodeRow> cafcs)
        : base($"Contingencia manual · {point.BranchCode} · punto {point.Code}", "Declarar contingencia", Glyphs.Power, isDanger: true)
    {
        _app = app;
        _point = point;
        Events = events.Where(e => e.IsCurrent && FiscalIssuer.IsManualEvent(e.Description))
            .Select(e => new Choice<int>($"{e.Code} · {Fmt.SentenceCase(e.Description)}", e.Code)).ToList();
        _event = Events.FirstOrDefault();
        Cafcs = cafcs.Where(c => c.IsActive && c.BranchCode == point.BranchCode && c.DocumentSector == SiatCodes.SectorPurchaseSale)
            .Select(c => new Choice<string>($"{c.Code} · N° {c.NumberFrom} a {c.NumberTo}" + (c.ValidUntil is { } until ? $" · vence {until:dd/MM/yyyy}" : ""), c.Code))
            .ToList();
        _cafc = Cafcs.FirstOrDefault();
        _start = BillingClock.Text(BillingClock.Now(app));
        if (Events.Count == 0)
        {
            Error = "El catálogo de eventos significativos no está sincronizado: use «Sincronizar catálogos».";
        }
        else if (Cafcs.Count == 0)
        {
            Error = $"La sucursal {point.BranchCode} no tiene un talonario CAFC activo: regístrelo en la pestaña CAFC.";
        }
    }

    public override string Subtitle =>
        "La caja deja de emitir en línea: use las facturas del talonario CAFC y transcríbalas en M-INV cuando termine (hasta 72 h después del fin).";

    public IReadOnlyList<Choice<int>> Events { get; }

    public Choice<int>? Event { get => _event; set => Set(ref _event, value); }

    public IReadOnlyList<Choice<string>> Cafcs { get; }

    public Choice<string>? Cafc { get => _cafc; set => Set(ref _cafc, value); }

    public string Start { get => _start; set => Set(ref _start, value ?? string.Empty); }

    public string Description { get => _description; set => Set(ref _description, value ?? string.Empty); }

    public string? ResultMessage { get; private set; }

    protected override bool CanConfirm() => _event is not null && _cafc is not null;

    protected override async Task<bool> SubmitAsync()
    {
        if (!BillingClock.TryParse(_start, out var start))
        {
            Error = "Escriba el inicio como día/mes/año hora:minuto, por ejemplo 25/09/2026 10:30.";
            return false;
        }
        ResultMessage = await _app.SendAsync(new StartManualContingencyCommand(_point.Id, _event!.Value,
            string.IsNullOrWhiteSpace(_description) ? null : _description.Trim(), start, _cafc!.Value));
        return true;
    }
}

// --------------------------------------------------------------------------------------------------- punto de venta
/// <summary>Registrar un punto de venta en el SIN (y vincularlo a una caja).</summary>
public sealed class RegisterPointDialog : FormDialog
{
    private readonly AppServices _app;
    private SiatBranchView? _branch;
    private Choice<string?>? _register;
    private string _name = "Caja";
    private string _description = string.Empty;

    public RegisterPointDialog(AppServices app, IReadOnlyList<SiatBranchView> branches, IReadOnlyList<PosOption> registers)
        : base("Registrar punto de venta", "Registrar en el SIN", Glyphs.Add)
    {
        _app = app;
        Branches = branches.Where(b => b.SiatCode is not null).ToList();
        _branch = Branches.FirstOrDefault(b => b.BranchId == app.Session.Access.ActiveBranchId) ?? Branches.FirstOrDefault();
        Registers = [new("Sin caja (se vincula después)", null), .. registers.Select(r => new Choice<string?>($"{r.Code} · {r.Name}", r.Code))];
        _register = Registers[0];
        if (Branches.Count == 0)
        {
            Error = "Primero asigne el código del Padrón a la sucursal (Administración › Facturación SIAT › Sucursales).";
        }
    }

    public override string Subtitle => "El SIN asigna el número del punto de venta. Si la sucursal todavía no tiene su punto 0, se registra también, con su CUIS.";

    public IReadOnlyList<SiatBranchView> Branches { get; }

    public SiatBranchView? Branch { get => _branch; set => Set(ref _branch, value); }

    public IReadOnlyList<Choice<string?>> Registers { get; }

    public Choice<string?>? Register { get => _register; set => Set(ref _register, value); }

    public string Name { get => _name; set => Set(ref _name, value ?? string.Empty); }

    public string Description { get => _description; set => Set(ref _description, value ?? string.Empty); }

    public SiatPointOfSaleStatus? Result { get; private set; }

    protected override bool CanConfirm() => _branch is not null && _name.Trim().Length > 0;

    protected override async Task<bool> SubmitAsync()
    {
        Result = await _app.SendAsync(new RegisterSiatPointOfSaleCommand(_branch!.BranchCode, _name.Trim(),
            string.IsNullOrWhiteSpace(_description) ? null : _description.Trim(), _register?.Value));
        return true;
    }
}

// --------------------------------------------------------------------------------------------------- CAFC
/// <summary>Registrar un talonario de facturas de contingencia (CAFC) de una sucursal.</summary>
public sealed class RegisterCafcDialog : FormDialog
{
    private readonly AppServices _app;
    private SiatBranchView? _branch;
    private Choice<int> _sector;
    private string _code = string.Empty;
    private string _from = "1";
    private string _to = "100";
    private string _validUntil = string.Empty;

    public RegisterCafcDialog(AppServices app, IReadOnlyList<SiatBranchView> branches)
        : base("Registrar talonario CAFC", "Registrar talonario", Glyphs.Report)
    {
        _app = app;
        Branches = branches.Where(b => b.SiatCode is not null).ToList();
        _branch = Branches.FirstOrDefault(b => b.BranchId == app.Session.Access.ActiveBranchId) ?? Branches.FirstOrDefault();
        Sectors = [new("Factura compra venta (sector 1)", SiatCodes.SectorPurchaseSale), new("Nota crédito-débito (sector 24)", SiatCodes.SectorCreditDebitNote)];
        _sector = Sectors[0];
    }

    public override string Subtitle =>
        "El CAFC (código de autorización de facturas de contingencia) lo entrega el SIN para facturar a mano cuando se corta la energía o falla el equipo.";

    public IReadOnlyList<SiatBranchView> Branches { get; }

    public SiatBranchView? Branch { get => _branch; set => Set(ref _branch, value); }

    public IReadOnlyList<Choice<int>> Sectors { get; }

    public Choice<int> Sector { get => _sector; set => Set(ref _sector, value ?? Sectors[0]); }

    public string Code { get => _code; set => Set(ref _code, value ?? string.Empty); }

    public string NumberFrom { get => _from; set => Set(ref _from, value ?? string.Empty); }

    public string NumberTo { get => _to; set => Set(ref _to, value ?? string.Empty); }

    public string ValidUntil { get => _validUntil; set => Set(ref _validUntil, value ?? string.Empty); }

    public string? ResultMessage { get; private set; }

    protected override bool CanConfirm() => _branch is not null && _code.Trim().Length > 0;

    protected override async Task<bool> SubmitAsync()
    {
        if (!long.TryParse(_from.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var from)
            || !long.TryParse(_to.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var to))
        {
            Error = "Los números del talonario son enteros (del primero al último).";
            return false;
        }
        DateOnly? until = null;
        if (_validUntil.Trim().Length > 0)
        {
            if (!BillingClock.TryParse(_validUntil, out var date))
            {
                Error = "Escriba el vencimiento como día/mes/año (por ejemplo 31/12/2026) o déjelo vacío.";
                return false;
            }
            until = DateOnly.FromDateTime(date);
        }
        ResultMessage = await _app.SendAsync(new RegisterContingencyCodeCommand(_branch!.BranchCode, _sector.Value, _code.Trim(), from, to, until));
        return true;
    }
}

// --------------------------------------------------------------------------------------------------- transcripción CAFC
/// <summary>Línea de una factura manual que se transcribe.</summary>
public sealed class TranscribeLine(string sku, string name, string unit) : ObservableObject
{
    private string _quantity = "1";
    private string _discount = string.Empty;

    public string Sku { get; } = sku;

    public string Name { get; } = name;

    public string Unit { get; } = unit;

    public string Quantity { get => _quantity; set => Set(ref _quantity, value ?? string.Empty); }

    public string Discount { get => _discount; set => Set(ref _discount, value ?? string.Empty); }
}

/// <summary>
/// Transcribe una factura manual del talonario CAFC emitida durante una contingencia manual: evento, número del
/// talonario, fecha y hora escritas en la factura, comprador, medio de pago y productos. Registra la venta (stock, pago,
/// asiento) y el documento fuera de línea que viaja en el paquete del evento.
/// </summary>
public sealed class TranscribeDialog : FormDialog
{
    private readonly AppServices _app;
    private Choice<SignificantEventRow>? _event;
    private PosOption? _method;
    private string _number = string.Empty;
    private string _issuedAt;

    public TranscribeDialog(AppServices app, IReadOnlyList<SignificantEventRow> events, IReadOnlyList<PosOption> methods,
        IReadOnlyList<SiatCatalogItemView>? documentTypes, ProductPickerViewModel picker)
        : base("Transcribir factura manual (CAFC)", "Transcribir", Glyphs.Report, width: 720)
    {
        _app = app;
        Events = events.Where(e => e.Kind == SignificantEventKind.ManualCafc && e.Status is not (SignificantEventStatus.Reconciled or SignificantEventStatus.WithObservations))
            .Select(e => new Choice<SignificantEventRow>(
                $"{e.BranchCode} · punto {e.PointOfSaleCode} · CAFC {e.Cafc} · desde {e.StartedAt:dd/MM/yyyy HH:mm}" + (e.EndedAt is { } end ? $" hasta {end:dd/MM HH:mm}" : " (abierta)"),
                e)).ToList();
        _event = Events.FirstOrDefault();
        Methods = methods;
        _method = methods.FirstOrDefault(m => m.Code == "EFECTIVO") ?? methods.FirstOrDefault();
        _issuedAt = _event is { } first ? BillingClock.Text(first.Value.StartedAt.AddMinutes(5)) : BillingClock.Text(BillingClock.Now(app));
        Buyer = new BuyerForm(app, documentTypes);
        Picker = picker;
        Picker.Picked += (_, item) =>
        {
            if (Lines.All(l => l.Sku != item.Sku))
            {
                Lines.Add(new TranscribeLine(item.Sku, item.Name, item.Unit));
            }
            Picker.Clear();
        };
        RemoveLine = new RelayCommand<TranscribeLine>(l => Lines.Remove(l));
        if (Events.Count == 0)
        {
            Error = "No hay contingencias manuales abiertas o por transcribir.";
        }
    }

    public override string Subtitle => "Copie los datos de la factura manual tal como se entregó al comprador (número del talonario, fecha y hora, comprador y productos).";

    public IReadOnlyList<Choice<SignificantEventRow>> Events { get; }

    public Choice<SignificantEventRow>? Event { get => _event; set => Set(ref _event, value); }

    public string Number { get => _number; set => Set(ref _number, value ?? string.Empty); }

    public string IssuedAt { get => _issuedAt; set => Set(ref _issuedAt, value ?? string.Empty); }

    public BuyerForm Buyer { get; }

    public IReadOnlyList<PosOption> Methods { get; }

    public PosOption? Method { get => _method; set => Set(ref _method, value); }

    public ProductPickerViewModel Picker { get; }

    public ObservableCollection<TranscribeLine> Lines { get; } = [];

    public RelayCommand<TranscribeLine> RemoveLine { get; }

    public FiscalDocumentRow? Result { get; private set; }

    protected override bool CanConfirm() => _event is not null && _method is not null && Lines.Count > 0;

    protected override async Task<bool> SubmitAsync()
    {
        if (!long.TryParse(_number.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            Error = "Escriba el número de la factura manual (el del talonario).";
            return false;
        }
        if (!BillingClock.TryParse(_issuedAt, out var issuedAt))
        {
            Error = "Escriba la fecha y hora de la factura manual, por ejemplo 25/09/2026 10:45.";
            return false;
        }
        if (Buyer.ToInput() is not { } buyer)
        {
            Error = "Escriba el documento del comprador tal como figura en la factura manual (nominatividad).";
            return false;
        }
        if (Buyer.Check() is { } problem)
        {
            Error = problem;
            return false;
        }
        var lines = new List<SaleLineInput>();
        foreach (var line in Lines)
        {
            if (!Numbers.TryParse(line.Quantity, out var quantity) || quantity <= 0)
            {
                Error = $"Revise la cantidad de {line.Name}.";
                return false;
            }
            if (!Numbers.TryParse(line.Discount, out var discount, emptyIsZero: true) || discount is < 0 or > 100)
            {
                Error = $"El descuento de {line.Name} va de 0 a 100 %.";
                return false;
            }
            lines.Add(new SaleLineInput(line.Sku, quantity, discount));
        }
        Result = await _app.SendAsync(new TranscribeManualInvoiceCommand(_event!.Value.Id, number, issuedAt, buyer, _method!.Code, lines));
        return true;
    }
}

// --------------------------------------------------------------------------------------------------- homologación
/// <summary>Asignar a un producto su actividad económica y su código de producto del SIN (búsqueda en el catálogo).</summary>
public sealed class AssignProductDialog : FormDialog
{
    private readonly AppServices _app;
    private readonly ProductHomologationRow _product;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private SiatActivityView? _activity;
    private string _search;
    private SiatProductView? _selected;
    private IReadOnlyList<SiatProductView> _results = [];
    private bool _searching;

    public AssignProductDialog(AppServices app, ProductHomologationRow product, IReadOnlyList<SiatActivityView> activities)
        : base($"Homologar {product.Sku}", "Guardar homologación", Glyphs.Tag, width: 680)
    {
        _app = app;
        _product = product;
        Activities = activities.Where(a => a.IsCurrent).ToList();
        _activity = Activities.FirstOrDefault(a => a.Code == product.ActivityCode) ?? Activities.FirstOrDefault(a => a.Sectors.Contains(SiatCodes.SectorPurchaseSale))
                    ?? Activities.FirstOrDefault();
        _search = product.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await SearchAsync();
        };
        if (Activities.Count == 0)
        {
            Error = "La empresa todavía no tiene actividades económicas sincronizadas: use «Sincronizar catálogos» en Estado SIAT.";
        }
    }

    public override string Subtitle => $"{_product.Name} · {_product.Category}" +
                                       (_product.SinProductCode is { } code ? $" · hoy: {code} {_product.SinProductDescription}" : " · pendiente");

    public IReadOnlyList<SiatActivityView> Activities { get; }

    public SiatActivityView? Activity
    {
        get => _activity;
        set
        {
            if (Set(ref _activity, value))
            {
                _debounce.Stop();
                _debounce.Start();
            }
        }
    }

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value ?? string.Empty))
            {
                _debounce.Stop();
                _debounce.Start();
            }
        }
    }

    public IReadOnlyList<SiatProductView> Results { get => _results; private set => Set(ref _results, value); }

    public SiatProductView? Selected { get => _selected; set => Set(ref _selected, value); }

    public bool IsSearching { get => _searching; private set => Set(ref _searching, value); }

    public string ResultsText => Results.Count == 0 ? "Ningún producto del SIN coincide: pruebe con otra palabra." : $"{Results.Count} productos del SIN";

    /// <summary>Primera búsqueda al abrir.</summary>
    public Task StartAsync() => SearchAsync();

    protected override bool CanConfirm() => _selected is not null;

    protected override async Task<bool> SubmitAsync()
    {
        await _app.SendAsync(new SaveProductHomologationCommand([new ProductHomologationInput(_product.Sku, _selected!.ActivityCode, _selected.ProductCode)]));
        return true;
    }

    private async Task SearchAsync()
    {
        IsSearching = true;
        try
        {
            Results = await _app.SendAsync(new SearchSiatProductsQuery(_activity?.Code, _search, 100));
            Selected = Results.FirstOrDefault(r => r.ProductCode == _product.SinProductCode) ?? (Results.Count == 1 ? Results[0] : null);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Results = [];
            Error = AppServices.Describe(ex);
        }
        finally
        {
            IsSearching = false;
            OnPropertyChanged(nameof(ResultsText));
        }
    }
}

/// <summary>Propuesta de homologación («Sugerir»): se acepta o se descarta.</summary>
public sealed class SuggestionItem(ProductHomologationInput input, string productName, string? sinDescription) : ObservableObject
{
    private bool _accepted = true;

    public ProductHomologationInput Input { get; } = input;

    public string ProductName { get; } = productName;

    public string SinText { get; } = $"{input.SinProductCode} · {sinDescription ?? "(sin descripción)"}";

    public bool Accepted { get => _accepted; set => Set(ref _accepted, value); }
}

/// <summary>Propuestas de homologación para aceptar en lote.</summary>
public sealed class SuggestionsDialog : FormDialog
{
    private readonly AppServices _app;

    public SuggestionsDialog(AppServices app, string activity, IReadOnlyList<SuggestionItem> items)
        : base($"Sugerencias de homologación · actividad {activity}", "Aceptar las marcadas", Glyphs.Sparkle, width: 780)
    {
        _app = app;
        Items = items;
        SelectAll = new RelayCommand(() => Toggle(true));
        SelectNone = new RelayCommand(() => Toggle(false));
    }

    public override string Subtitle =>
        $"{Items.Count} propuestas por palabras del nombre y la categoría. Revise cada una: el código del SIN define cómo se informa el producto.";

    public IReadOnlyList<SuggestionItem> Items { get; }

    public RelayCommand SelectAll { get; }

    public RelayCommand SelectNone { get; }

    public string? ResultMessage { get; private set; }

    protected override bool CanConfirm() => Items.Any(i => i.Accepted);

    protected override async Task<bool> SubmitAsync()
    {
        ResultMessage = await _app.SendAsync(new SaveProductHomologationCommand(Items.Where(i => i.Accepted).Select(i => i.Input).ToList()));
        return true;
    }

    private void Toggle(bool value)
    {
        foreach (var item in Items)
        {
            item.Accepted = value;
        }
    }
}

// --------------------------------------------------------------------------------------------------- factura del proveedor
/// <summary>Registrar la factura del proveedor de una recepción (entra al libro de compras con su crédito fiscal).</summary>
public sealed class SupplierInvoiceDialog : FormDialog
{
    private readonly AppServices _app;
    private readonly PendingSupplierInvoiceRow _receipt;
    private string _number = string.Empty;
    private string _authorization = string.Empty;
    private string _date;
    private string _total;
    private string _discounts = string.Empty;
    private string _notSubject = string.Empty;
    private string _controlCode = string.Empty;
    private Choice<int> _type;

    public SupplierInvoiceDialog(AppServices app, PendingSupplierInvoiceRow receipt)
        : base($"Factura del proveedor · recepción {receipt.ReceiptNumber}", "Registrar factura", Glyphs.Invoice, width: 620)
    {
        _app = app;
        _receipt = receipt;
        _date = receipt.ReceivedOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        _total = Numbers.Plain(receipt.Total);
        Types =
        [
            new("1 · Compras para el mercado interno (actividades gravadas)", 1),
            new("2 · Compras para el mercado interno (actividades no gravadas)", 2),
            new("3 · Compras sujetas a proporcionalidad", 3),
            new("4 · Compras para exportaciones", 4),
            new("5 · Compras para mercado interno y exportaciones", 5),
        ];
        _type = Types[0];
    }

    public override string Subtitle => $"{_receipt.Supplier} · recibida el {_receipt.ReceivedOn:dd/MM/yyyy} · {Fmt.Money(_receipt.Total)}";

    public string InvoiceNumber { get => _number; set => Set(ref _number, value ?? string.Empty); }

    public string AuthorizationCode { get => _authorization; set => Set(ref _authorization, value ?? string.Empty); }

    public string Date { get => _date; set => Set(ref _date, value ?? string.Empty); }

    public string Total
    {
        get => _total;
        set
        {
            if (Set(ref _total, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CreditText));
            }
        }
    }

    public string Discounts
    {
        get => _discounts;
        set
        {
            if (Set(ref _discounts, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CreditText));
            }
        }
    }

    public string NotSubject
    {
        get => _notSubject;
        set
        {
            if (Set(ref _notSubject, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CreditText));
            }
        }
    }

    public string ControlCode { get => _controlCode; set => Set(ref _controlCode, value ?? string.Empty); }

    public IReadOnlyList<Choice<int>> Types { get; }

    public Choice<int> Type { get => _type; set => Set(ref _type, value ?? Types[0]); }

    /// <summary>Base y crédito fiscal (13 %) que entrarán al libro de compras.</summary>
    public string CreditText
    {
        get
        {
            Numbers.TryParse(_total, out var total, emptyIsZero: true);
            Numbers.TryParse(_discounts, out var discounts, emptyIsZero: true);
            Numbers.TryParse(_notSubject, out var notSubject, emptyIsZero: true);
            var taxBase = Math.Max(0, total - discounts - notSubject);
            return $"Base para crédito fiscal {Fmt.Money(taxBase)} · crédito fiscal IVA 13 % {Fmt.Money(decimal.Round(taxBase * 0.13m, 2, MidpointRounding.AwayFromZero))}";
        }
    }

    public string? ResultMessage { get; private set; }

    protected override bool CanConfirm() => _number.Trim().Length > 0 && _authorization.Trim().Length > 0;

    protected override async Task<bool> SubmitAsync()
    {
        if (!BillingClock.TryParse(_date, out var date))
        {
            Error = "Escriba la fecha de la factura como día/mes/año, por ejemplo 20/09/2026.";
            return false;
        }
        if (!Numbers.TryParse(_total, out var total) || !Numbers.TryParse(_discounts, out var discounts, emptyIsZero: true)
                                                     || !Numbers.TryParse(_notSubject, out var notSubject, emptyIsZero: true))
        {
            Error = "Revise los importes (use coma o punto para los decimales).";
            return false;
        }
        ResultMessage = await _app.SendAsync(new RegisterSupplierInvoiceCommand(_receipt.ReceiptNumber, _number.Trim(), _authorization.Trim(),
            DateOnly.FromDateTime(date), total, discounts, notSubject, _type.Value, string.IsNullOrWhiteSpace(_controlCode) ? null : _controlCode.Trim()));
        return true;
    }
}
