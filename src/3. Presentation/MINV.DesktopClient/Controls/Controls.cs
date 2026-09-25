using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MINV.Domain.Inventory;

namespace MINV.DesktopClient.Controls;

/// <summary>Íconos de Segoe Fluent Icons (Windows 11) con respaldo en Segoe MDL2 Assets (Windows 10).</summary>
public static class Glyphs
{
    public const string Home = "";
    public const string Box = "";
    public const string Swap = "";
    public const string Checklist = "";
    public const string Warning = "";
    public const string Cart = "";
    public const string History = "";
    public const string Settings = "";
    public const string Help = "";
    public const string Search = "";
    public const string Add = "";
    public const string Remove = "";
    public const string Refresh = "";
    public const string Check = "";
    public const string CheckCircle = "";
    public const string Info = "";
    public const string Error = "";
    public const string Close = "";
    public const string Menu = "";
    public const string ChevronRight = "";
    public const string ChevronLeft = "";
    public const string ChevronDown = "";
    public const string SignOut = "";
    public const string Person = "";
    public const string People = "";
    public const string Barcode = "";
    public const string Chart = "";
    public const string Pie = "";
    public const string Printer = "";
    public const string Download = "";
    public const string Export = "";
    public const string Copy = "";
    public const string Save = "";
    public const string Filter = "";
    public const string Calendar = "";
    public const string Clock = "";
    public const string Lock = "";
    public const string Mail = "";
    public const string Briefcase = "";
    public const string Tag = "";
    public const string Location = "";
    public const string Globe = "";
    public const string Keyboard = "";
    public const string Shield = "";
    public const string Key = "";
    public const string Sun = "";
    public const string Moon = "";
    public const string Eye = "";
    public const string Delete = "";
    public const string Money = "";
    public const string Pulse = "";
    public const string Bulb = "";
    public const string Clipboard = "";
    public const string Report = "";
    public const string Wrench = "";
    public const string ArrowUp = "";
    public const string ArrowDown = "";
    public const string Flag = "";
    public const string Library = "";
    public const string Phone = "";
    public const string Sparkle = "";
    public const string Monitor = "";
}

/// <summary>
/// Propiedades adjuntas de la interfaz: texto de ayuda (placeholder), ícono de botones y campos, seleccionar todo al
/// enfocar, estado de error, botón «ocupado» (muestra el anillo de carga) y placeholder de contraseñas.
/// </summary>
public static class Ui
{
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached("Placeholder", typeof(string),
        typeof(Ui), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached("Icon", typeof(string),
        typeof(Ui), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectAllOnFocusProperty = DependencyProperty.RegisterAttached("SelectAllOnFocus",
        typeof(bool), typeof(Ui), new PropertyMetadata(false, OnSelectAllChanged));

    public static readonly DependencyProperty HasErrorProperty = DependencyProperty.RegisterAttached("HasError", typeof(bool),
        typeof(Ui), new PropertyMetadata(false));

    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.RegisterAttached("IsBusy", typeof(bool),
        typeof(Ui), new PropertyMetadata(false));

    public static readonly DependencyProperty WatchPasswordProperty = DependencyProperty.RegisterAttached("WatchPassword", typeof(bool),
        typeof(Ui), new PropertyMetadata(false, OnWatchPasswordChanged));

    public static readonly DependencyProperty HasTextProperty = DependencyProperty.RegisterAttached("HasText", typeof(bool),
        typeof(Ui), new PropertyMetadata(false));

    /// <summary>Clave de la paleta para el color principal del elemento (Fill de una figura, Background de un borde,
    /// Foreground de un texto). Usa una referencia dinámica: cambia con el tema.</summary>
    public static readonly DependencyProperty BrushKeyProperty = DependencyProperty.RegisterAttached("BrushKey", typeof(string),
        typeof(Ui), new PropertyMetadata(null, OnBrushKeyChanged));

    public static string? GetBrushKey(DependencyObject d) => (string?)d.GetValue(BrushKeyProperty);

    public static void SetBrushKey(DependencyObject d, string? value) => d.SetValue(BrushKeyProperty, value);

    private static void OnBrushKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not string key || key.Length == 0 || d is not FrameworkElement element)
        {
            return;
        }
        var property = d switch
        {
            System.Windows.Shapes.Shape => System.Windows.Shapes.Shape.FillProperty,
            Border => Border.BackgroundProperty,
            TextBlock => TextBlock.ForegroundProperty,
            Control => Control.ForegroundProperty,
            _ => null,
        };
        if (property is not null)
        {
            element.SetResourceReference(property, key);
        }
    }

    public static string GetPlaceholder(DependencyObject d) => (string)d.GetValue(PlaceholderProperty);

    public static void SetPlaceholder(DependencyObject d, string value) => d.SetValue(PlaceholderProperty, value);

    public static string? GetIcon(DependencyObject d) => (string?)d.GetValue(IconProperty);

    public static void SetIcon(DependencyObject d, string? value) => d.SetValue(IconProperty, value);

    public static bool GetSelectAllOnFocus(DependencyObject d) => (bool)d.GetValue(SelectAllOnFocusProperty);

    public static void SetSelectAllOnFocus(DependencyObject d, bool value) => d.SetValue(SelectAllOnFocusProperty, value);

    public static bool GetHasError(DependencyObject d) => (bool)d.GetValue(HasErrorProperty);

    public static void SetHasError(DependencyObject d, bool value) => d.SetValue(HasErrorProperty, value);

    public static bool GetIsBusy(DependencyObject d) => (bool)d.GetValue(IsBusyProperty);

    public static void SetIsBusy(DependencyObject d, bool value) => d.SetValue(IsBusyProperty, value);

    public static bool GetWatchPassword(DependencyObject d) => (bool)d.GetValue(WatchPasswordProperty);

    public static void SetWatchPassword(DependencyObject d, bool value) => d.SetValue(WatchPasswordProperty, value);

    public static bool GetHasText(DependencyObject d) => (bool)d.GetValue(HasTextProperty);

    public static void SetHasText(DependencyObject d, bool value) => d.SetValue(HasTextProperty, value);

    private static void OnSelectAllChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box)
        {
            return;
        }
        box.GotKeyboardFocus -= SelectAll;
        box.PreviewMouseLeftButtonDown -= FocusOnClick;
        if ((bool)e.NewValue)
        {
            box.GotKeyboardFocus += SelectAll;
            box.PreviewMouseLeftButtonDown += FocusOnClick;
        }
    }

    private static void SelectAll(object sender, KeyboardFocusChangedEventArgs e) => ((TextBox)sender).SelectAll();

    private static void FocusOnClick(object sender, MouseButtonEventArgs e)
    {
        var box = (TextBox)sender;
        if (!box.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            box.Focus();
        }
    }

    /// <summary>El PasswordBox no expone su texto (A-09): solo se observa si está vacío para mostrar el placeholder.</summary>
    private static void OnWatchPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box)
        {
            return;
        }
        box.PasswordChanged -= PasswordChanged;
        if ((bool)e.NewValue)
        {
            box.PasswordChanged += PasswordChanged;
        }
    }

    private static void PasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        using var secure = box.SecurePassword;
        SetHasText(box, secure.Length > 0);
    }
}

/// <summary>Insignia del semáforo de stock (AGOTADO, CRÍTICO, BAJO, ÓPTIMO…). Los colores vienen de la paleta.</summary>
public sealed class StatusBadge : Control
{
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(nameof(Status), typeof(StockStatusCode),
        typeof(StatusBadge), new PropertyMetadata(StockStatusCode.Optimal, (d, _) => ((StatusBadge)d).UpdateLabel()));

    private static readonly DependencyPropertyKey LabelPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Label), typeof(string),
        typeof(StatusBadge), new PropertyMetadata(StockRules.Label(StockStatusCode.Optimal)));

    public static readonly DependencyProperty LabelProperty = LabelPropertyKey.DependencyProperty;

    static StatusBadge() => DefaultStyleKeyProperty.OverrideMetadata(typeof(StatusBadge), new FrameworkPropertyMetadata(typeof(StatusBadge)));

    public StockStatusCode Status
    {
        get => (StockStatusCode)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string Label => (string)GetValue(LabelProperty);

    private void UpdateLabel() => SetValue(LabelPropertyKey, StockRules.Label(Status));
}

/// <summary>Barra del nivel de stock (stock / máximo) coloreada según el semáforo.</summary>
public sealed class LevelBar : Control
{
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(nameof(Status), typeof(StockStatusCode),
        typeof(LevelBar), new PropertyMetadata(StockStatusCode.Optimal));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double),
        typeof(LevelBar), new PropertyMetadata(0d, (d, _) => ((LevelBar)d).UpdateFill()));

    private static readonly DependencyPropertyKey FillWidthPropertyKey = DependencyProperty.RegisterReadOnly(nameof(FillWidth),
        typeof(double), typeof(LevelBar), new PropertyMetadata(0d));

    public static readonly DependencyProperty FillWidthProperty = FillWidthPropertyKey.DependencyProperty;

    static LevelBar() => DefaultStyleKeyProperty.OverrideMetadata(typeof(LevelBar), new FrameworkPropertyMetadata(typeof(LevelBar)));

    public LevelBar() => SizeChanged += (_, _) => UpdateFill();

    public StockStatusCode Status
    {
        get => (StockStatusCode)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <summary>Nivel entre 0 y 1 (se recorta).</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double FillWidth => (double)GetValue(FillWidthProperty);

    private void UpdateFill() => SetValue(FillWidthPropertyKey, Math.Max(0, ActualWidth) * Math.Clamp(Value, 0, 1));
}

/// <summary>Anillo de carga animado (indeterminado).</summary>
public sealed class ProgressRing : Control
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(nameof(IsActive), typeof(bool),
        typeof(ProgressRing), new PropertyMetadata(true));

    static ProgressRing() => DefaultStyleKeyProperty.OverrideMetadata(typeof(ProgressRing), new FrameworkPropertyMetadata(typeof(ProgressRing)));

    /// <summary>El giro se inicia al aplicar la plantilla (un disparador «Loaded» fallaría si el control nace oculto).</summary>
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Rotor") is UIElement { RenderTransform: System.Windows.Media.RotateTransform rotate } rotor)
        {
            var spin = new System.Windows.Media.RotateTransform();
            rotor.RenderTransform = spin;
            spin.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
                {
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                });
            _ = rotate;
        }
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }
}

/// <summary>
/// Miniatura de un producto: la imagen recortada con esquinas redondeadas o, si no tiene, un ícono sobre fondo suave.
/// Los colores salen de la paleta (cambian con el tema).
/// </summary>
public sealed class ProductThumb : Border
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(System.Windows.Media.ImageSource),
        typeof(ProductThumb), new PropertyMetadata(null, (d, _) => ((ProductThumb)d).Update()));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string),
        typeof(ProductThumb), new PropertyMetadata(Glyphs.Box, (d, _) => ((ProductThumb)d).Update()));

    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(nameof(Stretch), typeof(System.Windows.Media.Stretch),
        typeof(ProductThumb), new PropertyMetadata(System.Windows.Media.Stretch.Uniform, (d, _) => ((ProductThumb)d).Update()));

    private readonly System.Windows.Controls.Image _image = new() { SnapsToDevicePixels = true };
    private readonly TextBlock _glyph = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    public ProductThumb()
    {
        CornerRadius = new CornerRadius(10);
        ClipToBounds = true;
        SetResourceReference(BackgroundProperty, "SurfaceAlt");
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(_image, System.Windows.Media.BitmapScalingMode.HighQuality);
        _glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        _glyph.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
        SizeChanged += (_, _) =>
        {
            _glyph.FontSize = Math.Max(12, Math.Min(ActualWidth, ActualHeight) * 0.36);
            Clip = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), CornerRadius.TopLeft, CornerRadius.TopLeft);
        };
        Update();
    }

    public System.Windows.Media.ImageSource? Source
    {
        get => (System.Windows.Media.ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Ícono cuando no hay imagen.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public System.Windows.Media.Stretch Stretch
    {
        get => (System.Windows.Media.Stretch)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    private void Update()
    {
        if (Source is { } source)
        {
            _image.Source = source;
            _image.Stretch = Stretch;
            _image.Margin = Stretch == System.Windows.Media.Stretch.Uniform ? new Thickness(6) : new Thickness(0);
            Child = _image;
        }
        else
        {
            _glyph.Text = Glyph;
            Child = _glyph;
        }
    }
}
