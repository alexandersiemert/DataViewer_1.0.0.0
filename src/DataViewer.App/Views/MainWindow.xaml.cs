using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Siemert.DataViewer.App.Controls;
using Siemert.DataViewer.App.Services;
using Siemert.DataViewer.App.ViewModels;
using Siemert.DataViewer.Core.Analysis;
using Siemert.DataViewer.Core.Model;
using Siemert.DataViewer.Core.Protocol;
using Siemert.DataViewer.Core.Units;

namespace Siemert.DataViewer.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly DeviceWatcher _deviceWatcher;
    private PlotController? _plot;
    private bool _suppressToolEvents;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        Title = AppInfo.Title;
        VersionLabel.Text = "Version " + AppInfo.Version;

        Width = _vm.Settings.WindowWidth;
        Height = _vm.Settings.WindowHeight;
        if (_vm.Settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        _deviceWatcher = new DeviceWatcher(Dispatcher);
        _deviceWatcher.DevicesChanged += OnDevicesChanged;

        _vm.PlotInvalidated += (_, _) => RedrawPlot();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _plot = new PlotController(Plot);
        _plot.CrosshairChanged += OnCrosshairChanged;
        _plot.MeasureChanged += OnMeasureChanged;
        _plot.ShowEmpty();

        SyncMenuChecks();
        SyncToolPanels();
        BuildMetrics();
        BuildMeasurePanel(null);

        await _vm.RefreshDevicesAsync(false);
        _deviceWatcher.Start();
    }

    /// <summary>
    /// Ein Gerät wurde angesteckt oder abgezogen.
    /// </summary>
    /// <remarks>
    /// Waehrend eines laufenden Auslesevorgangs wird nicht gesucht. Die Suche selbst laesst
    /// belegte Anschluesse ohnehin unangetastet; hier wird zusaetzlich gar nicht erst begonnen,
    /// damit die Anzeige waehrend der Uebertragung ruhig bleibt.
    /// </remarks>
    private async void OnDevicesChanged(object? sender, EventArgs e)
    {
        if (_vm.IsBusy)
        {
            return;
        }

        await _vm.RefreshDevicesAsync(false);
    }

    /// <summary>Überträgt die gespeicherten Einstellungen in die Bedienelemente.</summary>
    private void SyncToolPanels()
    {
        _suppressToolEvents = true;
        try
        {
            AppSettings s = _vm.Settings;
            SeriesAltitude.IsChecked = s.ShowAltitude;
            SeriesSpeed.IsChecked = s.ShowSpeed;
            SeriesTemperature.IsChecked = s.ShowTemperature;
            SeriesAcc.IsChecked = s.ShowAcceleration;
            SeriesAccX.IsChecked = s.ShowAccX;
            SeriesAccY.IsChecked = s.ShowAccY;
            SeriesAccZ.IsChecked = s.ShowAccZ;
            ToggleLegend.IsChecked = s.ShowLegend;
            ShowGridBox.IsChecked = s.ShowGrid;
            AxisLabelsFollowBox.IsChecked = s.AxisLabelsFollowSeries;

            foreach (ComboBoxItem item in SmoothingMethodBox.Items)
            {
                if (Enum.TryParse(item.Tag as string, out SmoothingMethod m) && m == s.Smoothing)
                {
                    SmoothingMethodBox.SelectedItem = item;
                    ExplainSmoothing(s.Smoothing);
                    break;
                }
            }

            ChartStyleBox.SelectedIndex = (int)s.ChartStyle;
            UpdateStyleSwatches();

            foreach (TextBox box in SmoothingBoxes)
            {
                box.Text = Fmt(s.SmoothingFor(KindOf(box)));
            }
        }
        finally
        {
            _suppressToolEvents = false;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _deviceWatcher.DevicesChanged -= OnDevicesChanged;
        _deviceWatcher.Dispose();

        _vm.Settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _vm.Settings.WindowWidth = Width;
            _vm.Settings.WindowHeight = Height;
        }

        _vm.Settings.Save();
    }

    // ------------------------------------------------------------------ Diagramm

    private void RedrawPlot()
    {
        if (_plot is null)
        {
            return;
        }

        if (_vm.SelectedRecording is null)
        {
            _plot.ShowEmpty();
        }
        else
        {
            _plot.Device = _vm.LoadedDevice;

            // Waehrend einer Live-Aufzeichnung bleibt ein selbst eingestellter Ausschnitt
            // erhalten. "Ansicht" setzt ihn wieder auf automatisch.
            _plot.Render(_vm.SelectedRecording.Recording, _vm.Reference, _vm.Metrics, _vm.Settings,
                keepView: _vm.IsLiveRecording);
        }

        BuildMetrics();
    }

    private void OnResetView(object sender, RoutedEventArgs e)
    {
        _plot?.ClearMarkers();
        _plot?.ResetView();

        foreach (TextBox box in AxisBoxes)
        {
            box.Clear();
        }
    }

    // ------------------------------------------------------------------ Werkzeuge

    /// <summary>
    /// Die drei Werkzeuge schließen sich gegenseitig aus. Sie sind trotzdem als Umschalter und
    /// nicht als Optionsfeld ausgeführt, damit sich das aktive Werkzeug durch erneutes Drücken
    /// wieder abschalten lässt.
    /// </summary>
    private void OnToolChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressToolEvents || _plot is null)
        {
            return;
        }

        _suppressToolEvents = true;
        try
        {
            var active = sender as ToggleButton;
            foreach (ToggleButton t in new[] { ToolMeasure, ToolCrosshair, ToolMarker })
            {
                if (!ReferenceEquals(t, active))
                {
                    t.IsChecked = false;
                }
            }

            PlotTool tool =
                ToolMeasure.IsChecked == true ? PlotTool.Measure :
                ToolCrosshair.IsChecked == true ? PlotTool.Crosshair :
                ToolMarker.IsChecked == true ? PlotTool.Marker :
                PlotTool.None;

            _plot.SetTool(tool);

            CrosshairBar.Visibility = tool == PlotTool.Crosshair ? Visibility.Visible : Visibility.Collapsed;

            if (tool == PlotTool.Measure)
            {
                RightTabs.SelectedItem = MeasureTab;
                _vm.StatusText = "Messen: Zeitbereich im Diagramm mit gedrückter Maustaste aufziehen.";
            }
            else if (tool == PlotTool.Marker)
            {
                _vm.StatusText = "Marker: Klick ins Diagramm setzt einen Marker. „Ansicht“ entfernt alle wieder.";
            }
            else if (tool == PlotTool.Crosshair)
            {
                _vm.StatusText = "Fadenkreuz: Mauszeiger über das Diagramm bewegen.";
            }
            else
            {
                _vm.StatusText = "Bereit";
            }
        }
        finally
        {
            _suppressToolEvents = false;
        }
    }

    private void OnLegendToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToolEvents)
        {
            return;
        }

        _vm.Settings.ShowLegend = ToggleLegend.IsChecked == true;
        _vm.Settings.Save();
        RedrawPlot();
    }

    private void OnSmoothingMethodChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolEvents || SmoothingMethodBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        if (Enum.TryParse(item.Tag as string, out SmoothingMethod method))
        {
            _vm.Settings.Smoothing = method;
            _vm.Settings.Save();
            ExplainSmoothing(method);
            RedrawPlot();
        }
    }

    /// <summary>
    /// Erklaert das gewaehlte Glaettungsverfahren in zwei Saetzen.
    /// </summary>
    /// <remarks>
    /// Die Wahl des Verfahrens veraendert abgelesene Spitzenwerte. Wer sie trifft, muss wissen,
    /// was sie bewirkt, ohne dafuer in die Dokumentation wechseln zu muessen.
    /// </remarks>
    private void ExplainSmoothing(SmoothingMethod method)
    {
        SmoothingExplanation.Text = method switch
        {
            SmoothingMethod.SavitzkyGolay =>
                "Legt in jedes Fenster eine Parabel nach der Methode der kleinsten Quadrate und nimmt " +
                "deren Wert in der Mitte. Höhe und Lage von Spitzen bleiben dabei weitgehend erhalten. " +
                "Empfohlen, wenn Öffnungsstoß oder Spitzensinkrate beurteilt werden sollen.",

            SmoothingMethod.MovingAverage =>
                "Mittelt alle Werte im Fenster gleich stark. Einfach und wirksam gegen Rauschen, drückt " +
                "echte Spitzen aber systematisch nach unten. Ein abgelesener Höchstwert fällt damit zu " +
                "klein aus.",

            SmoothingMethod.Gaussian =>
                "Gewichtet die Werte im Fenster glockenförmig. Das ergibt die gleichmäßigste Kurve ohne " +
                "Überschwingen. Spitzen werden ähnlich gedämpft wie beim gleitenden Mittelwert.",

            SmoothingMethod.Median =>
                "Nimmt den mittleren Wert des Fensters statt des Durchschnitts. Einzelne Ausreißer " +
                "verschwinden dadurch vollständig, Sprünge bleiben scharf. Geeignet gegen die " +
                "Quantisierungsstufen des Drucksensors.",

            SmoothingMethod.Spline =>
                "Legt eine einzige Kurve durch die gesamte Reihe und wägt dabei zwischen Nähe zu " +
                "den Messwerten und geringer Krümmung ab. Kein Fenster, dadurch glättet es in der " +
                "Mitte der Reihe am stärksten. Eine gleichmäßige Sinkrate geht unverändert durch.",

            _ => string.Empty
        };
    }

    private void OnSeriesToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToolEvents)
        {
            return;
        }

        AppSettings s = _vm.Settings;
        s.ShowAltitude = SeriesAltitude.IsChecked == true;
        s.ShowSpeed = SeriesSpeed.IsChecked == true;
        s.ShowTemperature = SeriesTemperature.IsChecked == true;
        s.ShowAcceleration = SeriesAcc.IsChecked == true;
        s.ShowAccX = SeriesAccX.IsChecked == true;
        s.ShowAccY = SeriesAccY.IsChecked == true;
        s.ShowAccZ = SeriesAccZ.IsChecked == true;
        s.Save();

        // Nur die Sichtbarkeit umschalten, nicht das ganze Diagramm neu aufbauen: Zoom,
        // Ausschnitt, gesetzte Marker und ein laufender Messbereich bleiben dadurch erhalten.
        if (_plot?.HasData == true)
        {
            _plot.SetSeriesVisible(s);
            BuildMetrics();
            return;
        }

        RedrawPlot();
    }

    private static readonly (SeriesKind Kind, string Swatch)[] SeriesRows =
    [
        (SeriesKind.Altitude, "SwAltitude"),
        (SeriesKind.Speed, "SwSpeed"),
        (SeriesKind.Temperature, "SwTemperature"),
        (SeriesKind.AccMagnitude, "SwAcc"),
        (SeriesKind.AccX, "SwAccX"),
        (SeriesKind.AccY, "SwAccY"),
        (SeriesKind.AccZ, "SwAccZ")
    ];

    /// <summary>Faerbt die kleinen Farbbalken neben den Kurven-Haekchen.</summary>
    private void UpdateStyleSwatches()
    {
        foreach ((SeriesKind kind, string name) in SeriesRows)
        {
            if (FindName(name) is Border swatch)
            {
                SeriesStyle st = _vm.Settings.StyleFor(kind);
                try
                {
                    swatch.Background = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(st.Color));
                }
                catch (FormatException)
                {
                    // Ungueltige Farbe in den Einstellungen - Vorgabe verwenden.
                    swatch.Background = Brushes.Gray;
                }

                swatch.Height = Math.Clamp(st.Width, 1, 6);
            }
        }
    }

    /// <summary>Die Farben, die zur Auswahl stehen. Okabe-Ito plus einige neutrale Toene.</summary>
    private static readonly (string Hex, string Name)[] Palette =
    [
        ("#0B0B0B", "Schwarz"), ("#0072B2", "Blau"), ("#009E73", "Grün"), ("#D55E00", "Orangerot"),
        ("#E69F00", "Orange"), ("#CC79A7", "Rosa"), ("#56B4E9", "Hellblau"), ("#F0E442", "Gelb"),
        ("#B3261E", "Rot"), ("#6B7785", "Grau"), ("#5A3E8E", "Violett"), ("#1B7F4B", "Dunkelgrün")
    ];

    /// <summary>
    /// Farbe, Linienbreite und Muster einer Kurve.
    /// </summary>
    /// <remarks>
    /// Fassung 1 verteilte das auf drei Untermenues mit zusammen 24 Eintraegen im
    /// Einstellungsmenue. Hier sitzt alles an der Kurve selbst, wo es hingehoert.
    /// </remarks>
    private void OnSeriesStyle(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        SeriesKind kind = KindOf(button);
        SeriesStyle current = _vm.Settings.StyleFor(kind);

        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };

        var colors = new MenuItem { Header = "Farbe" };
        foreach ((string hex, string name) in Palette)
        {
            var item = new MenuItem
            {
                Header = name,
                IsCheckable = true,
                IsChecked = string.Equals(hex, current.Color, StringComparison.OrdinalIgnoreCase),
                Icon = new Border
                {
                    Width = 14,
                    Height = 4,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))
                }
            };

            string captured = hex;
            item.Click += (_, _) => ApplyStyle(kind, st => st.Color = captured);
            colors.Items.Add(item);
        }

        var widths = new MenuItem { Header = "Linienbreite" };
        foreach (double w in new[] { 1.0, 1.5, 2.0, 2.5, 3.0, 4.0 })
        {
            double captured = w;
            var item = new MenuItem
            {
                Header = w.ToString("0.0", CultureInfo.CurrentCulture),
                IsCheckable = true,
                IsChecked = Math.Abs(current.Width - w) < 0.01
            };
            item.Click += (_, _) => ApplyStyle(kind, st => st.Width = captured);
            widths.Items.Add(item);
        }

        var patterns = new MenuItem { Header = "Linienmuster" };
        foreach ((string key, string label) in new[]
                 {
                     ("Solid", "Durchgezogen"), ("Dashed", "Gestrichelt"),
                     ("DenselyDashed", "Eng gestrichelt"), ("Dotted", "Gepunktet")
                 })
        {
            string captured = key;
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = string.Equals(key, current.Pattern, StringComparison.Ordinal)
            };
            item.Click += (_, _) => ApplyStyle(kind, st => st.Pattern = captured);
            patterns.Items.Add(item);
        }

        var reset = new MenuItem { Header = "Auf Vorgabe zurücksetzen" };
        reset.Click += (_, _) =>
        {
            _vm.Settings.SetStyle(kind, SeriesStyle.Default(kind));
            _vm.Settings.Save();
            UpdateStyleSwatches();
            RedrawPlot();
        };

        menu.Items.Add(colors);
        menu.Items.Add(widths);
        menu.Items.Add(patterns);
        menu.Items.Add(new Separator());
        menu.Items.Add(reset);
        menu.IsOpen = true;
    }

    private void ApplyStyle(SeriesKind kind, Action<SeriesStyle> change)
    {
        SeriesStyle st = _vm.Settings.StyleFor(kind).Clone();
        change(st);
        _vm.Settings.SetStyle(kind, st);
        _vm.Settings.Save();
        UpdateStyleSwatches();
        RedrawPlot();
    }

    private void OnResetSeriesStyles(object sender, RoutedEventArgs e)
    {
        _vm.Settings.ResetStyles();
        _vm.Settings.Save();
        SyncToolPanels();
        RedrawPlot();
    }

    private void OnChartStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolEvents || !IsLoaded)
        {
            return;
        }

        _vm.Settings.ChartStyle = (ChartStyle)Math.Max(0, ChartStyleBox.SelectedIndex);
        _vm.Settings.Save();
        RedrawPlot();
    }

    private void OnChromeToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToolEvents)
        {
            return;
        }

        _vm.Settings.ShowGrid = ShowGridBox.IsChecked == true;
        _vm.Settings.AxisLabelsFollowSeries = AxisLabelsFollowBox.IsChecked == true;
        _vm.Settings.Save();
        RedrawPlot();
    }

    private void OnExportCsvContext(object sender, RoutedEventArgs e) => _vm.ExportCsv();

    private void OnSaveSingleContext(object sender, RoutedEventArgs e) => _vm.SaveSelectedRecording();

    private TextBox[] SmoothingBoxes =>
        [SmoothAltitude, SmoothSpeed, SmoothTemperature, SmoothAcc, SmoothAccX, SmoothAccY, SmoothAccZ];

    private TextBox[] AxisBoxes =>
        [AxisAltMin, AxisAltMax, AxisSpeedMin, AxisSpeedMax, AxisAccMin, AxisAccMax];

    private static SeriesKind KindOf(FrameworkElement element) =>
        element.Tag is string tag && Enum.TryParse(tag, out SeriesKind kind) ? kind : SeriesKind.Altitude;

    private static string Fmt(double v) =>
        v <= 0 ? string.Empty : v.ToString("0.#", CultureInfo.CurrentCulture);

    private static bool TryNumber(string text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
               double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void OnSmoothingKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        CommitSmoothing(sender as TextBox);
        e.Handled = true;
    }

    private void OnSmoothingCommit(object sender, RoutedEventArgs e) => CommitSmoothing(sender as TextBox);

    private void CommitSmoothing(TextBox? box)
    {
        if (box is null || _suppressToolEvents)
        {
            return;
        }

        SeriesKind kind = KindOf(box);
        double seconds = TryNumber(box.Text, out double v) ? Math.Clamp(v, 0, AppSettings.MaxSmoothingSeconds) : 0;

        if (Math.Abs(_vm.Settings.SmoothingFor(kind) - seconds) < 0.001)
        {
            box.Text = Fmt(seconds);
            return;
        }

        _vm.Settings.SetSmoothing(kind, seconds);
        _vm.Settings.Save();

        _suppressToolEvents = true;
        box.Text = Fmt(seconds);
        _suppressToolEvents = false;

        RedrawPlot();
    }

    private void OnSmoothingReset(object sender, RoutedEventArgs e)
    {
        _suppressToolEvents = true;
        foreach (TextBox box in SmoothingBoxes)
        {
            _vm.Settings.SetSmoothing(KindOf(box), 0);
            box.Text = string.Empty;
        }

        _suppressToolEvents = false;
        _vm.Settings.Save();
        RedrawPlot();
    }

    private void OnRightTabChanged(object sender, SelectionChangedEventArgs e)
    {
        // Wer auf "Messen" wechselt, will messen - also das Werkzeug gleich einschalten.
        if (!IsLoaded || _plot is null || RightTabs.SelectedItem != MeasureTab)
        {
            return;
        }

        if (ToolMeasure.IsChecked != true)
        {
            ToolMeasure.IsChecked = true;
        }
    }

    private void OnAxisLimitKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _plot is null)
        {
            return;
        }

        ApplyAxisLimits();
        e.Handled = true;
    }

    /// <summary>
    /// Uebernimmt die eingetragenen Achsengrenzen.
    /// </summary>
    /// <remarks>
    /// Frueher wurde hier nur die Hoehenachse ausgewertet; die Felder fuer Sinkrate und
    /// Beschleunigung waren vorhanden, taten aber nichts.
    /// </remarks>
    private void ApplyAxisLimits()
    {
        if (_plot is null)
        {
            return;
        }

        Apply(SeriesKind.Altitude, AxisAltMin, AxisAltMax);
        Apply(SeriesKind.Speed, AxisSpeedMin, AxisSpeedMax);
        Apply(SeriesKind.AccMagnitude, AxisAccMin, AxisAccMax);

        void Apply(SeriesKind axis, TextBox minBox, TextBox maxBox)
        {
            bool hasMin = TryNumber(minBox.Text, out double min);
            bool hasMax = TryNumber(maxBox.Text, out double max);

            if (!hasMin && !hasMax)
            {
                return;
            }

            if (!_plot!.HasAxis(axis))
            {
                _vm.Notify(NoticeLevel.Info,
                    "Die Achse ist im Diagramm nicht vorhanden, weil die zugehoerige Kurve ausgeblendet ist.");
                return;
            }

            if (!hasMin || !hasMax || max <= min)
            {
                _vm.Notify(NoticeLevel.Warning,
                    "Fuer eine feste Achsengrenze werden beide Werte gebraucht, und der obere muss groesser sein als der untere.");
                return;
            }

            _plot.SetAxisLimits(axis, min, max);
        }
    }

    // ------------------------------------------------------------------ Maus im Diagramm

    // Die Ereignisse werden in der Vorschauphase abgegriffen. Sonst verarbeitet die
    // Diagrammbibliothek den Klick zuerst und die Werkzeuge bekaemen ihn nie zu sehen.
    private void OnPlotMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_plot?.OnMouseDown(e) == true)
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Mittlere Maustaste: Ausschnitt verschieben, unabhaengig vom gewaehlten Werkzeug.
    /// </summary>
    private void OnPlotMouseDownAny(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _plot?.OnPanStart(e) == true)
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Das Zoomen mit dem Mausrad erledigt die Diagrammbibliothek selbst.
    /// </summary>
    /// <remarks>
    /// Wir bekommen davon sonst nichts mit. Ohne diesen Vermerk wuerde ein herangezoomter
    /// Ausschnitt waehrend einer Live-Aufzeichnung beim naechsten Takt wieder verworfen.
    /// </remarks>
    private void OnPlotMouseWheel(object sender, MouseWheelEventArgs e) => _plot?.NoteViewAdjusted();

    private void OnPlotMouseUpAny(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _plot?.OnPanEnd() == true)
        {
            e.Handled = true;
        }
    }

    private void OnPlotMouseMove(object sender, MouseEventArgs e)
    {
        if (_plot?.OnMouseMove(e) == true)
        {
            e.Handled = true;
        }
    }

    private void OnPlotMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_plot?.OnMouseUp(e) == true)
        {
            e.Handled = true;
        }
    }

    private void OnPlotMouseLeave(object sender, MouseEventArgs e) => _plot?.OnMouseLeave();

    // ------------------------------------------------------------------ Ablesewerte

    private void OnCrosshairChanged(object? sender, CrosshairReadout? readout)
    {
        if (readout is null)
        {
            CrosshairTime.Text = "-";
            CrosshairValues.ItemsSource = null;
            return;
        }

        // Uhrzeit statt Sekunden - dieselbe Angabe wie auf der Zeitachse.
        CrosshairTime.Text = _plot?.TimeOfDay(readout.TimeSeconds) ?? string.Empty;

        var chips = readout.Values
            .Select(v => new Border
            {
                Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                BorderBrush = (Brush)Application.Current.Resources["LineBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 2, 6, 2),
                Child = BuildChip(v.Kind, v.Label, v.Value, v.Unit)
            })
            .ToList();

        CrosshairValues.ItemsSource = chips;
    }

    private static UIElement BuildChip(SeriesKind kind, string label, string value, string unit)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = SeriesBrush(kind)
        });
        panel.Children.Add(new TextBlock
        {
            Text = value + " " + unit,
            FontSize = 11.5,
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"]
        });
        return panel;
    }

    private static Brush SeriesBrush(SeriesKind kind) => (Brush)Application.Current.Resources[kind switch
    {
        SeriesKind.Altitude => "SeriesAltitudeBrush",
        SeriesKind.Speed => "SeriesSpeedBrush",
        SeriesKind.Temperature => "SeriesTemperatureBrush",
        SeriesKind.AccMagnitude => "SeriesAccBrush",
        SeriesKind.AccX => "SeriesAccXBrush",
        SeriesKind.AccY => "SeriesAccYBrush",
        _ => "SeriesAccZBrush"
    }];

    private void OnMeasureChanged(object? sender, MeasureResult? result) => BuildMeasurePanel(result);

    /// <summary>
    /// Baut die Messwertetafel.
    /// </summary>
    /// <remarks>
    /// Anders als in Fassung 1 steht hier auch die <b>Dauer</b> des gewählten Bereichs. Sie wurde
    /// dort intern für die Geschwindigkeit gebraucht, aber nie angezeigt — obwohl die Freifallzeit
    /// die Zahl ist, nach der als Erstes gefragt wird.
    /// </remarks>
    private void BuildMeasurePanel(MeasureResult? r)
    {
        MeasurePanel.Children.Clear();

        if (r is null)
        {
            MeasurePanel.Children.Add(Hint(
                "Werkzeug „Messen“ einschalten und im Diagramm mit gedrückter Maustaste einen " +
                "Zeitbereich aufziehen. Für jede sichtbare Kurve erscheinen dann Wert an beiden " +
                "Rändern, kleinster und größter Wert, Differenz und Mittelwert."));
            return;
        }

        MeasurePanel.Children.Add(Section("Bereich"));
        string from = _plot?.TimeOfDay(Math.Min(r.Time1, r.Time2)) ?? string.Empty;
        string to = _plot?.TimeOfDay(Math.Max(r.Time1, r.Time2)) ?? string.Empty;

        MeasurePanel.Children.Add(Row("von", from, string.Empty));
        MeasurePanel.Children.Add(Row("bis", to, string.Empty));
        MeasurePanel.Children.Add(Row("Dauer", FormatSeconds(r.Duration), string.Empty, strong: true));
        MeasurePanel.Children.Add(Row("Messpunkte", r.SampleCount.ToString("N0", CultureInfo.CurrentCulture), string.Empty));

        if (r.SmoothingActive)
        {
            MeasurePanel.Children.Add(Hint(
                "Die Darstellung ist geglättet. Die folgenden Zahlen stammen aus den ungeglätteten " +
                "Messwerten und können deshalb von der gezeichneten Kurve abweichen."));
        }

        // Bewusst alle Serien, auch die ausgeblendeten - so wie in Fassung 1. Wer einen Bereich
        // misst, will alle Messgrößen sehen, nicht nur die gerade gezeichneten.
        foreach (SeriesStatistics s in r.Series)
        {
            MeasurePanel.Children.Add(SeriesSection(s));
            MeasurePanel.Children.Add(Row("bei 1", PlotController.Format(s.At1, s.Digits), s.Unit));
            MeasurePanel.Children.Add(Row("bei 2", PlotController.Format(s.At2, s.Digits), s.Unit));
            MeasurePanel.Children.Add(Row("Differenz", PlotController.Format(s.Delta, s.Digits), s.Unit, strong: true));
            MeasurePanel.Children.Add(Row("kleinster", PlotController.Format(s.Min, s.Digits), s.Unit));
            MeasurePanel.Children.Add(Row("größter", PlotController.Format(s.Max, s.Digits), s.Unit));
            MeasurePanel.Children.Add(Row("Mittelwert", PlotController.Format(s.Average, s.Digits), s.Unit));

            if (s.Kind == SeriesKind.Altitude && !double.IsNaN(s.RatePerSecond))
            {
                // Aus der Höhendifferenz gerechnet, nicht aus der gefilterten Geschwindigkeitskurve.
                double ms = _vm.Settings.UnitSystem == UnitSystem.Metric
                    ? s.RatePerSecond
                    : UnitConverter.FeetToMeters(s.RatePerSecond);

                MeasurePanel.Children.Add(Row("Sinkrate",
                    Num(UnitConverter.Speed(ms, _vm.Settings.SpeedUnit), 1),
                    UnitConverter.SpeedUnitLabel(_vm.Settings.SpeedUnit), strong: true));
            }
        }
    }

    /// <summary>Kompakte Zeile fuer die Messwertetafel.</summary>
    private static UIElement Row(string label, string value, string unit, bool strong = false)
    {
        var grid = new Grid { Height = 19 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });

        var caption = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextMutedBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        var number = new TextBlock
        {
            Text = value,
            FontSize = 12,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = strong ? FontWeights.Bold : FontWeights.Normal,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"]
        };

        var unitText = new TextBlock
        {
            Text = unit,
            FontSize = 10,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextMutedBrush"]
        };

        Grid.SetColumn(caption, 0);
        Grid.SetColumn(number, 1);
        Grid.SetColumn(unitText, 2);
        grid.Children.Add(caption);
        grid.Children.Add(number);
        grid.Children.Add(unitText);
        return grid;
    }

    private static UIElement SeriesSection(SeriesStatistics s)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 6) };
        panel.Children.Add(new Border
        {
            Width = 10,
            Height = 3,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
            Background = SeriesBrush(s.Kind)
        });
        panel.Children.Add(new TextBlock
        {
            Text = s.Label.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextMutedBrush"]
        });

        if (!s.Visible)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "ausgeblendet",
                FontSize = 9.5,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextMutedBrush"],
                ToolTip = "Die Kurve ist im Diagramm ausgeblendet. Die Zahlen werden trotzdem berechnet."
            });
        }

        return panel;
    }

    private void OnExportImage(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedRecording is null)
        {
            _vm.Notify(NoticeLevel.Info, "Es ist keine Aufnahme geladen, die sich als Bild speichern ließe.");
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Diagramm als Bild speichern",
            Filter = "PNG-Bild (*.png)|*.png",
            FileName = "Sprungdiagramm.png",
            InitialDirectory = _vm.Settings.LastExportFolder ?? string.Empty
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            Plot.Plot.SavePng(dlg.FileName, 1800, 1000);
            _vm.Settings.LastExportFolder = Path.GetDirectoryName(dlg.FileName);
            _vm.Settings.Save();
            _vm.Notify(NoticeLevel.Success, "Bild gespeichert: " + Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            _vm.Notify(NoticeLevel.Error, "Das Bild konnte nicht gespeichert werden: " + ex.Message);
            AppLog.Error("Bildexport fehlgeschlagen.", ex);
        }
    }

    // ------------------------------------------------------------------ Kennzahlen

    /// <summary>
    /// Baut die Sprungübersicht auf.
    /// </summary>
    /// <remarks>
    /// Früher standen hier 37 Felder, die alle auf einem Gedankenstrich blieben, bis der Anwender
    /// von Hand einen Messbereich aufgezogen hatte – und die Freifallzeit, die zentrale Zahl eines
    /// Sprungs, war überhaupt nicht dabei, obwohl sie intern für die Geschwindigkeit berechnet
    /// wurde. Jetzt wird alles sofort berechnet, und fehlende Werte werden als fehlend gezeigt.
    /// </remarks>
    /// <summary>
    /// Baut die Uebersicht auf.
    /// </summary>
    /// <remarks>
    /// Oben drei grosse Zahlen, darunter die Einzelheiten. Hinweise stehen als schmale Streifen
    /// darueber und sind auf einen Satz gekuerzt.
    /// </remarks>
    private void BuildMetrics()
    {
        MetricsPanel.Children.Clear();

        if (_vm.SelectedRecording is null)
        {
            MetricsPanel.Children.Add(Note(
                "Keine Aufnahme ausgewählt.", "AccentBrush", "AccentSoftBrush"));
            return;
        }

        Recording rec = _vm.SelectedRecording.Recording;
        JumpMetrics m = _vm.Metrics;

        // Der Takt der Temperatur haengt davon ab, woher die Aufnahme stammt: Aus dem
        // Geraetespeicher kommt alle 240 Messpunkte ein neuer Wert, live liefert jede Abfrage
        // einen frischen.
        TemperatureHint.Text = rec.IsLive
            ? "live bei jeder Abfrage"
            : "aus dem Gerät nur alle 60 s";
        UnitSystem units = _vm.Settings.UnitSystem;
        SpeedUnit speedUnit = _vm.Settings.SpeedUnit;
        string aUnit = UnitConverter.AltitudeUnitLabel(units);
        string vUnit = UnitConverter.SpeedUnitLabel(speedUnit);
        string tUnit = UnitConverter.TemperatureUnitLabel(units);

        // ----------------------------------------------------------- Hinweise, je einen Satz
        if (!rec.IsComplete)
        {
            MetricsPanel.Children.Add(Note(
                "Übertragung unvollständig. Alle Werte gelten nur für den empfangenen Teil.",
                "DangerBrush", "DangerSoftBrush"));
        }

        if (rec.HasSaturatedSamples)
        {
            MetricsPanel.Children.Add(Note(
                "Beschleunigungssensor zeitweise am Anschlag. Der Spitzenwert lag höher als angezeigt.",
                "WarningBrush", "WarningSoftBrush"));
        }

        if (rec.LowBattery)
        {
            MetricsPanel.Children.Add(Note(
                "Das Gerät meldete zu geringe Batteriespannung.",
                "WarningBrush", "WarningSoftBrush"));
        }

        if (!rec.ClockWasSet)
        {
            MetricsPanel.Children.Add(Note(
                "Uhr nicht gestellt, deshalb kein Datum. Zeiten gelten ab Aufnahmebeginn. " +
                "Die Uhr geht verloren, sobald das Gerät ohne Strom ist.",
                "WarningBrush", "WarningSoftBrush"));
        }

        // ----------------------------------------------------------- Die drei grossen Zahlen
        if (m.JumpDetected)
        {
            MetricsPanel.Children.Add(TileRow(
                HeroTile("Absprung", Num(UnitConverter.Altitude(m.ExitAltitudeM ?? double.NaN, units), 0), aUnit,
                    Res("SeriesAltitudeBrush")),
                HeroTile("Freifall", SplitSeconds(m.FreefallSeconds).Value,
                    SplitSeconds(m.FreefallSeconds).Unit, Res("AccentBrush")),
                HeroTile("Öffnung", Num(UnitConverter.Altitude(m.DeploymentAltitudeM ?? double.NaN, units), 0), aUnit,
                    Res("SuccessBrush"))));

            MetricsPanel.Children.Add(TileRow(
                HeroTile("Sinkrate", Num(UnitConverter.Speed(m.MaxDescentRateMs, speedUnit), 0), vUnit,
                    Res("SeriesSpeedBrush")),
                HeroTile("Öffnungsstoß", Num(m.MaxOpeningG ?? double.NaN, 1),
                    m.OpeningGSaturated ? "g +" : "g", Res("SeriesAccBrush")),
                HeroTile("Gesamt", SplitSeconds(m.TotalSeconds).Value,
                    SplitSeconds(m.TotalSeconds).Unit, Res("TextPrimaryBrush"))));

            MetricsPanel.Children.Add(Section("Sprungverlauf"));
            MetricsPanel.Children.Add(Metric("Schirmfahrt", FormatSeconds(m.CanopySeconds), string.Empty));
            MetricsPanel.Children.Add(Metric("Mittlere Freifallrate",
                Num(UnitConverter.Speed(m.MeanFreefallRateMs ?? double.NaN, speedUnit), 0), vUnit));
            MetricsPanel.Children.Add(Metric("Höchste Beschleunigung", Num(m.MaxAccelerationG, 1), "g"));
        }
        else
        {
            MetricsPanel.Children.Add(TileRow(
                HeroTile("Dauer", SplitSeconds(m.TotalSeconds).Value,
                    SplitSeconds(m.TotalSeconds).Unit, Res("TextPrimaryBrush")),
                HeroTile("Höhenhub", Num(UnitConverter.Altitude(m.MaxAltitudeM - m.MinAltitudeM, units), 0), aUnit,
                    Res("SeriesAltitudeBrush")),
                HeroTile("Punkte", rec.Samples.Count.ToString("N0", CultureInfo.CurrentCulture), string.Empty,
                    Res("TextPrimaryBrush"))));

            MetricsPanel.Children.Add(TileRow(
                HeroTile("Sinkrate", Num(UnitConverter.Speed(m.MaxDescentRateMs, speedUnit), 0), vUnit,
                    Res("SeriesSpeedBrush")),
                HeroTile("Beschleunigung", Num(m.MaxAccelerationG, 1), "g", Res("SeriesAccBrush")),
                HeroTile("Temperatur", Num(UnitConverter.Temperature(rec.EndTemperatureC, units), 1), tUnit,
                    Res("SeriesTemperatureBrush"))));

            MetricsPanel.Children.Add(Note(m.Note, "AccentBrush", "AccentSoftBrush"));
        }

        AppendEnvironment(rec, m, tUnit, units);
    }


    /// <summary>
    /// Haengt Umgebung, Geraetezustand und Verbindungshistorie an.
    /// </summary>
    /// <remarks>
    /// Gegliedert in Karten statt in eine durchlaufende Liste. Angaben, die schon als Kachel
    /// oben stehen, werden hier nicht wiederholt.
    /// </remarks>
    private void AppendEnvironment(Recording rec, JumpMetrics m, string tUnit, UnitSystem units)
    {
        var environment = new StackPanel();
        environment.Children.Add(Metric("Temperatur min/max",
            $"{Num(UnitConverter.Temperature(m.MinTemperatureC, units), 1)} / {Num(UnitConverter.Temperature(m.MaxTemperatureC, units), 1)}",
            tUnit));
        environment.Children.Add(Metric("Druck Start/Ende",
            $"{Num(rec.StartPressureHpa, 1)} / {Num(rec.EndPressureHpa, 1)}", "hPa"));
        environment.Children.Add(Metric("Abtastrate", "4", "Hz"));

        MetricsPanel.Children.Add(Section("Umgebung"));
        MetricsPanel.Children.Add(Card(environment));

        // ----------------------------------------------------------- Gerätezustand
        // Die gemessene Spannung wird hinter dem Spannungswandler abgegriffen. Sie bleibt
        // deshalb weitgehend konstant, solange der Wandler arbeitet, und sagt nichts ueber den
        // Ladezustand der Batterie aus. Sie hier anzuzeigen wuerde eine Aussage vortaeuschen,
        // die der Wert nicht traegt. Belastbar ist allein das Statusbit, das das Geraet selbst
        // setzt.
        var device = new StackPanel();

        device.Children.Add(Metric("Batterie",
            rec.LowBattery ? "zu niedrig" : "unauffällig", string.Empty, emphasize: rec.LowBattery));

        device.Children.Add(Metric("Statuswort", "0x" + rec.StatusRaw.ToString("X4"), string.Empty, small: true));


        MetricsPanel.Children.Add(Section("Gerät"));
        MetricsPanel.Children.Add(Card(device));

        // ----------------------------------------------------------- Verbindungshistorie
        if (_vm.Events.Count == 0)
        {
            return;
        }

        var history = new StackPanel();
        history.Children.Add(Metric("Gespeicherte Vorgänge",
            _vm.Events.Count.ToString("N0", CultureInfo.CurrentCulture), string.Empty));

        foreach (DeviceEvent ev in _vm.Events.Reverse().Take(4))
        {
            string when = ev.ClockWasSet && ev.Timestamp.HasValue
                ? ev.Timestamp.Value.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture)
                : ev.TimeOfDay.ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture) + " (ohne Datum)";

            history.Children.Add(Metric(ev.KindText, when, string.Empty, small: true));
        }

        if (_vm.Events.Count > 4)
        {
            history.Children.Add(Metric("weitere", (_vm.Events.Count - 4).ToString("N0", CultureInfo.CurrentCulture),
                "im Auszug", small: true));
        }

        MetricsPanel.Children.Add(Section("Verbindungen zum PC"));
        MetricsPanel.Children.Add(Card(history));
    }

    /// <summary>Umschliesst Inhalte mit einer hellen Karte.</summary>
    private static UIElement Card(UIElement content) => new Border
    {
        Background = Res("SurfaceBrush"),
        BorderBrush = Res("LineBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(7),
        Padding = new Thickness(11, 9, 11, 4),
        Margin = new Thickness(0, 0, 0, 10),
        Child = content
    };

    /// <summary>
    /// Anteilswert als Balken.
    /// </summary>
    /// <remarks>
    /// Ein Ladezustand ist eine Groesse, die man abschaetzt und nicht abliest. Ein Balken sagt
    /// das auf einen Blick, eine Prozentzahl auf zwei Stellen taeuscht Genauigkeit vor, die
    /// dieser Wert nicht hat.
    /// </remarks>
    private static UIElement Bar(string label, double fraction, string accentKey)
    {
        double clamped = Math.Clamp(fraction, 0, 1);

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var caption = new TextBlock
        {
            Text = label,
            FontSize = 11.5,
            Foreground = Res("TextSecondaryBrush")
        };

        var value = new TextBlock
        {
            Text = (clamped * 100).ToString("N0", CultureInfo.CurrentCulture) + " %",
            FontSize = 11.5,
            Foreground = Res("TextPrimaryBrush")
        };

        Grid.SetColumn(value, 1);
        head.Children.Add(caption);
        head.Children.Add(value);

        var track = new Border
        {
            Height = 5,
            CornerRadius = new CornerRadius(3),
            Background = Res("LineBrush"),
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var fill = new Border
        {
            Height = 5,
            CornerRadius = new CornerRadius(3),
            Background = Res(accentKey),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * clamped;

        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
        stack.Children.Add(head);
        stack.Children.Add(new Grid { Children = { track, fill }, Margin = new Thickness(0, 4, 0, 0) });

        return stack;
    }


    private static string Num(double v, int digits) =>
        double.IsNaN(v) || double.IsInfinity(v) ? "-" : v.ToString("N" + digits);

    /// <summary>Dauer getrennt nach Zahl und Einheit, fuer die Kacheln der Uebersicht.</summary>
    private static (string Value, string Unit) SplitSeconds(double? seconds)
    {
        if (seconds is null || double.IsNaN(seconds.Value))
        {
            return ("-", string.Empty);
        }

        double s = seconds.Value;

        return s >= 60
            ? ($"{(int)(s / 60)}:{(int)(s % 60):00}", "min")
            : ($"{s:F0}", "s");
    }

    private static string FormatSeconds(double? seconds)
    {
        if (seconds is null || double.IsNaN(seconds.Value))
        {
            return "-";
        }

        double s = seconds.Value;
        return s >= 60 ? $"{(int)(s / 60)}:{(int)(s % 60):00} min" : $"{s:F0} s";
    }

    // ------------------------------------------------------------------ Kennzahlen-Bausteine

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>
    /// Grosse Kennzahl fuer die Kopfzeile der Uebersicht.
    /// </summary>
    /// <remarks>
    /// Drei Zahlen nebeneinander, die man aus zwei Metern Abstand lesen kann. Alles Weitere
    /// steht darunter. Wer eine Auswertung aufruft, will zuerst diese drei sehen.
    /// </remarks>
    private static UIElement HeroTile(string caption, string value, string unit, Brush accent)
    {
        var stack = new StackPanel();

        // Die Beschriftung darf umbrechen statt abzuschneiden. "GRÖSSTE SINK…" war keine
        // Beschriftung mehr, sondern ein Raten.
        stack.Children.Add(new TextBlock
        {
            Text = caption.ToUpperInvariant(),
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res("TextMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 11,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Height = 22,
            VerticalAlignment = VerticalAlignment.Top
        });

        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };

        // Bei mehr als vier Stellen wird die Zahl kleiner gesetzt, statt abgeschnitten zu werden.
        line.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = value.Length > 5 ? 17 : value.Length > 4 ? 19 : 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = accent,
            VerticalAlignment = VerticalAlignment.Bottom
        });

        if (!string.IsNullOrEmpty(unit))
        {
            line.Children.Add(new TextBlock
            {
                Text = unit,
                FontSize = 9.5,
                Margin = new Thickness(2, 0, 0, 3),
                Foreground = Res("TextMutedBrush"),
                VerticalAlignment = VerticalAlignment.Bottom
            });
        }

        stack.Children.Add(line);

        return new Border
        {
            Background = Res("SurfaceBrush"),
            BorderBrush = Res("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(9, 7, 7, 8),
            Child = stack
        };
    }

    /// <summary>Reihe aus gleich breiten Kennzahlen.</summary>
    private static UIElement TileRow(params UIElement[] tiles)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 7) };

        for (int i = 0; i < tiles.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (tiles[i] is FrameworkElement fe)
            {
                fe.Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0);
            }

            Grid.SetColumn(tiles[i], i);
            grid.Children.Add(tiles[i]);
        }

        return grid;
    }

    /// <summary>
    /// Kurzer Hinweis als Streifen mit farbiger Kante.
    /// </summary>
    /// <remarks>
    /// Ersetzt die frueheren Absaetze aus drei bis vier Saetzen. Ein Hinweis, den niemand liest,
    /// weil er zu lang ist, ist kein Hinweis.
    /// </remarks>
    private static UIElement Note(string text, string accentKey, string backgroundKey)
    {
        return new Border
        {
            Background = Res(backgroundKey),
            BorderBrush = Res(accentKey),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(9, 6, 9, 7),
            Margin = new Thickness(0, 0, 0, 6),
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11.5,
                Foreground = Res("TextPrimaryBrush")
            }
        };
    }

    private static UIElement Section(string title) => new TextBlock
    {
        Text = title.ToUpperInvariant(),
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = (Brush)Application.Current.Resources["TextMutedBrush"],
        Margin = new Thickness(0, 14, 0, 6)
    };

    private static UIElement Metric(string label, string value, string unit, bool emphasize = false, bool small = false)
    {
        // Die Beschriftung bekommt den Rest, der Wert so viel Platz wie er braucht. Umgekehrt
        // wurde bisher der Wert beschnitten - aus "1.003,1" wurde ",1".
        var grid = new Grid { Margin = new Thickness(0, 0, 0, small ? 3 : 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var caption = new TextBlock
        {
            Text = label,
            FontSize = 11.5,
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 10, 0)
        };

        var values = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        values.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = small ? 11.5 : emphasize ? 18 : 14,
            FontWeight = emphasize ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });

        if (!string.IsNullOrEmpty(unit))
        {
            values.Children.Add(new TextBlock
            {
                Text = unit,
                FontSize = 10.5,
                Margin = new Thickness(4, 0, 0, emphasize ? 2 : 0),
                Foreground = (Brush)Application.Current.Resources["TextMutedBrush"],
                VerticalAlignment = VerticalAlignment.Bottom
            });
        }

        Grid.SetColumn(caption, 0);
        Grid.SetColumn(values, 1);
        grid.Children.Add(caption);
        grid.Children.Add(values);
        return grid;
    }

    private static UIElement Hint(string text) => new Border
    {
        Background = (Brush)Application.Current.Resources["AccentSoftBrush"],
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(10, 8, 10, 8),
        Margin = new Thickness(0, 0, 0, 8),
        Child = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"]
        }
    };

    private static UIElement Warning(string text)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var icon = new AppIcon
        {
            Kind = IconKind.Warning,
            Width = 15,
            Height = 15,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 8, 0),
            Foreground = (Brush)Application.Current.Resources["WarningBrush"]
        };

        DockPanel.SetDock(icon, Dock.Left);
        panel.Children.Add(icon);
        panel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"]
        });

        return new Border
        {
            Background = (Brush)Application.Current.Resources["WarningSoftBrush"],
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Child = panel
        };
    }

    // ------------------------------------------------------------------ Menü

    private void SyncMenuChecks()
    {
        MenuMetric.IsChecked = _vm.Settings.UnitSystem == UnitSystem.Metric;
        MenuImperial.IsChecked = _vm.Settings.UnitSystem == UnitSystem.Imperial;
        MenuSpeedMs.IsChecked = _vm.Settings.SpeedUnit == SpeedUnit.MetersPerSecond;
        MenuSpeedKmh.IsChecked = _vm.Settings.SpeedUnit == SpeedUnit.KilometersPerHour;
        MenuSpeedMph.IsChecked = _vm.Settings.SpeedUnit == SpeedUnit.MilesPerHour;
        MenuSpeedFpm.IsChecked = _vm.Settings.SpeedUnit == SpeedUnit.FeetPerMinute;
        MenuProbeAll.IsChecked = _vm.Settings.ProbeAllSerialPorts;
    }

    private void OnUnitsMetric(object sender, RoutedEventArgs e)
    {
        _vm.UnitSystem = UnitSystem.Metric;
        SyncMenuChecks();
        RedrawPlot();
    }

    private void OnUnitsImperial(object sender, RoutedEventArgs e)
    {
        _vm.UnitSystem = UnitSystem.Imperial;
        SyncMenuChecks();
        RedrawPlot();
    }

    private void OnSpeedUnit(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse(tag, out SpeedUnit unit))
        {
            _vm.Settings.SpeedUnit = unit;
            _vm.Settings.Save();
            SyncMenuChecks();
            RedrawPlot();
        }
    }

    private void OnToggleProbeAll(object sender, RoutedEventArgs e)
    {
        _vm.Settings.ProbeAllSerialPorts = MenuProbeAll.IsChecked;
        _vm.Settings.Save();

        if (MenuProbeAll.IsChecked)
        {
            _vm.Notify(NoticeLevel.Warning,
                "Es werden jetzt alle seriellen Schnittstellen angesprochen. Andere Geräte am Rechner " +
                "können dadurch gestört werden. Diese Einstellung nur verwenden, wenn der Logger sonst " +
                "nicht gefunden wird.");
        }
    }

    private void OnEditReference(object sender, RoutedEventArgs e)
    {
        double? ground = _vm.SelectedRecording is not null && !double.IsNaN(_vm.SelectedRecording.Recording.EndPressureHpa)
            ? _vm.SelectedRecording.Recording.EndPressureHpa
            : null;

        double? start = _vm.SelectedRecording is not null && !double.IsNaN(_vm.SelectedRecording.Recording.StartPressureHpa)
            ? _vm.SelectedRecording.Recording.StartPressureHpa
            : null;

        var dlg = new AltitudeReferenceWindow(_vm.Settings, ground, start) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _vm.Settings.Save();
            _vm.Refresh();
            RedrawPlot();
            _vm.Notify(NoticeLevel.Info, "Höhenbezug geändert: " + _vm.Reference.Describe());
        }
    }

    /// <summary>Oeffnet die fortlaufende Anzeige der Momentanwerte.</summary>
    private void OnShowLive(object sender, RoutedEventArgs e) =>
        OpenDeviceWindow(port => new LiveWindow(
            port, _vm.SelectedDevice?.Title ?? "Logger", _vm.Reference, _vm.Settings,
            _vm.ReserveSelectedDevice("Momentanwerte werden gelesen …")));

    /// <summary>
    /// Oeffnet ein Fenster, das den Logger exklusiv benutzt.
    /// </summary>
    /// <remarks>
    /// Die Belegung des Anschlusses und die Pruefung, ob ueberhaupt ein Geraet bereitsteht,
    /// sind fuer alle solchen Fenster gleich und stehen deshalb nur einmal hier.
    /// </remarks>
    private void OpenDeviceWindow(Func<string, Window> build)
    {
        if (_vm.SelectedPort is null)
        {
            MessageBox.Show(this,
                "Es ist kein Logger ausgewählt.\n\n" +
                "Bitte das Gerät anschließen und in der Geräteliste auswählen.",
                "Gerät", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!_vm.CanUseDevice)
        {
            MessageBox.Show(this,
                "Am Logger läuft gerade ein anderer Vorgang.\n\nBitte warten, bis er abgeschlossen ist.",
                "Gerät", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Window window = build(_vm.SelectedPort);
        window.Owner = this;
        window.ShowDialog();
    }

    private void OnSetClock(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedPort is null)
        {
            MessageBox.Show(this,
                "Es ist kein Logger ausgewählt.\n\n" +
                "Bitte das Gerät anschließen und in der Geräteliste auswählen.",
                "Uhr des Loggers stellen", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!_vm.CanUseDevice)
        {
            MessageBox.Show(this,
                "Am Logger läuft gerade ein anderer Vorgang.\n\n" +
                "Bitte warten, bis er abgeschlossen ist.",
                "Uhr des Loggers stellen", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Der Anschluss bleibt fuer die gesamte Lebensdauer des Fensters belegt. Das Fenster gibt
        // die Belegung selbst frei, sobald es die Verbindung ordentlich beendet hat.
        IDisposable reservation = _vm.ReserveSelectedDevice("Uhr des Loggers …");

        try
        {
            var dlg = new ClockWindow(_vm.SelectedPort, _vm.SelectedDevice?.Title ?? "Logger", reservation)
            {
                Owner = this
            };

            dlg.ShowDialog();
        }
        catch (Exception)
        {
            reservation.Dispose();
            throw;
        }
    }

    private void OnHelp(object sender, RoutedEventArgs e) => OpenDoc("Bedienungsanleitung.pdf", "Bedienungsanleitung.txt");

    private void OnAccuracy(object sender, RoutedEventArgs e) => OpenDoc("Messgenauigkeit.pdf", "Messgenauigkeit.txt");

    private void OpenDoc(params string[] candidates)
    {
        string baseDir = Path.Combine(AppContext.BaseDirectory, "Dokumentation");
        foreach (string c in candidates)
        {
            string path = Path.Combine(baseDir, c);
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }
        }

        _vm.Notify(NoticeLevel.Warning, "Das Dokument wurde nicht gefunden: " + string.Join(", ", candidates));
    }

    private void OnOpenRawFolder(object sender, RoutedEventArgs e) => OpenFolder(AppInfo.RawArchiveDirectory);

    private void OnOpenLogFolder(object sender, RoutedEventArgs e) => OpenFolder(AppInfo.LogDirectory);

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _vm.Notify(NoticeLevel.Error, "Der Ordner konnte nicht geöffnet werden: " + ex.Message);
        }
    }

    private void OnAbout(object sender, RoutedEventArgs e) =>
        new AboutWindow { Owner = this }.ShowDialog();

    private void OnExit(object sender, RoutedEventArgs e) => Close();
}
