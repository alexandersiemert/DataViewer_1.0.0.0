using System.Globalization;
using System.Windows;
using ScottPlot;
using ScottPlot.Plottables;
using System.Windows.Media;
using System.Windows.Threading;
using Siemert.DataViewer.App.Services;
using Siemert.DataViewer.Core.Analysis;
using Siemert.DataViewer.Core.Device;

namespace Siemert.DataViewer.App.Views;

/// <summary>
/// Zeigt fortlaufend die Momentanwerte des angeschlossenen Loggers.
/// </summary>
/// <remarks>
/// Das Geraet liefert auf Befehl <c>D</c> Temperatur und Druck, auf Befehl <c>M</c> die
/// Beschleunigung aller drei Achsen. Beide werden im Sekundentakt abgefragt. Das Fenster haelt
/// die Verbindung offen und den Anschluss belegt, solange es sichtbar ist.
/// </remarks>
public partial class LiveWindow : Window
{
    /// <summary>Abstand zwischen zwei Abfragen.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly string _port;
    private readonly AltitudeReference _reference;
    private readonly IDisposable? _reservation;
    private readonly DispatcherTimer _timer = new() { Interval = Interval };

    /// <summary>So viele Messwerte bleiben im Diagramm stehen.</summary>
    private const int MaxPoints = 900;

    private readonly AppSettings _settings;
    private readonly DateTime _start = DateTime.Now;

    private readonly List<double> _seconds = [];
    private readonly List<double> _altitude = [];
    private readonly List<double> _temperature = [];
    private readonly List<double> _acceleration = [];

    private Scatter? _altitudeLine;
    private Scatter? _temperatureLine;
    private Scatter? _accelerationLine;
    private IYAxis? _temperatureAxis;
    private IYAxis? _accelerationAxis;

    private LoggerConnection? _connection;
    private bool _busy;
    private bool _paused;
    private int _readings;

    public LiveWindow(string port, string deviceName, AltitudeReference reference,
        AppSettings? settings = null, IDisposable? reservation = null)
    {
        InitializeComponent();
        _port = port;
        _reference = reference;
        _settings = settings ?? new AppSettings();
        _reservation = reservation;
        DeviceLine.Text = deviceName + "  ·  " + port;
        _timer.Tick += async (_, _) => await PollAsync();
        BuildPlot();
    }

    /// <summary>
    /// Richtet das Verlaufsdiagramm ein.
    /// </summary>
    /// <remarks>
    /// Stilgebung, Achsenfarben und Zeitformat stammen aus derselben Stelle wie beim
    /// Hauptdiagramm, damit beide gleich aussehen und gleich zu lesen sind.
    /// </remarks>
    private void BuildPlot()
    {
        Plot p = LivePlot.Plot;
        p.Clear();

        _altitudeLine = p.Add.Scatter(_seconds, _altitude);
        _altitudeLine.MarkerStyle.IsVisible = false;
        _altitudeLine.LineWidth = 2;
        _altitudeLine.Color = PlotController.ColorFor(SeriesKind.Altitude);
        _altitudeLine.LegendText = "Höhe [m]";

        _temperatureAxis = p.Axes.AddRightAxis();
        _temperatureAxis.Label.Text = "Temperatur [°C]";

        _temperatureLine = p.Add.Scatter(_seconds, _temperature);
        _temperatureLine.MarkerStyle.IsVisible = false;
        _temperatureLine.LineWidth = 2;
        _temperatureLine.Color = PlotController.ColorFor(SeriesKind.Temperature);
        _temperatureLine.LegendText = "Temperatur [°C]";
        _temperatureLine.Axes.YAxis = _temperatureAxis;

        _accelerationAxis = p.Axes.AddRightAxis();
        _accelerationAxis.Label.Text = "Beschleunigung [g]";

        _accelerationLine = p.Add.Scatter(_seconds, _acceleration);
        _accelerationLine.MarkerStyle.IsVisible = false;
        _accelerationLine.LineWidth = 2;
        _accelerationLine.Color = PlotController.ColorFor(SeriesKind.AccMagnitude);
        _accelerationLine.LegendText = "Beschleunigung [g]";
        _accelerationLine.Axes.YAxis = _accelerationAxis;

        p.Axes.Left.Label.Text = _reference.ShortLabel + " [m]";
        p.Axes.Bottom.Label.Text = "Uhrzeit";
        p.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
        {
            LabelFormatter = seconds => _start.AddSeconds(seconds).ToString("HH:mm:ss", CultureInfo.CurrentCulture)
        };

        p.ShowLegend(Alignment.UpperLeft);
        p.Legend.FontSize = 11;

        PlotController.ApplyChrome(p, _settings);

        PlotController.ColorAxis(p.Axes.Left, SeriesKind.Altitude, _settings);
        PlotController.ColorAxis(_temperatureAxis, SeriesKind.Temperature, _settings);
        PlotController.ColorAxis(_accelerationAxis, SeriesKind.AccMagnitude, _settings);

        LivePlot.Refresh();
    }

    /// <summary>
    /// Traegt einen Messwertsatz in das Diagramm ein.
    /// </summary>
    /// <remarks>
    /// Es werden hoechstens <see cref="MaxPoints"/> Punkte gehalten; der aelteste faellt danach
    /// heraus. Ohne diese Grenze waechst der Speicherbedarf eines offenen Fensters unbegrenzt.
    /// </remarks>
    private void AppendToPlot(LoggerLiveReading reading, double altitude)
    {
        _seconds.Add((DateTime.Now - _start).TotalSeconds);
        _altitude.Add(altitude);
        _temperature.Add(reading.TemperatureC);
        _acceleration.Add(reading.AccMagnitude);

        while (_seconds.Count > MaxPoints)
        {
            _seconds.RemoveAt(0);
            _altitude.RemoveAt(0);
            _temperature.RemoveAt(0);
            _acceleration.RemoveAt(0);
        }

        // Vor dem zweiten Punkt gibt es keine Strecke zu zeichnen.
        if (_seconds.Count < 2)
        {
            return;
        }

        LivePlot.Plot.Axes.AutoScale();
        LivePlot.Refresh();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Note("Verbindung wird aufgebaut.", "AccentBrush", "AccentSoftBrush");

        try
        {
            _connection = new LoggerConnection(_port);
            _connection.Open();

            if (!await _connection.HandshakeAsync())
            {
                Note(LoggerConnection.NotAnsweringMessage(_port), "DangerBrush", "DangerSoftBrush");
                return;
            }

            await PollAsync();
            _timer.Start();
        }
        catch (Exception ex)
        {
            AppLog.Error("Momentanwerte konnten nicht gelesen werden.", ex);
            Note(ex.Message, "DangerBrush", "DangerSoftBrush");
        }
    }

    /// <summary>
    /// Holt einen Satz Momentanwerte.
    /// </summary>
    /// <remarks>
    /// Ueberlappende Abfragen werden verhindert: Bleibt eine Antwort einmal laenger aus, darf
    /// der naechste Takt nicht denselben Anschluss ein zweites Mal beschreiben.
    /// </remarks>
    private async Task PollAsync()
    {
        if (_busy || _paused || _connection is null)
        {
            return;
        }

        _busy = true;

        try
        {
            LoggerLiveReading? reading = await _connection.ReadLiveAsync();

            if (reading is null)
            {
                Note("Das Gerät hat nicht verständlich geantwortet.", "WarningBrush", "WarningSoftBrush");
                return;
            }

            PressureValue.Text = reading.PressureHpa.ToString("F1", CultureInfo.CurrentCulture);
            TemperatureValue.Text = reading.TemperatureC.ToString("F1", CultureInfo.CurrentCulture);
            AccValue.Text = reading.AccMagnitude.ToString("F2", CultureInfo.CurrentCulture);

            AccX.Text = reading.AccX.ToString("F2", CultureInfo.CurrentCulture) + " g";
            AccY.Text = reading.AccY.ToString("F2", CultureInfo.CurrentCulture) + " g";
            AccZ.Text = reading.AccZ.ToString("F2", CultureInfo.CurrentCulture) + " g";

            double altitude = _reference.ToAltitudeMeters(reading.PressureHpa, reading.TemperatureC);
            AltitudeValue.Text = altitude.ToString("F1", CultureInfo.CurrentCulture) + " m  ·  " +
                                 _reference.ShortLabel;

            AppendToPlot(reading, altitude);

            _readings++;
            CounterText.Text = _readings == 1 ? "1 Messung" : _readings + " Messungen";

            Note("Der Logger wird im Sekundentakt abgefragt. Solange dieses Fenster offen ist, " +
                 "lässt sich das Gerät nicht auslesen.", "SuccessBrush", "SuccessSoftBrush");
        }
        catch (Exception ex)
        {
            AppLog.Warn("Momentanwert nicht lesbar: " + ex.Message);
            Note(ex.Message, "WarningBrush", "WarningSoftBrush");
        }
        finally
        {
            _busy = false;
        }
    }

    private async void OnTogglePause(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        PauseButton.Content = _paused ? "Fortsetzen" : "Anhalten";

        if (_paused)
        {
            Note("Angehalten. Die letzten Werte bleiben stehen.", "TextSecondaryBrush", "SurfaceAltBrush");
        }
        else
        {
            await PollAsync();
        }
    }

    private void Note(string text, string accentKey, string backgroundKey)
    {
        StateText.Text = text;
        StateText.Foreground = (Brush)FindResource("TextPrimaryBrush");
        StateBox.Background = (Brush)FindResource(backgroundKey);
        StateBox.BorderBrush = (Brush)FindResource(accentKey);
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _timer.Stop();

        LoggerConnection? connection = _connection;
        _connection = null;

        if (connection is null)
        {
            _reservation?.Dispose();
            return;
        }

        // Kommunikation ordentlich beenden, danach erst den Anschluss freigeben.
        _ = Task.Run(async () =>
        {
            try
            {
                await connection.EndCommunicationAsync();
            }
            finally
            {
                connection.Dispose();
                _reservation?.Dispose();
            }
        });
    }
}
