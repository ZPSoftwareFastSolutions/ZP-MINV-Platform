using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MINV.DesktopClient.Controls;

/// <summary>Porción de un gráfico de dona. <see cref="BrushKey"/> es una clave de la paleta (cambia con el tema).</summary>
public sealed record ChartSegment(string Label, double Value, string BrushKey);

/// <summary>Columna doble (p. ej. entradas y salidas de un día).</summary>
public sealed record ColumnPoint(string Label, double Primary, double Secondary, string Tooltip);

/// <summary>
/// Base de los gráficos dibujados a mano (sin librerías externas): colores de la paleta activa, redibujo al cambiar el
/// tema o los datos y texto con la tipografía de la aplicación.
/// </summary>
public abstract class ChartBase : FrameworkElement
{
    protected static readonly Typeface Font = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    protected static readonly Typeface FontBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    protected ChartBase()
    {
        Loaded += (_, _) => ThemeEvents.Changed += OnThemeChanged;
        Unloaded += (_, _) => ThemeEvents.Changed -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    protected FormattedText Text(string text, double size, string brushKey, bool bold = false) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold ? FontBold : Font, size, Res(brushKey),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>Redibuja cuando cambia una colección observable enlazada.</summary>
    protected static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (ChartBase)d;
        if (e.OldValue is INotifyCollectionChanged oldList)
        {
            oldList.CollectionChanged -= chart.OnCollectionChanged;
        }
        if (e.NewValue is INotifyCollectionChanged newList)
        {
            newList.CollectionChanged += chart.OnCollectionChanged;
        }
        chart.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}

/// <summary>Aviso global de cambio de tema (los gráficos vuelven a resolver sus colores).</summary>
public static class ThemeEvents
{
    public static event EventHandler? Changed;

    public static void Raise() => Changed?.Invoke(null, EventArgs.Empty);
}

/// <summary>Gráfico de dona: estado del inventario por semáforo.</summary>
public sealed class DonutChart : ChartBase
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(nameof(Segments), typeof(IEnumerable),
        typeof(DonutChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(double),
        typeof(DonutChart), new FrameworkPropertyMetadata(22d, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? Segments
    {
        get => (IEnumerable?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double Stroke
    {
        get => (double)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Stroke * 2)
        {
            return;
        }
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - Stroke) / 2;
        var track = new Pen(Res("ChartTrack"), Stroke);
        dc.DrawEllipse(null, track, center, radius, radius);
        var segments = Segments?.OfType<ChartSegment>().Where(s => s.Value > 0).ToList() ?? [];
        var total = segments.Sum(s => s.Value);
        if (total <= 0)
        {
            return;
        }
        var gap = segments.Count > 1 ? 1.6 : 0;   // grados de separación entre porciones
        var angle = -90d;
        foreach (var s in segments)
        {
            var sweep = s.Value / total * 360d;
            if (sweep >= 359.99)
            {
                dc.DrawEllipse(null, new Pen(Res(s.BrushKey), Stroke), center, radius, radius);
                break;
            }
            var visible = Math.Max(0.5, sweep - gap);
            DrawArc(dc, center, radius, angle + gap / 2, visible, new Pen(Res(s.BrushKey), Stroke) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat });
            angle += sweep;
        }
    }

    private static void DrawArc(DrawingContext dc, Point c, double r, double startDeg, double sweepDeg, Pen pen)
    {
        static Point At(Point c, double r, double deg) => new(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(At(c, r, startDeg), false, false);
            ctx.ArcTo(At(c, r, startDeg + sweepDeg), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}

/// <summary>Columnas dobles por día (entradas y salidas) con cuadrícula, etiquetas y detalle al pasar el mouse.</summary>
public sealed class ColumnChart : ChartBase
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(nameof(Points), typeof(IEnumerable),
        typeof(ColumnChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    private readonly ToolTip _tip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
    private int _hover = -1;

    public ColumnChart()
    {
        ToolTip = _tip;
        ToolTipService.SetInitialShowDelay(this, 0);
        MouseLeave += (_, _) => SetHover(-1);
    }

    public IEnumerable? Points
    {
        get => (IEnumerable?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    private List<ColumnPoint> Items => Points?.OfType<ColumnPoint>().ToList() ?? [];

    private const double AxisWidth = 44;
    private const double LabelHeight = 22;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var items = Items;
        var x = e.GetPosition(this).X - AxisWidth;
        var slot = (ActualWidth - AxisWidth) / Math.Max(1, items.Count);
        SetHover(x < 0 || items.Count == 0 ? -1 : Math.Min(items.Count - 1, (int)(x / slot)));
    }

    private void SetHover(int index)
    {
        if (_hover == index)
        {
            return;
        }
        _hover = index;
        var items = Items;
        _tip.Content = index >= 0 && index < items.Count ? items[index].Tooltip : null;
        _tip.IsOpen = _tip.Content is not null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));   // recibe el mouse
        var items = Items;
        var plotHeight = ActualHeight - LabelHeight - 6;
        var plotWidth = ActualWidth - AxisWidth;
        if (items.Count == 0 || plotHeight <= 20 || plotWidth <= 20)
        {
            return;
        }
        var max = Nice(items.Max(p => Math.Max(p.Primary, p.Secondary)));
        var grid = new Pen(Res("ChartGrid"), 1);
        for (var i = 0; i <= 4; i++)
        {
            var y = Math.Round(6 + plotHeight * (1 - i / 4d)) + 0.5;
            dc.DrawLine(grid, new Point(AxisWidth, y), new Point(ActualWidth, y));
            var label = Text(Short(max * i / 4), 10.5, "TextFaint");
            dc.DrawText(label, new Point(AxisWidth - 8 - label.Width, y - label.Height / 2));
        }
        var slot = plotWidth / items.Count;
        var barWidth = Math.Clamp(slot * 0.3, 3, 16);
        var every = Math.Max(1, (int)Math.Ceiling(items.Count / Math.Max(1, plotWidth / 56)));
        for (var i = 0; i < items.Count; i++)
        {
            var p = items[i];
            var x0 = AxisWidth + slot * i;
            if (i == _hover)
            {
                dc.DrawRoundedRectangle(Res("SurfaceHover"), null, new Rect(x0 + 2, 2, slot - 4, plotHeight + 6), 6, 6);
            }
            var cx = x0 + slot / 2;
            Bar(dc, cx - barWidth - 1, barWidth, p.Primary / max * plotHeight, plotHeight, Res("ChartEntries"));
            Bar(dc, cx + 1, barWidth, p.Secondary / max * plotHeight, plotHeight, Res("ChartIssues"));
            if (i % every == 0 || i == items.Count - 1)
            {
                var label = Text(p.Label, 10.5, i == _hover ? "TextPrimary" : "TextFaint");
                dc.DrawText(label, new Point(cx - label.Width / 2, plotHeight + 12));
            }
        }
    }

    private static void Bar(DrawingContext dc, double x, double width, double height, double plotHeight, Brush brush)
    {
        if (height <= 0)
        {
            return;
        }
        height = Math.Max(2, height);
        var r = Math.Min(width / 2, 4);
        dc.DrawRoundedRectangle(brush, null, new Rect(x, 6 + plotHeight - height, width, height), r, r);
    }

    private static double Nice(double value)
    {
        if (value <= 0)
        {
            return 1;
        }
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var step in new[] { 1, 2, 2.5, 5, 10 })
        {
            if (step * magnitude >= value)
            {
                return step * magnitude;
            }
        }
        return 10 * magnitude;
    }

    private static string Short(double v) => v >= 1_000_000 ? (v / 1_000_000).ToString("0.#", CultureInfo.CurrentCulture) + " M"
        : v >= 10_000 ? (v / 1000).ToString("0.#", CultureInfo.CurrentCulture) + " k"
        : v.ToString("#,##0.#", CultureInfo.CurrentCulture);
}

/// <summary>Línea con área (evolución del saldo en el kardex de un producto).</summary>
public sealed class LineChart : ChartBase
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(nameof(Values), typeof(IEnumerable),
        typeof(LineChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    public static readonly DependencyProperty LineBrushKeyProperty = DependencyProperty.Register(nameof(LineBrushKey), typeof(string),
        typeof(LineChart), new FrameworkPropertyMetadata("Brand", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ReferenceProperty = DependencyProperty.Register(nameof(Reference), typeof(double),
        typeof(LineChart), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? Values
    {
        get => (IEnumerable?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public string LineBrushKey
    {
        get => (string)GetValue(LineBrushKeyProperty);
        set => SetValue(LineBrushKeyProperty, value);
    }

    /// <summary>Línea de referencia punteada (p. ej. el stock mínimo); 0 = sin referencia.</summary>
    public double Reference
    {
        get => (double)GetValue(ReferenceProperty);
        set => SetValue(ReferenceProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var values = Values?.Cast<object>().Select(v => Convert.ToDouble(v, CultureInfo.InvariantCulture)).ToList() ?? [];
        if (values.Count == 0 || ActualWidth < 10 || ActualHeight < 10)
        {
            return;
        }
        if (values.Count == 1)
        {
            values.Add(values[0]);
        }
        var min = Math.Min(0, values.Min());
        var max = Math.Max(values.Max(), Reference) * 1.1;
        if (max - min < 1e-9)
        {
            max = min + 1;
        }
        double X(int i) => 2 + (ActualWidth - 4) * i / (values.Count - 1);
        double Y(double v) => 4 + (ActualHeight - 8) * (1 - (v - min) / (max - min));
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var l = line.Open())
        using (var a = area.Open())
        {
            l.BeginFigure(new Point(X(0), Y(values[0])), false, false);
            a.BeginFigure(new Point(X(0), Y(min)), true, true);
            a.LineTo(new Point(X(0), Y(values[0])), false, false);
            for (var i = 1; i < values.Count; i++)
            {
                // Kardex: el saldo cambia en escalón (se mantiene hasta el siguiente movimiento)
                var step = new Point(X(i), Y(values[i - 1]));
                var next = new Point(X(i), Y(values[i]));
                l.LineTo(step, true, true);
                l.LineTo(next, true, true);
                a.LineTo(step, false, false);
                a.LineTo(next, false, false);
            }
            a.LineTo(new Point(X(values.Count - 1), Y(min)), false, false);
        }
        line.Freeze();
        area.Freeze();
        var brush = Res(LineBrushKey);
        var color = brush is SolidColorBrush solid ? solid.Color : Colors.SteelBlue;
        var fill = new LinearGradientBrush(Color.FromArgb(70, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90);
        fill.Freeze();
        dc.DrawGeometry(fill, null, area);
        if (Reference > 0)
        {
            var dash = new Pen(Res("StatusCritical"), 1) { DashStyle = new DashStyle([4, 4], 0) };
            dc.DrawLine(dash, new Point(0, Y(Reference)), new Point(ActualWidth, Y(Reference)));
        }
        dc.DrawGeometry(null, new Pen(brush, 2) { LineJoin = PenLineJoin.Round }, line);
        dc.DrawEllipse(Res("Surface"), new Pen(brush, 2), new Point(X(values.Count - 1), Y(values[^1])), 4, 4);
    }
}
