using System.Windows;
using System.Windows.Input;
using MINV.DesktopClient.ViewModels;
using MINV.Hardware.Scanners;

namespace MINV.DesktopClient.Views;

/// <summary>Ventana principal. Detecta el lector de códigos de barras en modo teclado (ráfaga de teclas + Enter) y
/// envía la lectura a la pantalla de registro.</summary>
public partial class MainWindow : Window
{
    private readonly KeyboardWedgeDetector _scanner = new();

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _scanner.Scanned += (_, e) =>
        {
            if (viewModel.Current is MovementViewModel movement)
            {
                movement.OnScanned(e.Code);
            }
        };
        Loaded += async (_, _) => await viewModel.Current.LoadAsync();
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        foreach (var ch in e.Text)
        {
            _scanner.OnKey(ch, DateTimeOffset.UtcNow);
        }
        base.OnPreviewTextInput(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _scanner.OnKey('\r', DateTimeOffset.UtcNow);
        }
        base.OnPreviewKeyDown(e);
    }
}
