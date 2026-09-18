namespace Siemert.DataViewer.Core.Model;

/// <summary>
/// Eine zusammenhängende Aufnahme des Loggers (Marker AAAA bis FFFF).
/// </summary>
public sealed class Recording
{
    /// <summary>
    /// Startzeitpunkt. <c>null</c>, wenn die Loggeruhr nicht gestellt war – dann ist nur
    /// <see cref="StartTimeOfDay"/> belastbar. Wir erfinden hier bewusst kein Ersatzdatum:
    /// ein falsches Datum ist schlimmer als gar keins.
    /// </summary>
    public DateTime? StartTime { get; init; }

    /// <summary>Tageszeit des Aufnahmebeginns – auch bei ungestellter Uhr gültig.</summary>
    public TimeSpan StartTimeOfDay { get; init; }

    /// <summary>Endzeitpunkt laut Abschlussdatensatz, sofern die Uhr gestellt war.</summary>
    public DateTime? EndTime { get; init; }

    public TimeSpan EndTimeOfDay { get; init; }

    /// <summary>False, wenn der Logger kein gültiges Datum hinterlegt hatte.</summary>
    public bool ClockWasSet { get; init; }

    public double StartTemperatureC { get; init; }

    public double StartPressureHpa { get; init; }

    public double EndTemperatureC { get; init; }

    public double EndPressureHpa { get; init; }

    /// <summary>Statuswort aus dem Abschlussdatensatz, roh.</summary>
    public int StatusRaw { get; init; }

    /// <summary>Ausgewertetes Statuswort.</summary>
    public DeviceStatus Status => (DeviceStatus)StatusRaw;

    /// <summary>True, wenn das Gerät beim Beenden der Aufnahme zu geringe Spannung gemeldet hat.</summary>
    public bool LowBattery => Status.HasFlag(DeviceStatus.LowBattery);

    /// <summary>
    /// Versorgungsspannung (VCC) beim Beenden der Aufnahme, Rohwert.
    /// </summary>
    /// <remarks>
    /// Das Protokoll benennt das Feld als VCC, nennt aber keine Skalierung. Aussagekräftig ist
    /// deshalb vor allem das Statusbit <see cref="DeviceStatus.LowBattery"/> sowie der Verlauf
    /// über mehrere Aufnahmen. Während des Auslesens versorgt der USB-Anschluss das Gerät; ein
    /// zu diesem Zeitpunkt geschriebener Wert sagt daher nichts über die Batterie aus.
    /// </remarks>
    public int SupplyVoltageRaw { get; init; }

    /// <summary>Viertelsekunden-Anteil des Endzeitstempels (0 bis 3).</summary>
    public int EndQuarterSecond { get; init; }

    /// <summary>
    /// Versorgungsspannung in Volt.
    /// </summary>
    /// <remarks>
    /// Der Umrechnungsfaktor ist hergeleitet und noch nicht durch eine Messung bestaetigt, siehe
    /// <see cref="Protocol.LoggerProtocol.SupplyVoltagePerCount"/>.
    /// <para>
    /// Achtung: Waehrend des Auslesens versorgt der USB-Anschluss das Geraet. Ein Wert, der zu
    /// diesem Zeitpunkt geschrieben wurde, sagt nichts ueber den Ladezustand der Batterie aus.
    /// Belastbar ist der Wert aus dem Abschlussdatensatz einer Aufnahme, die im Batteriebetrieb
    /// entstanden ist - und in jedem Fall das Statusbit.
    /// </para>
    /// </remarks>
    public double SupplyVoltage => SupplyVoltageRaw * Protocol.LoggerProtocol.SupplyVoltagePerCount;

    /// <summary>Grob geschaetzter Ladezustand aus der Spannung, 0 bis 1. NaN ohne Messwert.</summary>
    public double BatteryFraction
    {
        get
        {
            if (SupplyVoltageRaw <= 0)
            {
                return double.NaN;
            }

            double span = Protocol.LoggerProtocol.BatteryFreshVoltage - Protocol.LoggerProtocol.BatteryEmptyVoltage;
            return span <= 0
                ? double.NaN
                : Math.Clamp((SupplyVoltage - Protocol.LoggerProtocol.BatteryEmptyVoltage) / span, 0, 1);
        }
    }

    public IReadOnlyList<Sample> Samples { get; init; } = Array.Empty<Sample>();

    /// <summary>
    /// Der unveraenderte Ausschnitt des Rohdatenstroms, aus dem diese Aufnahme entstand.
    /// </summary>
    /// <remarks>
    /// Zeichengenau von <c>AAAA</c> bis zum Ende des Abschlussdatensatzes. Das ist keine
    /// Wiedergabe der ausgewerteten Werte, sondern der Originaltext des Geraets - dadurch
    /// bleibt eine einzeln gespeicherte Aufnahme neu auswertbar, wenn sich die Auswertung
    /// spaeter aendert oder ein Fehler darin behoben wird.
    /// </remarks>
    public string RawSegment { get; init; } = string.Empty;

    /// <summary>Position dieses Ausschnitts im vollstaendigen Auszug, in Hexzeichen.</summary>
    public int RawOffset { get; init; }

    /// <summary>
    /// Abstand zweier Messpunkte in Sekunden.
    /// </summary>
    /// <remarks>
    /// Aus dem Geraetespeicher kommen 4 Hz. Eine live mitgeschriebene Aufnahme entsteht dagegen
    /// im Takt der Abfrage, also deutlich langsamer. Ableitung und Glaettung rechnen mit diesem
    /// Wert; stuende hier fest 0,25 s, waere jede Sinkrate einer Live-Aufnahme um den Faktor
    /// des Taktverhaeltnisses falsch.
    /// </remarks>
    public double SampleIntervalSeconds { get; init; } = Protocol.LoggerProtocol.SampleIntervalSeconds;

    /// <summary>True, wenn die Aufnahme live am Rechner mitgeschrieben wurde.</summary>
    public bool IsLive { get; init; }

    /// <summary>Warnungen, die beim Dekodieren genau dieser Aufnahme aufgetreten sind.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>True, wenn der Abschlussdatensatz fehlte – die Aufnahme ist dann unvollständig.</summary>
    public bool IsComplete { get; init; }

    /// <summary>Dauer aus der Anzahl der Messpunkte (4 Hz).</summary>
    public TimeSpan Duration => Samples.Count == 0
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds(Samples[^1].TimeSeconds);

    /// <summary>True, wenn mindestens ein Messpunkt den Messbereich des Beschleunigungssensors erreicht hat.</summary>
    public bool HasSaturatedSamples => Samples.Any(s => s.AccSaturated);

    /// <summary>Anzeigename für Listen: Datum/Uhrzeit, sonst nur Uhrzeit mit Hinweis.</summary>
    public string DisplayName => ClockWasSet && StartTime.HasValue
        ? StartTime.Value.ToString("dd.MM.yyyy HH:mm:ss")
        : StartTimeOfDay.ToString("hh\\:mm\\:ss") + " (Uhr nicht gestellt)";
}
