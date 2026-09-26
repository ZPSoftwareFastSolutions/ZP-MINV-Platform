using System.Globalization;
using System.IO;
using MINV.Application.Billing;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;

namespace MINV.DesktopClient.ViewModels;

/// <summary>Mes del libro (año y mes) elegido de un combo.</summary>
public sealed record MonthOption(int Year, int Month, string Label)
{
    public override string ToString() => Label;

    public static IReadOnlyList<MonthOption> Recent(DateOnly today, int count = 18)
    {
        var first = new DateOnly(today.Year, today.Month, 1);
        return Enumerable.Range(0, count).Select(i => first.AddMonths(-i))
            .Select(d => new MonthOption(d.Year, d.Month, Fmt.Culture.TextInfo.ToTitleCase(d.ToString("MMMM yyyy", Fmt.Culture)))).ToList();
    }
}

/// <summary>Fila del libro de ventas.</summary>
public sealed record SalesBookItem(SalesBookRow Row)
{
    public string DateText => Row.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public string CufText => FiscalText.ShortCuf(Row.Cuf);

    public string DocumentText => Row.Complement is { Length: > 0 } c ? $"{Row.BuyerDocument}-{c}" : Row.BuyerDocument;

    public string KindText => Row.DocumentSector == Domain.Billing.SiatCodes.SectorPurchaseSale ? "Factura" : "Nota";

    public string StatusText => Row.Status switch
    {
        "V" => "Válida",
        "A" => "Anulada",
        "E" => "Extraviada",
        "N" => "No utilizada",
        "C" => "Contingencia",
        "L" => "Libre consignación",
        _ => Row.Status,
    };

    public string StatusBrush => Row.Status == "V" ? "Success" : Row.Status == "A" ? "StatusInactive" : "Warning";

    public string StatusSoftBrush => StatusBrush + "Soft";
}

/// <summary>Fila del libro de compras.</summary>
public sealed record PurchasesBookItem(PurchasesBookRow Row)
{
    public string DateText => Row.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public string AuthorizationText => FiscalText.ShortCuf(Row.AuthorizationCode);
}

/// <summary>Línea del resumen IVA / IT con su explicación.</summary>
public sealed record TaxLine(string Label, string Amount, string Explanation, bool IsTotal = false, string Brush = "TextPrimary");

/// <summary>
/// V4.1 · «Libros fiscales» del mes: libro de ventas IVA (facturas y notas del SIN), libro de compras (facturas de los
/// proveedores y las recepciones que todavía no la tienen) y el resumen IVA / IT para el contador; exporta en CSV (orden
/// de columnas de las plantillas del SIN) y en Excel.
/// </summary>
public sealed class FiscalBooksViewModel : PageViewModel
{
    private MonthOption? _month;
    private int _tab;
    private SalesBookView? _sales;
    private PurchasesBookView? _purchases;
    private TaxSummaryView? _summary;
    private PendingSupplierInvoiceItem? _pending;

    public FiscalBooksViewModel(AppServices app)
        : base(app, "libros-fiscales", "Libros fiscales", "Libro de ventas IVA, libro de compras y resumen IVA / IT del mes", Glyphs.Library)
    {
        ExportCsv = new AsyncRelayCommand(() => ExportAsync("csv"), () => _tab is 0 or 1);
        ExportExcel = new AsyncRelayCommand(() => ExportAsync("xlsx"), () => _tab is 0 or 1);
        RegisterInvoice = new AsyncRelayCommand<PendingSupplierInvoiceItem>(RegisterInvoiceAsync, _ => CanPurchases);
    }

    public BulkObservableCollection<MonthOption> Months { get; } = [];

    public MonthOption? Month
    {
        get => _month;
        set
        {
            if (Set(ref _month, value) && value is not null && HasLoaded)
            {
                _ = LoadAsync(force: false);
            }
        }
    }

    /// <summary>0 = ventas, 1 = compras, 2 = resumen IVA / IT.</summary>
    public int Tab
    {
        get => _tab;
        set
        {
            if (Set(ref _tab, value))
            {
                OnPropertiesChanged(nameof(IsSalesTab), nameof(IsPurchasesTab), nameof(IsSummaryTab), nameof(CanExport));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsSalesTab { get => _tab == 0; set { if (value) { Tab = 0; } } }

    public bool IsPurchasesTab { get => _tab == 1; set { if (value) { Tab = 1; } } }

    public bool IsSummaryTab { get => _tab == 2; set { if (value) { Tab = 2; } } }

    public bool CanExport => _tab is 0 or 1;

    /// <summary>Registrar facturas de proveedores exige el permiso de compras.</summary>
    public bool CanPurchases => App.Session.Can(PermissionCodes.PurchasingManage);

    public BulkObservableCollection<SalesBookItem> SalesRows { get; } = [];

    public BulkObservableCollection<PurchasesBookItem> PurchaseRows { get; } = [];

    public BulkObservableCollection<PendingSupplierInvoiceItem> PendingInvoices { get; } = [];

    public BulkObservableCollection<TaxLine> TaxLines { get; } = [];

    public PendingSupplierInvoiceItem? SelectedPending { get => _pending; set => Set(ref _pending, value); }

    public bool HasPending => PendingInvoices.Count > 0;

    public string SalesTotals => _sales is { } s
        ? $"{s.Rows.Count} registros · {s.Valid} válidas · {s.Voided} anuladas · total {Fmt.Money(s.Total)} · base {Fmt.Money(s.TaxBase)} · débito fiscal {Fmt.Money(s.TaxDebit)}"
        : string.Empty;

    public string PurchasesTotals => _purchases is { } p
        ? $"{p.Rows.Count} facturas · total {Fmt.Money(p.Total)} · base {Fmt.Money(p.TaxBase)} · crédito fiscal {Fmt.Money(p.TaxCredit)}"
        : string.Empty;

    public KpiCard SalesKpi { get; } = new("Ventas del mes (libro)", Glyphs.Invoice, "Success", "SuccessSoft");

    public KpiCard DebitKpi { get; } = new("Débito fiscal IVA", Glyphs.ArrowUp, "Warning", "WarningSoft");

    public KpiCard CreditKpi { get; } = new("Crédito fiscal IVA", Glyphs.ArrowDown, "Info", "InfoSoft");

    public KpiCard PayableKpi { get; } = new("IVA a pagar", Glyphs.Calculator);

    public bool NoSales => HasLoaded && SalesRows.Count == 0;

    public bool NoPurchases => HasLoaded && PurchaseRows.Count == 0;

    public AsyncRelayCommand ExportCsv { get; }

    public AsyncRelayCommand ExportExcel { get; }

    public AsyncRelayCommand<PendingSupplierInvoiceItem> RegisterInvoice { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        if (Months.Count == 0)
        {
            Months.ReplaceAll(MonthOption.Recent(BillingClock.Today(App)));
            _month = Months[0];
            OnPropertyChanged(nameof(Month));
        }
        var month = _month ?? Months[0];
        _sales = await App.SendAsync(new GetSalesBookQuery(month.Year, month.Month));
        _purchases = await App.SendAsync(new GetPurchasesBookQuery(month.Year, month.Month));
        _summary = await App.SendAsync(new GetTaxSummaryQuery(month.Year, month.Month));
        SalesRows.ReplaceAll(_sales.Rows.Select(r => new SalesBookItem(r)));
        PurchaseRows.ReplaceAll(_purchases.Rows.Select(r => new PurchasesBookItem(r)));
        if (CanPurchases)
        {
            var pending = await App.SendAsync(new GetReceiptsWithoutInvoiceQuery());
            PendingInvoices.ReplaceAll(pending.Select(p => new PendingSupplierInvoiceItem(p)));
        }
        BuildTaxLines(_summary);
        SalesKpi.Value = Fmt.Money(_summary.GrossSales);
        SalesKpi.Detail = $"{_summary.Invoices} facturas · {_summary.VoidedInvoices} anuladas · {_summary.Notes} notas";
        DebitKpi.Value = Fmt.Money(_summary.TaxDebit);
        DebitKpi.Detail = "13 % de la base de las facturas válidas";
        CreditKpi.Value = Fmt.Money(_summary.TaxCreditPurchases + _summary.TaxCreditNotes);
        CreditKpi.Detail = $"Compras {Fmt.Money(_summary.TaxCreditPurchases)} · notas {Fmt.Money(_summary.TaxCreditNotes)}";
        PayableKpi.Value = _summary.VatPayable > 0 ? Fmt.Money(_summary.VatPayable) : Fmt.Money(0);
        PayableKpi.Detail = _summary.VatCarryForward > 0 ? $"Saldo a favor {Fmt.Money(_summary.VatCarryForward)}" : $"IT {Fmt.Money(_summary.TransactionTax)}";
        Subtitle = $"{month.Label} · libro de ventas IVA, libro de compras y resumen IVA / IT";
        OnPropertiesChanged(nameof(Subtitle), nameof(SalesTotals), nameof(PurchasesTotals), nameof(HasPending), nameof(NoSales), nameof(NoPurchases));
    }

    private void BuildTaxLines(TaxSummaryView s)
    {
        TaxLines.ReplaceAll(
        [
            new("Ventas brutas facturadas", Fmt.Money(s.GrossSales),
                "Suma de las facturas válidas, pendientes o fuera de línea del mes (las anuladas no cuentan)."),
            new("Devoluciones con nota crédito-débito", "− " + Fmt.Money(s.CreditNotes), "Monto devuelto en las notas crédito-débito emitidas en el mes."),
            new("Débito fiscal IVA (13 %)", Fmt.Money(s.TaxDebit), "13 % de la base para débito fiscal del libro de ventas (el IVA que cobró en sus ventas)."),
            new("Crédito fiscal de compras", "− " + Fmt.Money(s.TaxCreditPurchases),
                "13 % de la base de las facturas de proveedores registradas en el libro de compras (el IVA que pagó al comprar)."),
            new("Crédito fiscal de las notas", "− " + Fmt.Money(s.TaxCreditNotes), "13 % de lo devuelto con notas crédito-débito: reduce el IVA a pagar."),
            new("IVA a pagar", Fmt.Money(s.VatPayable), "Débito fiscal menos los créditos fiscales. Si es negativo, queda como saldo a favor para el mes siguiente.",
                true, s.VatPayable > 0 ? "Warning" : "Success"),
            new("Saldo a favor (crédito que pasa al mes siguiente)", Fmt.Money(s.VatCarryForward), "Crédito fiscal que no se usó este mes."),
            new("Base del IT (ventas netas de devoluciones)", Fmt.Money(s.TransactionTaxBase), "Ingresos brutos del mes menos las devoluciones."),
            new("Impuesto a las Transacciones (IT, 3 %)", Fmt.Money(s.TransactionTax),
                "3 % de la base del IT. El traslado a los formularios 200 (IVA) y 400 (IT) lo hace el contador.", true),
        ]);
    }

    private async Task ExportAsync(string format)
    {
        if (_month is not { } month)
        {
            return;
        }
        var purchases = _tab == 1;
        try
        {
            var file = await App.SendAsync(new ExportFiscalBookQuery(month.Year, month.Month, purchases, format));
            var path = App.Settings.IsReadOnly
                ? FiscalOutput.Save(file.FileName, file.Content)
                : FiscalOutput.AskSavePath(file.FileName, purchases ? "Exportar el libro de compras" : "Exportar el libro de ventas");
            if (path is null)
            {
                return;
            }
            await File.WriteAllBytesAsync(path, file.Content);
            App.Notify.Success(purchases ? "Libro de compras exportado" : "Libro de ventas exportado", Path.GetFileName(path));
        }
        catch (Exception ex) when (AppServices.IsExpected(ex) || ex is IOException or UnauthorizedAccessException)
        {
            App.Notify.Error("No se pudo exportar el libro", AppServices.IsExpected(ex) ? AppServices.Describe(ex) : ex.Message);
        }
    }

    private async Task RegisterInvoiceAsync(PendingSupplierInvoiceItem item)
    {
        var dialog = new SupplierInvoiceDialog(App, item.Row);
        if (await App.Dialogs.ShowAsync(dialog))
        {
            App.Notify.Success("Factura del proveedor registrada", FiscalText.Plain(dialog.ResultMessage ?? string.Empty));
            await LoadAsync(force: true);
        }
    }
}

/// <summary>Recepción de mercadería sin factura del proveedor.</summary>
public sealed record PendingSupplierInvoiceItem(PendingSupplierInvoiceRow Row)
{
    public string Text => $"{Row.ReceiptNumber} · {Row.Supplier}";

    public string Detail => $"Recibida el {Row.ReceivedOn:dd/MM/yyyy} · {Fmt.Money(Row.Total)}";
}
