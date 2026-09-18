using System.Globalization;
using System.Text;
using Siemert.DataViewer.Core.Model;
using Siemert.DataViewer.Core.Protocol;

namespace Siemert.DataViewer.Core.Device;

/// <summary>
/// Mitschrift einer Live-Aufzeichnung.
/// </summary>
/// <remarks>
/// <para>
/// Aufgezeichnet werden die <b>unveränderten Antworten des Geräts</b> auf die Befehle
/// <c>D</c> (Temperatur und Druck) und <c>M</c> (Beschleunigung), je Zeile ein Abfragetakt,
/// dazu die Zeit seit Beginn. Es werden keine ausgewerteten Werte gespeichert.
/// </para>
/// <para>
/// Das ist dieselbe Regel wie beim Auslesen des Gerätespeichers: Was gespeichert wird, ist das,
/// was das Gerät gesendet hat. Die Auswertung geschieht beim Öffnen und benutzt dieselben
/// Zerlegungsfunktionen wie die Live-Anzeige. Wird dort später ein Fehler behoben, zeigt eine
/// alte Datei danach die berichtigten Werte.
/// </para>
/// <para>
/// Die einzige Veränderung an den Antworten ist das Entfernen von Wagenrücklauf und
/// Zeilenvorschub. Beide sind Rahmen der Übertragung, keine Nutzdaten, und würden das
/// Zeilenformat der Mitschrift zerstören.
/// </para>
/// </remarks>
public static class LiveLog
{
    /// <summary>Kennung in der ersten Zeile.</summary>
    public const string Magic = "#SIEMERT-DATAVIEWER-LIVE";

    /// <summary>Fassung des Formats.</summary>
    public const int FormatVersion = 1;

    /// <summary>Ein Abfragetakt mit den Rohantworten des Geräts.</summary>
    public sealed record Entry(double Seconds, string Environment, string Acceleration);

    /// <summary>
    /// Schreibt die Mitschrift.
    /// </summary>
    /// <param name="entries">Die Takte in zeitlicher Reihenfolge.</param>
    /// <param name="device">Gerät, von dem die Werte stammen.</param>
    /// <param name="startedAt">Ortszeit des ersten Takts.</param>
    /// <param name="intervalSeconds">Angestrebter Abstand zweier Takte.</param>
    public static string Write(
        IReadOnlyList<Entry> entries,
        DeviceInfo? device,
        DateTime startedAt,
        double intervalSeconds)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var sb = new StringBuilder();
        sb.Append(Magic).Append(' ').Append(FormatVersion).Append('\n');
        sb.Append("#geraet=").Append(device?.Model ?? "unbekannt").Append('\n');
        sb.Append("#seriennummer=").Append(device?.SerialNumber.ToString(CultureInfo.InvariantCulture) ?? "0").Append('\n');
        sb.Append("#kopf=").Append(device?.RawHeader ?? string.Empty).Append('\n');
        sb.Append("#beginn=").Append(startedAt.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("#takt=").Append(intervalSeconds.ToString("F3", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("#spalten=sekunden;antwort-D;antwort-M").Append('\n');

        foreach (Entry e in entries)
        {
            sb.Append(e.Seconds.ToString("F3", CultureInfo.InvariantCulture))
              .Append(';').Append(Clean(e.Environment))
              .Append(';').Append(Clean(e.Acceleration))
              .Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Wahr, wenn der Text eine Live-Mitschrift ist.</summary>
    public static bool IsLiveLog(string? text) =>
        text is not null && text.TrimStart().StartsWith(Magic, StringComparison.Ordinal);

    /// <summary>
    /// Wertet eine Mitschrift aus.
    /// </summary>
    /// <remarks>
    /// Zerlegt wird mit denselben Funktionen, die auch die Live-Anzeige benutzt. Takte, deren
    /// Antworten sich nicht zerlegen lassen, werden übersprungen und gemeldet, nicht geraten.
    /// </remarks>
    public static DecodeResult Parse(string? text)
    {
        var messages = new List<DecodeMessage>();

        if (!IsLiveLog(text))
        {
            messages.Add(new DecodeMessage(DecodeSeverity.Error, "Die Datei ist keine Live-Mitschrift."));
            return new DecodeResult { Messages = messages };
        }

        string[] lines = text!.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        DateTime? start = null;
        double interval = 1.0;
        string header = string.Empty;
        var samples = new List<Sample>();
        int skipped = 0;

        foreach (string line in lines)
        {
            if (line.StartsWith('#'))
            {
                if (line.StartsWith("#beginn=", StringComparison.Ordinal) &&
                    DateTime.TryParse(line[8..], CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out DateTime parsed))
                {
                    start = parsed;
                }
                else if (line.StartsWith("#takt=", StringComparison.Ordinal) &&
                         double.TryParse(line[6..], NumberStyles.Float, CultureInfo.InvariantCulture, out double t) &&
                         t > 0)
                {
                    interval = t;
                }
                else if (line.StartsWith("#kopf=", StringComparison.Ordinal))
                {
                    header = line[6..];
                }

                continue;
            }

            string[] parts = line.Split(';');
            if (parts.Length < 3 ||
                !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
            {
                skipped++;
                continue;
            }

            (double TemperatureC, double PressureHpa)? env = LoggerCommands.ParseEnvironment(parts[1]);
            (double X, double Y, double Z)? acc = LoggerCommands.ParseAcceleration(parts[2]);

            if (env is null || acc is null)
            {
                skipped++;
                continue;
            }

            samples.Add(new Sample
            {
                TimeSeconds = seconds,
                PressureHpa = env.Value.PressureHpa,
                TemperatureC = env.Value.TemperatureC,
                AccX = acc.Value.X,
                AccY = acc.Value.Y,
                AccZ = acc.Value.Z
            });
        }

        if (skipped > 0)
        {
            messages.Add(new DecodeMessage(DecodeSeverity.Warning,
                skipped + " Takt(e) der Mitschrift waren nicht auswertbar und wurden übergangen."));
        }

        if (samples.Count == 0)
        {
            messages.Add(new DecodeMessage(DecodeSeverity.Error, "Die Mitschrift enthält keinen auswertbaren Messpunkt."));
            return new DecodeResult { Messages = messages, RawPayload = text };
        }

        DateTime begin = start ?? DateTime.Now;

        var recording = new Recording
        {
            StartTime = begin,
            StartTimeOfDay = begin.TimeOfDay,
            EndTime = begin.AddSeconds(samples[^1].TimeSeconds),
            EndTimeOfDay = begin.TimeOfDay + TimeSpan.FromSeconds(samples[^1].TimeSeconds),
            ClockWasSet = true,
            StartPressureHpa = samples[0].PressureHpa,
            StartTemperatureC = samples[0].TemperatureC,
            EndPressureHpa = samples[^1].PressureHpa,
            EndTemperatureC = samples[^1].TemperatureC,
            Samples = samples,
            IsComplete = true,
            IsLive = true,
            SampleIntervalSeconds = interval,
            RawSegment = text,
            Warnings = ["Live am Rechner mitgeschrieben, nicht aus dem Gerätespeicher gelesen."]
        };

        DeviceInfo? device = null;
        if (header.Length > 0)
        {
            HeaderParser.TryParse(header, out device, out _);
        }

        messages.Add(new DecodeMessage(DecodeSeverity.Info,
            samples.Count + " Messpunkte aus der Live-Mitschrift, Takt " +
            interval.ToString("F2", CultureInfo.InvariantCulture) + " s."));

        return new DecodeResult
        {
            Recordings = [recording],
            Messages = messages,
            RawPayload = text,
            RawContext = header,
            Device = device
        };
    }

    /// <summary>Entfernt Zeilenrahmen und Trennzeichen aus einer Geräteantwort.</summary>
    private static string Clean(string? answer) =>
        (answer ?? string.Empty)
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty)
            .Replace(";", string.Empty)
            .Trim();
}
