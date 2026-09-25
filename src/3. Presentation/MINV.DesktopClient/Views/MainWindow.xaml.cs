using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using MINV.DesktopClient.ViewModels;
using MINV.Hardware.Scanners;

namespace MINV.DesktopClient.Views;

/// <summary>
/// Ventana principal (sin marco del sistema: barra de título propia). Atajos de teclado, lector de códigos de barras en
/// modo teclado (ráfaga de teclas + Enter) y animación del panel de la ficha del producto.
/// </summary>
public partial class MainWindow : Window
{
    private readonly KeyboardWedgeDetector _scanner = new();

    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        _scanner.Scanned += (_, e) => Dispatcher.BeginInvoke(() => OnScanned(e.Code));
        viewModel.PropertyChanged += OnShellChanged;
        viewModel.Dialogs.PropertyChanged += (_, e) =>
        {
            // Cuadro con dato a completar: el foco va directo al campo
            if (e.PropertyName == nameof(Services.DialogService.Current) && viewModel.Dialogs.Current is { HasInput: true })
            {
                Dispatcher.BeginInvoke(() => DialogInput.Focus(), System.Windows.Threading.DispatcherPriority.Input);
            }
        };
        StateChanged += (_, _) => FitMaximized();
        SourceInitialized += (_, _) => RoundCorners();
        Loaded += async (_, _) => await viewModel.StartAsync();
    }

    public ShellViewModel ViewModel { get; }

    // ------------------------------------------------------------------------------------------------ teclado
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (e.Key == Key.Enter && _scanner.OnKey('\r', DateTimeOffset.UtcNow))
        {
            e.Handled = true;   // era una lectura del escáner: no dispara el botón por defecto
            return;
        }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (key)
        {
            case Key.K when ctrl:
                GlobalSearch.FocusBox();
                e.Handled = true;
                break;
            case Key.N when ctrl:
                ViewModel.Navigate("registro");
                e.Handled = true;
                break;
            case Key.B when ctrl:
                ViewModel.IsCompact = !ViewModel.IsCompact;
                e.Handled = true;
                break;
            case Key.L when ctrl && shift:
                ViewModel.ToggleTheme.Execute(null);
                e.Handled = true;
                break;
            case Key.F5:
                ViewModel.Current.Refresh.Execute(null);
                e.Handled = true;
                break;
            case Key.F1:
                ViewModel.Navigate("ayuda");
                e.Handled = true;
                break;
            case Key.Escape when ViewModel.Dialogs.Current is { } dialog:
                dialog.Cancel.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape when ViewModel.IsProductOpen:
                ViewModel.CloseProduct.Execute(null);
                e.Handled = true;
                break;
            case >= Key.D1 and <= Key.D9 when ctrl:
                ViewModel.NavigateByIndex(key - Key.D1);
                e.Handled = true;
                break;
            case >= Key.NumPad1 and <= Key.NumPad9 when ctrl:
                ViewModel.NavigateByIndex(key - Key.NumPad1);
                e.Handled = true;
                break;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        foreach (var ch in e.Text)
        {
            _scanner.OnKey(ch, DateTimeOffset.UtcNow);
        }
        base.OnPreviewTextInput(e);
    }

    /// <summary>La lectura también se escribió en el campo con el foco: se quita y se entrega a la pantalla.</summary>
    private void OnScanned(string code)
    {
        if (Keyboard.FocusedElement is TextBox box && box.Text.EndsWith(code, StringComparison.Ordinal))
        {
            box.Text = box.Text[..^code.Length];
        }
        ViewModel.OnScanned(code);
    }

    // ------------------------------------------------------------------------------------------------ ficha del producto
    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.ProductDetail) && ViewModel.ProductDetail is not null)
        {
            DrawerShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
                new DoubleAnimation(Drawer.Width, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    private void OnCloseProduct(object sender, MouseButtonEventArgs e) => ViewModel.CloseProduct.Execute(null);

    // ------------------------------------------------------------------------------------------------ barra de título
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>Maximizada, una ventana sin marco se sale unos píxeles de la pantalla: se compensa con un margen.</summary>
    private void FitMaximized()
    {
        var t = SystemParameters.WindowResizeBorderThickness;
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(t.Left + 4, t.Top + 4, t.Right + 4, t.Bottom + 4) : new Thickness(0);
        MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
        MaxButton.ToolTip = WindowState == WindowState.Maximized ? "Restaurar" : "Maximizar";
    }

    /// <summary>Esquinas redondeadas de Windows 11 (en Windows 10 no hace nada).</summary>
    private void RoundCorners()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var preference = 2; // DWMWCP_ROUND
            _ = DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Windows sin DWM: se deja la ventana como está.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
