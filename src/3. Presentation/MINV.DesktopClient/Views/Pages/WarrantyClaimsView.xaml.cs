using System.Windows;
using System.Windows.Controls;
using MINV.DesktopClient.ViewModels;

namespace MINV.DesktopClient.Views.Pages;

/// <summary>V4.2 · Garantías y RMA.</summary>
public partial class WarrantyClaimsView : UserControl
{
    public WarrantyClaimsView() => InitializeComponent();

    private void OnClose(object sender, RoutedEventArgs e)
    {
        if (DataContext is WarrantyClaimsViewModel vm)
        {
            vm.Selected = null;
        }
    }
}
