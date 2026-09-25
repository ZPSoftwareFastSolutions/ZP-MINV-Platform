using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MINV.DesktopClient.ViewModels;

namespace MINV.DesktopClient.Views.Pages;

public partial class StockView : UserControl
{
    public StockView() => InitializeComponent();

    /// <summary>Doble clic en una fila: abre la ficha del producto.</summary>
    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VisualTrees.FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is { Item: StockItem item } && DataContext is StockViewModel vm)
        {
            vm.OpenProduct.Execute(item);
        }
    }
}

/// <summary>Búsqueda en el árbol visual (también desde elementos de texto como Run).</summary>
public static class VisualTrees
{
    public static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null and not T)
        {
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return source as T;
    }
}
