using System.Windows;
using System.Windows.Input;
using MINV.DesktopClient.ViewModels;

namespace MINV.DesktopClient.Views;

/// <summary>Inicio de sesión. La contraseña nunca se enlaza a una propiedad: se lee del PasswordBox solo al ingresar
/// y se borra enseguida (regla A-09).</summary>
public partial class LoginWindow : Window
{
    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) =>
        {
            (string.IsNullOrEmpty(viewModel.TenantCode) ? TenantBox : string.IsNullOrEmpty(viewModel.Email) ? EmailBox : (IInputElement)PasswordBox).Focus();
            UpdateCapsLock();
        };
        PreviewKeyDown += (_, _) => UpdateCapsLock();
        PreviewKeyUp += (_, _) => UpdateCapsLock();
    }

    public LoginViewModel ViewModel { get; }

    private void UpdateCapsLock() => ViewModel.CapsLockOn = Keyboard.IsKeyToggled(Key.CapsLock);

    private async void OnLogin(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await ViewModel.LoginAsync(PasswordBox.Password))
            {
                PasswordBox.Focus();
                PasswordBox.SelectAll();
            }
        }
        finally
        {
            PasswordBox.Clear();
        }
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
