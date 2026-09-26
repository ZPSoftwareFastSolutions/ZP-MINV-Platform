using System.Windows;
using System.Windows.Controls;
using MINV.DesktopClient.ViewModels;

namespace MINV.DesktopClient.Views.Pages;

public partial class FiscalDocumentsView : UserControl
{
    public FiscalDocumentsView() => InitializeComponent();

    private void OnCloseDetail(object sender, RoutedEventArgs e)
    {
        if (DataContext is FiscalDocumentsViewModel vm)
        {
            vm.Selected = null;
        }
    }
}
