namespace Siemert.DataViewer.Core.Model;

/// <summary>
/// Statuswort des Geräts.
/// </summary>
/// <remarks>
/// Herstellerseitig dokumentiert ist bisher nur Bit 3 (0x0008). Die übrigen Bits werden
/// unverändert mitgeführt und im Rohwert angezeigt, damit später dokumentierte Bedeutungen
/// nachgereicht werden können, ohne dass Altdaten neu eingelesen werden müssen.
/// </remarks>
[Flags]
public enum DeviceStatus
{
    None = 0,

    /// <summary>Batteriespannung zu niedrig.</summary>
    LowBattery = 0x0008
}

public enum DeviceEventKind
{
    /// <summary>Logger an den PC angeschlossen (Marker CCCC).</summary>
    ConnectedToPc,

    /// <summary>Logger vom PC getrennt (Marker EEEE).</summary>
    DisconnectedFromPc
}

/// <summary>
/// Betriebsereignis aus dem Loggerspeicher. Der Logger schreibt bei jedem Ein- und Ausschalten
/// einen solchen Satz mit Zeitstempel, Temperatur und Druck. Für Unfalluntersuchung und
/// Materialbeurteilung ist das die Betriebshistorie des Geräts.
/// </summary>
public sealed class DeviceEvent
{
    public required DeviceEventKind Kind { get; init; }

    /// <summary>Zeitstempel, sofern die Loggeruhr gestellt war.</summary>
    public DateTime? Timestamp { get; init; }

    /// <summary>Tageszeit ohne Datum – immer vorhanden, auch wenn die Uhr nicht gestellt war.</summary>
    public required TimeSpan TimeOfDay { get; init; }

    /// <summary>False, wenn der Logger kein gültiges Datum hinterlegt hatte.</summary>
    public required bool ClockWasSet { get; init; }

    public required double TemperatureC { get; init; }

    public required double PressureHpa { get; init; }

    /// <summary>Byteversatz im Rohdatenstrom – für Diagnose.</summary>
    public int RawOffset { get; init; }

    /// <summary>
    /// Statuswort. Nur der Anschlusssatz (CCCC) führt eines; beim Trennsatz (EEEE) sieht das
    /// Protokoll keines vor.
    /// </summary>
    public DeviceStatus Status { get; init; } = DeviceStatus.None;

    /// <summary>Viertelsekunden-Anteil des Zeitstempels (0 bis 3).</summary>
    public int QuarterSecond { get; init; }

    public string KindText => Kind == DeviceEventKind.ConnectedToPc
        ? "Am PC angeschlossen"
        : "Vom PC getrennt";
}
