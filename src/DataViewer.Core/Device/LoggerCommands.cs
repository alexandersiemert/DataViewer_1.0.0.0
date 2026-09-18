using System.Globalization;
using Siemert.DataViewer.Core.Protocol;

namespace Siemert.DataViewer.Core.Device;

/// <summary>Uhrzeit und Datum, wie der Logger sie fuehrt.</summary>
public sealed record LoggerClock(TimeSpan TimeOfDay, DateTime? Date, bool IsSet)
{
    public string Describe() => IsSet && Date.HasValue
        ? Date.Value.ToString("dd.MM.yyyy") + " " + TimeOfDay.ToString(@"hh\:mm\:ss")
        : TimeOfDay.ToString(@"hh\:mm\:ss") + " (kein Datum gesetzt)";
}

/// <summary>Momentanwerte des Geraets.</summary>
public sealed record LoggerLiveReading(double TemperatureC, double PressureHpa,
    double AccX, double AccY, double AccZ)
{
    public double AccMagnitude => Math.Sqrt((AccX * AccX) + (AccY * AccY) + (AccZ * AccZ));
}

/// <summary>Kenndaten, die das Geraet auf Einzelbefehle meldet.</summary>
public sealed record LoggerDeviceFacts(int Checksum, int MemoryWraps);

/// <summary>
/// Einzelbefehle des SI-TL1 jenseits des Auslesens.
/// </summary>
/// <remarks>
/// Grundlage ist die Befehlsuebersicht des Herstellers. Alle hier verwendeten Befehle wurden am
/// Geraet mit der Seriennummer 349 nachgeprueft.
/// <list type="bullet">
/// <item><c>L</c> Uhrzeit und Datum lesen</item>
/// <item><c>U</c> Uhr und Kalender stellen</item>
/// <item><c>D</c> Temperatur und Druck als Momentanwert</item>
/// <item><c>M</c> Beschleunigung als Momentanwert</item>
/// <item><c>C</c> Geraetekennung (Pruefsumme)</item>
/// <item><c>R</c> Anzahl der Speicherumlaeufe</item>
/// <item><c>B</c> Kommunikation beenden</item>
/// </list>
/// Nicht angebunden sind <c>Z</c> (Fehler zuruecksetzen) und <c>K</c> (Korrektur des Druckwerts).
/// Beide veraendern den Zustand des Geraets und gehoeren nicht in eine Auswertesoftware, solange
/// ihre Wirkung nicht vollstaendig dokumentiert ist.
/// </remarks>
public static class LoggerCommands
{
    public const string ReadClock = "L";
    public const string SetClock = "U";
    public const string ReadEnvironment = "D";
    public const string ReadAcceleration = "M";
    public const string ReadChecksum = "C";
    public const string ReadMemoryWraps = "R";
    public const string EndCommunication = "B";

    /// <summary>Zerlegt die Antwort auf <c>L</c>: ss mm hh (hex) TT MM JJJJ (dezimal).</summary>
    public static LoggerClock? ParseClock(string response)
    {
        string s = Clean(response, ReadClock);
        if (s.Length < 14)
        {
            return null;
        }

        if (!TimestampCodec.TryDecode(s.AsSpan(0, TimestampCodec.HexLength),
                out DateTime? stamp, out TimeSpan tod, out bool set))
        {
            return null;
        }

        return new LoggerClock(tod, stamp, set);
    }

    /// <summary>
    /// Baut den Befehl zum Stellen der Uhr.
    /// </summary>
    /// <remarks>
    /// Reihenfolge laut Herstellerangabe: Minute und Stunde hexadezimal, danach Tag, Monat und
    /// Jahr dezimal. Die Sekunden setzt das Geraet selbst auf null - deshalb darf der Befehl nur
    /// im Augenblick eines vollen Minutenwechsels abgesetzt werden, sonst geht die Uhr um bis zu
    /// 59 Sekunden vor.
    /// </remarks>
    public static string BuildSetClock(DateTime when) =>
        SetClock +
        when.Minute.ToString("X2", CultureInfo.InvariantCulture) +
        when.Hour.ToString("X2", CultureInfo.InvariantCulture) +
        when.Day.ToString("D2", CultureInfo.InvariantCulture) +
        when.Month.ToString("D2", CultureInfo.InvariantCulture) +
        when.Year.ToString("D4", CultureInfo.InvariantCulture);

    /// <summary>Zerlegt die Antwort auf <c>D</c>: fuenf Ziffern Temperatur, Leerzeichen, fuenf Ziffern Druck.</summary>
    public static (double TemperatureC, double PressureHpa)? ParseEnvironment(string response)
    {
        string s = response.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        if (s.StartsWith(ReadEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            s = s[1..];
        }

        string[] parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int t) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int p))
        {
            return null;
        }

        return ((t - LoggerProtocol.TemperatureOffset) / LoggerProtocol.TemperatureScale,
                p / LoggerProtocol.PressureScale);
    }

    /// <summary>
    /// Zerlegt die Antwort auf <c>M</c>: drei 16-Bit-Werte fuer X, Y und Z.
    /// </summary>
    /// <remarks>
    /// Die Skalierung entspricht der des gespeicherten Datenstroms. Die handschriftliche Notiz
    /// des Herstellers nennt hier "Wert / 2048"; das ergibt fuer ein ruhendes Geraet aber nur
    /// 0,64 g statt der physikalisch zwingenden 1,00 g und trifft daher nicht zu.
    /// </remarks>
    public static (double X, double Y, double Z)? ParseAcceleration(string response)
    {
        string s = Clean(response, ReadAcceleration);
        if (s.Length < 12)
        {
            return null;
        }

        (double x, _) = PayloadDecoder.DecodeAcceleration(s.AsSpan(0, 4));
        (double y, _) = PayloadDecoder.DecodeAcceleration(s.AsSpan(4, 4));
        (double z, _) = PayloadDecoder.DecodeAcceleration(s.AsSpan(8, 4));
        return (x, y, z);
    }

    /// <summary>Zerlegt eine Antwort mit reiner Dezimalzahl, etwa auf <c>C</c> oder <c>R</c>.</summary>
    public static int? ParseNumber(string response, string command)
    {
        string s = Clean(response, command);
        return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int v) ? v : null;
    }

    private static string Clean(string response, string command)
    {
        string s = response.Replace("\r", string.Empty)
                           .Replace("\n", string.Empty)
                           .Replace("?", string.Empty)
                           .Trim();

        if (s.StartsWith(command, StringComparison.OrdinalIgnoreCase))
        {
            s = s[command.Length..];
        }

        return s.Trim();
    }

    /// <summary>
    /// Wartet bis zum naechsten vollen Minutenwechsel.
    /// </summary>
    /// <remarks>
    /// Der Hersteller hebt das eigens hervor: Das Geraet setzt die Sekunden beim Stellen auf null.
    /// Wird der Befehl mitten in einer Minute abgesetzt, geht die Loggeruhr anschliessend um die
    /// bereits verstrichenen Sekunden vor.
    /// </remarks>
    public static async Task<DateTime> WaitForFullMinuteAsync(
        IProgress<TimeSpan>? countdown = null, CancellationToken cancellationToken = default)
    {
        DateTime next = DateTime.Now.AddMinutes(1);
        next = new DateTime(next.Year, next.Month, next.Day, next.Hour, next.Minute, 0, next.Kind);

        while (true)
        {
            TimeSpan left = next - DateTime.Now;
            if (left <= TimeSpan.Zero)
            {
                return next;
            }

            countdown?.Report(left);
            await Task.Delay(left > TimeSpan.FromMilliseconds(250)
                ? TimeSpan.FromMilliseconds(200)
                : left, cancellationToken).ConfigureAwait(false);
        }
    }
}
