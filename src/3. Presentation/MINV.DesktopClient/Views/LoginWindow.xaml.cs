using System.Windows;
using MINV.DesktopClient.ViewModels;

namespace MINV.DesktopClient.Views;

/// <summary>Inicio de sesión. La contraseña nunca se enlaza a una propiedad: se lee del PasswordBox solo al ingresar.</summary>
public partial class LoginWindow : Window
{
    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) => EmailBox.Focus();
    }

    public LoginViewModel ViewModel { get; }

    private async void OnLogin(object sender, RoutedEventArgs e)
    {
        IsEnabled = false;
        try
        {
            if (await ViewModel.LoginAsync(PasswordBox.Password))
            {
                DialogResult = true;
            }
        }
        finally
        {
            PasswordBox.Clear();
            IsEnabled = true;
        }
    }
}
