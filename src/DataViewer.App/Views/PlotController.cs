using System.Globalization;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using Siemert.DataViewer.App.Services;
using Siemert.DataViewer.Core.Analysis;
using Siemert.DataViewer.Core.Model;
using Siemert.DataViewer.Core.Protocol;
using Siemert.DataViewer.Core.Units;

namespace Siemert.DataViewer.App.Views;

/// <summary>
/// Zeichnet eine Aufnahme und stellt die Ablesewerkzeuge bereit.
/// </summary>
/// <remarks>
/// Die Werkzeuge (Messbereich, Fadenkreuz, Marker) sind hier selbst umgesetzt und benutzen nur
/// die öffentliche Schnittstelle der Diagrammbibliothek. Fassung 1 hatte dafür über Reflexion in
/// ein privates Feld der Bibliothek geschrieben; eine Umbenennung dort hätte sämtliche Werkzeuge
/// stillschweigend außer Betrieb gesetzt, ohne Fehlermeldung.
/// </remarks>
public sealed class PlotController(WpfPlot host)
{
    private static readonly Color AltitudeColor = Color.FromHex("#0B0B0B");
    private static readonly Color SpeedColor = Color.FromHex("#0072B2");
    private static readonly Color TemperatureColor = Color.FromHex("#D55E00");
    private static readonly Color AccColor = Color.FromHex("#CC79A7");
    private static readonly Color AccXColor = Color.FromHex("#56B4E9");
    private static readonly Color AccYColor = Color.FromHex("#009E73");
    private static readonly Color AccZColor = Color.FromHex("#E69F00");
    private static readonly Color PhaseColor = Color.FromHex("#6B7785");
    private static readonly Color ToolColor = Color.FromHex("#B3261E");
    private static readonly Color MarkerColor = Color.FromHex("#1B7F4B");

    private readonly WpfPlot _host = host;

    private Recording? _recording;
    private AltitudeReference _reference = AltitudeReference.Standard;
    private AppSettings? _settings;
    private double[] _time = [];
    private double[] _verticalSpeedMs = [];
    private readonly List<SeriesData> _series = [];
    /// <summary>Gezeichnete Kurven je Art, damit sie sich ohne Neuaufbau umschalten lassen.</summary>
    private readonly Dictionary<SeriesKind, Scatter> _plotted = [];

    private IYAxis? _speedAxis;
    private IYAxis? _accAxis;
    private IYAxis? _tempAxis;

    // Werkzeug-Elemente
    private VerticalLine? _crosshairV;
    private HorizontalLine? _crosshairH;
    private VerticalLine? _measure1;
    private VerticalLine? _measure2;
    /// <summary>Ein gesetzter Marker samt der Kurve, auf der er sitzt.</summary>
    private sealed record PlacedMarker(
        Marker Dot,
        ScottPlot.Plottables.Text Label,
        SeriesKind Kind,
        double X,
        double Y);

    private readonly List<PlacedMarker> _markers = [];
    private List<ScottPlot.Interactivity.IUserActionResponse>? _removedPan;

    private double? _measureStart;
    private bool _dragging;

    /// <summary>Welche Kante des Messbereichs gerade gezogen wird (1 oder 2), sonst 0.</summary>
    private int _draggingEdge;

    /// <summary>Je sichtbarer Kurve ein Punkt unter dem Mauszeiger.</summary>
    private readonly List<Marker> _hoverDots = [];
    private ScottPlot.Plottables.Text? _hoverLabel;

    /// <summary>Fangbereich in Pixeln, in dem eine Messkante mit der Maus gegriffen wird.</summary>
    private const double EdgeGrabPixels = 8.0;

    /// <summary>Fangbereich in Pixeln, in dem ein gesetzter Marker wieder getroffen wird.</summary>
    private const double MarkerGrabPixels = 12.0;

    /// <summary>
    /// Ab diesem Abstand zur naechsten Kurve verschwindet die Werteanzeige.
    /// </summary>
    /// <remarks>
    /// Ohne Grenze klebte die Anzeige auch dann an einer Kurve, wenn der Zeiger weit entfernt
    /// im leeren Feld stand. Sie hat dann nichts angezeigt, was der Anwender wissen wollte.
    /// </remarks>
    private const double HoverReachPixels = 40.0;

    /// <summary>Letzte Zeigerposition beim Verschieben mit der mittleren Maustaste.</summary>
    private Pixel? _panAnchor;

    /// <summary>
    /// Wahr, sobald der Anwender den Ausschnitt selbst eingestellt hat.
    /// </summary>
    /// <remarks>
    /// Waehrend einer Live-Aufzeichnung wird sonst bei jedem Takt neu skaliert. Wer sich einen
    /// Bereich herangezogen hat, verliert ihn dann im Sekundentakt wieder. Ab der ersten
    /// eigenen Verstellung bleibt der Ausschnitt deshalb stehen, bis "Ansicht" gedrueckt wird.
    /// </remarks>
    public bool ViewAdjustedByUser { get; private set; }

    /// <summary>
    /// Das Geraet, von dem die gezeigte Aufnahme stammt.
    /// </summary>
    /// <remarks>
    /// Nur fuer die Beschriftung. Ohne Geraet und Seriennummer ueber dem Diagramm laesst sich
    /// ein ausgedrucktes oder weitergereichtes Bild spaeter nicht mehr zuordnen.
    /// </remarks>
    public DeviceInfo? Device { get; set; }

    public PlotTool Tool { get; private set; } = PlotTool.None;

    public bool HasData => _recording is not null && _time.Length > 0;

    public IReadOnlyList<SeriesData> Series => _series;

    /// <summary>Meldet die Ablesewerte des Fadenkreuzes.</summary>
    public event EventHandler<CrosshairReadout?>? CrosshairChanged;

    /// <summary>Meldet das Ergebnis des Messbereichs.</summary>
    public event EventHandler<MeasureResult?>? MeasureChanged;

    // ================================================================= Zeichnen

    public void ShowEmpty()
    {
        _recording = null;
        _series.Clear();
        _time = [];
        ClearTools();

        Plot p = _host.Plot;
        p.Clear();
        p.Axes.Remove(Edge.Right);
        p.Title(string.Empty);
        p.Axes.AutoScale();
        _host.Refresh();
    }

    /// <summary>
    /// Zeichnet eine Aufnahme.
    /// </summary>
    /// <param name="keepView">
    /// Wahr, wenn ein von Hand eingestellter Ausschnitt erhalten bleiben soll. Waehrend einer
    /// Live-Aufzeichnung wird im Sekundentakt neu gezeichnet; ohne diese Ruecksicht waere jede
    /// eigene Einstellung nach einer Sekunde wieder weg.
    /// </param>
    public void Render(Recording recording, AltitudeReference reference, JumpMetrics metrics,
        AppSettings settings, bool keepView = false)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(settings);

        AxisLimits[]? previous = keepView && ViewAdjustedByUser ? CaptureLimits() : null;

        _recording = recording;
        _reference = reference;
        _settings = settings;

        Plot p = _host.Plot;
        p.Clear();
        p.Axes.Remove(Edge.Right);
        ClearToolReferences();

        BuildSeries(recording, reference, settings);

        if (_time.Length == 0)
        {
            _host.Refresh();
            return;
        }

        string altUnit = UnitConverter.AltitudeUnitLabel(settings.UnitSystem);
        string speedUnit = UnitConverter.SpeedUnitLabel(settings.SpeedUnit);

        p.Axes.Left.Label.Text = $"{reference.ShortLabel} [{altUnit}]";

        // Die Zeitachse zeigt die Uhrzeit, nicht Sekunden seit Aufnahmebeginn. Gerechnet wird
        // intern weiter in Sekunden - nur die Beschriftung wird umgesetzt.
        p.Axes.Bottom.Label.Text = recording.ClockWasSet ? "Uhrzeit" : "Uhrzeit (Uhr nicht gestellt)";
        p.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
        {
            LabelFormatter = seconds => ClockLabel(seconds)
        };

        // Achsen nur fuer Kurven, die auch gezeichnet werden. Eine Skala fuer eine ausgeblendete
        // Kurve ist schlimmer als keine: Sie legt nahe, dass die Kurve zu sehen waere.
        _speedAxis = p.Axes.AddRightAxis();
        _speedAxis.Label.Text = $"Sinkrate [{speedUnit}]";

        _accAxis = p.Axes.AddRightAxis();
        _accAxis.Label.Text = "Beschleunigung [g]";

        _tempAxis = p.Axes.AddRightAxis();
        _tempAxis.Label.Text = $"Temperatur [{UnitConverter.TemperatureUnitLabel(settings.UnitSystem)}]";

        // Es werden alle Kurven angelegt, auch die ausgeblendeten. Nur so laesst sich spaeter
        // eine Kurve zuschalten, ohne das Diagramm neu aufzubauen - und ohne dabei Zoom,
        // Ausschnitt, Messbereich und Marker zu verlieren.
        _plotted.Clear();

        foreach (SeriesData s in _series)
        {
            Scatter sc = p.Add.Scatter(_time, s.Values);
            sc.MarkerStyle.IsVisible = false;
            sc.IsVisible = s.Visible;
            sc.LegendText = s.Visible ? BuildLegend(s, settings) : string.Empty;
            _plotted[s.Kind] = sc;

            SeriesStyle style = settings.StyleFor(s.Kind);
            sc.Color = ParseColor(style.Color, ColorFor(s.Kind));
            sc.LineWidth = (float)style.Width;
            sc.LinePattern = ParsePattern(style.Pattern);

            sc.Axes.YAxis = s.Kind switch
            {
                SeriesKind.Altitude => sc.Axes.YAxis,
                SeriesKind.Speed => _speedAxis ?? sc.Axes.YAxis,
                SeriesKind.Temperature => _tempAxis ?? sc.Axes.YAxis,
                _ => _accAxis ?? sc.Axes.YAxis
            };
        }

        if (metrics.JumpDetected)
        {
            AddPhaseMarker(p, metrics.ExitTimeSeconds, "Absprung");
            AddPhaseMarker(p, metrics.DeploymentTimeSeconds, "Öffnung");
        }

        AddSaturationMarkers(p, recording);

        p.Title(RecordingLabel.Full(recording, Device) + "   ·   " + reference.Describe());

        // Waehrend einer laufenden Aufzeichnung wird der eingestellte Ausschnitt gehalten.
        if (keepView && ViewAdjustedByUser && previous is not null)
        {
            RestoreLimits(p, previous);
        }
        else
        {
            p.Axes.AutoScale();
        }

        if (settings.ShowLegend)
        {
            p.ShowLegend(Alignment.UpperRight);
            p.Legend.FontSize = 12;
        }
        else
        {
            p.HideLegend();
        }

        UpdateAxisVisibility();

        ApplyChrome(p, settings);

        // Muss nach ApplyChrome stehen: Dieses faerbt mit p.Axes.Color(...) saemtliche Achsen
        // einheitlich um und hat die Zuordnung Kurve/Skala bisher wieder zunichtegemacht.
        ColorAxis(p.Axes.Left, SeriesKind.Altitude, settings);
        ColorAxis(_speedAxis, SeriesKind.Speed, settings);
        ColorAxis(_accAxis, AccAxisKind(), settings);
        ColorAxis(_tempAxis, SeriesKind.Temperature, settings);

        RestoreTools();
        _host.Refresh();
    }

    /// <summary>
    /// Faerbt Beschriftung, Teilstrichwerte und Achsenlinie in der Farbe der zugehoerigen Kurve.
    /// </summary>
    public static void ColorAxis(IAxis? axis, SeriesKind kind, AppSettings settings)
    {
        if (axis is null || !settings.AxisLabelsFollowSeries)
        {
            return;
        }

        Color c = ParseColor(settings.StyleFor(kind).Color, ColorFor(kind));

        axis.Label.ForeColor = c;
        axis.Label.Bold = true;
        axis.TickLabelStyle.ForeColor = c;
        axis.MajorTickStyle.Color = c;
        axis.MinorTickStyle.Color = c;
        axis.FrameLineStyle.Color = c;
    }

    /// <summary>
    /// Welche Beschleunigungskurve die gemeinsame Achse faerbt.
    /// </summary>
    /// <remarks>
    /// Sind mehrere Achsenkurven sichtbar, gibt es keine eindeutige Farbe. Dann bleibt die
    /// Achse neutral, statt eine der Kurven willkuerlich zu bevorzugen.
    /// </remarks>
    /// <summary>
    /// Blendet eine Kurve ein oder aus, ohne das Diagramm neu aufzubauen.
    /// </summary>
    /// <remarks>
    /// Ein Neuaufbau wuerde Zoom, Ausschnitt, gesetzte Marker und einen laufenden Messbereich
    /// verwerfen. Beim blossen Umschalten einer Kurve ist das nicht hinnehmbar.
    /// </remarks>
    public void SetSeriesVisible(AppSettings settings)
    {
        if (_settings is null || _recording is null)
        {
            return;
        }

        _settings = settings;

        for (int i = 0; i < _series.Count; i++)
        {
            SeriesData old = _series[i];
            bool visible = VisibleBySetting(old.Kind, settings);

            if (visible == old.Visible)
            {
                continue;
            }

            _series[i] = old with { Visible = visible };

            if (_plotted.TryGetValue(old.Kind, out Scatter? sc))
            {
                sc.IsVisible = visible;
                sc.LegendText = visible ? BuildLegend(_series[i], settings) : string.Empty;
            }
        }

        UpdateAxisVisibility();
        _host.Refresh();
    }

    private static bool VisibleBySetting(SeriesKind kind, AppSettings s) => kind switch
    {
        SeriesKind.Altitude => s.ShowAltitude,
        SeriesKind.Speed => s.ShowSpeed,
        SeriesKind.Temperature => s.ShowTemperature,
        SeriesKind.AccMagnitude => s.ShowAcceleration,
        SeriesKind.AccX => s.ShowAccX,
        SeriesKind.AccY => s.ShowAccY,
        SeriesKind.AccZ => s.ShowAccZ,
        _ => false
    };

    /// <summary>Zeigt nur die Achsen, zu denen auch eine Kurve sichtbar ist.</summary>
    private void UpdateAxisVisibility()
    {
        if (_speedAxis is not null)
        {
            _speedAxis.IsVisible = IsVisible(SeriesKind.Speed);
        }

        if (_accAxis is not null)
        {
            _accAxis.IsVisible = IsVisible(SeriesKind.AccMagnitude) || IsVisible(SeriesKind.AccX) ||
                                 IsVisible(SeriesKind.AccY) || IsVisible(SeriesKind.AccZ);
        }

        if (_tempAxis is not null)
        {
            _tempAxis.IsVisible = IsVisible(SeriesKind.Temperature);
        }
    }

    private SeriesKind AccAxisKind()
    {
        SeriesKind[] kinds = [SeriesKind.AccMagnitude, SeriesKind.AccX, SeriesKind.AccY, SeriesKind.AccZ];
        SeriesKind[] shown = [.. kinds.Where(IsVisible)];
        return shown.Length == 1 ? shown[0] : SeriesKind.AccMagnitude;
    }

    /// <summary>Setzt Sekunden seit Aufnahmebeginn in die Uhrzeit des Geraets um.</summary>
    public string TimeOfDay(double seconds) => ClockLabel(seconds);

    private string ClockLabel(double seconds)
    {
        if (_recording is null)
        {
            return seconds.ToString("0", CultureInfo.CurrentCulture);
        }

        TimeSpan t = _recording.StartTimeOfDay + TimeSpan.FromSeconds(seconds);

        // Ueber Mitternacht hinaus bleibt die Anzeige innerhalb des Tages.
        while (t < TimeSpan.Zero)
        {
            t += TimeSpan.FromDays(1);
        }

        return (t - TimeSpan.FromDays(t.Days)).ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture);
    }

    private static string BuildLegend(SeriesData s, AppSettings settings)
    {
        double window = settings.SmoothingFor(s.Kind);
        bool smoothed = window > 0;
        string text = $"{s.Label} [{s.Unit}]";
        return smoothed
            ? $"{text} · geglättet {window.ToString("0.#", CultureInfo.CurrentCulture)} s"
            : text;
    }

    private void BuildSeries(Recording recording, AltitudeReference reference, AppSettings settings)
    {
        _series.Clear();

        IReadOnlyList<Sample> samples = recording.Samples;
        int n = samples.Count;
        _time = new double[n];

        var altitudeM = new double[n];
        var altitude = new double[n];
        var temperature = new double[n];
        var accMag = new double[n];
        var accX = new double[n];
        var accY = new double[n];
        var accZ = new double[n];

        for (int i = 0; i < n; i++)
        {
            Sample s = samples[i];
            _time[i] = s.TimeSeconds;
            altitudeM[i] = reference.ToAltitudeMeters(s.PressureHpa, s.TemperatureC);
            altitude[i] = UnitConverter.Altitude(altitudeM[i], settings.UnitSystem);
            temperature[i] = UnitConverter.Temperature(s.TemperatureC, settings.UnitSystem);
            accMag[i] = s.AccMagnitude;
            accX[i] = s.AccX;
            accY[i] = s.AccY;
            accZ[i] = s.AccZ;
        }

        // Die Sinkrate wird immer aus der metrischen Höhe abgeleitet und erst danach umgerechnet,
        // damit das Ergebnis nicht von der eingestellten Einheit abhängt.
        _verticalSpeedMs = Core.Analysis.Signal.Derivative(
            altitudeM, recording.SampleIntervalSeconds, JumpAnalyzer.DerivativeWindow);

        var speed = new double[n];
        for (int i = 0; i < n; i++)
        {
            speed[i] = UnitConverter.Speed(_verticalSpeedMs[i], settings.SpeedUnit);
        }

        string altUnit = UnitConverter.AltitudeUnitLabel(settings.UnitSystem);
        string tempUnit = UnitConverter.TemperatureUnitLabel(settings.UnitSystem);
        string speedUnit = UnitConverter.SpeedUnitLabel(settings.SpeedUnit);

        Add(SeriesKind.Altitude, "Höhe", altUnit, altitude, settings.ShowAltitude, 1);
        Add(SeriesKind.Speed, "Sinkrate", speedUnit, speed, settings.ShowSpeed, 1);
        Add(SeriesKind.Temperature, "Temperatur", tempUnit, temperature, settings.ShowTemperature, 1);
        Add(SeriesKind.AccMagnitude, "Beschleunigung", "g", accMag, settings.ShowAcceleration, 2);
        Add(SeriesKind.AccX, "a X", "g", accX, settings.ShowAccX, 2);
        Add(SeriesKind.AccY, "a Y", "g", accY, settings.ShowAccY, 2);
        Add(SeriesKind.AccZ, "a Z", "g", accZ, settings.ShowAccZ, 2);

        void Add(SeriesKind kind, string label, string unit, double[] raw, bool visible, int digits)
        {
            // Geglättet wird nur die Darstellung. Die Statistik des Messcursors rechnet immer auf
            // den Rohwerten, damit ein abgelesenes Maximum nicht durch die Glättung gedämpft ist.
            double window = settings.SmoothingFor(kind);
            double[] shown = window > 0
                ? Core.Analysis.Smoothing.Apply(raw, recording.SampleIntervalSeconds, window, settings.Smoothing)
                : raw;

            _series.Add(new SeriesData
            {
                Kind = kind,
                Label = label,
                Unit = unit,
                Values = shown,
                RawValues = raw,
                Visible = visible,
                Digits = digits
            });
        }
    }

    private bool IsVisible(SeriesKind kind) => _series.Any(s => s.Kind == kind && s.Visible);

    private static void AddPhaseMarker(Plot p, double? seconds, string label)
    {
        if (seconds is null)
        {
            return;
        }

        VerticalLine line = p.Add.VerticalLine(seconds.Value);
        line.Color = PhaseColor;
        line.LineWidth = 1.4f;
        line.LinePattern = LinePattern.Dotted;
        line.Text = label;
        line.LabelOppositeAxis = true;
    }

    private void AddSaturationMarkers(Plot p, Recording recording)
    {
        if (!recording.HasSaturatedSamples || _accAxis is null || !IsVisible(SeriesKind.AccMagnitude))
        {
            return;
        }

        var xs = new List<double>();
        var ys = new List<double>();
        for (int i = 0; i < recording.Samples.Count; i++)
        {
            if (recording.Samples[i].AccSaturated)
            {
                xs.Add(_time[i]);
                ys.Add(recording.Samples[i].AccMagnitude);
            }
        }

        if (xs.Count == 0)
        {
            return;
        }

        Scatter sat = p.Add.Scatter(xs.ToArray(), ys.ToArray());
        sat.LineWidth = 0;
        sat.MarkerStyle.Shape = MarkerShape.OpenCircle;
        sat.MarkerStyle.Size = 8;
        sat.MarkerStyle.LineColor = ToolColor;
        sat.MarkerStyle.LineWidth = 2;
        sat.LegendText = "Sensor am Anschlag";
        sat.Axes.YAxis = _accAxis;
    }

    /// <summary>Setzt Hintergrund, Gitter und Achsenfarben nach dem gewaehlten Diagrammstil.</summary>
    /// <summary>
    /// Faerbt Hintergrund, Gitter, Achsen und Legende nach der gewaehlten Darstellung.
    /// </summary>
    /// <remarks>
    /// Oeffentlich, damit das Fenster mit den Momentanwerten dieselbe Stilgebung benutzt. Zwei
    /// Diagramme im selben Programm, die unterschiedlich aussehen, wirken wie ein Versehen.
    /// </remarks>
    public static void ApplyChrome(Plot p, AppSettings settings)
    {
        (string figure, string data, string grid, string axis, string title) = settings.ChartStyle switch
        {
            ChartStyle.Dark => ("#1E2733", "#232D3A", "#33404F", "#B8C4D0", "#F2F5F8"),
            ChartStyle.Slate => ("#F4F6F8", "#FFFFFF", "#DBE2E9", "#3A4653", "#16202B"),
            _ => ("#FFFFFF", "#FCFDFE", "#E3E8ED", "#3A4653", "#16202B")
        };

        p.FigureBackground.Color = Color.FromHex(figure);
        p.DataBackground.Color = Color.FromHex(data);
        p.Grid.MajorLineColor = Color.FromHex(settings.ShowGrid ? grid : data);
        p.Grid.IsVisible = settings.ShowGrid;
        p.Axes.Color(Color.FromHex(axis));
        p.Axes.Title.Label.FontSize = 14;
        p.Axes.Title.Label.ForeColor = Color.FromHex(title);

        if (settings.ChartStyle == ChartStyle.Dark)
        {
            p.Legend.BackgroundColor = Color.FromHex("#27313F");
            p.Legend.FontColor = Color.FromHex("#F2F5F8");
            p.Legend.OutlineColor = Color.FromHex("#3A4653");
        }
    }

    public static Color ParseColor(string hex, Color fallback)
    {
        try
        {
            return Color.FromHex(hex);
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    public static LinePattern ParsePattern(string name) => name switch
    {
        "Dashed" => LinePattern.Dashed,
        "DenselyDashed" => LinePattern.DenselyDashed,
        "Dotted" => LinePattern.Dotted,
        _ => LinePattern.Solid
    };

    // ================================================================= Werkzeuge

    public void SetTool(PlotTool tool)
    {
        if (Tool == tool)
        {
            return;
        }

        Tool = tool;
        _host.Cursor = System.Windows.Input.Cursors.Arrow;

        if (tool != PlotTool.Crosshair)
        {
            RemoveCrosshair();
            CrosshairChanged?.Invoke(this, null);
        }

        if (tool != PlotTool.Measure)
        {
            RemoveMeasure();
            MeasureChanged?.Invoke(this, null);
        }

        // Beim Messen wird mit gedrückter Maustaste der Bereich aufgezogen. Das Verschieben des
        // Ausschnitts würde damit kollidieren und wird währenddessen abgeschaltet; Zoomen über
        // das Mausrad bleibt möglich.
        SetPanEnabled(tool != PlotTool.Measure);

        _host.Refresh();
    }

    private void SetPanEnabled(bool enabled)
    {
        var responses = _host.UserInputProcessor.UserActionResponses;

        if (!enabled)
        {
            if (_removedPan is null)
            {
                _removedPan = responses.Where(r => r.GetType().Name.Contains("Pan", StringComparison.Ordinal)).ToList();
                foreach (var r in _removedPan)
                {
                    responses.Remove(r);
                }
            }
        }
        else if (_removedPan is not null)
        {
            foreach (var r in _removedPan)
            {
                responses.Add(r);
            }

            _removedPan = null;
        }
    }

    public void ClearMarkers()
    {
        foreach (PlacedMarker m in _markers)
        {
            _host.Plot.Remove(m.Dot);
            _host.Plot.Remove(m.Label);
        }

        _markers.Clear();
        _host.Refresh();
    }

    /// <summary>Anzahl der gesetzten Marker.</summary>
    public int MarkerCount => _markers.Count;

    /// <summary>Die Y-Achse, auf der eine Kurve gezeichnet wird.</summary>
    private IYAxis AxisFor(SeriesKind kind) => kind switch
    {
        SeriesKind.Speed => _speedAxis ?? _host.Plot.Axes.Left,
        SeriesKind.Temperature => _tempAxis ?? _host.Plot.Axes.Left,
        SeriesKind.Altitude => _host.Plot.Axes.Left,
        _ => _accAxis ?? _host.Plot.Axes.Left
    };

    /// <summary>Bildschirmposition eines Datenpunkts auf der Achse seiner Kurve.</summary>
    private Pixel PixelOf(double x, double y, SeriesKind kind) =>
        _host.Plot.GetPixel(new Coordinates(x, y), _host.Plot.Axes.Bottom, AxisFor(kind));

    /// <summary>
    /// Sucht die sichtbare Kurve, die dem Mauszeiger am naechsten liegt.
    /// </summary>
    /// <remarks>
    /// Verglichen wird in Pixeln, nicht in Datenwerten - anders ginge es nicht, weil jede Kurve
    /// ihre eigene Skala hat und ein Abstand von "5" auf der Hoehenachse etwas voellig anderes
    /// bedeutet als auf der Beschleunigungsachse.
    /// </remarks>
    private SeriesData? SeriesUnderCursor(Pixel mouse, int index)
    {
        SeriesData? best = null;
        double bestDistance = double.MaxValue;

        foreach (SeriesData sd in _series.Where(sd => sd.Visible))
        {
            double y = sd.Values[index];
            if (double.IsNaN(y))
            {
                continue;
            }

            Pixel px = PixelOf(_time[index], y, sd.Kind);
            double distance = Math.Abs(px.Y - mouse.Y);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = sd;
            }
        }

        return best;
    }

    public void ResetView()
    {
        ViewAdjustedByUser = false;
        _host.Plot.Axes.AutoScale();
        _host.Refresh();
    }

    /// <summary>Vermerkt, dass der Ausschnitt von Hand veraendert wurde.</summary>
    public void NoteViewAdjusted() => ViewAdjustedByUser = true;

    /// <summary>Sichert die Grenzen aller Achsen vor einem Neuaufbau.</summary>
    private AxisLimits[] CaptureLimits()
    {
        Plot p = _host.Plot;
        var limits = new List<AxisLimits>
        {
            new(p.Axes.Bottom.Min, p.Axes.Bottom.Max, p.Axes.Left.Min, p.Axes.Left.Max)
        };

        foreach (IYAxis axis in p.Axes.GetYAxes())
        {
            limits.Add(new AxisLimits(0, 0, axis.Min, axis.Max));
        }

        return [.. limits];
    }

    /// <summary>
    /// Stellt gesicherte Achsengrenzen wieder her.
    /// </summary>
    /// <remarks>
    /// Die Zeitachse waechst waehrend einer Aufzeichnung mit: Ihr rechter Rand folgt dem
    /// neuesten Messpunkt, die Breite des Fensters bleibt. Sonst liefe die Kurve aus dem Bild.
    /// </remarks>
    private void RestoreLimits(Plot p, AxisLimits[] saved)
    {
        if (saved.Length == 0)
        {
            return;
        }

        double span = saved[0].Right - saved[0].Left;
        double newest = _time.Length > 0 ? _time[^1] : saved[0].Right;

        if (span > 0 && newest > saved[0].Right)
        {
            p.Axes.Bottom.Min = newest - span;
            p.Axes.Bottom.Max = newest;
        }
        else
        {
            p.Axes.Bottom.Min = saved[0].Left;
            p.Axes.Bottom.Max = saved[0].Right;
        }

        p.Axes.Left.Min = saved[0].Bottom;
        p.Axes.Left.Max = saved[0].Top;

        int i = 1;
        foreach (IYAxis axis in p.Axes.GetYAxes())
        {
            if (i >= saved.Length)
            {
                break;
            }

            if (!ReferenceEquals(axis, p.Axes.Left))
            {
                axis.Min = saved[i].Bottom;
                axis.Max = saved[i].Top;
            }

            i++;
        }
    }

    /// <summary>
    /// Setzt feste Grenzen einer Achse. <c>null</c> bedeutet automatisch.
    /// </summary>
    /// <remarks>
    /// Die frueher hier ausgelieferte Fassung hatte Eingabefelder fuer Hoehe, Sinkrate und
    /// Beschleunigung, wertete aber nur die Hoehe aus - die beiden anderen Felder taten nichts.
    /// </remarks>
    public void SetAxisLimits(SeriesKind axis, double? min, double? max)
    {
        IYAxis? target = axis switch
        {
            SeriesKind.Altitude => _host.Plot.Axes.Left,
            SeriesKind.Speed => _speedAxis,
            SeriesKind.Temperature => _tempAxis,
            _ => _accAxis
        };

        if (target is null)
        {
            return;
        }

        if (min.HasValue && max.HasValue && max.Value > min.Value)
        {
            target.Min = min.Value;
            target.Max = max.Value;
            ViewAdjustedByUser = true;
        }
        else
        {
            // Automatik fuer genau diese Achse wiederherstellen.
            _host.Plot.Axes.AutoScale();
        }

        _host.Refresh();
    }

    /// <summary>True, wenn die gewaehlte Achse im Diagramm ueberhaupt vorhanden ist.</summary>
    public bool HasAxis(SeriesKind axis) => axis switch
    {
        SeriesKind.Altitude => true,
        SeriesKind.Speed => _speedAxis is not null,
        SeriesKind.Temperature => _tempAxis is not null,
        _ => _accAxis is not null
    };

    private void ClearTools()
    {
        _markers.Clear();
        ClearToolReferences();
        _measureStart = null;
    }

    private void ClearToolReferences()
    {
        _crosshairV = null;
        _crosshairH = null;
        _measure1 = null;
        _measure2 = null;
        _hoverDots.Clear();
        _hoverLabel = null;
        _draggingEdge = 0;
    }

    /// <summary>Stellt Werkzeug-Elemente nach einem Neuzeichnen wieder her.</summary>
    private void RestoreTools()
    {
        _markers.Clear();
        ClearToolReferences();
    }

    private void RemoveCrosshair()
    {
        if (_crosshairV is not null)
        {
            _host.Plot.Remove(_crosshairV);
            _crosshairV = null;
        }

        if (_crosshairH is not null)
        {
            _host.Plot.Remove(_crosshairH);
            _crosshairH = null;
        }
    }

    private void RemoveMeasureLines()
    {
        if (_measure1 is not null)
        {
            _host.Plot.Remove(_measure1);
            _measure1 = null;
        }

        if (_measure2 is not null)
        {
            _host.Plot.Remove(_measure2);
            _measure2 = null;
        }
    }

    private void RemoveMeasure()
    {
        if (_measure1 is not null)
        {
            _host.Plot.Remove(_measure1);
            _measure1 = null;
        }

        if (_measure2 is not null)
        {
            _host.Plot.Remove(_measure2);
            _measure2 = null;
        }

        _measureStart = null;
        _dragging = false;
    }

    // ================================================================= Maus

    /// <summary>Behandelt einen Mausklick im Diagramm. Gibt true zurueck, wenn das Ereignis verbraucht wurde.</summary>
    public bool OnMouseDown(System.Windows.Input.MouseEventArgs e)
    {
        if (!HasData || Tool == PlotTool.None)
        {
            return false;
        }

        double x = ToTime(e);

        switch (Tool)
        {
            case PlotTool.Measure:
                // Liegt der Zeiger nahe an einer vorhandenen Kante, wird diese nachgezogen,
                // statt den Bereich neu aufzuspannen.
                int edge = EdgeUnderCursor(e);
                if (edge != 0)
                {
                    _draggingEdge = edge;
                    _dragging = true;
                    _host.Cursor = System.Windows.Input.Cursors.SizeWE;
                    _host.CaptureMouse();
                    return true;
                }

                RemoveMeasureLines();
                _measureStart = x;
                _dragging = true;
                _draggingEdge = 2;
                _measure1 = AddToolLine(x, "1");
                _measure2 = AddToolLine(x, "2");
                _host.CaptureMouse();
                _host.Refresh();
                return true;

            case PlotTool.Marker:
                ToggleMarker(e);
                return true;
        }

        return false;
    }

    /// <summary>
    /// Beginnt das Verschieben mit der mittleren Maustaste.
    /// </summary>
    /// <remarks>
    /// Das Werkzeug belegt die linke Taste - beim Messen zog ein Ziehen im Diagramm deshalb
    /// einen neuen Messbereich auf, statt den Ausschnitt zu verschieben. Die mittlere Taste
    /// kollidiert mit keinem Werkzeug und verschiebt deshalb immer, gleich welches gerade
    /// aktiv ist.
    /// </remarks>
    public bool OnPanStart(System.Windows.Input.MouseEventArgs e)
    {
        if (!HasData)
        {
            return false;
        }

        _panAnchor = _host.GetPlotPixelPosition(e);
        _host.Cursor = System.Windows.Input.Cursors.ScrollAll;
        _host.CaptureMouse();
        return true;
    }

    public bool OnPanEnd()
    {
        if (_panAnchor is null)
        {
            return false;
        }

        _panAnchor = null;
        _host.ReleaseMouseCapture();
        _host.Cursor = System.Windows.Input.Cursors.Arrow;
        return true;
    }

    public bool OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (!HasData)
        {
            return false;
        }

        // Verschieben hat Vorrang vor jedem Werkzeug.
        if (_panAnchor is { } anchor)
        {
            Pixel now = _host.GetPlotPixelPosition(e);
            PanBy(anchor.X - now.X, anchor.Y - now.Y);
            ViewAdjustedByUser = true;
            _panAnchor = now;
            _host.Refresh();
            return true;
        }

        if (Tool == PlotTool.Crosshair)
        {
            UpdateCrosshair(e);
            return false;
        }

        // Der Zeiger muss zeigen, dass hier geschoben werden kann.
        if (Tool == PlotTool.Measure && !_dragging)
        {
            _host.Cursor = EdgeUnderCursor(e) != 0
                ? System.Windows.Input.Cursors.SizeWE
                : System.Windows.Input.Cursors.Arrow;
        }

        if (Tool == PlotTool.Measure && _dragging)
        {
            VerticalLine? dragged = _draggingEdge == 1 ? _measure1 : _measure2;
            if (dragged is not null)
            {
                dragged.X = ToTime(e);
                _host.Refresh();
                ReportMeasure();
            }

            return true;
        }

        // Ohne aktives Werkzeug: naechstgelegenen Messpunkt anzeigen.
        if (Tool == PlotTool.None)
        {
            UpdateHover(e);
        }

        return false;
    }

    public bool OnMouseUp(System.Windows.Input.MouseEventArgs e)
    {
        if (Tool != PlotTool.Measure || !_dragging)
        {
            return false;
        }

        _dragging = false;
        _host.ReleaseMouseCapture();
        _host.Cursor = System.Windows.Input.Cursors.Arrow;

        VerticalLine? dragged = _draggingEdge == 1 ? _measure1 : _measure2;
        if (dragged is not null && HasData)
        {
            dragged.X = ToTime(e);
            _host.Refresh();
            ReportMeasure();
        }

        _draggingEdge = 0;
        return true;
    }

    public void OnMouseLeave()
    {
        _host.Cursor = System.Windows.Input.Cursors.Arrow;

        if (Tool == PlotTool.Crosshair)
        {
            CrosshairChanged?.Invoke(this, null);
        }

        HideHover();
    }

    /// <summary>
    /// Verschiebt den sichtbaren Ausschnitt um eine Strecke in Bildpunkten.
    /// </summary>
    /// <remarks>
    /// Jede Y-Achse wird einzeln umgerechnet: Bei vier Skalen nebeneinander entspricht
    /// derselbe Weg in Bildpunkten auf jeder Achse einem anderen Betrag in ihrer Einheit.
    /// </remarks>
    private void PanBy(float dxPixels, float dyPixels)
    {
        Plot p = _host.Plot;
        PixelRect rect = p.RenderManager.LastRender.DataRect;

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        IXAxis bottom = p.Axes.Bottom;
        double dx = dxPixels / rect.Width * bottom.Range.Span;
        bottom.Min += dx;
        bottom.Max += dx;

        foreach (IYAxis axis in p.Axes.GetYAxes())
        {
            // Nach unten ziehen heisst: der Ausschnitt wandert nach oben.
            double dy = -dyPixels / rect.Height * axis.Range.Span;
            axis.Min += dy;
            axis.Max += dy;
        }
    }

    /// <summary>Gibt 1 oder 2 zurueck, wenn der Zeiger nahe an einer Messkante steht, sonst 0.</summary>
    private int EdgeUnderCursor(System.Windows.Input.MouseEventArgs e)
    {
        if (_measure1 is null || _measure2 is null)
        {
            return 0;
        }

        Pixel mouse = _host.GetPlotPixelPosition(e);
        double px1 = _host.Plot.GetPixel(new Coordinates(_measure1.X, 0)).X;
        double px2 = _host.Plot.GetPixel(new Coordinates(_measure2.X, 0)).X;

        double d1 = Math.Abs(mouse.X - px1);
        double d2 = Math.Abs(mouse.X - px2);

        if (d1 <= EdgeGrabPixels && d1 <= d2)
        {
            return 1;
        }

        return d2 <= EdgeGrabPixels ? 2 : 0;
    }

    /// <summary>
    /// Zeigt den naechstgelegenen Messpunkt mit Zeit und Wert an.
    /// </summary>
    /// <remarks>
    /// Fassung 1 hatte diese Anzeige; in der ersten Ueberarbeitung war sie ersatzlos entfallen.
    /// </remarks>
    /// <summary>
    /// Zeigt zu jeder sichtbaren Kurve den Wert unter dem Mauszeiger.
    /// </summary>
    /// <remarks>
    /// Die vorherige Fassung nahm die erste sichtbare Kurve und zeigte nur diese an - in der
    /// Praxis also immer die Hoehe. Damit war die Anzeige fuer Sinkrate, Temperatur und
    /// Beschleunigung wertlos. Jetzt steht zu jeder gezeichneten Kurve ihr Wert da, jeweils in
    /// ihrer Farbe markiert.
    /// </remarks>
    /// <summary>
    /// Zeigt den Messpunkt, der dem Mauszeiger am naechsten liegt.
    /// </summary>
    /// <remarks>
    /// Bewusst genau einer, nicht alle Kurven gleichzeitig: Bei fuenf sichtbaren Kurven wird
    /// aus der Anzeige sonst eine Textwand, die die Stelle verdeckt, die man ablesen will.
    /// Gesucht wird in Bildpunkten, denn jede Kurve hat ihre eigene Skala - ein Abstand von 5
    /// bedeutet auf der Hoehenachse etwas voellig anderes als auf der Beschleunigungsachse.
    /// </remarks>
    private void UpdateHover(System.Windows.Input.MouseEventArgs e)
    {
        if (_recording is null || _time.Length == 0)
        {
            return;
        }

        Pixel mouse = _host.GetPlotPixelPosition(e);
        int index = RangeStatistics.IndexOfTime(_recording.Samples, ToTime(e));
        SeriesData? target = SeriesUnderCursor(mouse, index);

        if (target is null || double.IsNaN(target.Values[index]))
        {
            HideHover();
            return;
        }

        double x = _time[index];
        double y = target.Values[index];

        // Zu weit weg von jeder Kurve: nichts anzeigen.
        Pixel onCurve = PixelOf(x, y, target.Kind);
        if (Math.Abs(onCurve.Y - mouse.Y) > HoverReachPixels)
        {
            HideHover();
            return;
        }

        if (_hoverDots.Count == 0)
        {
            Marker dot = _host.Plot.Add.Marker(x, y);
            dot.MarkerStyle.Shape = MarkerShape.OpenCircle;
            dot.MarkerStyle.Size = 11;
            dot.MarkerStyle.LineWidth = 2;
            _hoverDots.Add(dot);
        }

        if (_hoverLabel is null)
        {
            _hoverLabel = _host.Plot.Add.Text(string.Empty, x, y);
            _hoverLabel.LabelFontSize = 12;
            _hoverLabel.LabelBold = true;
            _hoverLabel.LabelBackgroundColor = Color.FromHex("#FFFFFFE6");
            _hoverLabel.LabelBorderWidth = 1;
            _hoverLabel.LabelPadding = 5;
            _hoverLabel.OffsetY = -16;
        }

        Color color = _settings is not null
            ? ParseColor(_settings.StyleFor(target.Kind).Color, ColorFor(target.Kind))
            : ColorFor(target.Kind);

        IYAxis axis = AxisFor(target.Kind);

        Marker point = _hoverDots[0];
        point.MarkerStyle.LineColor = color;
        point.Axes.YAxis = axis;
        point.Location = new Coordinates(x, y);
        point.IsVisible = true;

        _hoverLabel.Axes.YAxis = axis;
        _hoverLabel.Location = new Coordinates(x, y);
        _hoverLabel.LabelFontColor = color;
        _hoverLabel.LabelBorderColor = color;
        _hoverLabel.LabelText = ClockLabel(x) + "   " +
                                Format(target.RawValues[index], target.Digits) + " " + target.Unit;
        _hoverLabel.IsVisible = true;

        _host.Refresh();
    }

    private void HideHover()
    {
        bool changed = false;

        foreach (Marker dot in _hoverDots)
        {
            if (dot.IsVisible)
            {
                dot.IsVisible = false;
                changed = true;
            }
        }

        if (_hoverLabel is not null && _hoverLabel.IsVisible)
        {
            _hoverLabel.IsVisible = false;
            changed = true;
        }

        if (changed)
        {
            _host.Refresh();
        }
    }

    public static Color ColorFor(SeriesKind kind) => kind switch
    {
        SeriesKind.Altitude => AltitudeColor,
        SeriesKind.Speed => SpeedColor,
        SeriesKind.Temperature => TemperatureColor,
        SeriesKind.AccMagnitude => AccColor,
        SeriesKind.AccX => AccXColor,
        SeriesKind.AccY => AccYColor,
        _ => AccZColor
    };

    /// <summary>
    /// Wandelt die Mausposition in eine Zeit um.
    /// </summary>
    /// <remarks>
    /// Die Umrechnung laeuft ueber <c>GetPlotPixelPosition</c> der Diagrammbibliothek. Wer die
    /// Position selbst aus <c>GetPosition</c> bildet, bekommt geraeteunabhaengige WPF-Einheiten;
    /// auf einem skalierten Bildschirm (125 %, 150 %) liegt der Wert dann um genau diesen Faktor
    /// daneben, und der Messcursor springt an eine falsche Stelle.
    /// </remarks>
    private double ToTime(System.Windows.Input.MouseEventArgs e)
    {
        Pixel pixel = _host.GetPlotPixelPosition(e);
        double x = _host.Plot.GetCoordinates(pixel).X;
        return Math.Clamp(x, _time[0], _time[^1]);
    }

    private VerticalLine AddToolLine(double x, string label)
    {
        VerticalLine line = _host.Plot.Add.VerticalLine(x);
        line.Color = ToolColor;
        line.LineWidth = 1.8f;
        line.LinePattern = label == "1" ? LinePattern.Dashed : LinePattern.Solid;
        line.Text = label;
        return line;
    }

    private void UpdateCrosshair(System.Windows.Input.MouseEventArgs e)
    {
        if (_recording is null)
        {
            return;
        }

        Pixel pixel = _host.GetPlotPixelPosition(e);
        double x = Math.Clamp(_host.Plot.GetCoordinates(pixel).X, _time[0], _time[^1]);
        int index = RangeStatistics.IndexOfTime(_recording.Samples, x);
        double snapped = _time[index];

        _crosshairV ??= CreateCrosshairV();
        _crosshairV.X = snapped;

        _crosshairH ??= CreateCrosshairH();
        _crosshairH.Y = _host.Plot.GetCoordinates(pixel).Y;

        _host.Refresh();

        var values = new List<(SeriesKind, string, string, string)>();
        foreach (SeriesData s in _series)
        {
            double v = s.RawValues.Length > index ? s.RawValues[index] : double.NaN;
            values.Add((s.Kind, s.Label, Format(v, s.Digits), s.Unit));
        }

        CrosshairChanged?.Invoke(this, new CrosshairReadout
        {
            TimeSeconds = snapped,
            Values = values
        });
    }

    private VerticalLine CreateCrosshairV()
    {
        VerticalLine l = _host.Plot.Add.VerticalLine(0);
        l.Color = ToolColor;
        l.LineWidth = 1.2f;
        l.LinePattern = LinePattern.Dashed;
        return l;
    }

    private HorizontalLine CreateCrosshairH()
    {
        HorizontalLine l = _host.Plot.Add.HorizontalLine(0);
        l.Color = ToolColor;
        l.LineWidth = 1.2f;
        l.LinePattern = LinePattern.Dashed;
        return l;
    }

    /// <summary>
    /// Setzt einen Marker auf die Kurve unter dem Mauszeiger - oder entfernt einen, der dort
    /// bereits sitzt.
    /// </summary>
    /// <remarks>
    /// Vorher gingen Marker ausschliesslich auf die Hoehenkurve und liessen sich nur alle
    /// zusammen loeschen. Jetzt traegt jede sichtbare Kurve Marker, in ihrer eigenen Farbe und
    /// mit ihrer eigenen Einheit, und ein Klick auf einen vorhandenen Marker nimmt ihn weg.
    /// </remarks>
    private void ToggleMarker(System.Windows.Input.MouseEventArgs e)
    {
        if (_recording is null || _settings is null || _time.Length == 0)
        {
            return;
        }

        Pixel mouse = _host.GetPlotPixelPosition(e);

        // Zuerst pruefen, ob ein vorhandener Marker getroffen wurde.
        foreach (PlacedMarker existing in _markers)
        {
            Pixel px = PixelOf(existing.X, existing.Y, existing.Kind);

            if (Math.Abs(px.X - mouse.X) <= MarkerGrabPixels &&
                Math.Abs(px.Y - mouse.Y) <= MarkerGrabPixels)
            {
                _host.Plot.Remove(existing.Dot);
                _host.Plot.Remove(existing.Label);
                _markers.Remove(existing);
                _host.Refresh();
                return;
            }
        }

        int index = RangeStatistics.IndexOfTime(_recording.Samples, ToTime(e));
        SeriesData? target = SeriesUnderCursor(mouse, index);

        if (target is null)
        {
            return;
        }

        double px_x = _time[index];
        double py = target.Values[index];

        Color color = ParseColor(_settings.StyleFor(target.Kind).Color, ColorFor(target.Kind));

        Marker dot = _host.Plot.Add.Marker(px_x, py);
        dot.MarkerStyle.Shape = MarkerShape.FilledDiamond;
        dot.MarkerStyle.Size = 11;
        dot.MarkerStyle.FillColor = color;
        dot.MarkerStyle.LineColor = Color.FromHex("#FFFFFF");
        dot.MarkerStyle.LineWidth = 1;
        dot.Axes.YAxis = AxisFor(target.Kind);

        string text = ClockLabel(px_x) + "  ·  " +
                      Format(target.RawValues[index], target.Digits) + " " + target.Unit;

        ScottPlot.Plottables.Text label = _host.Plot.Add.Text(text, px_x, py);
        label.LabelFontColor = color;
        label.LabelFontSize = 12;
        label.LabelBold = true;
        label.LabelBackgroundColor = Color.FromHex("#FFFFFFE0");
        label.LabelBorderColor = color;
        label.LabelBorderWidth = 1;
        label.LabelPadding = 3;
        label.OffsetY = -14;
        label.Axes.YAxis = AxisFor(target.Kind);

        _markers.Add(new PlacedMarker(dot, label, target.Kind, px_x, py));
        _host.Refresh();
    }

    private void ReportMeasure()
    {
        if (_recording is null || _measure1 is null || _measure2 is null || _settings is null)
        {
            MeasureChanged?.Invoke(this, null);
            return;
        }

        double t1 = _measure1.X;
        double t2 = _measure2.X;

        int i1 = RangeStatistics.IndexOfTime(_recording.Samples, t1);
        int i2 = RangeStatistics.IndexOfTime(_recording.Samples, t2);

        int lo = Math.Min(i1, i2);
        int hi = Math.Max(i1, i2);
        double span = Math.Abs(_time[hi] - _time[lo]);

        // Bewusst alle Serien, nicht nur die gezeichneten.
        var stats = new List<SeriesStatistics>();
        foreach (SeriesData sd in _series)
        {
            SeriesStatistics st = RangeStatistics.Compute(sd, i1, i2);

            if (sd.Kind == SeriesKind.Altitude && span > 0)
            {
                st = st with { RatePerSecond = st.Delta / span };
            }

            stats.Add(st);
        }

        // Mittlere Sinkrate über den Bereich, aus der Höhendifferenz - unabhängig von der
        // gefilterten Geschwindigkeitskurve.
        double meanSpeed = double.NaN;
        double dt = Math.Abs(_time[hi] - _time[lo]);
        if (dt > 0)
        {
            double h1 = _reference.ToAltitudeMeters(_recording.Samples[lo].PressureHpa, _recording.Samples[lo].TemperatureC);
            double h2 = _reference.ToAltitudeMeters(_recording.Samples[hi].PressureHpa, _recording.Samples[hi].TemperatureC);
            meanSpeed = (h2 - h1) / dt;
        }

        MeasureChanged?.Invoke(this, new MeasureResult
        {
            Time1 = _time[lo],
            Time2 = _time[hi],
            SampleCount = hi - lo + 1,
            Series = stats,
            MeanVerticalSpeedMs = meanSpeed,
            SmoothingActive = _settings.AnySmoothingActive
        });
    }

    public static string Format(double v, int digits) =>
        double.IsNaN(v) || double.IsInfinity(v)
            ? "-"
            : v.ToString("N" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture);
}
