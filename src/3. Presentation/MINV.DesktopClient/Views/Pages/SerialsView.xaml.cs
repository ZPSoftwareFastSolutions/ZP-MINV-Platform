using System.Windows;
using System.Windows.Controls;
using MINV.DesktopClient.ViewModels;

namespace MINV.DesktopClient.Views.Pages;

/// <summary>V4.2 · Series e IMEI.</summary>
public partial class SerialsView : UserControl
{
    public SerialsView() => InitializeComponent();

    private void OnClose(object sender, RoutedEventArgs e)
    {
        if (DataContext is SerialsViewModel vm)
        {
            vm.Selected = null;
        }
    }
}
