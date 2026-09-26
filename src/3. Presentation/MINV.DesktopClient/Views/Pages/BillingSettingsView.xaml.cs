using System.Windows;
using System.Windows.Controls;
using MINV.DesktopClient.ViewModels;

namespace MINV.DesktopClient.Views.Pages;

/// <summary>
/// V4.1 · Configuración de la facturación. El token delegado del SIN y la contraseña SMTP NO se enlazan a propiedades
/// (regla A-09): se leen del PasswordBox solo al guardar, viajan al caso de uso (que los cifra) y el campo se vacía.
/// </summary>
public partial class BillingSettingsView : UserControl
{
    public BillingSettingsView() => InitializeComponent();

    private async void OnSaveProfile(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BillingSettingsViewModel vm)
        {
            return;
        }
        SaveProfileButton.IsEnabled = false;
        try
        {
            var token = TokenBox.Password;
            if (await vm.SaveProfileAsync(token.Length == 0 ? null : token))
            {
                TokenBox.Clear();
            }
        }
        finally
        {
            SaveProfileButton.IsEnabled = vm.CanConfigure;
        }
    }

    private async void OnSaveMail(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BillingSettingsViewModel vm)
        {
            return;
        }
        SaveMailButton.IsEnabled = false;
        try
        {
            var password = MailPasswordBox.Password;
            if (await vm.SaveMailAsync(password.Length == 0 ? null : password))
            {
                MailPasswordBox.Clear();
            }
        }
        finally
        {
            SaveMailButton.IsEnabled = vm.CanConfigure;
        }
    }
}
