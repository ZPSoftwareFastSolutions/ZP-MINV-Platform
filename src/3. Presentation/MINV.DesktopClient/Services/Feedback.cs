using System.Collections.ObjectModel;
using System.Windows.Threading;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;

namespace MINV.DesktopClient.Services;

public enum ToastKind
{
    Success,
    Info,
    Warning,
    Error,
}

/// <summary>Aviso flotante (esquina inferior derecha) que se cierra solo.</summary>
public sealed class Toast(ToastKind kind, string title, string? message) : ObservableObject
{
    public ToastKind Kind { get; } = kind;

    public string Title { get; } = title;

    public string? Message { get; } = message;

    public string Glyph => Kind switch
    {
        ToastKind.Success => Glyphs.CheckCircle,
        ToastKind.Warning => Glyphs.Warning,
        ToastKind.Error => Glyphs.Error,
        _ => Glyphs.Info,
    };
}

/// <summary>Avisos de la aplicación: ✔ verde, ⚠ ámbar, ✖ rojo (los mismos significados de la V2.1).</summary>
public sealed class NotificationService
{
    public NotificationService() => Dismiss = new RelayCommand<Toast>(t => Items.Remove(t));

    public ObservableCollection<Toast> Items { get; } = [];

    public RelayCommand<Toast> Dismiss { get; }

    public void Success(string title, string? message = null) => Show(ToastKind.Success, title, message);

    public void Info(string title, string? message = null) => Show(ToastKind.Info, title, message);

    public void Warning(string title, string? message = null) => Show(ToastKind.Warning, title, message, 7);

    public void Error(string title, string? message = null) => Show(ToastKind.Error, title, message, 9);

    public void Show(ToastKind kind, string title, string? message = null, int seconds = 4)
    {
        var toast = new Toast(kind, title, message);
        Items.Add(toast);
        while (Items.Count > 4)
        {
            Items.RemoveAt(0);
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Items.Remove(toast);
        };
        timer.Start();
    }
}

/// <summary>Confirmación modal dentro de la ventana (fondo atenuado).</summary>
public sealed class DialogRequest : ObservableObject
{
    private readonly TaskCompletionSource<bool> _result = new();

    public DialogRequest(string title, string message, string confirmText, string cancelText, bool isDanger, string glyph,
        IReadOnlyList<string>? details)
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        CancelText = cancelText;
        IsDanger = isDanger;
        Glyph = glyph;
        Details = details ?? [];
        Confirm = new RelayCommand(() => _result.TrySetResult(true));
        Cancel = new RelayCommand(() => _result.TrySetResult(false));
    }

    public string Title { get; }

    public string Message { get; }

    public string ConfirmText { get; }

    public string CancelText { get; }

    public bool IsDanger { get; }

    public string Glyph { get; }

    public IReadOnlyList<string> Details { get; }

    public bool HasDetails => Details.Count > 0;

    public RelayCommand Confirm { get; }

    public RelayCommand Cancel { get; }

    public Task<bool> Result => _result.Task;
}

public sealed class DialogService : ObservableObject
{
    private DialogRequest? _current;

    public DialogRequest? Current
    {
        get => _current;
        private set => Set(ref _current, value);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "Aceptar", string cancelText = "Cancelar",
        bool isDanger = false, string? glyph = null, IReadOnlyList<string>? details = null)
    {
        var request = new DialogRequest(title, message, confirmText, cancelText, isDanger, glyph ?? (isDanger ? Glyphs.Warning : Glyphs.Info), details);
        Current = request;
        try
        {
            return await request.Result;
        }
        finally
        {
            if (ReferenceEquals(Current, request))
            {
                Current = null;
            }
        }
    }
}
