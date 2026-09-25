using System.Windows;
using System.Windows.Controls;
using MINV.DesktopClient.ViewModels;

namespace MINV.DesktopClient.Views.Pages;

public partial class TransfersView : UserControl
{
    public TransfersView() => InitializeComponent();

    private void OnCloseDetail(object sender, RoutedEventArgs e)
    {
        if (DataContext is TransfersViewModel vm)
        {
            vm.Selected = null;
        }
    }
}
