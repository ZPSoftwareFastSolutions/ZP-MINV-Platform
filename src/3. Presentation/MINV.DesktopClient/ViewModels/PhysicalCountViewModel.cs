using MINV.Application.Inventory.PhysicalCounts;
using MINV.Application.Inventory.Queries;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;

namespace MINV.DesktopClient.ViewModels;

/// <summary>
/// Toma física (en la V2.1: 13_CONTEO + GenerarAjustesConteo): abrir, contar producto por producto (a mano o con el
/// escáner), ver la diferencia contra el stock exacto y contabilizar los ajustes en una sola operación confirmada.
/// </summary>
public sealed class PhysicalCountViewModel : PageViewModel, IScannerTarget
{
    private PhysicalCountSheet? _sheet;
    private ProductCard? _product;
    private string _binCode = string.Empty;
    private string _countedText = string.Empty;
    private string? _notes;
    private int _focusRequest;

    public PhysicalCountViewModel(AppServices app) : base(app, "conteo", "Toma física", "Conteo del almacén y ajustes automáticos", Glyphs.Checklist)
    {
        Picker = new ProductPickerViewModel(app.Data);
        Picker.Picked += async (_, item) => await LoadProductAsync(item.Sku);
        Start = new AsyncRelayCommand(StartAsync, () => _sheet is null);
        Record = new AsyncRelayCommand(RecordAsync, () => CanRecord);
        RemoveLine = new AsyncRelayCommand<CountLineItem>(RemoveAsync);
        Post = new AsyncRelayCommand(PostAsync, () => _sheet is { Lines.Count: > 0 } && CanPost);
        Cancel = new AsyncRelayCommand(CancelAsync, () => _sheet is not null && CanPost);
    }

    public ProductPickerViewModel Picker { get; }

    public BulkObservableCollection<CountLineItem> Lines { get; } = [];

    public BulkObservableCollection<string> Bins { get; } = [];

    public bool HasOpenCount => _sheet is not null;

    public bool HasNoCount => HasLoaded && _sheet is null;

    public string Number => _sheet?.Number ?? "";

    public string CountInfo => _sheet is { } s ? $"Abierta el {Fmt.Date(s.CountDate)} · almacén {s.WarehouseCode}" + (s.Notes is { Length: > 0 } n ? " · " + n : "") : "";

    public bool CanPost => App.Session.Can(PermissionCodes.PhysicalCountPost);

    public int LineCount => Lines.Count;

    public int Surpluses => Lines.Count(l => l.Kind > 0);

    public int Shortages => Lines.Count(l => l.Kind < 0);

    public int Matches => Lines.Count(l => l.Kind == 0);

    public bool IsSheetEmpty => HasOpenCount && Lines.Count == 0;

    public string? NewCountNotes
    {
        get => _notes;
        set => Set(ref _notes, value);
    }

    public ProductCard? Product
    {
        get => _product;
        private set
        {
            if (Set(ref _product, value))
            {
                OnPropertiesChanged(nameof(HasProduct), nameof(ProductName), nameof(SystemText), nameof(DifferenceText), nameof(DifferenceKind),
                    nameof(CanRecord), nameof(UnitText));
            }
        }
    }

    public bool HasProduct => _product is not null;

    public string ProductName => _product is { } p ? $"{p.Sku} · {p.Name}" : "";

    public string UnitText => _product?.Unit ?? "";

    public string BinCode
    {
        get => _binCode;
        set
        {
            if (Set(ref _binCode, (value ?? "").Trim().ToUpperInvariant()))
            {
                OnPropertiesChanged(nameof(SystemText), nameof(DifferenceText), nameof(DifferenceKind), nameof(CanRecord));
            }
        }
    }

    public string CountedText
    {
        get => _countedText;
        set
        {
            if (Set(ref _countedText, value ?? ""))
            {
                OnPropertiesChanged(nameof(DifferenceText), nameof(DifferenceKind), nameof(CanRecord));
            }
        }
    }

    private decimal SystemAtBin => _product?.Bins.Where(b => b.BinCode == _binCode && b.LotNumber == Batch.DefaultLotNumber).Sum(b => b.OnHand) ?? 0;

    public string SystemText => _product is null ? "—" : Fmt.Qty(SystemAtBin, _product.Unit);

    private decimal? Counted => Fmt.TryParseQuantity(_countedText, out var q) && q >= 0 ? q : null;

    public string DifferenceText => _product is null || Counted is not { } c ? "—"
        : c - SystemAtBin == 0 ? "Cuadra" : Fmt.Signed(c - SystemAtBin) + " " + _product.Unit;

    public int DifferenceKind => Counted is { } c && _product is not null ? Math.Sign(c - SystemAtBin) : 0;

    public bool CanRecord => _sheet is not null && _product is not null && Counted is not null && _binCode.Length > 0;

    public int FocusRequest
    {
        get => _focusRequest;
        private set => Set(ref _focusRequest, value);
    }

    public AsyncRelayCommand Start { get; }

    public AsyncRelayCommand Record { get; }

    public AsyncRelayCommand<CountLineItem> RemoveLine { get; }

    public AsyncRelayCommand Post { get; }

    public AsyncRelayCommand Cancel { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        await Picker.EnsureLoadedAsync(force);
        if (Bins.Count == 0 || force)
        {
            Bins.ReplaceAll((await App.SendAsync(new GetBinsQuery())).Select(b => b.Code));
        }
        await ReloadSheetAsync();
    }

    public override void OnNavigatedTo(object? parameter) => FocusRequest++;

    public bool OnScanned(string code)
    {
        if (_sheet is null)
        {
            return false;
        }
        if (!Picker.TrySelectCode(code))
        {
            App.Notify.Warning("Código no encontrado", $"«{code}» no es un SKU ni un código de barras del catálogo.");
        }
        return true;
    }

    private async Task ReloadSheetAsync()
    {
        _sheet = await App.SendAsync(new GetOpenPhysicalCountQuery());
        var now = App.Now;
        Lines.ReplaceAll(_sheet?.Lines.Select(l => new CountLineItem(l, now)) ?? []);
        OnPropertiesChanged(nameof(HasOpenCount), nameof(HasNoCount), nameof(Number), nameof(CountInfo), nameof(LineCount), nameof(Surpluses),
            nameof(Shortages), nameof(Matches), nameof(IsSheetEmpty), nameof(CanRecord));
        Subtitle = _sheet is null ? "Conteo del almacén y ajustes automáticos" : $"{_sheet.Number} en curso";
        OnPropertyChanged(nameof(Subtitle));
    }

    private async Task LoadProductAsync(string sku)
    {
        try
        {
            var card = await App.SendAsync(new GetProductCardQuery(sku, 1));
            Product = card;
            BinCode = card.PrimaryBin ?? card.Bins.FirstOrDefault()?.BinCode ?? Bins.FirstOrDefault() ?? "";
            OnPropertyChanged(nameof(SystemText));
        }
        catch (Exception ex)
        {
            Product = null;
            App.Notify.Error("No se pudo abrir el producto", AppServices.Describe(ex));
        }
    }

    private Task StartAsync() => RunAsync(async () =>
    {
        var result = await App.SendAsync(new OpenPhysicalCountCommand(App.Session.Workspace.WarehouseCode, null,
            string.IsNullOrWhiteSpace(_notes) ? null : _notes.Trim()));
        NewCountNotes = null;
        App.Notify.Success("Toma física abierta", $"{result.Number}: cuente cada producto y registre la cantidad.");
        await ReloadSheetAsync();
        FocusRequest++;
    }, "No se pudo abrir la toma física");

    private Task RecordAsync() => RunAsync(async () =>
    {
        var product = _product!;
        await App.SendAsync(new RecordCountCommand(_sheet!.Id, product.Sku, _binCode, Counted!.Value));
        App.Notify.Success("Conteo registrado", $"{product.Sku}: {Fmt.Qty(Counted!.Value, product.Unit)} en {_binCode}");
        CountedText = string.Empty;
        Picker.Clear();
        Product = null;
        await ReloadSheetAsync();
        FocusRequest++;
    }, "No se registró el conteo");

    private Task RemoveAsync(CountLineItem line) => RunAsync(async () =>
    {
        await App.SendAsync(new RemoveCountCommand(_sheet!.Id, line.Sku, line.BinCode));
        App.Notify.Info("Conteo quitado", $"{line.Sku} en {line.BinCode}");
        await ReloadSheetAsync();
    }, "No se pudo quitar el conteo");

    private async Task PostAsync()
    {
        var sheet = _sheet!;
        var confirmed = await App.Dialogs.ConfirmAsync($"Contabilizar {sheet.Number}",
            "Se generarán los ajustes contra el stock exacto de este momento, en una sola operación. Los movimientos no se pueden borrar: " +
            "un error se corrige con otro ajuste.",
            "Generar ajustes", "Revisar primero", glyph: Glyphs.Checklist,
            details: [$"{LineCount} producto(s) contado(s)", $"{Surpluses} sobrante(s) → AJUSTE (+)", $"{Shortages} faltante(s) → AJUSTE (−)",
                $"{Matches} cuadra(n): sin movimiento"]);
        if (!confirmed)
        {
            return;
        }
        await RunAsync(async () =>
        {
            var result = await App.SendAsync(new PostPhysicalCountCommand(sheet.Id, Confirmed: true));
            App.Notify.Success("Toma física contabilizada", result.Message.TrimStart('✔', ' '));
            App.Data.Invalidate();
            await ReloadSheetAsync();
        }, "No se contabilizó la toma física");
    }

    private async Task CancelAsync()
    {
        var sheet = _sheet!;
        if (!await App.Dialogs.ConfirmAsync($"Anular {sheet.Number}", "Se descartarán los conteos registrados y no se generará ningún ajuste.",
                "Anular toma", "Volver", isDanger: true))
        {
            return;
        }
        await RunAsync(async () =>
        {
            var message = await App.SendAsync(new CancelPhysicalCountCommand(sheet.Id));
            App.Notify.Info("Toma física anulada", message.TrimStart('✔', ' '));
            await ReloadSheetAsync();
        }, "No se pudo anular la toma física");
    }
}
