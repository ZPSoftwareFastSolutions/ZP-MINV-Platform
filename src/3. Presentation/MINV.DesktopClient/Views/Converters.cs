using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MINV.DesktopClient.Views;

/// <summary>true → Visible; false → Collapsed (con <c>Invert=True</c> al revés).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible ^ Invert;
}

/// <summary>Visible si hay valor (no nulo, texto no vacío, colección con elementos, número distinto de 0).</summary>
public sealed class HasValueToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var has = value switch
        {
            null => false,
            string s => s.Length > 0,
            ICollection c => c.Count > 0,
            int i => i != 0,
            _ => true,
        };
        return has ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>true si hay valor (texto no vacío o no nulo): para estados de error enlazados a un mensaje.</summary>
public sealed class HasValueToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s ? s.Length > 0 : value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Índice de alternancia (0, 1, 2…) → número de paso (1, 2, 3…).</summary>
public sealed class StepNumberConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i ? (i + 1).ToString(culture) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Negación de un booleano.</summary>
public sealed class NotConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Color estable para el avatar de una persona o proveedor (según su nombre).</summary>
public sealed class AvatarBrushConverter : IValueConverter
{
    private static readonly Brush[] Palette =
    [
        Frozen("#1565C0"), Frozen("#0E7490"), Frozen("#7C3AED"), Frozen("#B45309"), Frozen("#15803D"), Frozen("#BE185D"),
        Frozen("#4338CA"), Frozen("#0F766E"), Frozen("#C2410C"), Frozen("#475569"),
    ];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? "";
        var hash = 17;
        foreach (var ch in text)
        {
            hash = unchecked(hash * 31 + ch);
        }
        return Palette[Math.Abs(hash % Palette.Length)];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Ancho del menú lateral: completo o compacto (solo íconos).</summary>
public sealed class SidebarWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new GridLength(value is true ? 76 : 252);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
