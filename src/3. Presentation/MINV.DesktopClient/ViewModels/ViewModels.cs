using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using MediatR;
using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.Application.Inventory.Movements;
using MINV.Application.Inventory.Queries;
using MINV.DesktopClient.Mvvm;
using MINV.Domain.Common;
using MINV.Domain.Inventory;

namespace MINV.DesktopClient.ViewModels;

/// <summary>Mensaje para el usuario: ✔ verde, ✖ rojo (como la columna Resultado de la V2.1).</summary>
public sealed record StatusMessage(string Text, bool IsError);

public abstract class PageViewModel(string title, string icon) : ObservableObject
{
    private StatusMessage? _status;
    private bool _busy;

    public string Title { get; } = title;

    public string Icon { get; } = icon;

    public StatusMessage? Status
    {
        get => _status;
        protected set => Set(ref _status, value);
    }

    public bool IsBusy
    {
        get => _busy;
        protected set => Set(ref _busy, value);
    }

    public virtual Task LoadAsync() => Task.CompletedTask;

    /// <summary>Ejecuta un caso de uso y traduce las excepciones esperadas a mensajes (nunca un cuadro de error).</summary>
    protected async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is DomainException or RequestValidationException or AccessDeniedException
                                       or NotFoundException or ConcurrencyConflictException or AuthenticationFailedException)
        {
            Status = new StatusMessage("✖ " + ex.Message, true);
        }
        catch (Exception ex)
        {
            Status = new StatusMessage("✖ Error inesperado: " + ex.GetBaseException().Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

// ------------------------------------------------------------------------------------------------ login
public sealed class LoginViewModel(IMediator mediator) : ObservableObject
{
    private string _tenantCode = "DEMO";
    private string _email = string.Empty;
    private string? _error;
    private bool _busy;

    public string TenantCode
    {
        get => _tenantCode;
        set => Set(ref _tenantCode, value);
    }

    public string Email
    {
        get => _email;
        set => Set(ref _email, value);
    }

    public string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    public bool IsBusy
    {
        get => _busy;
        private set => Set(ref _busy, value);
    }

    public LoginResult? Result { get; private set; }

    public async Task<bool> LoginAsync(string password)
    {
        IsBusy = true;
        Error = null;
        try
        {
            Result = await mediator.Send(new LoginCommand(TenantCode, Email, password, Environment.MachineName,
                typeof(LoginViewModel).Assembly.GetName().Version?.ToString() ?? "3.0.0"));
            return true;
        }
        catch (Exception ex) when (ex is AuthenticationFailedException or RequestValidationException)
        {
            Error = ex.Message;
        }
        catch (Exception ex)
        {
            Error = "No se pudo conectar con la base de datos: " + ex.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
        return false;
    }
}

// ------------------------------------------------------------------------------------------------ principal
public sealed class MainViewModel : ObservableObject
{
    private PageViewModel _current;

    public MainViewModel(LoginResult session, StockViewModel stock, MovementViewModel movement, AlertsViewModel alerts,
        ActivityViewModel activity)
    {
        UserName = session.DisplayName;
        Roles = string.Join(", ", session.Roles);
        Pages = [stock, movement, alerts, activity];
        _current = stock;
        Navigate = new RelayCommandOf<PageViewModel>(async p =>
        {
            Current = p;
            await p.LoadAsync();
        });
    }

    public string UserName { get; }

    public string Roles { get; }

    public IReadOnlyList<PageViewModel> Pages { get; }

    public PageViewModel Current
    {
        get => _current;
        private set => Set(ref _current, value);
    }

    public RelayCommandOf<PageViewModel> Navigate { get; }
}

public sealed class RelayCommandOf<T>(Func<T, Task> execute) : System.Windows.Input.ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => parameter is T;

    public async void Execute(object? parameter)
    {
        if (parameter is T value)
        {
            await execute(value);
        }
    }
}

// ------------------------------------------------------------------------------------------------ stock
public sealed class StockViewModel : PageViewModel
{
    private readonly IMediator _mediator;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private List<StockRow> _rows = [];
    private string _search = string.Empty;
    private string _summary = string.Empty;

    public StockViewModel(IMediator mediator) : base("Stock", "▦")
    {
        _mediator = mediator;
        Rows = CollectionViewSource.GetDefaultView(_rows);
        Refresh = new AsyncRelayCommand(LoadAsync);
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Rows.Refresh();
        };
    }

    /// <summary>Vista filtrable (la DataGrid virtualiza: 100.000+ filas sin latencia).</summary>
    public ICollectionView Rows { get; private set; }

    public AsyncRelayCommand Refresh { get; }

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value))
            {
                _debounce.Stop();
                _debounce.Start();
            }
        }
    }

    public string Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var view = await _mediator.Send(new GetStockProjectionQuery());
        _rows = view.Result.Stock.ToList();
        Rows = CollectionViewSource.GetDefaultView(_rows);
        Rows.Filter = o => o is StockRow r && (_search.Length == 0
            || r.Sku.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || r.Name.Contains(_search, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(Rows));
        var value = view.Result.Stock.Sum(r => r.InventoryValue);
        Summary = $"{view.WarehouseCode} · {view.Result.Stock.Count} productos · {view.Result.Alerts.Count} alertas · " +
                  $"valor {value:#,##0.00} · calculado al instante ({view.Today:dd/MM/yyyy})";
        Status = new StatusMessage($"✔ Stock al día: {view.Result.Movements} movimientos procesados", false);
    });
}

// ------------------------------------------------------------------------------------------------ registrar movimiento
public sealed class MovementViewModel : PageViewModel
{
    private readonly IMediator _mediator;
    private string _sku = string.Empty;
    private string _binCode = "ALM01-GENERAL";
    private string _typeCode = MovementTypeCodes.Receipt;
    private decimal _quantity;
    private string? _document;
    private string? _notes;

    public MovementViewModel(IMediator mediator) : base("Registrar movimiento", "⇄")
    {
        _mediator = mediator;
        Register = new AsyncRelayCommand(RegisterAsync, () => Sku.Length > 0 && Quantity > 0);
    }

    public IReadOnlyList<string> Types { get; } =
    [
        MovementTypeCodes.Receipt, MovementTypeCodes.Issue, MovementTypeCodes.Sale, MovementTypeCodes.AdjustmentIn,
        MovementTypeCodes.AdjustmentOut, MovementTypeCodes.InitialBalance, MovementTypeCodes.SaleReturn,
    ];

    public string Sku { get => _sku; set => Set(ref _sku, value.Trim().ToUpperInvariant()); }

    public string BinCode { get => _binCode; set => Set(ref _binCode, value.Trim().ToUpperInvariant()); }

    public string TypeCode { get => _typeCode; set => Set(ref _typeCode, value); }

    public decimal Quantity { get => _quantity; set => Set(ref _quantity, value); }

    public string? Document { get => _document; set => Set(ref _document, value); }

    public string? Notes { get => _notes; set => Set(ref _notes, value); }

    public AsyncRelayCommand Register { get; }

    /// <summary>Lectura del escáner: completa el SKU (el código de barras se resuelve igual que un SKU escrito).</summary>
    public void OnScanned(string code) => Sku = code;

    private Task RegisterAsync() => RunAsync(async () =>
    {
        var result = await _mediator.Send(new RegisterMovementCommand(Sku, BinCode, TypeCode, Quantity, null, Document, Notes));
        Status = new StatusMessage(result.Message, false);
        Quantity = 0;
        Document = null;
        Notes = null;
    });
}

// ------------------------------------------------------------------------------------------------ alertas y pedido
public sealed class AlertsViewModel(IMediator mediator) : PageViewModel("Alertas y pedido", "⚠")
{
    public ObservableCollection<AlertRow> Alerts { get; } = [];

    public ObservableCollection<SuggestedOrderLine> Order { get; } = [];

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var view = await mediator.Send(new GetStockProjectionQuery());
        Alerts.Clear();
        Order.Clear();
        foreach (var a in view.Result.Alerts)
        {
            Alerts.Add(a);
        }
        foreach (var o in view.Result.Order)
        {
            Order.Add(o);
        }
        Status = new StatusMessage($"✔ {Alerts.Count} alertas · pedido sugerido {Order.Sum(o => o.Subtotal):#,##0.00} en {Order.Count} líneas", false);
    });
}

// ------------------------------------------------------------------------------------------------ actividad
public sealed class ActivityViewModel(IMediator mediator) : PageViewModel("Actividad", "☰")
{
    public ObservableCollection<ActivityRow> Rows { get; } = [];

    public override Task LoadAsync() => RunAsync(async () =>
    {
        Rows.Clear();
        foreach (var r in await mediator.Send(new GetActivityQuery(500)))
        {
            Rows.Add(r);
        }
        Status = new StatusMessage($"✔ {Rows.Count} registros de auditoría", false);
    });
}
