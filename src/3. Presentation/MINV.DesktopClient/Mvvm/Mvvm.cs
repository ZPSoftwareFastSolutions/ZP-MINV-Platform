using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace MINV.DesktopClient.Mvvm;

/// <summary>Base MVVM mínima (sin dependencias): notificación de cambios de propiedades.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void OnPropertiesChanged(params string[] names)
    {
        foreach (var name in names)
        {
            OnPropertyChanged(name);
        }
    }
}

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();
}

/// <summary>Comando con parámetro (p. ej. la fila sobre la que se hizo clic).</summary>
public sealed class RelayCommand<T>(Action<T> execute, Func<T, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => parameter is T value && (canExecute?.Invoke(value) ?? true);

    public void Execute(object? parameter)
    {
        if (parameter is T value)
        {
            execute(value);
        }
    }
}

/// <summary>
/// Comando asíncrono que se deshabilita mientras corre (evita doble clic y doble registro) y expone
/// <see cref="IsRunning"/> para mostrar el anillo de carga en el botón. Un error inesperado nunca cierra la aplicación:
/// se informa con <see cref="UnhandledError"/>.
/// </summary>
public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ObservableObject, ICommand
{
    private bool _running;

    /// <summary>Receptor global de errores no controlados (lo asigna la aplicación: muestra un aviso).</summary>
    public static Action<Exception>? UnhandledError { get; set; }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool IsRunning
    {
        get => _running;
        private set => Set(ref _running, value);
    }

    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync()
    {
        if (_running)
        {
            return;
        }
        IsRunning = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await execute();
        }
        catch (Exception ex)
        {
            if (UnhandledError is { } handler)
            {
                handler(ex);
            }
            else
            {
                throw;
            }
        }
        finally
        {
            IsRunning = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}

/// <summary>Comando asíncrono con parámetro.</summary>
public sealed class AsyncRelayCommand<T>(Func<T, Task> execute, Func<T, bool>? canExecute = null) : ICommand
{
    private bool _running;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_running && parameter is T value && (canExecute?.Invoke(value) ?? true);

    public async void Execute(object? parameter)
    {
        if (parameter is not T value || _running)
        {
            return;
        }
        _running = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await execute(value);
        }
        catch (Exception ex)
        {
            if (AsyncRelayCommand.UnhandledError is { } handler)
            {
                handler(ex);
            }
            else
            {
                throw;
            }
        }
        finally
        {
            _running = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
