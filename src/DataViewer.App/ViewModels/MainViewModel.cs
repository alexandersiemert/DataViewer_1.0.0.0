using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Siemert.DataViewer.App.Services;
using Siemert.DataViewer.Core.Analysis;
using Siemert.DataViewer.Core.Device;
using Siemert.DataViewer.Core.Io;
using Siemert.DataViewer.Core.Model;
using Siemert.DataViewer.Core.Protocol;
using Siemert.DataViewer.Core.Units;

namespace Siemert.DataViewer.App.ViewModels;

public enum NoticeLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed class Notice(NoticeLevel level, string text)
{
    public NoticeLevel Level { get; } = level;

    public string Text { get; } = text;

    public DateTime Time { get; } = DateTime.Now;

    public string TimeText => Time.ToString("HH:mm:ss");
}

/// <summary>Eine Aufnahme in der Liste, mit den Kennzahlen, die zur Auswahl nötig sind.</summary>
public sealed class RecordingItem(
    Recording recording, int index, JumpMetrics metrics, UnitSystem units, DeviceInfo? device)
{
    public Recording Recording { get; } = recording;

    public int Index { get; } = index;

    public JumpMetrics Metrics { get; } = metrics;

    public DeviceInfo? Device { get; } = device;

    /// <summary>
    /// Erste Zeile der Liste: Geraet, Seriennummer und Datum.
    /// </summary>
    /// <remarks>
    /// Ohne Geraet und Seriennummer laesst sich eine Aufnahme spaeter nicht mehr zuordnen -
    /// besonders dann nicht, wenn mehrere Logger im Umlauf sind.
    /// </remarks>
    public string Title => RecordingLabel.Headline(Recording, Device);

    /// <summary>
    /// Zweite Zeile der Liste. Früher stand hier nur <c>Startzeit.ToString()</c> - der Anwender
    /// musste jede Aufnahme öffnen, um zu sehen, was darin steckt.
    /// </summary>
    public string Subtitle
    {
        get
        {
            string duration = Recording.Duration.TotalSeconds >= 60
                ? $"{(int)Recording.Duration.TotalMinutes}:{Recording.Duration.Seconds:00} min"
                : $"{Recording.Duration.TotalSeconds:F0} s";

            string span = RecordingLabel.TimeSpanText(Recording);

            if (!Metrics.JumpDetected)
            {
                return $"{span} · {duration} · {Recording.Samples.Count} Messpunkte · kein Sprungprofil";
            }

            string alt = units == UnitSystem.Metric
                ? $"{Metrics.MaxAltitudeM:F0} m"
                : $"{UnitConverter.MetersToFeet(Metrics.MaxAltitudeM):F0} ft";

            return $"{span} · {duration} · max. {alt} · {Metrics.FreefallSeconds:F0} s Freifall";
        }
    }

    public bool HasWarning => !Recording.IsComplete || Recording.HasSaturatedSamples || Recording.Warnings.Count > 0;

    public string WarningText => string.Join(Environment.NewLine, Recording.Warnings);
}

/// <summary>Ein erkanntes Gerät.</summary>
public sealed class DeviceItem(SerialPortCandidate port, DeviceInfo? info) : ObservableObject
{
    public SerialPortCandidate Port { get; } = port;

    public DeviceInfo? Info { get; set; } = info;

    public string Title => Info is not null ? Info.DisplayName : Port.PortName;

    public string Subtitle => Info is not null
        ? $"{Port.PortName} · Prüfsumme {Info.Checksum}"
        : Port.Description;

    /// <summary>
    /// Meldet der Oberflaeche, dass sich die Anzeige geaendert hat.
    /// </summary>
    /// <remarks>
    /// Noetig, weil die Kopfdaten erst nach dem Eintragen in die Liste nachgeladen werden.
    /// Ohne diese Meldung bliebe dort weiterhin nur die Anschlussbezeichnung stehen.
    /// </remarks>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
    }
}

public sealed class MainViewModel : ObservableObject
{
    private readonly HashSet<string> _portsInUse = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Wie oft ein bereits bekanntes Geraet in Folge nicht geantwortet hat.
    /// </summary>
    /// <remarks>
    /// Ein einzelner ausgebliebener Ping ist kein Beweis, dass der Logger weg ist. Das Geraet
    /// verbucht jedes Oeffnen und Schliessen des Anschlusses als An- und Abmeldung und braucht
    /// danach einen Moment, bis es wieder antwortet. Wer es deswegen sofort aus der Liste
    /// wirft, erzeugt genau das Flackern, das er zu melden glaubt.
    /// </remarks>
    private readonly Dictionary<string, int> _misses = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>So oft darf ein bekanntes Geraet ausbleiben, bevor es als entfernt gilt.</summary>
    internal const int MissesBeforeRemoval = 2;

    /// <summary>
    /// Die Geraetesuche. Austauschbar, damit sich das Nachfuehren der Liste pruefen laesst,
    /// ohne dass ein Geraet angeschlossen sein muss.
    /// </summary>
    internal Func<bool, IReadOnlySet<string>, CancellationToken, Task<IReadOnlyList<SerialPortCandidate>>>
        ProbeAsync { get; set; } =
        (probeAll, inUse, token) => LoggerPortScanner.FindLoggersAsync(probeAll, inUse, token);

    /// <summary>Liest die Kopfdaten eines Geraets. Ebenfalls austauschbar, siehe <see cref="ProbeAsync"/>.</summary>
    internal Func<string, Task<DeviceInfo?>> ReadHeaderAsync { get; set; } = async port =>
    {
        using var connection = new LoggerConnection(port);
        connection.Open();
        return await connection.ReadInfoAsync();
    };
    private CancellationTokenSource? _readCts;

    private DeviceItem? _selectedDevice;
    private RecordingItem? _selectedRecording;
    private DeviceInfo? _loadedDevice;
    private DecodeResult _data = DecodeResult.Empty;
    private AltitudeReference _reference = AltitudeReference.Standard;
    private JumpMetrics _metrics = JumpMetrics.None;
    private bool _isBusy;
    private bool _isScanning;
    private string _statusText = "Bereit";
    private double? _progress;
    private string? _progressDetail;
    private string? _currentFilePath;

    public MainViewModel()
    {
        Settings = AppSettings.Load();
        _reference = Settings.BuildReference(null);

        RefreshDevicesCommand = new RelayCommand(async () => await RefreshDevicesAsync(true), () => !IsBusy && !IsScanning);
        ReadAllCommand = new RelayCommand(async () => await ReadAsync(LoggerProtocol.CommandReadAll), CanRead);
        ReadNewCommand = new RelayCommand(async () => await ReadAsync(LoggerProtocol.CommandReadNew), CanRead);
        CancelReadCommand = new RelayCommand(CancelRead, () => IsBusy);
        OpenFileCommand = new RelayCommand(OpenFile, () => !IsBusy);
        ImportRawCommand = new RelayCommand(ImportRaw, () => !IsBusy);
        SaveFileCommand = new RelayCommand(SaveFile, () => HasData && !IsBusy);
        ExportCsvCommand = new RelayCommand(ExportCsv, () => SelectedRecording is not null && !IsBusy);
        ClearNoticesCommand = new RelayCommand(() => Notices.Clear());
        ToggleLiveCommand = new RelayCommand(async () => await ToggleLiveAsync(),
            () => IsLiveRecording || (SelectedDevice is not null && !IsBusy && !IsScanning));
    }

    public AppSettings Settings { get; }

    public ObservableCollection<DeviceItem> Devices { get; } = [];

    public ObservableCollection<RecordingItem> Recordings { get; } = [];

    public ObservableCollection<DeviceEvent> Events { get; } = [];

    public ObservableCollection<Notice> Notices { get; } = [];

    public RelayCommand RefreshDevicesCommand { get; }

    public RelayCommand ReadAllCommand { get; }

    public RelayCommand ReadNewCommand { get; }

    /// <summary>Startet oder beendet das Live-Mitschreiben.</summary>
    public RelayCommand ToggleLiveCommand { get; }

    public RelayCommand CancelReadCommand { get; }

    public RelayCommand OpenFileCommand { get; }

    public RelayCommand ImportRawCommand { get; }

    public RelayCommand SaveFileCommand { get; }

    public RelayCommand ExportCsvCommand { get; }

    public RelayCommand ClearNoticesCommand { get; }

    /// <summary>Wird ausgelöst, wenn das Diagramm neu gezeichnet werden muss.</summary>
    public event EventHandler? PlotInvalidated;

    public DeviceItem? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (Set(ref _selectedDevice, value))
            {
                OnPropertyChanged(nameof(HasDevice));
            }
        }
    }

    public bool HasDevice => SelectedDevice is not null;

    public RecordingItem? SelectedRecording
    {
        get => _selectedRecording;
        set
        {
            if (Set(ref _selectedRecording, value))
            {
                ApplyRecording();
            }
        }
    }

    public DeviceInfo? LoadedDevice
    {
        get => _loadedDevice;
        private set => Set(ref _loadedDevice, value);
    }

    public AltitudeReference Reference
    {
        get => _reference;
        private set
        {
            if (Set(ref _reference, value))
            {
                OnPropertyChanged(nameof(ReferenceText));
            }
        }
    }

    public string ReferenceText => Reference.Describe();

    public JumpMetrics Metrics
    {
        get => _metrics;
        private set => Set(ref _metrics, value);
    }

    public bool HasData => Recordings.Count > 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                InvalidateCommands();
            }
        }
    }

    public bool IsIdle => !IsBusy;

    /// <summary>
    /// Bewertet die Verfuegbarkeit der Befehle neu.
    /// </summary>
    /// <remarks>
    /// <see cref="RelayCommand"/> haengt an <c>CommandManager.RequerySuggested</c>, und das
    /// wird nur durch Eingaben ausgeloest. Nach einem Suchlauf, der im Hintergrund lief, blieben
    /// "Neue" und "Alles" deshalb gesperrt, obwohl ein Logger ausgewaehlt war - der Anwender
    /// musste erst irgendwo hinklicken, damit die Schaltflaechen aufwachten.
    /// </remarks>
    private static void InvalidateCommands() =>
        Application.Current?.Dispatcher.BeginInvoke(CommandManager.InvalidateRequerySuggested);

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (Set(ref _isScanning, value))
            {
                InvalidateCommands();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    public double? Progress
    {
        get => _progress;
        private set => Set(ref _progress, value);
    }

    public string? ProgressDetail
    {
        get => _progressDetail;
        private set => Set(ref _progressDetail, value);
    }

    public UnitSystem UnitSystem
    {
        get => Settings.UnitSystem;
        set
        {
            if (Settings.UnitSystem == value)
            {
                return;
            }

            Settings.UnitSystem = value;
            Settings.Save();
            OnPropertyChanged();
            RebuildRecordingList();
            Refresh();
        }
    }

    private bool CanRead() => SelectedDevice is not null && !IsBusy && !IsScanning;
    /// <summary>Anschluss des ausgewaehlten Loggers.</summary>
    public string? SelectedPort => SelectedDevice?.Port.PortName;
    /// <summary>Wahr, wenn sich gerade ein Geraetevorgang starten laesst.</summary>
    public bool CanUseDevice => SelectedDevice is not null && !IsBusy && !IsScanning;
    /// <summary>
    /// Belegt den ausgewaehlten Logger fuer einen Direktzugriff ausserhalb des Auslesens.
    /// </summary>
    /// <remarks>
    /// Solange die Belegung besteht, laesst die Geraetesuche den Anschluss in Ruhe und die
    /// Auslesebefehle bleiben gesperrt. Ohne diese Klammer wuerde ein beliebiges USB-Ereignis
    /// mitten im Vorgang den Port oeffnen wollen, daran scheitern und den Logger als entfernt
    /// melden - derselbe Fehler, der schon einen laufenden Auslesevorgang zerstoert hat.
    /// </remarks>
    public IDisposable ReserveSelectedDevice(string status)
    {
        if (SelectedDevice is null)
        {
            throw new InvalidOperationException("Es ist kein Logger ausgewählt.");
        }

        if (IsBusy || IsScanning)
        {
            throw new InvalidOperationException("Es läuft bereits ein Gerätevorgang.");
        }

        return new DeviceReservation(this, SelectedDevice.Port.PortName, status);
    }

    private sealed class DeviceReservation : IDisposable
    {
        private readonly MainViewModel _owner;
        private readonly string _port;
        private bool _released;
        public DeviceReservation(MainViewModel owner, string port, string status)
        {
            _owner = owner;
            _port = port;
            _owner._portsInUse.Add(port);
            _owner.IsBusy = true;
            _owner.StatusText = status;
        }

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _owner._portsInUse.Remove(_port);
            _owner.IsBusy = false;
            _owner.StatusText = "Bereit";
        }
    }

    // ------------------------------------------------------------------ Geräte

    /// <summary>
    /// Sucht angeschlossene Logger und fuehrt die Geraeteliste nach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Zwei Dinge sind hier wichtig und waren es vorher nicht:
    /// </para>
    /// <para>
    /// <b>Die Liste wird nachgefuehrt, nicht neu aufgebaut.</b> Ein <c>Clear()</c> mit
    /// anschliessendem Neuaufbau laesst die Auswahl springen und die Anzeige flackern, auch
    /// wenn sich gar nichts geaendert hat.
    /// </para>
    /// <para>
    /// <b>Die Kopfdaten werden nur einmal je Geraet gelesen.</b> Sie aendern sich nicht.
    /// Sie bei jedem Suchlauf erneut zu holen bedeutet, den Anschluss ein zweites Mal zu
    /// oeffnen und zu schliessen - und das Geraet verbucht jedes Mal eine An- und Abmeldung
    /// und ist danach kurz nicht ansprechbar. Der Suchlauf hat sich damit selbst gestoert.
    /// </para>
    /// </remarks>
    public async Task RefreshDevicesAsync(bool reportEmpty)
    {
        if (IsScanning || IsBusy)
        {
            return;
        }

        IsScanning = true;
        StatusText = "Geräte werden gesucht …";

        try
        {
            IReadOnlyList<SerialPortCandidate> found =
                await ProbeAsync(Settings.ProbeAllSerialPorts, _portsInUse, CancellationToken.None);

            string? previous = SelectedDevice?.Port.PortName;
            var now = found.Select(f => f.PortName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Fehlversuche fortschreiben. Wer geantwortet hat, faengt wieder bei null an.
            var stale = new List<DeviceItem>();

            foreach (DeviceItem known in Devices)
            {
                string name = known.Port.PortName;

                if (now.Contains(name))
                {
                    _misses.Remove(name);
                    continue;
                }

                int misses = _misses.GetValueOrDefault(name) + 1;
                _misses[name] = misses;

                if (misses >= MissesBeforeRemoval)
                {
                    stale.Add(known);
                }
                else
                {
                    AppLog.Info($"{name} hat einmal nicht geantwortet; das Gerät bleibt vorerst in der Liste.");
                }
            }

            foreach (DeviceItem gone in stale)
            {
                Devices.Remove(gone);
                _misses.Remove(gone.Port.PortName);
                Notify(NoticeLevel.Warning, $"Der Logger an {gone.Port.PortName} ist nicht mehr erreichbar.");
            }

            // Neue Anschluesse kommen erst in die Liste, wenn Modell und Seriennummer
            // feststehen. Ein Eintrag, der nur "COM3" heisst und sich Sekunden spaeter in
            // "SI-TL1 Nr. 349" verwandelt, sieht aus wie ein Fehler - und solange die Kopfdaten
            // fehlen, ist gar nicht gesichert, dass dort wirklich ein Logger haengt.
            foreach (SerialPortCandidate candidate in found)
            {
                if (Devices.Any(d => string.Equals(d.Port.PortName, candidate.PortName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (_portsInUse.Contains(candidate.PortName))
                {
                    continue;
                }

                DeviceInfo? info;

                try
                {
                    info = await ReadHeaderAsync(candidate.PortName);
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"Kopfdaten von {candidate.PortName} nicht lesbar: {ex.Message}");
                    continue;
                }

                if (info is null)
                {
                    AppLog.Info($"{candidate.PortName} hat noch keine Kopfdaten geliefert; " +
                                "der Eintrag wird beim nächsten Suchlauf erneut versucht.");
                    continue;
                }

                Devices.Add(new DeviceItem(candidate, info));
                Notify(NoticeLevel.Success, $"{info.DisplayName} an {candidate.PortName} erkannt.");
            }

            // Die Liste wird nachgefuehrt statt neu aufgebaut; die bisherige Auswahl ist
            // deshalb im Regelfall noch vorhanden und bleibt bestehen.
            SelectedDevice = Devices.FirstOrDefault(d =>
                                  string.Equals(d.Port.PortName, previous, StringComparison.OrdinalIgnoreCase))
                              ?? Devices.FirstOrDefault();

            if (Devices.Count == 0)
            {
                StatusText = "Kein Logger gefunden";
                if (reportEmpty)
                {
                    Notify(NoticeLevel.Warning,
                        Settings.ProbeAllSerialPorts
                            ? "An keiner seriellen Schnittstelle hat ein Logger geantwortet."
                            : "Kein Logger gefunden. Es wurden nur Anschlüsse mit passender USB-Kennung geprüft. " +
                              "unter Einstellungen lässt sich die Suche auf alle Schnittstellen ausweiten.");
                }
            }
            else
            {
                StatusText = Devices.Count == 1 ? "1 Gerät verbunden" : $"{Devices.Count} Geräte verbunden";
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Gerätesuche fehlgeschlagen.", ex);
            Notify(NoticeLevel.Error, "Die Gerätesuche ist fehlgeschlagen: " + ex.Message);
            StatusText = "Gerätesuche fehlgeschlagen";
        }
        finally
        {
            IsScanning = false;
        }
    }

    // ------------------------------------------------------------------ Auslesen

    private async Task ReadAsync(string command)
    {
        if (SelectedDevice is null || IsBusy)
        {
            return;
        }

        string port = SelectedDevice.Port.PortName;
        _readCts = new CancellationTokenSource();
        _portsInUse.Add(port);
        IsBusy = true;
        Progress = null;
        ProgressDetail = null;

        bool readAll = command == LoggerProtocol.CommandReadAll;
        bool silentDevice = false;
        StatusText = readAll ? "Gesamter Speicher wird gelesen …" : "Neue Aufnahmen werden gelesen …";

        try
        {
            using var connection = new LoggerConnection(port);
            connection.Open();

            var progress = new Progress<ReadProgress>(p =>
            {
                Progress = p.Fraction;
                StatusText = p.Phase;
                ProgressDetail = p.EstimatedRemaining is { TotalSeconds: > 1 }
                    ? $"{p.CharsReceived / 2048.0:F0} KB · noch etwa {FormatDuration(p.EstimatedRemaining.Value)}"
                    : $"{p.CharsReceived / 2048.0:F0} KB";
            });

            ReadoutResult result = await connection.ReadMemoryAsync(command, progress, _readCts.Token);

            DecodeResult decoded = PayloadDecoder.Decode(result.Payload);

            // "Neue" liefert alles seit dem letzten Auslesen. Wurde seither nichts
            // aufgezeichnet, kommt nichts - das ist eine gueltige Auskunft und kein
            // Auslesevorgang. Die vorhandene Auswertung bleibt dann unberuehrt stehen, statt
            // durch eine leere ersetzt zu werden.
            if (!readAll && decoded.Recordings.Count == 0)
            {
                StatusText = "Keine neuen Aufnahmen";
                Notify(NoticeLevel.Info,
                    "Der Logger hat seit dem letzten Auslesen nichts aufgezeichnet. " +
                    "Die angezeigte Auswertung bleibt unverändert. Mit „Alles“ lässt sich der " +
                    "gesamte Speicher erneut holen.");
                return;
            }

            // Die Rohdaten werden sofort gesichert - vor jeder Auswertung. Damit ist ein
            // Auslesevorgang nie wieder wegen eines Auswertungsfehlers verloren.
            string? archived = ArchiveRaw(result, SelectedDevice.Info);

            LoadData(decoded, result.DeviceInfo ?? SelectedDevice.Info, null);

            StatusText = $"{decoded.Recordings.Count} Aufnahme(n) gelesen in {FormatDuration(result.Duration)}";
            Notify(NoticeLevel.Success,
                $"{decoded.Recordings.Count} Aufnahme(n) und {decoded.Events.Count} Betriebsereignisse gelesen." +
                (archived is not null ? $" Rohdaten gesichert unter {archived}." : string.Empty));
        }
        catch (OperationCanceledException)
        {
            StatusText = "Auslesen abgebrochen";
            Notify(NoticeLevel.Info, "Der Auslesevorgang wurde abgebrochen. Am Gerät wurde nichts verändert.");
        }
        catch (LoggerTimeoutException ex)
        {
            StatusText = "Gerät antwortet nicht";
            Notify(NoticeLevel.Error, ex.Message);
            AppLog.Error("Zeitüberschreitung beim Auslesen.", ex);

            // Die Geräteliste behauptet sonst weiter, der Logger sei da. Sie kann das nicht von
            // selbst merken: Die USB-Schnittstelle sitzt im Adapter, der Anschluss bleibt also
            // bestehen, auch wenn das Gerät abgezogen wurde. Nur ein neuer Suchlauf mit Ping
            // schafft Klarheit.
            silentDevice = true;
        }
        catch (Exception ex)
        {
            StatusText = "Auslesen fehlgeschlagen";
            Notify(NoticeLevel.Error, "Das Auslesen ist fehlgeschlagen: " + ex.Message);
            AppLog.Error("Auslesen fehlgeschlagen.", ex);
        }
        finally
        {
            _portsInUse.Remove(port);
            _readCts?.Dispose();
            _readCts = null;
            IsBusy = false;
            Progress = null;
            ProgressDetail = null;
        }

        if (silentDevice)
        {
            await RefreshDevicesAsync(false);
        }
    }

    // ------------------------------------------------------------------ Live mitschreiben

    private LiveRecorder? _live;
    private LoggerConnection? _liveConnection;
    private IDisposable? _liveReservation;
    private DispatcherTimer? _liveTimer;
    private bool _livePolling;

    /// <summary>Wahr, solange live mitgeschrieben wird.</summary>
    public bool IsLiveRecording => _live is not null;

    /// <summary>Beschriftung der Schaltflaeche.</summary>
    public string LiveButtonText => IsLiveRecording
        ? $"Aufzeichnung beenden ({_live!.Count})"
        : "Live mitschreiben";

    private async Task ToggleLiveAsync()
    {
        if (IsLiveRecording)
        {
            StopLive();
        }
        else
        {
            await StartLiveAsync();
        }
    }

    /// <summary>
    /// Beginnt eine Live-Aufzeichnung.
    /// </summary>
    /// <remarks>
    /// Aufgezeichnet werden die unveraenderten Antworten des Geraets auf 'D' und 'M'. Die
    /// entstehende Aufnahme verhaelt sich wie jede andere: Sie steht in der Liste, laesst sich
    /// auswerten, messen, exportieren und speichern.
    /// </remarks>
    private async Task StartLiveAsync()
    {
        if (SelectedDevice is null || IsBusy || IsScanning)
        {
            return;
        }

        string port = SelectedDevice.Port.PortName;

        try
        {
            _liveReservation = ReserveSelectedDevice("Live-Aufzeichnung läuft …");
            _liveConnection = new LoggerConnection(port);
            _liveConnection.Open();

            if (!await _liveConnection.HandshakeAsync())
            {
                Notify(NoticeLevel.Error, LoggerConnection.NotAnsweringMessage(port));
                StopLive();
                return;
            }

            _live = new LiveRecorder(LiveIntervalSeconds) { Device = SelectedDevice.Info };

            _liveTimer = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher)
            {
                Interval = TimeSpan.FromSeconds(LiveIntervalSeconds)
            };

            _liveTimer.Tick += async (_, _) => await PollLiveAsync();
            _liveTimer.Start();

            OnPropertyChanged(nameof(IsLiveRecording));
            OnPropertyChanged(nameof(LiveButtonText));
            InvalidateCommands();

            StatusText = "Live-Aufzeichnung läuft";
            Notify(NoticeLevel.Info,
                "Die Momentanwerte werden im Sekundentakt mitgeschrieben. Solange das läuft, " +
                "ist der Logger für anderes gesperrt. Zum Beenden dieselbe Schaltfläche erneut drücken.");

            await PollLiveAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("Live-Aufzeichnung konnte nicht gestartet werden.", ex);
            Notify(NoticeLevel.Error, "Die Aufzeichnung konnte nicht gestartet werden: " + ex.Message);
            StopLive();
        }
    }

    /// <summary>Angestrebter Abstand zweier Abfragen.</summary>
    private const double LiveIntervalSeconds = 1.0;

    private async Task PollLiveAsync()
    {
        // Ueberlappende Abfragen ausschliessen: Bleibt eine Antwort einmal laenger aus, darf der
        // naechste Takt nicht denselben Anschluss ein zweites Mal beschreiben.
        if (_livePolling || _live is null || _liveConnection is null)
        {
            return;
        }

        _livePolling = true;

        try
        {
            string env = await _liveConnection.SendSimpleAsync(LoggerCommands.ReadEnvironment);
            string acc = await _liveConnection.SendSimpleAsync(LoggerCommands.ReadAcceleration);

            if (!_live.Add(env, acc, DateTime.Now))
            {
                AppLog.Warn("Ein Live-Takt war nicht auswertbar und wurde übergangen.");
                return;
            }

            ShowLive();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Live-Takt fehlgeschlagen: " + ex.Message);
            StatusText = "Live-Aufzeichnung: Gerät antwortet nicht";
        }
        finally
        {
            _livePolling = false;
        }
    }

    /// <summary>Zeigt den bisherigen Stand der Aufzeichnung an.</summary>
    private void ShowLive()
    {
        if (_live is null || _live.Count == 0)
        {
            return;
        }

        LoadData(_live.BuildResult(), _live.Device, null);

        StatusText = $"Live-Aufzeichnung: {_live.Count} Messpunkte, {FormatDuration(_live.Duration)}";
        OnPropertyChanged(nameof(LiveButtonText));
    }

    /// <summary>
    /// Beendet die Aufzeichnung.
    /// </summary>
    /// <remarks>
    /// Die entstandene Aufnahme bleibt stehen. Sie ist ab jetzt eine gewoehnliche Aufnahme und
    /// muss vom Anwender gespeichert werden - anders als beim Auslesen gibt es hier kein
    /// Rohdatenarchiv, das im Hintergrund mitschreibt.
    /// </remarks>
    private void StopLive()
    {
        _liveTimer?.Stop();
        _liveTimer = null;

        LiveRecorder? finished = _live;
        _live = null;

        LoggerConnection? connection = _liveConnection;
        _liveConnection = null;

        IDisposable? reservation = _liveReservation;
        _liveReservation = null;

        if (connection is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await connection.EndCommunicationAsync();
                }
                finally
                {
                    connection.Dispose();
                    reservation?.Dispose();
                }
            });
        }
        else
        {
            reservation?.Dispose();
        }

        OnPropertyChanged(nameof(IsLiveRecording));
        OnPropertyChanged(nameof(LiveButtonText));
        InvalidateCommands();

        if (finished is null || finished.Count == 0)
        {
            StatusText = "Bereit";
            return;
        }

        StatusText = $"Aufzeichnung beendet: {finished.Count} Messpunkte";
        Notify(NoticeLevel.Success,
            $"{finished.Count} Messpunkte über {FormatDuration(finished.Duration)} mitgeschrieben. " +
            "Die Aufnahme ist noch nicht gesichert; sie geht verloren, wenn sie nicht gespeichert wird.");
    }

    private void CancelRead()
    {
        _readCts?.Cancel();
        StatusText = "Abbruch wird ausgeführt …";
    }

    private static string? ArchiveRaw(ReadoutResult result, DeviceInfo? device)
    {
        try
        {
            Directory.CreateDirectory(AppInfo.RawArchiveDirectory);
            string name = $"{device?.SerialNumber.ToString() ?? "unbekannt"}_{DateTime.Now:yyyyMMdd_HHmmss}.lgd";
            string path = Path.Combine(AppInfo.RawArchiveDirectory, name);
            File.WriteAllText(path, result.RawResponse);
            return path;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Rohdaten konnten nicht gesichert werden: " + ex.Message);
            return null;
        }
    }

    // ------------------------------------------------------------------ Dateien

    private void OpenFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Aufzeichnung öffnen",
            Filter = "SIEMERT-Aufzeichnung (*.sdvlog)|*.sdvlog|Alle Dateien (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            SdvDocument doc = SdvLogFile.Load(dlg.FileName);
            LoadData(doc.Data, doc.Device, dlg.FileName);

            if (!doc.IntegrityVerified && doc.IntegrityMessage is not null)
            {
                Notify(NoticeLevel.Warning, doc.IntegrityMessage);
            }
            else
            {
                Notify(NoticeLevel.Success, "Datei geladen, Prüfsumme der Rohdaten bestätigt.");
            }

            StatusText = Path.GetFileName(dlg.FileName);
        }
        catch (Exception ex)
        {
            Notify(NoticeLevel.Error, "Die Datei konnte nicht geladen werden: " + ex.Message);
            AppLog.Error("Datei laden fehlgeschlagen.", ex);
        }
    }

    private void ImportRaw()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Rohdaten einlesen",
            Filter = "Rohdaten (*.lgd;*.txt)|*.lgd;*.txt|Alle Dateien (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            string content = File.ReadAllText(dlg.FileName);
            DecodeResult decoded = PayloadDecoder.Decode(content);
            LoadData(decoded, null, null);
            StatusText = Path.GetFileName(dlg.FileName);
            Notify(NoticeLevel.Success, $"{decoded.Recordings.Count} Aufnahme(n) aus Rohdaten eingelesen.");
        }
        catch (Exception ex)
        {
            Notify(NoticeLevel.Error, "Die Rohdaten konnten nicht eingelesen werden: " + ex.Message);
            AppLog.Error("Rohdatenimport fehlgeschlagen.", ex);
        }
    }

    /// <summary>Speichert nur die gewaehlte Aufnahme in eine eigene Datei.</summary>
    /// <summary>
    /// Speichert die ausgewaehlte Aufnahme als eigene Datei.
    /// </summary>
    /// <remarks>
    /// Gespeichert wird der unveraenderte Rohausschnitt dieser Aufnahme samt Geraetekopf und
    /// Betriebshistorie - nicht die ausgewerteten Werte. Der vollstaendige Auszug bleibt
    /// davon unberuehrt in der Rohdatenablage liegen.
    /// </remarks>
    public void SaveSelectedRecording()
    {
        if (SelectedRecording is null)
        {
            Notify(NoticeLevel.Info, "Es ist keine Aufnahme ausgewählt.");
            return;
        }

        Recording recording = SelectedRecording.Recording;
        if (string.IsNullOrEmpty(recording.RawSegment))
        {
            Notify(NoticeLevel.Warning,
                "Für diese Aufnahme liegt kein Rohausschnitt vor. Sie stammt aus einer älteren " +
                "Datei, die nur ausgewertete Werte enthält. Bitte den zugehörigen Auszug erneut " +
                "öffnen oder das Gerät neu auslesen.");
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Nur diese Aufnahme speichern",
            Filter = "SIEMERT-Aufzeichnung (*.sdvlog)|*.sdvlog",
            FileName = BuildFileName(SdvLogFile.Extension),
            InitialDirectory = Settings.LastExportFolder ?? string.Empty
        };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            string source = _currentFilePath is not null
                ? Path.GetFileName(_currentFilePath)
                : LoadedDevice?.Source ?? "Auslesevorgang";
            SdvLogFile.SaveRecording(
                dlg.FileName, LoadedDevice, recording, _data, Reference, AppInfo.Version, source,
                "Einzelne Aufnahme " + recording.DisplayName + " aus " + source + ". " +
                "Enthalten ist der unveränderte Rohausschnitt dieser Aufnahme sowie Gerätekopf " +
                "und Betriebsereignisse des Auszugs.");
            Settings.LastExportFolder = Path.GetDirectoryName(dlg.FileName);
            Settings.Save();
            Notify(NoticeLevel.Success,
                $"Gespeichert: {Path.GetFileName(dlg.FileName)} " +
                $"({recording.Samples.Count} Messpunkte, Rohausschnitt {recording.RawSegment.Length} Zeichen).");
        }
        catch (Exception ex)
        {
            Notify(NoticeLevel.Error, "Speichern fehlgeschlagen: " + ex.Message);
            AppLog.Error("Einzelaufnahme speichern fehlgeschlagen.", ex);
        }
    }


    public void ExportCsvForSelected() => ExportCsv();

    private void SaveFile()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Aufzeichnung speichern",
            Filter = "SIEMERT-Aufzeichnung (*.sdvlog)|*.sdvlog",
            FileName = BuildFileName(SdvLogFile.Extension),
            InitialDirectory = Settings.LastExportFolder ?? string.Empty
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            SdvLogFile.Save(dlg.FileName, LoadedDevice, _data, Reference, AppInfo.Version);
            _currentFilePath = dlg.FileName;
            Settings.LastExportFolder = Path.GetDirectoryName(dlg.FileName);
            Settings.Save();
            Notify(NoticeLevel.Success, "Gespeichert: " + Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            Notify(NoticeLevel.Error, "Speichern fehlgeschlagen: " + ex.Message);
            AppLog.Error("Speichern fehlgeschlagen.", ex);
        }
    }

    internal void ExportCsv()
    {
        if (SelectedRecording is null)
        {
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Als CSV exportieren",
            Filter = "CSV, international (*.csv)|*.csv|CSV für deutsches Excel (*.csv)|*.csv",
            FilterIndex = Settings.CsvFlavor == CsvFlavor.International ? 1 : 2,
            FileName = BuildFileName(".csv"),
            InitialDirectory = Settings.LastExportFolder ?? string.Empty
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        CsvFlavor flavor = dlg.FilterIndex == 2 ? CsvFlavor.GermanExcel : CsvFlavor.International;

        try
        {
            CsvExporter.Save(dlg.FileName, SelectedRecording.Recording, LoadedDevice, Reference,
                Settings.UnitSystem, flavor, AppInfo.Version);

            Settings.CsvFlavor = flavor;
            Settings.LastExportFolder = Path.GetDirectoryName(dlg.FileName);
            Settings.Save();
            Notify(NoticeLevel.Success, "Exportiert: " + Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            Notify(NoticeLevel.Error, "Export fehlgeschlagen: " + ex.Message);
            AppLog.Error("CSV-Export fehlgeschlagen.", ex);
        }
    }

    private string BuildFileName(string extension)
    {
        string device = LoadedDevice is not null ? $"{LoadedDevice.Model}_{LoadedDevice.SerialNumber}" : "Aufzeichnung";
        string when = SelectedRecording?.Recording.StartTime?.ToString("yyyyMMdd_HHmmss")
                      ?? DateTime.Now.ToString("yyyyMMdd_HHmmss");

        string name = $"{device}_{when}{extension}";
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name;
    }

    // ------------------------------------------------------------------ Daten übernehmen

    private void LoadData(DecodeResult data, DeviceInfo? device, string? filePath)
    {
        _data = data;
        LoadedDevice = device;
        _currentFilePath = filePath;

        Events.Clear();
        foreach (DeviceEvent e in data.Events)
        {
            Events.Add(e);
        }

        foreach (DecodeMessage m in data.Messages)
        {
            Notify(m.Severity switch
            {
                DecodeSeverity.Error => NoticeLevel.Error,
                DecodeSeverity.Warning => NoticeLevel.Warning,
                _ => NoticeLevel.Info
            }, m.Text);
        }

        RebuildRecordingList();
        SelectedRecording = Recordings.FirstOrDefault();
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(HasEvents));
    }

    public bool HasEvents => Events.Count > 0;

    private void RebuildRecordingList()
    {
        int previous = SelectedRecording?.Index ?? 0;
        Recordings.Clear();

        for (int i = 0; i < _data.Recordings.Count; i++)
        {
            Recording rec = _data.Recordings[i];
            AltitudeReference reference = BuildReferenceFor(rec);
            JumpMetrics metrics = JumpAnalyzer.Analyze(rec, reference);
            Recordings.Add(new RecordingItem(rec, i, metrics, Settings.UnitSystem, LoadedDevice));
        }

        if (Recordings.Count > 0)
        {
            _selectedRecording = Recordings[Math.Clamp(previous, 0, Recordings.Count - 1)];
            OnPropertyChanged(nameof(SelectedRecording));
            ApplyRecording();
        }
        else
        {
            _selectedRecording = null;
            OnPropertyChanged(nameof(SelectedRecording));
        }
    }

    private AltitudeReference BuildReferenceFor(Recording rec)
    {
        double? ground = !double.IsNaN(rec.EndPressureHpa) && rec.EndPressureHpa > 0
            ? rec.EndPressureHpa
            : rec.Samples.Count > 0
                ? rec.Samples[^1].PressureHpa
                : null;

        // Der Anfangsdruck stammt bevorzugt aus dem Kopfsatz der Aufnahme; fehlt er, wird der
        // erste Messpunkt genommen.
        double? start = !double.IsNaN(rec.StartPressureHpa) && rec.StartPressureHpa > 0
            ? rec.StartPressureHpa
            : rec.Samples.Count > 0
                ? rec.Samples[0].PressureHpa
                : null;

        return Settings.BuildReference(ground, start);
    }

    private void ApplyRecording()
    {
        if (SelectedRecording is null)
        {
            Metrics = JumpMetrics.None;
            Reference = Settings.BuildReference(null);
            PlotInvalidated?.Invoke(this, EventArgs.Empty);
            return;
        }

        Reference = BuildReferenceFor(SelectedRecording.Recording);
        Metrics = SelectedRecording.Metrics;

        foreach (string w in SelectedRecording.Recording.Warnings)
        {
            Notify(NoticeLevel.Warning, w);
        }

        PlotInvalidated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Erzwingt Neuberechnung und Neuzeichnen, z. B. nach geänderter Höhenreferenz.</summary>
    public void Refresh()
    {
        RebuildRecordingList();
        OnPropertyChanged(nameof(ReferenceText));
        PlotInvalidated?.Invoke(this, EventArgs.Empty);
    }

    public void Notify(NoticeLevel level, string text)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            Notices.Insert(0, new Notice(level, text));
            while (Notices.Count > 100)
            {
                Notices.RemoveAt(Notices.Count - 1);
            }
        });

        if (level is NoticeLevel.Error)
        {
            AppLog.Error(text);
        }
        else if (level is NoticeLevel.Warning)
        {
            AppLog.Warn(text);
        }
        else
        {
            AppLog.Info(text);
        }
    }

    public static string FormatDuration(TimeSpan t) => t.TotalMinutes >= 1
        ? $"{(int)t.TotalMinutes}:{t.Seconds:00} min"
        : $"{t.TotalSeconds:F0} s";
}

