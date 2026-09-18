using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Siemert.DataViewer.App.Services;
using Siemert.DataViewer.Core.Device;

namespace Siemert.DataViewer.App.Views;

/// <summary>
/// Stellt Uhr und Kalender des angeschlossenen Loggers.
/// </summary>
/// <remarks>
/// Das Fenster haelt die Verbindung ueber seine gesamte Lebensdauer offen. Der eigentliche
/// Stellbefehl geht erst im Augenblick des naechsten Minutenwechsels hinaus, weil das Geraet die
/// Sekunden dabei selbst auf null setzt.
/// </remarks>
public partial class ClockWindow : Window
{
    private readonly string _port;
    private readonly string _deviceName;

    /// <summary>Belegung des Anschlusses gegenueber der Geraetesuche.</summary>
    private readonly IDisposable? _reservation;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };

    private LoggerConnection? _connection;
    private CancellationTokenSource? _cts;

    /// <summary>Stand der Loggeruhr zum Zeitpunkt des letzten Lesens.</summary>
    private LoggerClock? _clock;

    /// <summary>Ortszeit im Augenblick dieses Lesens - Grundlage fuer das Weiterzaehlen.</summary>
    private DateTime _readAt;

    private bool _setting;

    public ClockWindow(string port, string deviceName, IDisposable? reservation = null)
    {
        InitializeComponent();
        _port = port;
        _deviceName = deviceName;
        _reservation = reservation;
        DeviceLine.Text = deviceName + "  ·  " + port;
        _tick.Tick += (_, _) => UpdateDisplay();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        PcTime.Text = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
        Note("HINWEIS", "Die Uhr des Loggers wird gelesen …", "AccentBrush", "AccentSoftBrush");

        try
        {
            _connection = new LoggerConnection(_port);
            _connection.Open();
            await ReloadClockAsync();
            SetButton.IsEnabled = true;
            _tick.Start();
        }
        catch (Exception ex)
        {
            AppLog.Error("Loggeruhr konnte nicht gelesen werden.", ex);
            LoggerTime.Text = "nicht lesbar";
            Note("FEHLER",
                 "Die Verbindung zum Logger kam nicht zustande: " + ex.Message +
                 "  Bitte das Kabel prüfen und das Fenster erneut öffnen.",
                 "DangerBrush", "DangerSoftBrush");
        }
    }

    private async Task ReloadClockAsync()
    {
        if (_connection is null)
        {
            return;
        }

        _clock = await _connection.ReadClockAsync();
        _readAt = DateTime.Now;
        UpdateDisplay();

        if (_clock is null)
        {
            Note("HINWEIS",
                 "Der Logger hat auf die Abfrage der Uhrzeit nicht verständlich geantwortet. " +
                 "Die Uhr lässt sich trotzdem stellen.",
                 "WarningBrush", "WarningSoftBrush");
        }
        else if (!_clock.IsSet)
        {
            Note("UHR NICHT GESTELLT",
                 "Der Logger führt zwar eine Uhrzeit, aber kein Datum. So aufgezeichnete " +
                 "Sprünge lassen sich zeitlich nicht einordnen. Das Stellen der Uhr behebt das " +
                 "für alle künftigen Aufnahmen; bereits gespeicherte Aufnahmen bleiben ohne Datum.",
                 "WarningBrush", "WarningSoftBrush");
        }
        else
        {
            Note("HINWEIS",
                 "Uhr und Kalender sind gestellt. Beide laufen nur, solange die Batterie Spannung " +
                 "liefert. Nach einem Batteriewechsel sind Uhrzeit, Datum und die gespeicherten " +
                 "Aufnahmen verloren und die Uhr muss neu gestellt werden.",
                 "AccentBrush", "AccentSoftBrush");
        }
    }

    /// <summary>
    /// Schreibt Loggerzeit, Rechnerzeit und Abweichung fort.
    /// </summary>
    /// <remarks>
    /// Der Logger wird nicht laufend abgefragt - das wuerde die Schnittstelle unnoetig belasten und
    /// waehrend des Wartens auf den Minutenwechsel stoeren. Stattdessen wird der zuletzt gelesene
    /// Stand um die seither verstrichene Zeit weitergezaehlt.
    /// </remarks>
    private void UpdateDisplay()
    {
        DateTime now = DateTime.Now;
        PcTime.Text = now.ToString("dd.MM.yyyy HH:mm:ss");

        if (_clock is null)
        {
            LoggerTime.Text = "unbekannt";
            DriftText.Text = "-";
            return;
        }

        TimeSpan elapsed = now - _readAt;

        if (_clock.IsSet && _clock.Date.HasValue)
        {
            DateTime loggerNow = _clock.Date.Value.Date + _clock.TimeOfDay + elapsed;
            LoggerTime.Text = loggerNow.ToString("dd.MM.yyyy HH:mm:ss");
            ShowDrift(loggerNow - now);
        }
        else
        {
            TimeSpan loggerNow = _clock.TimeOfDay + elapsed;
            LoggerTime.Text = loggerNow.ToString(@"hh\:mm\:ss") + "   (kein Datum)";

            // Ohne Datum ist nur der Abstand innerhalb des Tages vergleichbar.
            TimeSpan drift = loggerNow - now.TimeOfDay;
            if (drift > TimeSpan.FromHours(12))
            {
                drift -= TimeSpan.FromHours(24);
            }
            else if (drift < TimeSpan.FromHours(-12))
            {
                drift += TimeSpan.FromHours(24);
            }

            ShowDrift(drift);
        }
    }

    private void ShowDrift(TimeSpan drift)
    {
        double seconds = drift.TotalSeconds;
        string sign = seconds >= 0 ? "+" : "−";
        double abs = Math.Abs(seconds);

        DriftText.Text = abs < 90
            ? $"{sign}{abs:F0} s"
            : abs < 5400
                ? $"{sign}{abs / 60:F1} min"
                : $"{sign}{abs / 3600:F1} h";

        DriftText.Foreground = (Brush)FindResource(
            abs <= 2 ? "SuccessBrush" : abs <= 60 ? "WarningBrush" : "DangerBrush");
    }

    private async void OnSet(object sender, RoutedEventArgs e)
    {
        if (_connection is null || _setting)
        {
            return;
        }

        _setting = true;
        SetButton.IsEnabled = false;
        CloseButton.Content = "Abbrechen";
        Countdown.Visibility = Visibility.Visible;
        _cts = new CancellationTokenSource();

        // Der Balken wird auf die tatsaechliche Wartezeit geeicht, sonst begaenne er je nach
        // Sekunde des Drucks irgendwo in der Mitte.
        double span = 0;

        var countdown = new Progress<TimeSpan>(left =>
        {
            if (span <= 0)
            {
                span = Math.Max(1, left.TotalSeconds);
                Countdown.Maximum = span;
            }

            Countdown.Value = Math.Clamp(span - left.TotalSeconds, 0, span);
            Note("WIRD GESTELLT",
                 $"Der Befehl geht in {left.TotalSeconds:F0} s hinaus, beim nächsten vollen " +
                 "Minutenwechsel. Bitte das Kabel bis dahin nicht abziehen.",
                 "AccentBrush", "AccentSoftBrush");
        });

        try
        {
            DateTime target = await _connection.SetClockAsync(countdown, _cts.Token);
            await ReloadClockAsync();

            Note("GESTELLT",
                 $"Der Logger läuft jetzt auf {target:dd.MM.yyyy HH:mm:ss} Ortszeit.",
                 "SuccessBrush", "SuccessSoftBrush");
            AppLog.Info($"Loggeruhr auf {target:dd.MM.yyyy HH:mm:ss} gestellt ({_deviceName}, {_port}).");
        }
        catch (OperationCanceledException)
        {
            Note("ABGEBROCHEN",
                 "Es wurde nichts an den Logger gesendet. Die Uhr steht unverändert.",
                 "TextSecondaryBrush", "SurfaceAltBrush");
        }
        catch (Exception ex)
        {
            AppLog.Error("Loggeruhr konnte nicht gestellt werden.", ex);
            Note("FEHLGESCHLAGEN", ex.Message, "DangerBrush", "DangerSoftBrush");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _setting = false;
            Countdown.Visibility = Visibility.Collapsed;
            CloseButton.Content = "Schließen";
            SetButton.IsEnabled = _connection is not null;
        }
    }

    private void Note(string header, string text, string accentKey, string backgroundKey)
    {
        var accent = (Brush)FindResource(accentKey);
        StateHeader.Text = header;
        StateHeader.Foreground = accent;
        StateText.Text = text;
        StateBox.Background = (Brush)FindResource(backgroundKey);
        StateBox.BorderBrush = accent;
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Laeuft gerade ein Stellvorgang, bricht das Schliessen ihn ab, statt die Verbindung
        // unter dem laufenden Befehl wegzuziehen.
        if (_setting && _cts is not null)
        {
            _cts.Cancel();
            e.Cancel = true;
            return;
        }

        _tick.Stop();

        LoggerConnection? connection = _connection;
        _connection = null;

        if (connection is null)
        {
            _reservation?.Dispose();
            return;
        }

        {
            // Kommunikation ordentlich beenden ('B'), danach den Anschluss freigeben. Das laeuft
            // bewusst im Hintergrund weiter, damit das Fenster nicht daran haengt.
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
}
