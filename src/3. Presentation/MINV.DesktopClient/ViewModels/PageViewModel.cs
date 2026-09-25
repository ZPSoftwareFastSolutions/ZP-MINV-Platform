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
    DataCache data, ThemeService theme, ClientSettings settings, IClock clock)
{
    /// <summary>Ahora según el reloj de la aplicación (en la demostración, el día de los datos de la V2.1).</summary>
    public DateTimeOffset Now => clock.UtcNow;

    public NotificationService Notify { get; } = notify;

    public DialogService Dialogs { get; } = dialogs;

    public SessionContext Session { get; } = session;

    public DataCache Data { get; } = data;

    public ThemeService Theme { get; } = theme;

    public ClientSettings Settings { get; } = settings;

    public INavigator Navigator { get; set; } = NullNavigator.Instance;

    public Task<T> SendAsync<T>(IRequest<T> request, CancellationToken ct = default) => mediator.SendAsync(request, ct);

    /// <summary>Mensaje para el usuario a partir de una excepción (nunca un cuadro de error técnico).</summary>
    public static string Describe(Exception ex) => ex switch
    {
        RequestValidationException v => string.Join(" ", v.Errors),
        DomainException or AccessDeniedException or NotFoundException or ConcurrencyConflictException or AuthenticationFailedException => ex.Message,
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
            await LoadCoreAsync(force);
            HasLoaded = true;
            _version = App.Data.Version;
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
