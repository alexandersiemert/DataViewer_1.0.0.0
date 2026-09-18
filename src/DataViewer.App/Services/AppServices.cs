using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using Siemert.DataViewer.Core.Analysis;
using Siemert.DataViewer.Core.Io;
using Siemert.DataViewer.Core.Units;

namespace Siemert.DataViewer.App.Services;

/// <summary>Programmkennung und Ablageorte.</summary>
public static class AppInfo
{
    public const string ProductName = "SIEMERT DataViewer";

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "2.0.0";

    /// <summary>Benutzerbezogener Datenordner. Ohne Administratorrechte beschreibbar.</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SIEMERT", "DataViewer");

    public static string LogDirectory { get; } = Path.Combine(DataDirectory, "Protokoll");

    /// <summary>Hier werden Rohdaten sofort nach dem Auslesen abgelegt - vor jeder Auswertung.</summary>
    public static string RawArchiveDirectory { get; } = Path.Combine(DataDirectory, "Rohdaten");

    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "einstellungen.json");

    public static string Title => $"{ProductName} {Version}";
}

/// <summary>Datei-Protokollierung für den Kundendienst. Darf die Anwendung niemals stören.</summary>
public static class AppLog
{
    private static readonly Lock Gate = new();

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Warn(string message) => Write("WARN ", message, null);

    public static void Error(string message, Exception? ex = null) => Write("FEHLER", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppInfo.LogDirectory);
            string file = Path.Combine(AppInfo.LogDirectory, $"dataviewer-{DateTime.Now:yyyyMMdd}.log");

            var sb = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(level).Append("] ")
                .Append(message);

            if (ex is not null)
            {
                sb.AppendLine().Append(ex);
            }

            lock (Gate)
            {
                File.AppendAllText(file, sb.ToString() + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception logEx)
        {
            Debug.WriteLine("Protokollierung fehlgeschlagen: " + logEx.Message);
        }
    }

    /// <summary>Entfernt Protokolle, die älter als die angegebene Zahl von Tagen sind.</summary>
    public static void Prune(int keepDays = 60)
    {
        try
        {
            if (!Directory.Exists(AppInfo.LogDirectory))
            {
                return;
            }

            DateTime limit = DateTime.Now.AddDays(-keepDays);
            foreach (string f in Directory.EnumerateFiles(AppInfo.LogDirectory, "dataviewer-*.log"))
            {
                if (File.GetLastWriteTime(f) < limit)
                {
                    File.Delete(f);
                }
            }
        }
        catch (IOException)
        {
            // Aufräumen ist nachrangig.
        }
    }
}

/// <summary>
/// Benutzereinstellungen.
/// </summary>
/// <remarks>
/// Die Vorgängerversion besaß eine Einstellungsinfrastruktur, benutzte sie aber nirgends -
/// Einheiten, Fenstergröße und Darstellung gingen bei jedem Start verloren.
/// </remarks>
public sealed class AppSettings
{
    /// <summary>
    /// Stand des Einstellungsformats.
    /// </summary>
    /// <remarks>
    /// Wird gebraucht, damit geaenderte Voreinstellungen auch bei Anwendern greifen, die schon
    /// eine Einstellungsdatei haben. Ohne diesen Stempel wuerde eine neue Voreinstellung nur bei
    /// einer frischen Installation wirken - der Bestandskunde behielte stillschweigend den alten
    /// Zustand und wuerde sich fragen, warum bei ihm etwas fehlt.
    /// </remarks>
    [JsonPropertyName("settingsVersion")]
    public int SettingsVersion { get; set; }

    public const int CurrentSettingsVersion = 2;

    [JsonPropertyName("unitSystem")]
    public UnitSystem UnitSystem { get; set; } = UnitSystem.Metric;

    [JsonPropertyName("speedUnit")]
    public SpeedUnit SpeedUnit { get; set; } = SpeedUnit.MetersPerSecond;

    [JsonPropertyName("altitudeMode")]
    public AltitudeMode AltitudeMode { get; set; } = AltitudeMode.GroundZero;

    [JsonPropertyName("qnhHpa")]
    public double QnhHpa { get; set; } = Atmosphere.StandardPressureHpa;

    [JsonPropertyName("stationElevationM")]
    public double StationElevationM { get; set; }

    [JsonPropertyName("temperatureCorrection")]
    public bool TemperatureCorrection { get; set; }

    [JsonPropertyName("windowWidth")]
    public double WindowWidth { get; set; } = 1420;

    [JsonPropertyName("windowHeight")]
    public double WindowHeight { get; set; } = 900;

    [JsonPropertyName("windowMaximized")]
    public bool WindowMaximized { get; set; }

    [JsonPropertyName("probeAllSerialPorts")]
    public bool ProbeAllSerialPorts { get; set; }

    [JsonPropertyName("csvFlavor")]
    public CsvFlavor CsvFlavor { get; set; } = CsvFlavor.International;

    [JsonPropertyName("lastExportFolder")]
    public string? LastExportFolder { get; set; }

    [JsonPropertyName("showSeriesAltitude")]
    public bool ShowAltitude { get; set; } = true;

    [JsonPropertyName("showSeriesSpeed")]
    public bool ShowSpeed { get; set; } = true;

    [JsonPropertyName("showSeriesTemperature")]
    public bool ShowTemperature { get; set; } = true;

    [JsonPropertyName("showSeriesAcc")]
    public bool ShowAcceleration { get; set; } = true;

    /// <summary>
    /// Die drei Beschleunigungsachsen sind einzeln schaltbar - so wie in Fassung 1. Wer eine
    /// Drehung um eine bestimmte Achse sucht, will die anderen beiden ausblenden koennen.
    /// </summary>
    [JsonPropertyName("showSeriesAccX")]
    public bool ShowAccX { get; set; } = true;

    [JsonPropertyName("showSeriesAccY")]
    public bool ShowAccY { get; set; } = true;

    [JsonPropertyName("showSeriesAccZ")]
    public bool ShowAccZ { get; set; } = true;

    [JsonPropertyName("showLegend")]
    public bool ShowLegend { get; set; } = true;

    /// <summary>
    /// Fensterbreite der Glättung in Sekunden je Kurve, 0 = aus.
    /// </summary>
    /// <remarks>
    /// Je Kurve einstellbar, weil die Messgrößen unterschiedlich stark rauschen: Die Höhe verträgt
    /// eine kräftige Glättung, während sie bei der Beschleunigung genau die Spitzen wegnimmt, auf
    /// die es ankommt.
    /// <para>
    /// Wirkt ausschließlich auf die Darstellung. Die Zahlenwerte des Messcursors werden immer aus
    /// den Rohwerten gebildet — sonst läse man ein gedämpftes Maximum ab und hielte es für den
    /// Messwert.
    /// </para>
    /// </remarks>
    [JsonPropertyName("smoothingAltitude")]
    public double SmoothingAltitude { get; set; }

    [JsonPropertyName("smoothingSpeed")]
    public double SmoothingSpeed { get; set; }

    [JsonPropertyName("smoothingTemperature")]
    public double SmoothingTemperature { get; set; }

    [JsonPropertyName("smoothingAcc")]
    public double SmoothingAcc { get; set; }

    [JsonPropertyName("smoothingAccX")]
    public double SmoothingAccX { get; set; }

    [JsonPropertyName("smoothingAccY")]
    public double SmoothingAccY { get; set; }

    [JsonPropertyName("smoothingAccZ")]
    public double SmoothingAccZ { get; set; }

    public const double MaxSmoothingSeconds = 30.0;

    // ---------------------------------------------------------------- Darstellung
    // Fassung 1 hatte diese Einstellungen in einem funfstufigen Menuebaum verteilt. Inhaltlich
    // gehoeren sie zur jeweiligen Kurve und liegen deshalb jetzt beisammen im Reiter "Serien".

    [JsonPropertyName("chartStyle")]
    public ChartStyle ChartStyle { get; set; } = ChartStyle.Light;

    [JsonPropertyName("showGrid")]
    public bool ShowGrid { get; set; } = true;

    /// <summary>Achsenbeschriftungen in der Farbe der zugehoerigen Kurve.</summary>
    [JsonPropertyName("axisLabelsFollowSeries")]
    public bool AxisLabelsFollowSeries { get; set; } = true;

    /// <summary>Farbe, Linienbreite und Muster je Kurve, als Hexfarbe bzw. Zahl.</summary>
    [JsonPropertyName("seriesStyles")]
    public Dictionary<string, SeriesStyle> SeriesStyles { get; set; } = [];

    public SeriesStyle StyleFor(Views.SeriesKind kind)
    {
        string key = kind.ToString();
        if (SeriesStyles.TryGetValue(key, out SeriesStyle? style) && style is not null)
        {
            return style;
        }

        SeriesStyle fallback = SeriesStyle.Default(kind);
        SeriesStyles[key] = fallback;
        return fallback;
    }

    public void SetStyle(Views.SeriesKind kind, SeriesStyle style) => SeriesStyles[kind.ToString()] = style;

    public void ResetStyles()
    {
        SeriesStyles.Clear();
        ChartStyle = ChartStyle.Light;
        ShowGrid = true;
        AxisLabelsFollowSeries = true;
    }

    /// <summary>Glättungsfenster einer Kurve in Sekunden.</summary>
    /// <summary>
    /// Verfahren zur Glättung der Kurven.
    /// </summary>
    /// <remarks>
    /// Voreingestellt ist Savitzky-Golay. Der gleitende Mittelwert drückt Spitzen systematisch
    /// nach unten, und genau die Spitzen sind es, die bei einem Sprung beurteilt werden.
    /// </remarks>
    public SmoothingMethod Smoothing { get; set; } = SmoothingMethod.SavitzkyGolay;


    public double SmoothingFor(Views.SeriesKind kind) => kind switch
    {
        Views.SeriesKind.Altitude => SmoothingAltitude,
        Views.SeriesKind.Speed => SmoothingSpeed,
        Views.SeriesKind.Temperature => SmoothingTemperature,
        Views.SeriesKind.AccMagnitude => SmoothingAcc,
        Views.SeriesKind.AccX => SmoothingAccX,
        Views.SeriesKind.AccY => SmoothingAccY,
        Views.SeriesKind.AccZ => SmoothingAccZ,
        _ => 0
    };

    public void SetSmoothing(Views.SeriesKind kind, double seconds)
    {
        double v = Math.Clamp(seconds, 0, MaxSmoothingSeconds);
        switch (kind)
        {
            case Views.SeriesKind.Altitude: SmoothingAltitude = v; break;
            case Views.SeriesKind.Speed: SmoothingSpeed = v; break;
            case Views.SeriesKind.Temperature: SmoothingTemperature = v; break;
            case Views.SeriesKind.AccMagnitude: SmoothingAcc = v; break;
            case Views.SeriesKind.AccX: SmoothingAccX = v; break;
            case Views.SeriesKind.AccY: SmoothingAccY = v; break;
            case Views.SeriesKind.AccZ: SmoothingAccZ = v; break;
        }
    }

    public bool AnySmoothingActive =>
        SmoothingAltitude > 0 || SmoothingSpeed > 0 || SmoothingTemperature > 0 ||
        SmoothingAcc > 0 || SmoothingAccX > 0 || SmoothingAccY > 0 || SmoothingAccZ > 0;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppInfo.SettingsFile))
            {
                string json = File.ReadAllText(AppInfo.SettingsFile);
                AppSettings loaded = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
                loaded.Migrate();
                return loaded;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Einstellungen konnten nicht gelesen werden: " + ex.Message);
        }

        return new AppSettings();
    }

    /// <summary>Hebt eine aeltere Einstellungsdatei auf den aktuellen Stand.</summary>
    private void Migrate()
    {
        if (SettingsVersion >= CurrentSettingsVersion)
        {
            return;
        }

        if (SettingsVersion < 2)
        {
            // Stand 1 zeigte Temperatur und Einzelachsen nicht an. Ein Auswerteprogramm soll
            // aber erst einmal alles zeigen, was das Geraet gemessen hat; ausblenden kann der
            // Anwender selbst.
            ShowAltitude = true;
            ShowSpeed = true;
            ShowTemperature = true;
            ShowAcceleration = true;
            ShowAccX = true;
            ShowAccY = true;
            ShowAccZ = true;
            ShowLegend = true;
        }

        SettingsVersion = CurrentSettingsVersion;
        Save();
        AppLog.Info("Einstellungen auf Stand " + CurrentSettingsVersion + " gehoben.");
    }

    public void Save()
    {
        SettingsVersion = CurrentSettingsVersion;
        try
        {
            Directory.CreateDirectory(AppInfo.DataDirectory);
            File.WriteAllText(AppInfo.SettingsFile, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex)
        {
            AppLog.Warn("Einstellungen konnten nicht gespeichert werden: " + ex.Message);
        }
    }

    public AltitudeReference BuildReference(double? groundPressureHpa, double? startPressureHpa = null) => new()
    {
        Mode = AltitudeMode,
        QnhHpa = QnhHpa,
        StationElevationM = StationElevationM,
        GroundPressureHpa = groundPressureHpa,
        StartPressureHpa = startPressureHpa,
        ApplyTemperatureCorrection = TemperatureCorrection
    };
}

public enum ChartStyle
{
    Light,
    Dark,
    Slate
}

/// <summary>Darstellung einer Kurve.</summary>
public sealed class SeriesStyle
{
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#000000";

    [JsonPropertyName("width")]
    public double Width { get; set; } = 1.5;

    /// <summary>Solid, Dashed, DenselyDashed oder Dotted.</summary>
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = "Solid";

    public SeriesStyle Clone() => new() { Color = Color, Width = Width, Pattern = Pattern };

    /// <summary>
    /// Vorgabe je Kurve. Die Farben stammen aus der Okabe-Ito-Palette und bleiben bei allen
    /// verbreiteten Formen der Farbfehlsichtigkeit unterscheidbar.
    /// </summary>
    public static SeriesStyle Default(Views.SeriesKind kind) => kind switch
    {
        Views.SeriesKind.Altitude => new SeriesStyle { Color = "#0B0B0B", Width = 2.2 },
        Views.SeriesKind.Speed => new SeriesStyle { Color = "#0072B2", Width = 1.7 },
        Views.SeriesKind.Temperature => new SeriesStyle { Color = "#D55E00", Width = 1.3, Pattern = "Dashed" },
        Views.SeriesKind.AccMagnitude => new SeriesStyle { Color = "#CC79A7", Width = 1.4 },
        Views.SeriesKind.AccX => new SeriesStyle { Color = "#56B4E9", Width = 1.1 },
        Views.SeriesKind.AccY => new SeriesStyle { Color = "#009E73", Width = 1.1 },
        _ => new SeriesStyle { Color = "#E69F00", Width = 1.1 }
    };
}

/// <summary>Basisklasse mit Änderungsbenachrichtigung.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);
}
