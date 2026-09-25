using System.Windows;
using System.Windows.Controls;
using MINV.DesktopClient.ViewModels;

namespace MINV.DesktopClient.Views.Pages;

public partial class PurchaseOrdersView : UserControl
{
    public PurchaseOrdersView() => InitializeComponent();

    private void OnCloseDetail(object sender, RoutedEventArgs e)
    {
        if (DataContext is PurchaseOrdersViewModel vm)
        {
            vm.Selected = null;
        }
    }
}
