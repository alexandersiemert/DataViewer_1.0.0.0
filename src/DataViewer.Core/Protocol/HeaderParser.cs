using System.Globalization;
using System.Text;
using Siemert.DataViewer.Core.Model;

namespace Siemert.DataViewer.Core.Protocol;

/// <summary>
/// Liest den 128 Byte großen Gerätekopf. Er kommt sowohl als Antwort auf 'I' (dann mit
/// vorangestelltem Echo-Zeichen) als auch als erster Block einer 'G'-Antwort (dann ohne).
/// </summary>
public static class HeaderParser
{
    private const int OffsetChecksumLow = 0;
    private const int OffsetChecksumHigh = 2;
    private const int OffsetManufacturer = 56;
    private const int ManufacturerLength = 16;
    private const int OffsetSerial = 48;
    private const int OffsetModelCode = 52;
    private const int OffsetProdDay = 80;
    private const int OffsetProdMonth = 82;
    private const int OffsetProdYear = 84;

    /// <summary>Mindestlänge, die für alle ausgewerteten Felder gebraucht wird.</summary>
    public const int MinimumHexLength = 88;

    /// <summary>
    /// Entfernt ein eventuell vorangestelltes Echo-Zeichen und gibt den reinen Kopf zurück.
    /// </summary>
    public static string Normalize(string response)
    {
        ArgumentNullException.ThrowIfNull(response);
        string trimmed = response.Trim();
        if (trimmed.Length > 0 && (trimmed[0] == 'I' || trimmed[0] == 'i'))
        {
            trimmed = trimmed[1..];
        }

        return trimmed;
    }

    public static bool TryParse(string rawHeader, out DeviceInfo? info, out string? error)
    {
        info = null;
        error = null;

        if (string.IsNullOrWhiteSpace(rawHeader))
        {
            error = "Der Gerätekopf ist leer.";
            return false;
        }

        string hdr = Normalize(rawHeader);
        if (hdr.Length < MinimumHexLength)
        {
            error = $"Der Gerätekopf ist zu kurz ({hdr.Length} statt mindestens {MinimumHexLength} Zeichen).";
            return false;
        }

        if (!int.TryParse(hdr.AsSpan(OffsetSerial, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int serial))
        {
            error = "Die Seriennummer im Gerätekopf ist nicht lesbar.";
            return false;
        }

        string modelCode = hdr.Substring(OffsetModelCode, 2).ToUpperInvariant();
        string model = modelCode switch
        {
            "20" => "SI-TL1",
            _ => DeviceInfo.UnknownModel
        };

        DateOnly? produced = null;
        if (int.TryParse(hdr.AsSpan(OffsetProdDay, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int day) &&
            int.TryParse(hdr.AsSpan(OffsetProdMonth, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int month) &&
            int.TryParse(hdr.AsSpan(OffsetProdYear, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int year) &&
            year is >= 2000 and <= 2199 && month is >= 1 and <= 12 &&
            day >= 1 && day <= DateTime.DaysInMonth(year, month))
        {
            produced = new DateOnly(year, month, day);
        }

        info = new DeviceInfo
        {
            Model = model,
            ModelCode = modelCode,
            SerialNumber = serial,
            ProductionDate = produced,
            Checksum = (hdr.Substring(OffsetChecksumHigh, 2) + hdr.Substring(OffsetChecksumLow, 2)).ToUpperInvariant(),
            RawHeader = hdr
        };

        return true;
    }

    /// <summary>
    /// Liest die im Kopf hinterlegte Herstellerkennung als Klartext (Diagnose/Plausibilitätsprüfung).
    /// </summary>
    public static string ReadManufacturer(string rawHeader)
    {
        string hdr = Normalize(rawHeader);
        if (hdr.Length < OffsetManufacturer + ManufacturerLength)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < ManufacturerLength; i += 2)
        {
            if (!int.TryParse(hdr.AsSpan(OffsetManufacturer + i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int b))
            {
                break;
            }

            if (b is >= 32 and < 127)
            {
                sb.Append((char)b);
            }
        }

        return sb.ToString().Trim();
    }
}
