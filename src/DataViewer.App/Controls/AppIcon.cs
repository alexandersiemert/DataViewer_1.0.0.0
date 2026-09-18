using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Siemert.DataViewer.App.Controls;

public enum IconKind
{
    None,
    Usb,
    Download,
    Bolt,
    Save,
    Export,
    Chart,
    Crosshair,
    Ruler,
    Marker,
    Legend,
    Info,
    Book,
    Refresh,
    Close,
    Folder,
    Settings,
    Warning,
    Check,
    ChevronUp,
    ChevronDown,
    Cancel,
    Clock,
    Power,
    Thermometer,
    Shield,
    Search,
    Reset,
    Compare
}

/// <summary>
/// Vektor-Symbol.
/// </summary>
/// <remarks>
/// Ersetzt die frühere Abhängigkeit FontAwesome.WPF. Das Paket stammt aus dem Jahr 2017, liefert
/// nur eine Fassung für .NET Framework 4.0 und blockierte damit jede Aktualisierung der
/// Laufzeitumgebung. Die hier verwendeten Pfade sind Teil des Projekts und ohne Fremdlizenz.
/// </remarks>
public sealed class AppIcon : Control
{
    private static readonly Dictionary<IconKind, string> Paths = new()
    {
        [IconKind.Usb] = "M12 2 L12 17 M12 17 L7 12 M12 17 L17 12 M9 21 h6 M12 2 m-1.5 0 h3 v3 h-3 Z",
        [IconKind.Download] = "M12 3 v11 M7.5 10 L12 14.5 L16.5 10 M4 19 h16",
        [IconKind.Bolt] = "M13 2 L5 13 h5 l-1 9 L19 11 h-5 l1 -9 Z",
        [IconKind.Save] = "M4 4 h11 l5 5 v11 h-16 Z M8 4 v6 h7 v-6 M7 20 v-6 h10 v6",
        [IconKind.Export] = "M4 15 v4 h16 v-4 M12 3 v12 M8 7 L12 3 L16 7",
        [IconKind.Chart] = "M4 20 h16 M6 20 v-6 M11 20 v-11 M16 20 v-8 M20 20 v-14",
        [IconKind.Crosshair] = "M12 3 v18 M3 12 h18 M12 12 m-5 0 a5 5 0 1 0 10 0 a5 5 0 1 0 -10 0",
        [IconKind.Ruler] = "M3 9 h18 v6 h-18 Z M7 9 v3 M11 9 v4 M15 9 v3 M19 9 v4",
        [IconKind.Marker] = "M12 21 s7 -7.5 7 -12 a7 7 0 1 0 -14 0 c0 4.5 7 12 7 12 Z M12 9 m-2 0 a2 2 0 1 0 4 0 a2 2 0 1 0 -4 0",
        [IconKind.Legend] = "M4 7 h4 M11 7 h9 M4 12 h4 M11 12 h9 M4 17 h4 M11 17 h9",
        [IconKind.Info] = "M12 12 m-9 0 a9 9 0 1 0 18 0 a9 9 0 1 0 -18 0 M12 11 v6 M12 7.5 v0.01",
        [IconKind.Book] = "M4 4 h7 a3 3 0 0 1 3 3 v13 a2 2 0 0 0 -2 -2 h-8 Z M20 4 h-6 v16 a2 2 0 0 1 2 -2 h4 Z",
        [IconKind.Refresh] = "M20 12 a8 8 0 1 1 -2.5 -5.8 M20 4 v4 h-4",
        [IconKind.Close] = "M6 6 L18 18 M18 6 L6 18",
        [IconKind.Folder] = "M3 6 h6 l2 2.5 h10 v11 h-18 Z",
        [IconKind.Settings] = "M12 12 m-3 0 a3 3 0 1 0 6 0 a3 3 0 1 0 -6 0 M12 3 v2.5 M12 18.5 v2.5 M3 12 h2.5 M18.5 12 h2.5 M5.6 5.6 L7.4 7.4 M16.6 16.6 L18.4 18.4 M18.4 5.6 L16.6 7.4 M7.4 16.6 L5.6 18.4",
        [IconKind.Warning] = "M12 3 L22 20 h-20 Z M12 9.5 v5 M12 17.5 v0.01",
        [IconKind.Check] = "M4.5 12.5 L9.5 17.5 L19.5 6.5",
        [IconKind.ChevronUp] = "M6 15 L12 9 L18 15",
        [IconKind.ChevronDown] = "M6 9 L12 15 L18 9",
        [IconKind.Cancel] = "M12 12 m-9 0 a9 9 0 1 0 18 0 a9 9 0 1 0 -18 0 M8.5 8.5 L15.5 15.5 M15.5 8.5 L8.5 15.5",
        [IconKind.Clock] = "M12 12 m-9 0 a9 9 0 1 0 18 0 a9 9 0 1 0 -18 0 M12 7 v5.5 l3.5 2",
        [IconKind.Power] = "M12 3 v9 M6.8 6.8 a8 8 0 1 0 10.4 0",
        [IconKind.Thermometer] = "M13.5 14.5 V5 a1.75 1.75 0 1 0 -3.5 0 v9.5 a4 4 0 1 0 3.5 0",
        [IconKind.Shield] = "M12 3 L20 6 v6 c0 5 -4 8 -8 9 c-4 -1 -8 -4 -8 -9 V6 Z M8.5 12 L11 14.5 L15.5 9.5",
        [IconKind.Search] = "M11 11 m-7 0 a7 7 0 1 0 14 0 a7 7 0 1 0 -14 0 M16 16 L21 21",
        [IconKind.Reset] = "M4 12 a8 8 0 1 0 2.5 -5.8 M4 4 v4 h4",
        [IconKind.Compare] = "M12 3 v18 M4 8 h5 M4 16 h5 M15 8 h5 M15 16 h5"
    };

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(IconKind), typeof(AppIcon),
        new FrameworkPropertyMetadata(IconKind.None, FrameworkPropertyMetadataOptions.AffectsRender, OnKindChanged));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(AppIcon),
        new FrameworkPropertyMetadata(1.7, FrameworkPropertyMetadataOptions.AffectsRender));

    private Geometry? _geometry;

    static AppIcon()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AppIcon), new FrameworkPropertyMetadata(typeof(AppIcon)));
        WidthProperty.OverrideMetadata(typeof(AppIcon), new FrameworkPropertyMetadata(16.0));
        HeightProperty.OverrideMetadata(typeof(AppIcon), new FrameworkPropertyMetadata(16.0));
        FocusableProperty.OverrideMetadata(typeof(AppIcon), new FrameworkPropertyMetadata(false));
    }

    public IconKind Kind
    {
        get => (IconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AppIcon icon)
        {
            icon._geometry = null;
            icon.InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Kind == IconKind.None || !Paths.TryGetValue(Kind, out string? data))
        {
            return;
        }

        _geometry ??= Geometry.Parse(data);

        Brush stroke = Foreground ?? Brushes.Black;
        var pen = new Pen(stroke, StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        pen.Freeze();

        // Die Pfade sind für eine 24x24-Fläche gezeichnet und werden auf die Zielgröße skaliert.
        double scale = Math.Min(ActualWidth, ActualHeight) / 24.0;
        if (scale <= 0)
        {
            return;
        }

        drawingContext.PushTransform(new ScaleTransform(scale, scale));
        drawingContext.DrawGeometry(null, pen, _geometry);
        drawingContext.Pop();
    }
}
