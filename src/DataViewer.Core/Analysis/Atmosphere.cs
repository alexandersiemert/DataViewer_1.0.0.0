namespace Siemert.DataViewer.Core.Analysis;

/// <summary>Worauf sich die angezeigte Hoehe bezieht.</summary>
public enum AltitudeMode
{
    /// <summary>
    /// Druckhoehe gegen die Normatmosphaere (1013,25 hPa). Das ist die Rohgroesse des Geraets
    /// und entspricht dem Verhalten frueherer Programmversionen. Sie ist weder Hoehe ueber NN
    /// noch Hoehe ueber Grund.
    /// </summary>
    PressureAltitude,

    /// <summary>Hoehe ueber Normalnull, gerechnet mit dem eingestellten QNH.</summary>
    MeanSeaLevel,

    /// <summary>Hoehe ueber Grund: MSL abzueglich der eingetragenen Platzhoehe.</summary>
    AboveGroundLevel,

    /// <summary>
    /// Hoehe ueber dem Landepunkt. Als Bezug dient der Druck am Ende der Aufnahme.
    /// Das ist in der Praxis das genaueste Verfahren, weil sich ein QNH-Fehler im Wesentlichen
    /// wie ein Druckversatz verhaelt und dabei herausfaellt - und es braucht keinerlei Eingabe.
    /// </summary>
    GroundZero,

    /// <summary>
    /// Nullpunkt am <b>Anfang</b> der Aufnahme. Als Bezug dient der erste gemessene Druck.
    /// </summary>
    /// <remarks>
    /// Sinnvoll, wenn der Logger am Absetzpunkt eingeschaltet wurde und die Hoehe relativ dazu
    /// interessiert, oder wenn die Aufnahme nicht bis zur Landung reicht und der Enddruck
    /// deshalb kein Bodendruck ist.
    /// </remarks>
    StartZero
}

/// <summary>
/// Barometrische Hoehenrechnung nach der Normatmosphaere (ISA).
/// </summary>
public static class Atmosphere
{
    /// <summary>Normdruck auf Meereshoehe in hPa.</summary>
    public const double StandardPressureHpa = 1013.25;

    /// <summary>Normtemperatur auf Meereshoehe in Kelvin.</summary>
    public const double StandardTemperatureK = 288.15;

    /// <summary>Temperaturgradient der Troposphaere in K/m.</summary>
    public const double LapseRateKPerM = 0.0065;

    /// <summary>Exponent R*L/g der barometrischen Hoehenformel.</summary>
    public const double Exponent = 0.190294957;

    /// <summary>
    /// Hoehe ueber der Flaeche, auf der <paramref name="referencePressureHpa"/> herrscht.
    /// </summary>
    public static double AltitudeMeters(double pressureHpa, double referencePressureHpa)
    {
        if (pressureHpa <= 0 || referencePressureHpa <= 0)
        {
            return double.NaN;
        }

        return (StandardTemperatureK / LapseRateKPerM) *
               (1.0 - Math.Pow(pressureHpa / referencePressureHpa, Exponent));
    }

    /// <summary>
    /// Temperaturkorrektur der barometrischen Hoehe. Die Normatmosphaere unterstellt 15 °C am
    /// Boden; weicht die tatsaechliche Luftsaeule davon ab, ist die angezeigte Hoehe um etwa
    /// 0,35 % je Kelvin falsch - in kalter Luft zu hoch, in warmer zu niedrig.
    /// </summary>
    /// <param name="indicatedAltitudeM">Unkorrigierte Hoehe ueber dem Bezugspunkt.</param>
    /// <param name="meanColumnTemperatureC">Mittlere Temperatur der Luftsaeule in °C.</param>
    public static double ApplyTemperatureCorrection(double indicatedAltitudeM, double meanColumnTemperatureC)
    {
        if (double.IsNaN(indicatedAltitudeM))
        {
            return double.NaN;
        }

        double isaMeanK = StandardTemperatureK - (LapseRateKPerM * indicatedAltitudeM / 2.0);
        double actualMeanK = meanColumnTemperatureC + 273.15;
        if (isaMeanK <= 0)
        {
            return indicatedAltitudeM;
        }

        return indicatedAltitudeM * (actualMeanK / isaMeanK);
    }

    /// <summary>
    /// Umrechnung eines QNH in den Bezugsdruck fuer eine Platzhoehe (QFE-Naeherung).
    /// </summary>
    public static double PressureAtElevation(double qnhHpa, double elevationMeters)
    {
        if (elevationMeters == 0)
        {
            return qnhHpa;
        }

        double ratio = 1.0 - (LapseRateKPerM * elevationMeters / StandardTemperatureK);
        return qnhHpa * Math.Pow(ratio, 1.0 / Exponent);
    }
}

/// <summary>
/// Die vollstaendige Hoehenreferenz einer Auswertung. Sie wird mit jeder Datei gespeichert und
/// im Diagramm dauerhaft angezeigt, damit nie unklar ist, worauf sich eine Hoehe bezieht.
/// </summary>
public sealed class AltitudeReference
{
    public AltitudeMode Mode { get; init; } = AltitudeMode.GroundZero;

    /// <summary>Luftdruck auf Meereshoehe zum Zeitpunkt des Sprungs.</summary>
    public double QnhHpa { get; init; } = Atmosphere.StandardPressureHpa;

    /// <summary>Hoehe des Absetz- bzw. Landegelaendes ueber NN in Metern.</summary>
    public double StationElevationM { get; init; }

    /// <summary>Bodendruck fuer <see cref="AltitudeMode.GroundZero"/>, in der Regel der Enddruck der Aufnahme.</summary>
    public double? GroundPressureHpa { get; init; }

    /// <summary>Anfangsdruck fuer <see cref="AltitudeMode.StartZero"/>, der erste Druck der Aufnahme.</summary>
    public double? StartPressureHpa { get; init; }

    /// <summary>Optionale Temperaturkorrektur anhand der gemessenen Lufttemperatur.</summary>
    public bool ApplyTemperatureCorrection { get; init; }

    public static AltitudeReference Standard { get; } = new() { Mode = AltitudeMode.PressureAltitude };

    /// <summary>Rechnet einen Druckwert in eine Hoehe in Metern um.</summary>
    public double ToAltitudeMeters(double pressureHpa, double temperatureC = double.NaN)
    {
        double reference = Mode switch
        {
            AltitudeMode.PressureAltitude => Atmosphere.StandardPressureHpa,
            AltitudeMode.MeanSeaLevel => QnhHpa,
            AltitudeMode.AboveGroundLevel => QnhHpa,
            AltitudeMode.GroundZero => GroundPressureHpa ?? Atmosphere.StandardPressureHpa,
            AltitudeMode.StartZero => StartPressureHpa ?? Atmosphere.StandardPressureHpa,
            _ => Atmosphere.StandardPressureHpa
        };

        double altitude = Atmosphere.AltitudeMeters(pressureHpa, reference);

        if (Mode == AltitudeMode.AboveGroundLevel)
        {
            altitude -= StationElevationM;
        }

        if (ApplyTemperatureCorrection && !double.IsNaN(temperatureC))
        {
            altitude = Atmosphere.ApplyTemperatureCorrection(altitude, temperatureC);
        }

        return altitude;
    }

    /// <summary>Kurztext fuer die dauerhafte Anzeige ueber dem Diagramm.</summary>
    public string Describe() => Mode switch
    {
        AltitudeMode.PressureAltitude =>
            "Druckhöhe (Normatmosphäre 1013,25 hPa). nicht über Grund",
        AltitudeMode.MeanSeaLevel =>
            $"über NN · QNH {QnhHpa:F0} hPa",
        AltitudeMode.AboveGroundLevel =>
            $"über Grund · QNH {QnhHpa:F0} hPa · Platzhöhe {StationElevationM:F0} m",
        AltitudeMode.GroundZero =>
            GroundPressureHpa.HasValue
                ? $"über Landepunkt · Bodendruck {GroundPressureHpa.Value:F1} hPa"
                : "über Landepunkt (kein Bodendruck verfügbar)",
        AltitudeMode.StartZero =>
            StartPressureHpa.HasValue
                ? $"ab Aufnahmebeginn · Anfangsdruck {StartPressureHpa.Value:F1} hPa"
                : "ab Aufnahmebeginn (kein Anfangsdruck verfügbar)",
        _ => "unbekannter Bezug"
    };

    /// <summary>Kurzform fuer Achsenbeschriftungen.</summary>
    public string ShortLabel => Mode switch
    {
        AltitudeMode.PressureAltitude => "Druckhöhe",
        AltitudeMode.MeanSeaLevel => "Höhe ü. NN",
        AltitudeMode.AboveGroundLevel => "Höhe ü. Grund",
        AltitudeMode.GroundZero => "Höhe ü. Landepunkt",
        AltitudeMode.StartZero => "Höhe ab Beginn",
        _ => "Höhe"
    };
}
