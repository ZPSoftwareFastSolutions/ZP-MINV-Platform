using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MINV.DesktopClient.ViewModels;

namespace MINV.DesktopClient.Views.Pages;

public partial class CustomersView : UserControl
{
    public CustomersView() => InitializeComponent();

    /// <summary>Doble clic en una fila: abre el editor del cliente.</summary>
    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VisualTrees.FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is { Item: CustomerItem item } && DataContext is CustomersViewModel vm
            && vm.Edit.CanExecute(item))
        {
            vm.Edit.Execute(item);
        }
    }
}
