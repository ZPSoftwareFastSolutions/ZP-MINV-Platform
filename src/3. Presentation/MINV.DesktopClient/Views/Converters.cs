using System.Globalization;
using System.Windows.Data;
using MINV.Domain.Inventory;

namespace MINV.DesktopClient.Views;

/// <summary>Muestra el estado del semáforo con el texto de la V2.1 (AGOTADO, CRÍTICO, ÓPTIMO…).</summary>
public sealed class StockStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is StockStatusCode status ? StockRules.Label(status) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
