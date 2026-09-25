using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Threading;
using MINV.DesktopClient.ViewModels;

namespace MINV.DesktopClient.Views.Pages;

/// <summary>Registro de movimientos: lleva el foco al buscador al entrar y, al elegir un producto, a la cantidad.</summary>
public partial class MovementView : UserControl
{
    private MovementViewModel? _vm;

    public MovementView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as MovementViewModel);
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => ProductSearch.FocusBox());
        Unloaded += (_, _) => Attach(null);
    }

    private void Attach(MovementViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnChanged;
            _vm.Picker.Picked -= OnPicked;
        }
        _vm = vm;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnChanged;
            _vm.Picker.Picked += OnPicked;
        }
    }

    private void OnPicked(object? sender, MINV.Application.Inventory.Queries.ProductLookupItem e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => QuantityBox.Focus());

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MovementViewModel.FocusRequest))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => ProductSearch.FocusBox());
        }
    }
}
