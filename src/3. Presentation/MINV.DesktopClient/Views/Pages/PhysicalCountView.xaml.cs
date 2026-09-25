using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Threading;
using MINV.DesktopClient.ViewModels;

namespace MINV.DesktopClient.Views.Pages;

/// <summary>Toma física: al elegir un producto el foco pasa a la cantidad contada; al registrar, vuelve al buscador.</summary>
public partial class PhysicalCountView : UserControl
{
    private PhysicalCountViewModel? _vm;

    public PhysicalCountView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as PhysicalCountViewModel);
        Unloaded += (_, _) => Attach(null);
    }

    private void Attach(PhysicalCountViewModel? vm)
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
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => CountedBox.Focus());

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PhysicalCountViewModel.FocusRequest))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => ProductSearch.FocusBox());
        }
    }
}
