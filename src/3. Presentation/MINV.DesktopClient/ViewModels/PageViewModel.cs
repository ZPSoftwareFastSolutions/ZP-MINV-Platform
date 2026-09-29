using MediatR;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Common;

namespace MINV.DesktopClient.ViewModels;

/// <summary>Navegación entre pantallas (la implementa el Shell).</summary>
public interface INavigator
{
    void Navigate(string key, object? parameter = null);

    void OpenProduct(string sku);

    void RequestChangePassword();
}

/// <summary>Lo que necesita cualquier pantalla. Todas las llamadas a la aplicación pasan por <see cref="SendAsync{T}"/>, que
/// las ejecuta de a una (el contexto de datos de la sesión no admite consultas simultáneas).</summary>
public sealed class AppServices(SerialMediator mediator, NotificationService notify, DialogService dialogs, SessionContext session,
    DataCache data, ThemeService theme, ClientSettings settings, IClock clock, ImageCache images)
{
    /// <summary>Imágenes de los productos (miniaturas compartidas por todas las pantallas).</summary>
    public ImageCache Images { get; } = images;

    /// <summary>Ahora según el reloj de la aplicación (en la demostración, el último día de operación de Tech Zone Gaming).</summary>
    public DateTimeOffset Now => clock.UtcNow;

    public NotificationService Notify { get; } = notify;

    public DialogService Dialogs { get; } = dialogs;

    public SessionContext Session { get; } = session;

    public DataCache Data { get; } = data;

    public ThemeService Theme { get; } = theme;

    public ClientSettings Settings { get; } = settings;

    public INavigator Navigator { get; set; } = NullNavigator.Instance;

    public Task<T> SendAsync<T>(IRequest<T> request, CancellationToken ct = default) => mediator.SendAsync(request, ct);

    /// <summary>V4.1 · Vuelve a leer el estado de la facturación (menú, caja y trabajo automático).</summary>
    public Task RefreshBillingAsync() => Session.RefreshBillingAsync(mediator);

    /// <summary>V4 · Cambia la sucursal activa de la sesión y avisa a todas las pantallas que recarguen.</summary>
    public async Task SelectBranchAsync(Guid? branchId)
    {
        await Session.SelectBranchAsync(mediator, branchId);
        Data.Invalidate();
    }

    /// <summary>
    /// V7 · Exporta a CSV lo que la lista muestra (con sus filtros): pide dónde guardarlo, lo escribe con el formato único de
    /// <see cref="CsvExport"/> (UTF-8 con BOM, «;», celdas neutralizadas) y avisa el resultado. Devuelve la ruta escrita o null
    /// (cancelado, lista vacía o el archivo estaba abierto en otro programa: se avisa, nunca un error técnico).
    /// </summary>
    public string? ExportCsv(string fileName, string what, CsvTable table)
    {
        if (table.Count == 0)
        {
            Notify.Info("Nada que exportar", "La lista está vacía con los filtros elegidos: cambie o limpie los filtros.");
            return null;
        }
        var path = Dialogs.AskCsvPath(fileName);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        try
        {
            CsvExport.Write(path, table);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException
                                       or ArgumentException)
        {
            Notify.Error("No se pudo exportar", $"No se pudo guardar «{System.IO.Path.GetFileName(path)}»: ciérrelo si está abierto en Excel o elija otra carpeta.");
            return null;
        }
        Notify.Success($"{what} exportado", $"{table.Count} fila{(table.Count == 1 ? "" : "s")} en {System.IO.Path.GetFileName(path)}");
        return path;
    }

    /// <summary>Nombre de archivo sugerido: «reservas-20260929-1430.csv».</summary>
    public string CsvName(string prefix) => $"{prefix}-{Now.ToLocalTime():yyyyMMdd-HHmm}.csv";

    /// <summary>Mensaje para el usuario a partir de una excepción (nunca un cuadro de error técnico).</summary>
    public static string Describe(Exception ex) => ex switch
    {
        RequestValidationException v => string.Join(" ", v.Errors),
        DomainException or AccessDeniedException or NotFoundException or ConcurrencyConflictException or AuthenticationFailedException => ex.Message,
        System.IO.InvalidDataException => ex.Message,
        _ => "Error inesperado: " + ex.GetBaseException().Message,
    };

    public static bool IsExpected(Exception ex) => ex is DomainException or RequestValidationException or AccessDeniedException
        or NotFoundException or ConcurrencyConflictException or AuthenticationFailedException;

    private sealed class NullNavigator : INavigator
    {
        public static readonly NullNavigator Instance = new();

        public void Navigate(string key, object? parameter = null)
        {
        }

        public void OpenProduct(string sku)
        {
        }

        public void RequestChangePassword()
        {
        }
    }
}

/// <summary>
/// Pantalla de la aplicación: título, ícono, estado de carga y manejo uniforme de errores. Se recarga sola cuando
/// cambian los datos (p. ej. después de registrar un movimiento en otra pantalla).
/// </summary>
public abstract class PageViewModel : ObservableObject
{
    private bool _busy;
    private bool _loaded;
    private bool _selected;
    private string? _error;
    private string? _badge;
    private int _version = -1;

    protected PageViewModel(AppServices app, string key, string title, string subtitle, string glyph)
    {
        App = app;
        Key = key;
        Title = title;
        Subtitle = subtitle;
        Glyph = glyph;
        Refresh = new AsyncRelayCommand(() => LoadAsync(force: true));
    }

    protected AppServices App { get; }

    public string Key { get; }

    public string Title { get; }

    public string Subtitle { get; protected set; }

    public string Glyph { get; }

    /// <summary>Atajo de teclado que se muestra en el menú (Ctrl+1…).</summary>
    public string? Shortcut { get; set; }

    public bool IsBusy
    {
        get => _busy;
        protected set => Set(ref _busy, value);
    }

    public bool HasLoaded
    {
        get => _loaded;
        private set => Set(ref _loaded, value);
    }

    /// <summary>Pantalla actual (resalta su botón en el menú).</summary>
    public bool IsSelected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public string? ErrorMessage
    {
        get => _error;
        protected set
        {
            if (Set(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => _error is not null;

    /// <summary>Contador que se muestra junto al nombre en el menú (p. ej. alertas).</summary>
    public string? Badge
    {
        get => _badge;
        set => Set(ref _badge, value);
    }

    public AsyncRelayCommand Refresh { get; }

    /// <summary>Carga la primera vez y cada vez que los datos cambiaron desde la última carga.</summary>
    public Task EnsureLoadedAsync() => !HasLoaded || _version != App.Data.Version ? LoadAsync(force: false) : Task.CompletedTask;

    public async Task LoadAsync(bool force)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var first = !HasLoaded;
            await LoadCoreAsync(force);
            HasLoaded = true;
            _version = App.Data.Version;
            if (first)
            {
                // V7 · Las propiedades que dependen de HasLoaded (estados vacíos, guías, avisos) se notificaron dentro de LoadCoreAsync,
                // cuando todavía valía false: en la primera carga no aparecían hasta tocar un filtro o pulsar F5. Se vuelven a leer todas.
                OnPropertyChanged(string.Empty);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = AppServices.Describe(ex);
            if (HasLoaded)
            {
                App.Notify.Error("No se pudo actualizar " + Title.ToLowerInvariant(), ErrorMessage);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected abstract Task LoadCoreAsync(bool force);

    /// <summary>Al llegar a la pantalla desde otra (p. ej. «Registrar entrada» de una alerta).</summary>
    public virtual void OnNavigatedTo(object? parameter)
    {
    }

    /// <summary>Ejecuta un caso de uso y muestra el error como aviso; devuelve false si falló.</summary>
    protected async Task<bool> RunAsync(Func<Task> action, string failureTitle)
    {
        try
        {
            await action();
            return true;
        }
        catch (Exception ex)
        {
            App.Notify.Error(failureTitle, AppServices.Describe(ex));
            return false;
        }
    }
}

/// <summary>Prellenado de la pantalla de registro (desde alertas, stock o la ficha de un producto).</summary>
public sealed record MovementPrefill(string? Sku, string? TypeCode);
