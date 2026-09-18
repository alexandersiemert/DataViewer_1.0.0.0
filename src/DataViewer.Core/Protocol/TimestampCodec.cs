using System.Globalization;

namespace Siemert.DataViewer.Core.Protocol;

/// <summary>
/// Zeitstempel des SI-TL1, 14 Hexzeichen: <c>ss mm hh dd MM yyyy</c>.
/// </summary>
/// <remarks>
/// Die beiden Hälften sind unterschiedlich kodiert, und das ist kein Fehler, sondern Firmwarestand:
/// <list type="bullet">
/// <item><b>Uhrzeit binär</b> – belegt durch einen Mitschnitt mit Minutenwert 0x2E (= 46); als BCD
/// wäre 'E' keine gültige Ziffer.</item>
/// <item><b>Datum als Dezimalziffern (BCD)</b> – belegt durch das vierstellige Jahr "2026", das als
/// Binärwert 0x07EA lauten müsste und dann keine gültige Jahreszahl ergäbe.</item>
/// </list>
/// Ist die Loggeruhr nicht gestellt, liefert das Gerät ein Datum aus Nullen. Wir geben dann bewusst
/// kein Ersatzdatum zurück, sondern melden die Uhrzeit und <c>clockWasSet = false</c>.
/// </remarks>
public static class TimestampCodec
{
    public const int HexLength = 14;

    /// <summary>
    /// Dekodiert einen Zeitstempel. Wirft nie: unplausible Werte führen zu <c>false</c>.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<char> hex, out DateTime? timestamp, out TimeSpan timeOfDay, out bool clockWasSet)
    {
        timestamp = null;
        timeOfDay = TimeSpan.Zero;
        clockWasSet = false;

        if (hex.Length != HexLength)
        {
            return false;
        }

        // Uhrzeit: binär kodiert.
        if (!TryHex(hex.Slice(0, 2), out int seconds) ||
            !TryHex(hex.Slice(2, 2), out int minutes) ||
            !TryHex(hex.Slice(4, 2), out int hours))
        {
            return false;
        }

        if (seconds > 59 || minutes > 59 || hours > 23)
        {
            return false;
        }

        timeOfDay = new TimeSpan(hours, minutes, seconds);

        // Datum: Dezimalziffern.
        if (!TryDecimal(hex.Slice(6, 2), out int day) ||
            !TryDecimal(hex.Slice(8, 2), out int month) ||
            !TryDecimal(hex.Slice(10, 4), out int year))
        {
            // Uhrzeit bleibt gültig, das Datum nicht.
            return true;
        }

        if (year < 2000 || year > 2199 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return true;
        }

        timestamp = new DateTime(year, month, day, hours, minutes, seconds, DateTimeKind.Unspecified);
        clockWasSet = true;
        return true;
    }

    private static bool TryHex(ReadOnlySpan<char> s, out int value) =>
        int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

    private static bool TryDecimal(ReadOnlySpan<char> s, out int value) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
