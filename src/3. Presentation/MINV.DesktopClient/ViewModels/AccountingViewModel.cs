using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using MINV.Application.Accounting;
using MINV.Application.Common;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Accounting;

namespace MINV.DesktopClient.ViewModels;

public sealed class AccountItem(AccountRow r)
{
    public AccountRow Row { get; } = r;

    public string Code => Row.Code;

    public string Name => Row.Name;

    public string Display => $"{Row.Code} · {Row.Name}";

    public double Indent => (Row.Level - 1) * 18;

    public bool IsGroup => !Row.IsPostable;

    public string TypeText => AccountingText.Type(Row.Type);

    public string DebitText => Row.Debit != 0 ? Row.Debit.ToString("N2", Fmt.Culture) : "";

    public string CreditText => Row.Credit != 0 ? Row.Credit.ToString("N2", Fmt.Culture) : "";

    public string BalanceText => Row.Balance != 0 ? Fmt.Money(Row.Balance) : "—";

    public bool IsNegative => Row.Balance < 0;

    public override string ToString() => Display;
}

public sealed class JournalEntryItem(JournalEntryRow r)
{
    public JournalEntryRow Row { get; } = r;

    public string Number => Row.Number;

    public string DateText => Fmt.Date(Row.Date);

    public string Description => Row.Description;

    public string TotalText => Fmt.Money(Row.Total);

    public IReadOnlyList<JournalLineRow> Lines => Row.Lines;

    /// <summary>Origen del asiento según su descripción (venta, compra, anulación, manual…).</summary>
    public string Origin => Row.Description switch
    {
        var d when d.StartsWith("Venta", StringComparison.OrdinalIgnoreCase) => "Venta",
        var d when d.StartsWith("Anulación", StringComparison.OrdinalIgnoreCase) => "Anulación",
        var d when d.StartsWith("Recepción", StringComparison.OrdinalIgnoreCase) || d.StartsWith("Compra", StringComparison.OrdinalIgnoreCase) => "Compra",
        var d when d.Contains("conteo", StringComparison.OrdinalIgnoreCase) || d.Contains("ajuste", StringComparison.OrdinalIgnoreCase) => "Inventario",
        _ => "Manual",
    };
}

public static class AccountingText
{
    public static string Type(AccountType type) => type switch
    {
        AccountType.Asset => "Activo",
        AccountType.Liability => "Pasivo",
        AccountType.Equity => "Patrimonio",
        AccountType.Revenue => "Ingreso",
        _ => "Costo / gasto",
    };
}

/// <summary>Línea de un asiento manual: cuenta (combo), debe, haber y glosa.</summary>
public sealed class EntryLine(IReadOnlyList<AccountItem> accounts, Action changed) : ObservableObject
{
    private AccountItem? _account;
    private string _debit = string.Empty;
    private string _credit = string.Empty;
    private string _memo = string.Empty;

    public IReadOnlyList<AccountItem> Accounts { get; } = accounts;

    public AccountItem? Account { get => _account; set => Set(ref _account, value); }

    public string Debit
    {
        get => _debit;
        set
        {
            if (Set(ref _debit, value ?? string.Empty))
            {
                changed();
            }
        }
    }

    public string Credit
    {
        get => _credit;
        set
        {
            if (Set(ref _credit, value ?? string.Empty))
            {
                changed();
            }
        }
    }

    public string Memo { get => _memo; set => Set(ref _memo, value ?? string.Empty); }

    public decimal DebitValue => Numbers.TryParse(_debit, out var d) ? d : 0;

    public decimal CreditValue => Numbers.TryParse(_credit, out var c) ? c : 0;
}

/// <summary>Plantilla de asiento (cuentas prellenadas; el usuario pone los montos).</summary>
public sealed record EntryTemplate(string Label, string Description, IReadOnlyList<(string Account, bool Debit)> Lines)
{
    public override string ToString() => Label;
}

/// <summary>
/// Contabilidad automática: estado de resultados del período, libro diario (con los asientos que generan ventas,
/// compras y anulaciones), plan de cuentas con saldos y asientos manuales con plantillas (cuentas en combos, validación
/// de partida doble antes de enviar: el dominio vuelve a verificarla).
/// </summary>
public sealed class AccountingViewModel : PageViewModel
{
    private PeriodOption? _period;
    private string _tab = "results";
    private IncomeStatement? _statement;
    private IReadOnlyList<JournalEntryItem> _journal = [];
    private IReadOnlyList<AccountItem> _accounts = [];
    private IReadOnlyList<AccountItem> _postable = [];
    private string _journalSearch = string.Empty;
    private Choice<string> _journalOrigin;
    private Choice<AccountType?> _accountType;
    private DateOption? _entryDate;
    private string _entryDescription = string.Empty;
    private EntryTemplate? _template;
    private string? _entryError;
    private AccountItem? _parent;
    private string _newCode = string.Empty;
    private string _newName = string.Empty;

    public AccountingViewModel(AppServices app) : base(app, "contabilidad", "Contabilidad", "Resultados, libro diario y plan de cuentas", Glyphs.Library)
    {
        Origins = [new("Todos los asientos", "all"), new("Ventas", "Venta"), new("Compras", "Compra"), new("Anulaciones", "Anulación"), new("Manuales", "Manual")];
        _journalOrigin = Origins[0];
        AccountTypes =
        [
            new("Todas las cuentas", null), new("Activo", AccountType.Asset), new("Pasivo", AccountType.Liability), new("Patrimonio", AccountType.Equity),
            new("Ingresos", AccountType.Revenue), new("Costos y gastos", AccountType.Expense),
        ];
        _accountType = AccountTypes[0];
        Templates =
        [
            new("Asiento en blanco", "", []),
            new("Depósito de caja en el banco", "Depósito del efectivo en el banco", [(AccountCodes.Bank, true), (AccountCodes.Cash, false)]),
            new("Pago de sueldos", "Pago de sueldos del mes", [("6.1.01", true), (AccountCodes.Bank, false)]),
            new("Pago de alquiler", "Pago del alquiler del local", [("6.1.02", true), (AccountCodes.Bank, false)]),
            new("Pago de servicios básicos", "Pago de luz, agua e internet", [("6.1.03", true), (AccountCodes.Cash, false)]),
            new("Pago a proveedores", "Pago a proveedores por transferencia", [(AccountCodes.Payables, true), (AccountCodes.Bank, false)]),
            new("Aporte de capital", "Aporte de capital de los socios", [(AccountCodes.Bank, true), (AccountCodes.Capital, false)]),
            new("Gastos administrativos", "Útiles de oficina y gastos varios", [("6.1.04", true), (AccountCodes.Cash, false)]),
        ];
        EntryLines.CollectionChanged += (_, _) => EntryChanged();
        AddEntryLine = new RelayCommand(() => EntryLines.Add(new EntryLine(_postable, EntryChanged)));
        RemoveEntryLine = new RelayCommand<EntryLine>(l => EntryLines.Remove(l), _ => EntryLines.Count > 2);
        SaveEntry = new AsyncRelayCommand(SaveEntryAsync, () => IsBalanced);
        ClearEntry = new RelayCommand(ResetEntry);
        CreateAccount = new AsyncRelayCommand(CreateAccountAsync, () => _parent is not null && _newName.Trim().Length > 1);
        Export = new RelayCommand(ExportCsv);
    }

    public BulkObservableCollection<PeriodOption> Periods { get; } = [];

    public PeriodOption? Period
    {
        get => _period;
        set
        {
            if (Set(ref _period, value) && value is not null && HasLoaded)
            {
                _ = LoadAsync(force: false);
            }
        }
    }

    public string Tab
    {
        get => _tab;
        set
        {
            if (Set(ref _tab, value))
            {
                OnPropertiesChanged(nameof(IsResults), nameof(IsJournal), nameof(IsAccounts), nameof(IsNewEntry));
            }
        }
    }

    public bool IsResults { get => _tab == "results"; set { if (value) { Tab = "results"; } } }

    public bool IsJournal { get => _tab == "journal"; set { if (value) { Tab = "journal"; } } }

    public bool IsAccounts { get => _tab == "accounts"; set { if (value) { Tab = "accounts"; } } }

    public bool IsNewEntry { get => _tab == "entry"; set { if (value) { Tab = "entry"; } } }

    // ------------------------------------------------------------------------------------------------ resultados
    public KpiCard RevenueKpi { get; } = new("Ingresos (sin IVA)", Glyphs.Money, "Success", "SuccessSoft");

    public KpiCard CostKpi { get; } = new("Costo de ventas", Glyphs.Box, "Warning", "WarningSoft");

    public KpiCard GrossKpi { get; } = new("Utilidad bruta", Glyphs.ArrowUp, "Info", "InfoSoft");

    public KpiCard ExpensesKpi { get; } = new("Gastos de operación", Glyphs.Briefcase, "Danger", "DangerSoft");

    public KpiCard NetKpi { get; } = new("Utilidad neta", Glyphs.Flag, "Brand", "BrandSoft");

    public IncomeStatement? Statement { get => _statement; private set => Set(ref _statement, value); }

    public BulkObservableCollection<ChartSegment> ResultSegments { get; } = [];

    // ------------------------------------------------------------------------------------------------ libro diario
    public IReadOnlyList<Choice<string>> Origins { get; }

    public Choice<string> JournalOrigin { get => _journalOrigin; set { if (Set(ref _journalOrigin, value ?? Origins[0])) { OnPropertyChanged(nameof(Journal)); } } }

    public string JournalSearch { get => _journalSearch; set { if (Set(ref _journalSearch, value ?? string.Empty)) { OnPropertyChanged(nameof(Journal)); } } }

    public IReadOnlyList<JournalEntryItem> Journal => _journal
        .Where(j => _journalOrigin.Value == "all" || j.Origin == _journalOrigin.Value)
        .Where(j => _journalSearch.Trim().Length == 0 || j.Number.Contains(_journalSearch.Trim(), StringComparison.OrdinalIgnoreCase)
                    || Fmt.Culture.CompareInfo.IndexOf(j.Description, _journalSearch.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
        .ToList();

    public string JournalSummary => $"{_journal.Count} asientos · debe = haber en todos (partida doble verificada por la base)";

    // ------------------------------------------------------------------------------------------------ plan de cuentas
    public IReadOnlyList<Choice<AccountType?>> AccountTypes { get; }

    public Choice<AccountType?> AccountFilter { get => _accountType; set { if (Set(ref _accountType, value ?? AccountTypes[0])) { OnPropertyChanged(nameof(Accounts)); } } }

    public IReadOnlyList<AccountItem> Accounts => _accountType.Value is { } t ? _accounts.Where(a => a.Row.Type == t).ToList() : _accounts;

    public BulkObservableCollection<AccountItem> Groups { get; } = [];

    public AccountItem? Parent
    {
        get => _parent;
        set
        {
            if (Set(ref _parent, value) && value is not null)
            {
                NewCode = SuggestCode(value);
            }
        }
    }

    public string NewCode { get => _newCode; set => Set(ref _newCode, value ?? string.Empty); }

    public string NewName { get => _newName; set => Set(ref _newName, value ?? string.Empty); }

    // ------------------------------------------------------------------------------------------------ asiento manual
    public IReadOnlyList<EntryTemplate> Templates { get; }

    public BulkObservableCollection<DateOption> EntryDates { get; } = [];

    public ObservableCollection<EntryLine> EntryLines { get; } = [];

    public EntryTemplate? Template
    {
        get => _template;
        set
        {
            if (Set(ref _template, value) && value is not null)
            {
                ApplyTemplate(value);
            }
        }
    }

    public DateOption? EntryDate { get => _entryDate; set => Set(ref _entryDate, value); }

    public string EntryDescription { get => _entryDescription; set => Set(ref _entryDescription, value ?? string.Empty); }

    public string DebitTotalText => Fmt.Money(EntryLines.Sum(l => l.DebitValue));

    public string CreditTotalText => Fmt.Money(EntryLines.Sum(l => l.CreditValue));

    public bool IsBalanced => EntryLines.Sum(l => l.DebitValue) is var d && d > 0 && d == EntryLines.Sum(l => l.CreditValue);

    public string BalanceText => IsBalanced
        ? "✔ Cuadrado: el debe es igual al haber"
        : EntryLines.Sum(l => l.DebitValue) == 0 ? "Escriba los montos del debe y del haber"
        : $"Diferencia: {Fmt.Money(Math.Abs(EntryLines.Sum(l => l.DebitValue) - EntryLines.Sum(l => l.CreditValue)))}";

    public string? EntryError { get => _entryError; private set => Set(ref _entryError, value); }

    public RelayCommand AddEntryLine { get; }

    public RelayCommand<EntryLine> RemoveEntryLine { get; }

    public AsyncRelayCommand SaveEntry { get; }

    public RelayCommand ClearEntry { get; }

    public AsyncRelayCommand CreateAccount { get; }

    public RelayCommand Export { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        var today = DateOnly.FromDateTime(App.Now.ToLocalTime().DateTime);
        if (Periods.Count == 0)
        {
            Periods.ReplaceAll(PeriodOption.Presets(today));
            _period = Periods.First(p => p.Label == "Este mes");
            OnPropertyChanged(nameof(Period));
            EntryDates.ReplaceAll(DateOption.Past(today, 31));
            _entryDate = EntryDates[0];
            OnPropertyChanged(nameof(EntryDate));
        }
        var period = _period ?? Periods[0];
        Subtitle = $"{period.Label} · {period.RangeText}";
        OnPropertyChanged(nameof(Subtitle));

        var statement = await App.SendAsync(new GetIncomeStatementQuery(period.From, period.To));
        Statement = statement;
        RevenueKpi.Value = Fmt.Money(statement.TotalRevenue);
        CostKpi.Value = Fmt.Money(statement.TotalCosts);
        CostKpi.Detail = statement.TotalRevenue > 0 ? $"{(statement.TotalCosts / statement.TotalRevenue).ToString("P1", Fmt.Culture)} de los ingresos" : null;
        GrossKpi.Value = Fmt.Money(statement.GrossProfit);
        GrossKpi.Detail = statement.TotalRevenue > 0 ? $"Margen {(statement.GrossProfit / statement.TotalRevenue).ToString("P1", Fmt.Culture)}" : null;
        ExpensesKpi.Value = Fmt.Money(statement.TotalExpenses);
        NetKpi.Value = Fmt.Money(statement.NetIncome);
        NetKpi.Detail = statement.NetIncome >= 0 ? "Ganancia del período" : "Pérdida del período";
        ResultSegments.ReplaceAll(new[]
        {
            new ChartSegment("Costo de ventas", (double)Math.Max(0, statement.TotalCosts), "Warning"),
            new ChartSegment("Gastos", (double)Math.Max(0, statement.TotalExpenses), "Danger"),
            new ChartSegment("Utilidad neta", (double)Math.Max(0, statement.NetIncome), "Success"),
        });

        _journal = (await App.SendAsync(new GetJournalQuery(period.From, period.To))).OrderByDescending(j => j.Number).Select(j => new JournalEntryItem(j)).ToList();
        OnPropertiesChanged(nameof(Journal), nameof(JournalSummary));

        _accounts = (await App.SendAsync(new GetChartOfAccountsQuery())).Select(a => new AccountItem(a)).ToList();
        _postable = _accounts.Where(a => a.Row.IsPostable).ToList();
        OnPropertyChanged(nameof(Accounts));
        var parent = _parent?.Code;
        Groups.ReplaceAll(_accounts.Where(a => a.IsGroup));
        _parent = Groups.FirstOrDefault(g => g.Code == parent) ?? Groups.FirstOrDefault(g => g.Code == "6.1");
        OnPropertyChanged(nameof(Parent));
        if (_parent is not null && _newCode.Length == 0)
        {
            NewCode = SuggestCode(_parent);
        }
        if (EntryLines.Count == 0 || EntryLines[0].Accounts != _postable)
        {
            ResetEntry();
        }
    }

    private string SuggestCode(AccountItem parent)
    {
        var children = _accounts.Where(a => a.Row.ParentCode == parent.Code).Select(a => a.Code).ToList();
        var next = children.Select(c => int.TryParse(c[(c.LastIndexOf('.') + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .DefaultIfEmpty(0).Max() + 1;
        return parent.Code + "." + next.ToString(parent.Row.Level >= 2 ? "00" : "0", CultureInfo.InvariantCulture);
    }

    private void ApplyTemplate(EntryTemplate template)
    {
        EntryLines.Clear();
        foreach (var (account, _) in template.Lines)
        {
            EntryLines.Add(new EntryLine(_postable, EntryChanged) { Account = _postable.FirstOrDefault(a => a.Code == account) });
        }
        while (EntryLines.Count < 2)
        {
            EntryLines.Add(new EntryLine(_postable, EntryChanged));
        }
        if (template.Description.Length > 0)
        {
            EntryDescription = template.Description;
        }
    }

    private void ResetEntry()
    {
        EntryError = null;
        EntryDescription = string.Empty;
        _template = Templates[0];
        OnPropertyChanged(nameof(Template));
        EntryLines.Clear();
        EntryLines.Add(new EntryLine(_postable, EntryChanged));
        EntryLines.Add(new EntryLine(_postable, EntryChanged));
    }

    private void EntryChanged() => OnPropertiesChanged(nameof(DebitTotalText), nameof(CreditTotalText), nameof(IsBalanced), nameof(BalanceText));

    private async Task SaveEntryAsync()
    {
        EntryError = null;
        if (_entryDate is null || _entryDescription.Trim().Length < 3)
        {
            EntryError = "Elija la fecha y escriba la descripción del asiento.";
            return;
        }
        var lines = new List<JournalLineSpec>();
        foreach (var line in EntryLines.Where(l => l.DebitValue > 0 || l.CreditValue > 0))
        {
            if (line.Account is null)
            {
                EntryError = "Elija la cuenta de cada línea con monto.";
                return;
            }
            if (line.DebitValue > 0 && line.CreditValue > 0)
            {
                EntryError = $"La línea de {line.Account.Name} tiene debe y haber: use una línea para cada uno.";
                return;
            }
            lines.Add(new JournalLineSpec(line.Account.Code, line.DebitValue, line.CreditValue, string.IsNullOrWhiteSpace(line.Memo) ? null : line.Memo.Trim()));
        }
        try
        {
            var number = await App.SendAsync(new CreateJournalEntryCommand(_entryDate.Date, _entryDescription.Trim(), lines));
            App.Notify.Success($"Asiento {number} registrado", $"{_entryDescription.Trim()} · {DebitTotalText}");
            ResetEntry();
            Tab = "journal";
            await LoadAsync(force: true);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            EntryError = AppServices.Describe(ex);
        }
    }

    private async Task CreateAccountAsync()
    {
        try
        {
            var code = await App.SendAsync(new CreateAccountCommand(_newCode.Trim(), _newName.Trim(), _parent!.Code));
            App.Notify.Success("Cuenta creada", $"{code} · {_newName.Trim()} (bajo {_parent.Name})");
            NewName = string.Empty;
            NewCode = string.Empty;
            await LoadAsync(force: true);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo crear la cuenta", AppServices.Describe(ex));
        }
    }

    private void ExportCsv()
    {
        var path = FileDialogs.SaveCsv(_tab == "accounts" ? $"plan-de-cuentas-{DateTime.Now:yyyyMMdd}.csv" : $"libro-diario-{_period?.From:yyyyMMdd}-{_period?.To:yyyyMMdd}.csv");
        if (path is null)
        {
            return;
        }
        if (_tab == "accounts")
        {
            Csv.Write(path, ["Código", "Cuenta", "Tipo", "Nivel", "Imputable", "Debe", "Haber", "Saldo"],
                _accounts.Select(a => new object?[] { a.Code, a.Name, a.TypeText, a.Row.Level, a.Row.IsPostable ? "SÍ" : "NO", a.Row.Debit, a.Row.Credit, a.Row.Balance }));
        }
        else
        {
            Csv.Write(path, ["Asiento", "Fecha", "Descripción", "Cuenta", "Nombre", "Debe", "Haber", "Glosa"],
                _journal.SelectMany(j => j.Lines.Select(l => new object?[] { j.Number, j.Row.Date, j.Description, l.AccountCode, l.AccountName, l.Debit, l.Credit, l.Memo })));
        }
        App.Notify.Success("Exportado", Path.GetFileName(path));
    }
}
