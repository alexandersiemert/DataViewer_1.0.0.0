using Siemert.DataViewer.Core.Model;
using Siemert.DataViewer.Core.Protocol;

namespace Siemert.DataViewer.Core.Analysis;

/// <summary>Kennzahlen einer Aufnahme. Alle Hoehen in Metern, alle Geschwindigkeiten in m/s.</summary>
public sealed class JumpMetrics
{
    /// <summary>False, wenn in der Aufnahme kein Sprungprofil zu erkennen ist (z. B. Bodenaufzeichnung).</summary>
    public bool JumpDetected { get; init; }

    /// <summary>Erlaeuterung zur Erkennung - wird dem Anwender angezeigt.</summary>
    public string Note { get; init; } = string.Empty;

    public double MaxAltitudeM { get; init; }

    public double MinAltitudeM { get; init; }

    public double? ExitAltitudeM { get; init; }

    public double? ExitTimeSeconds { get; init; }

    public double? DeploymentAltitudeM { get; init; }

    public double? DeploymentTimeSeconds { get; init; }

    public double? FreefallSeconds { get; init; }

    public double? CanopySeconds { get; init; }

    /// <summary>Groesste Sinkrate als positiver Betrag in m/s.</summary>
    public double MaxDescentRateMs { get; init; }

    /// <summary>Mittlere Sinkrate im Freifall als positiver Betrag in m/s.</summary>
    public double? MeanFreefallRateMs { get; init; }

    /// <summary>Groesster Betrag der Beschleunigung waehrend der Oeffnung in g.</summary>
    public double? MaxOpeningG { get; init; }

    /// <summary>True, wenn der Sensor dabei am Anschlag war - der echte Spitzenwert ist dann hoeher.</summary>
    public bool OpeningGSaturated { get; init; }

    public double MaxAccelerationG { get; init; }

    public bool AnySaturated { get; init; }

    public double MinTemperatureC { get; init; }

    public double MaxTemperatureC { get; init; }

    public double TotalSeconds { get; init; }

    public static JumpMetrics None { get; } = new() { Note = "Keine Daten." };
}

/// <summary>
/// Erkennt Sprungphasen und berechnet die Kennzahlen, die ein Springer tatsaechlich ablesen will.
/// </summary>
/// <remarks>
/// Die Erkennung ist bewusst konservativ: Liegt kein eindeutiges Sprungprofil vor, werden keine
/// Kennzahlen erfunden, sondern <see cref="JumpMetrics.JumpDetected"/> bleibt false. Eine
/// plausibel aussehende, aber falsche Oeffnungshoehe waere schaedlicher als gar keine.
/// </remarks>
public static class JumpAnalyzer
{
    /// <summary>Ab dieser Sinkrate gehen wir von einem Absprung aus (m/s).</summary>
    public const double JumpDetectionRateMs = 12.0;

    /// <summary>Mindest-Hoehenverlust fuer ein Sprungprofil (m).</summary>
    public const double JumpDetectionDropM = 100.0;

    /// <summary>Schwelle, ab der der Absprung als begonnen gilt (m/s).</summary>
    private const double ExitRateMs = 5.0;

    /// <summary>Unterhalb dieser Sinkrate gilt der Schirm als offen (m/s).</summary>
    private const double CanopyRateMs = 11.0;

    /// <summary>Fensterbreite der Ableitung in Messpunkten (9 Punkte = 2,25 s bei 4 Hz).</summary>
    public const int DerivativeWindow = 9;

    public static JumpMetrics Analyze(Recording recording, AltitudeReference reference)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(reference);

        IReadOnlyList<Sample> s = recording.Samples;
        if (s.Count < 8)
        {
            return JumpMetrics.None;
        }

        double dt = recording.SampleIntervalSeconds;
        var altitude = new double[s.Count];
        var accMag = new double[s.Count];
        double minT = double.MaxValue;
        double maxT = double.MinValue;
        double maxAcc = 0;
        bool anySat = false;

        for (int i = 0; i < s.Count; i++)
        {
            altitude[i] = reference.ToAltitudeMeters(s[i].PressureHpa, s[i].TemperatureC);
            accMag[i] = s[i].AccMagnitude;
            if (accMag[i] > maxAcc)
            {
                maxAcc = accMag[i];
            }

            anySat |= s[i].AccSaturated;
            minT = Math.Min(minT, s[i].TemperatureC);
            maxT = Math.Max(maxT, s[i].TemperatureC);
        }

        double[] vSpeed = Signal.Derivative(altitude, dt, DerivativeWindow);

        double maxAlt = altitude.Max();
        double minAlt = altitude.Min();
        double total = s[^1].TimeSeconds;

        // Groesste Sinkrate (vSpeed ist negativ beim Sinken).
        int fastestIdx = 0;
        double fastest = 0;
        for (int i = 0; i < vSpeed.Length; i++)
        {
            if (!double.IsNaN(vSpeed[i]) && vSpeed[i] < fastest)
            {
                fastest = vSpeed[i];
                fastestIdx = i;
            }
        }

        double maxDescent = Math.Abs(fastest);

        var basics = new
        {
            MaxAlt = maxAlt,
            MinAlt = minAlt,
            MaxDescent = maxDescent,
            MaxAcc = maxAcc,
            AnySat = anySat,
            MinT = minT,
            MaxT = maxT,
            Total = total
        };

        if (maxDescent < JumpDetectionRateMs || (maxAlt - minAlt) < JumpDetectionDropM)
        {
            return new JumpMetrics
            {
                JumpDetected = false,
                Note = "Kein Sprungprofil erkannt: Die größte Sinkrate beträgt " +
                       maxDescent.ToString("F1") + " m/s bei " + (maxAlt - minAlt).ToString("F0") +
                       " m Höhenänderung. Die Aufnahme wird als Bodenaufzeichnung behandelt.",
                MaxAltitudeM = basics.MaxAlt,
                MinAltitudeM = basics.MinAlt,
                MaxDescentRateMs = basics.MaxDescent,
                MaxAccelerationG = basics.MaxAcc,
                AnySaturated = basics.AnySat,
                MinTemperatureC = basics.MinT,
                MaxTemperatureC = basics.MaxT,
                TotalSeconds = basics.Total
            };
        }

        // Absprung: rueckwaerts von der groessten Sinkrate bis die Sinkrate klein wird.
        int exitIdx = fastestIdx;
        while (exitIdx > 0 && !double.IsNaN(vSpeed[exitIdx]) && vSpeed[exitIdx] < -ExitRateMs)
        {
            exitIdx--;
        }

        // Oeffnung: vorwaerts ab der groessten Sinkrate bis der Schirm traegt.
        int deployIdx = fastestIdx;
        while (deployIdx < vSpeed.Length - 1 &&
               (double.IsNaN(vSpeed[deployIdx]) || vSpeed[deployIdx] < -CanopyRateMs))
        {
            deployIdx++;
        }

        // Landung: letzter Punkt mit nennenswerter Bewegung.
        int landIdx = s.Count - 1;
        while (landIdx > deployIdx && (double.IsNaN(vSpeed[landIdx]) || Math.Abs(vSpeed[landIdx]) < 0.5))
        {
            landIdx--;
        }

        double freefall = s[deployIdx].TimeSeconds - s[exitIdx].TimeSeconds;
        double canopy = s[landIdx].TimeSeconds - s[deployIdx].TimeSeconds;

        // Mittlere Freifallrate.
        double meanRate = double.NaN;
        if (deployIdx > exitIdx)
        {
            meanRate = Math.Abs((altitude[deployIdx] - altitude[exitIdx]) / (s[deployIdx].TimeSeconds - s[exitIdx].TimeSeconds));
        }

        // Spitzenbeschleunigung im Oeffnungsfenster (+/- 3 s um den Oeffnungspunkt).
        int win = (int)Math.Round(3.0 / dt);
        int oLo = Math.Max(0, deployIdx - win);
        int oHi = Math.Min(s.Count - 1, deployIdx + win);
        double openG = 0;
        bool openSat = false;
        for (int i = oLo; i <= oHi; i++)
        {
            if (accMag[i] > openG)
            {
                openG = accMag[i];
            }

            openSat |= s[i].AccSaturated;
        }

        return new JumpMetrics
        {
            JumpDetected = true,
            Note = "Sprungprofil erkannt.",
            MaxAltitudeM = maxAlt,
            MinAltitudeM = minAlt,
            ExitAltitudeM = altitude[exitIdx],
            ExitTimeSeconds = s[exitIdx].TimeSeconds,
            DeploymentAltitudeM = altitude[deployIdx],
            DeploymentTimeSeconds = s[deployIdx].TimeSeconds,
            FreefallSeconds = freefall,
            CanopySeconds = canopy,
            MaxDescentRateMs = maxDescent,
            MeanFreefallRateMs = meanRate,
            MaxOpeningG = openG,
            OpeningGSaturated = openSat,
            MaxAccelerationG = maxAcc,
            AnySaturated = anySat,
            MinTemperatureC = minT,
            MaxTemperatureC = maxT,
            TotalSeconds = total
        };
    }
}
