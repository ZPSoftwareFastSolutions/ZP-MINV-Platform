using System.Windows;
using System.Windows.Controls;
using MINV.DesktopClient.ViewModels;

namespace MINV.DesktopClient.Views.Pages;

public partial class SalesView : UserControl
{
    public SalesView() => InitializeComponent();

    private void OnCloseDetail(object sender, RoutedEventArgs e)
    {
        if (DataContext is SalesViewModel vm)
        {
            vm.Selected = null;
        }
    }
}
