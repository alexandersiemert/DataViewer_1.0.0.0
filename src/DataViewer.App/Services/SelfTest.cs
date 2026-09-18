using System.IO;
using System.Text;
using System.Windows;
using Siemert.DataViewer.App.Views;
using ScottPlot;
using Siemert.DataViewer.Core.Analysis;
using Siemert.DataViewer.Core.Io;
using Siemert.DataViewer.Core.Model;
using Siemert.DataViewer.Core.Protocol;
using Siemert.DataViewer.Core.Units;

namespace Siemert.DataViewer.App.Services;

/// <summary>
/// Selbsttest ohne Oberfläche.
/// </summary>
/// <remarks>
/// Aufruf: <c>SiemertDataViewer.exe --selftest &lt;Rohdatei&gt; [Ziel.png]</c>
/// <para>
/// Der Selbsttest liest eine Rohdatei ein, wertet sie aus und zeichnet das Ergebnis in eine
/// PNG-Datei. Er prüft damit in einem Durchgang Protokolldekodierung, Höhenrechnung,
/// Sprungerkennung und den Zeichenpfad. Für den Kundendienst ist er der schnellste Weg, eine vom
/// Anwender eingeschickte Aufzeichnung nachzuvollziehen, ohne die Oberfläche zu bedienen; in einer
/// Bauprüfung belegt er, dass die Grafikbibliothek auf der Zielplattform arbeitet.
/// </para>
/// </remarks>
public static class SelfTest
{
    private const int AttachParentProcess = -1;

    /// <summary>
    /// Hängt sich an die Konsole des aufrufenden Prozesses. Ohne das bliebe die Ausgabe einer
    /// WPF-Anwendung unsichtbar, weil sie als Fensteranwendung gebunden ist.
    /// </summary>
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    public static int Run(string[] args)
    {
        AttachConsole(AttachParentProcess);
        try
        {
            // Sonst erscheinen Umlaute in der Konsole als Buchstabensalat.
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // Ohne angehaengte Konsole nicht moeglich - unerheblich.
        }

        var log = new StringBuilder();

        try
        {
            string input = args.Length > 1 ? args[1] : string.Empty;
            if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
            {
                Console.Error.WriteLine("Verwendung: SiemertDataViewer.exe --selftest <Rohdatei> [Ziel.png]");
                return 2;
            }

            string output = args.Length > 2
                ? args[2]
                : Path.ChangeExtension(input, ".png");

            log.AppendLine($"{AppInfo.ProductName} {AppInfo.Version} Selbsttest");
            log.AppendLine("Eingabe: " + input);

            string content = File.ReadAllText(input);
            DecodeResult data = PayloadDecoder.Decode(content);

            log.AppendLine($"Aufnahmen: {data.Recordings.Count}, Betriebsereignisse: {data.Events.Count}");

            foreach (DecodeMessage m in data.Messages)
            {
                log.AppendLine("  " + m);
            }

            if (data.Recordings.Count == 0)
            {
                Console.Error.WriteLine(log.ToString());
                Console.Error.WriteLine("FEHLGESCHLAGEN: keine auswertbare Aufnahme gefunden.");
                return 1;
            }

            Recording recording = data.Recordings.OrderByDescending(r => r.Samples.Count).First();

            var reference = new AltitudeReference
            {
                Mode = AltitudeMode.GroundZero,
                GroundPressureHpa = double.IsNaN(recording.EndPressureHpa) ? null : recording.EndPressureHpa
            };

            JumpMetrics metrics = JumpAnalyzer.Analyze(recording, reference);

            log.AppendLine("Ausgewertete Aufnahme: " + recording.DisplayName);
            log.AppendLine($"  Messpunkte      : {recording.Samples.Count}");
            log.AppendLine($"  Dauer           : {recording.Duration.TotalSeconds:F1} s");
            log.AppendLine($"  Druck Start/Ende: {recording.StartPressureHpa:F1} / {recording.EndPressureHpa:F1} hPa");
            log.AppendLine($"  Temp Start/Ende : {recording.StartTemperatureC:F1} / {recording.EndTemperatureC:F1} °C");
            log.AppendLine($"  Höhenbezug      : {reference.Describe()}");
            log.AppendLine($"  Sprungprofil    : {metrics.Note}");

            if (metrics.JumpDetected)
            {
                log.AppendLine($"  Absprunghöhe    : {metrics.ExitAltitudeM:F0} m");
                log.AppendLine($"  Freifall        : {metrics.FreefallSeconds:F1} s");
                log.AppendLine($"  Öffnungshöhe    : {metrics.DeploymentAltitudeM:F0} m");
                log.AppendLine($"  max. Sinkrate   : {metrics.MaxDescentRateMs:F1} m/s");
            }

            RenderPng(recording, reference, metrics, output);
            log.AppendLine("Diagramm geschrieben: " + output);

            // Nebenbei den Dateipfad prüfen: speichern, wieder laden, Prüfsumme bestätigen.
            string tmp = Path.ChangeExtension(output, SdvLogFile.Extension);
            SdvLogFile.Save(tmp, null, data, reference, AppInfo.Version);
            SdvDocument reloaded = SdvLogFile.Load(tmp);
            log.AppendLine($"Dateiformat: gespeichert und erneut gelesen, " +
                           $"{reloaded.Data.Recordings.Count} Aufnahmen, " +
                           $"Prüfsumme {(reloaded.IntegrityVerified ? "bestätigt" : "NICHT bestätigt")}");

            if (!reloaded.IntegrityVerified || reloaded.Data.Recordings.Count != data.Recordings.Count)
            {
                Console.Error.WriteLine(log.ToString());
                return 1;
            }

            Console.Out.WriteLine(log.ToString());
            Console.Out.WriteLine("Selbsttest bestanden.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(log.ToString());
            Console.Error.WriteLine("FEHLGESCHLAGEN: " + ex);
            return 1;
        }
    }

    private static void RenderPng(Recording recording, AltitudeReference reference, JumpMetrics metrics, string path)
    {
        var plot = new Plot();

        int n = recording.Samples.Count;
        var time = new double[n];
        var altitude = new double[n];
        var accel = new double[n];

        for (int i = 0; i < n; i++)
        {
            Sample s = recording.Samples[i];
            time[i] = s.TimeSeconds;
            altitude[i] = reference.ToAltitudeMeters(s.PressureHpa, s.TemperatureC);
            accel[i] = s.AccMagnitude;
        }

        double[] speed = Signal.Derivative(altitude, LoggerProtocol.SampleIntervalSeconds, JumpAnalyzer.DerivativeWindow);

        var alt = plot.Add.Scatter(time, altitude);
        alt.Color = Color.FromHex("#0B0B0B");
        alt.LineWidth = 2;
        alt.MarkerStyle.IsVisible = false;
        alt.LegendText = reference.ShortLabel + " [m]";

        IYAxis speedAxis = plot.Axes.AddRightAxis();
        var sp = plot.Add.Scatter(time, speed);
        sp.Color = Color.FromHex("#0072B2");
        sp.LineWidth = 1.5f;
        sp.MarkerStyle.IsVisible = false;
        sp.LegendText = "Sinkrate [m/s]";
        sp.Axes.YAxis = speedAxis;

        IYAxis accAxis = plot.Axes.AddRightAxis();
        var ac = plot.Add.Scatter(time, accel);
        ac.Color = Color.FromHex("#CC79A7");
        ac.LineWidth = 1.3f;
        ac.MarkerStyle.IsVisible = false;
        ac.LegendText = "Beschleunigung [g]";
        ac.Axes.YAxis = accAxis;

        if (metrics.JumpDetected && metrics.DeploymentTimeSeconds is { } deploy)
        {
            var line = plot.Add.VerticalLine(deploy);
            line.Color = Color.FromHex("#6B7785");
            line.LinePattern = LinePattern.Dotted;
            line.Text = "Öffnung";
        }

        plot.Axes.Left.Label.Text = reference.ShortLabel + " [m]";
        plot.Axes.Bottom.Label.Text = recording.ClockWasSet ? "Uhrzeit" : "Uhrzeit (Uhr nicht gestellt)";
        speedAxis.Label.Text = "Sinkrate [m/s]";
        accAxis.Label.Text = "Beschleunigung [g]";
        plot.Title(RecordingLabel.Full(recording, null) + "   ·   " + reference.Describe());
        plot.ShowLegend(Alignment.UpperRight);
        plot.Axes.AutoScale();

        plot.SavePng(path, 1800, 1000);
    }
    /// <summary>
    /// Baut jedes Fenster der Anwendung einmal auf, ohne es anzuzeigen.
    /// </summary>
    /// <remarks>
    /// Der Uebersetzer prueft XAML nur oberflaechlich: ein falscher Ressourcenschluessel, ein
    /// vertippter Ereignisname oder eine fehlende Eigenschaft faellt erst auf, wenn das Fenster
    /// tatsaechlich aufgebaut wird. Diese Pruefung holt das in den Bauvorgang vor.
    /// Geraetefenster werden nur erzeugt, nicht geoeffnet - es wird also nichts gesendet.
    /// </remarks>
    public static int CheckWindows()
    {
        AttachConsole(AttachParentProcess);
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
        }

        (string Name, Func<Window> Build)[] windows =
        [
            ("AboutWindow", () => new AboutWindow()),
            ("AltitudeReferenceWindow", () => new AltitudeReferenceWindow(new AppSettings(), 1000.0)),
            ("ClockWindow", () => new ClockWindow("COM255", "SI-TL1 (Pruefung)")),
            ("LiveWindow", () => new LiveWindow("COM255", "SI-TL1 (Pruefung)", AltitudeReference.Standard, new AppSettings())),
            ("MainWindow", () => new MainWindow()),
        ];

        int failed = 0;

        foreach ((string name, Func<Window> build) in windows)
        {
            try
            {
                Window w = build();
                w.Close();
                Console.WriteLine($"  in Ordnung   {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"  FEHLER       {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine(failed == 0
            ? $"Fenstertest bestanden: {windows.Length} von {windows.Length}."
            : $"Fenstertest fehlgeschlagen: {failed} von {windows.Length} Fenster.");

        return failed == 0 ? 0 : 1;
    }
}
