using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MINV.Application.Inventory.Queries;
using MINV.DesktopClient.ViewModels;

namespace MINV.DesktopClient.Views;

/// <summary>
/// Buscador de productos con sugerencias (↑ ↓ para moverse, Enter para elegir, Esc para cerrar). Su DataContext es un
/// <see cref="ProductPickerViewModel"/>.
/// </summary>
public partial class ProductSearchBox : UserControl
{
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(nameof(Placeholder), typeof(string),
        typeof(ProductSearchBox), new PropertyMetadata("Buscar por nombre, SKU o código de barras"));

    public static readonly DependencyProperty IsLargeProperty = DependencyProperty.Register(nameof(IsLarge), typeof(bool),
        typeof(ProductSearchBox), new PropertyMetadata(false, (d, e) => ((ProductSearchBox)d).ApplySize((bool)e.NewValue)));

    public ProductSearchBox()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ProductPickerViewModel old)
            {
                old.Picked -= OnPicked;
            }
            if (e.NewValue is ProductPickerViewModel picker)
            {
                picker.Picked += OnPicked;
            }
        };
    }

    private void OnPicked(object? sender, ProductLookupItem e) => Dispatcher.BeginInvoke(() =>
    {
        Box.Select(0, 0);
        Box.ScrollToHome();
    }, System.Windows.Threading.DispatcherPriority.Background);

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public bool IsLarge
    {
        get => (bool)GetValue(IsLargeProperty);
        set => SetValue(IsLargeProperty, value);
    }

    private ProductPickerViewModel? Picker => DataContext as ProductPickerViewModel;

    public void FocusBox()
    {
        Box.Focus();
        Keyboard.Focus(Box);
    }

    private void ApplySize(bool large)
    {
        Box.FontSize = large ? 16 : 13.5;
        Box.MinHeight = large ? 48 : 38;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Picker is not { } picker)
        {
            return;
        }
        switch (e.Key)
        {
            case Key.Down:
                picker.MoveHighlight(+1);
                List.ScrollIntoView(picker.Highlighted);
                e.Handled = true;
                break;
            case Key.Up:
                picker.MoveHighlight(-1);
                List.ScrollIntoView(picker.Highlighted);
                e.Handled = true;
                break;
            case Key.Enter:
                if (picker.IsOpen || picker.Query.Length > 0)
                {
                    e.Handled = picker.Commit();
                }
                break;
            case Key.Escape:
                if (picker.IsOpen)
                {
                    picker.IsOpen = false;
                    e.Handled = true;
                }
                break;
        }
    }

    private void OnPick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(List, source) is ListBoxItem { DataContext: ProductLookupItem item })
        {
            Picker?.Select(item);
            FocusBox();
        }
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        Picker?.Clear();
        FocusBox();
    }
}
