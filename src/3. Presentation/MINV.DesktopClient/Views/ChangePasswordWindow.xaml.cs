using System.Windows;
using System.Windows.Input;
using MINV.DesktopClient.ViewModels;

namespace MINV.DesktopClient.Views;

/// <summary>Cambio de contraseña. Las contraseñas solo se leen de los PasswordBox al guardar y se borran enseguida.</summary>
public partial class ChangePasswordWindow : Window
{
    private readonly ChangePasswordViewModel _vm;

    public ChangePasswordWindow(ChangePasswordViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        CancelButton.Content = viewModel.IsMandatory ? "Cerrar sesión" : "Cancelar";
        Loaded += (_, _) => Current.Focus();
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            if (await _vm.ChangeAsync(Current.Password, Next.Password, Confirm.Password))
            {
                DialogResult = true;
            }
        }
        finally
        {
            Current.Clear();
            Next.Clear();
            Confirm.Clear();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.TextBox)
        {
            DragMove();
        }
    }
}
