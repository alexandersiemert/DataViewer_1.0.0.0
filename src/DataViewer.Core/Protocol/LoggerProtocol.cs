namespace Siemert.DataViewer.Core.Protocol;

/// <summary>
/// Konstanten des SI-TL1-Protokolls. Alle Werte sind gegen einen echten Mitschnitt des Geräts
/// (Seriennummer 349) verifiziert; die Belege stehen in den Testdaten unter tests/Data.
/// </summary>
public static class LoggerProtocol
{
    public const int BaudRate = 38400;

    /// <summary>Ping. Das Gerät antwortet mit '?'.</summary>
    public const string CommandPing = "*";

    /// <summary>Gerätekopf lesen (Modell, Seriennummer, Produktionsdatum, Prüfsumme).</summary>
    public const string CommandInfo = "I";

    /// <summary>Gesamten belegten Speicher lesen.</summary>
    public const string CommandReadAll = "G";

    /// <summary>Gesamten Speicher ab Adresse 0000h lesen (Sonderfunktion laut Herstellerliste).</summary>
    public const string CommandReadWholeMemory = "W";

    /// <summary>
    /// Speicher ab der letzten Auslesung lesen.
    /// </summary>
    /// <remarks>
    /// Wichtig: Der Befehl liefert <b>alles seit dem letzten Auslesevorgang</b>, nicht die letzte
    /// Aufnahme. Stehen mehrere neue Aufnahmen im Speicher, kommen alle. Wurde seit dem letzten
    /// Auslesen nichts aufgezeichnet, kommt nichts.
    /// </remarks>
    public const string CommandReadNew = "S";

    /// <summary>Beginn einer Aufnahme.</summary>
    public const string MarkerRecordingStart = "AAAA";

    /// <summary>Ende der Messdaten einer Aufnahme.</summary>
    public const string MarkerRecordingEnd = "FFFF";

    /// <summary>Logger an den PC angeschlossen.</summary>
    public const string MarkerConnected = "CCCC";

    /// <summary>Logger vom PC getrennt.</summary>
    public const string MarkerDisconnected = "EEEE";

    /// <summary>Alle Felder liegen auf einem Raster von vier Hexzeichen (2 Byte).</summary>
    public const int Alignment = 4;

    /// <summary>Gerätekopf: 128 Byte.</summary>
    public const int HeaderHexLength = 256;

    /// <summary>Kopfdaten einer Aufnahme nach dem Marker AAAA: 12 Byte.</summary>
    public const int RecordingHeaderHexLength = 24;

    /// <summary>Abschlussdaten nach dem Marker FFFF: 16 Byte.</summary>
    public const int RecordingTrailerHexLength = 32;

    /// <summary>Ein Messpunkt: Druck, AccX, AccY, AccZ – je 2 Byte.</summary>
    public const int SampleHexLength = 16;

    /// <summary>Anschlusssatz nach CCCC: Statuswort plus der Rumpf des Trennsatzes.</summary>
    public const int ConnectedBodyHexLength = 28;

    /// <summary>Trennsatz nach EEEE.</summary>
    public const int DisconnectedBodyHexLength = 24;

    /// <summary>Statusbit fuer zu geringe Batteriespannung.</summary>
    public const int StatusLowBattery = 0x0008;

    // ---------------------------------------------------------------- Stromversorgung
    // Der SI-TL1 wird von drei Knopfzellen AG13 / LR44 in Reihe versorgt.

    /// <summary>Anzahl Zellen.</summary>
    public const int BatteryCells = 3;

    /// <summary>Nennspannung einer Zelle AG13 / LR44.</summary>
    public const double CellNominalVoltage = 1.5;

    /// <summary>Spannung einer frischen Zelle.</summary>
    public const double CellFreshVoltage = 1.6;

    /// <summary>Spannung, ab der eine Zelle als erschoepft gilt.</summary>
    public const double CellEmptyVoltage = 1.1;

    public const double BatteryNominalVoltage = BatteryCells * CellNominalVoltage;

    public const double BatteryFreshVoltage = BatteryCells * CellFreshVoltage;

    public const double BatteryEmptyVoltage = BatteryCells * CellEmptyVoltage;

    /// <summary>Spannung der Referenz des Analog-Digital-Umsetzers, in Volt.</summary>
    public const double AdcReferenceVoltage = 2.5;

    /// <summary>Stufen des Analog-Digital-Umsetzers (10 Bit).</summary>
    public const int AdcSteps = 1024;

    /// <summary>Teilerverhaeltnis vor dem Eingang des Umsetzers.</summary>
    public const double SupplyDividerRatio = 2.0;

    /// <summary>
    /// Umrechnung des VCC-Rohwerts in Volt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Angabe des Herstellers: Der Rohwert stammt aus einem 10-Bit-Umsetzer mit 2,5 V Referenz,
    /// davor ein Spannungsteiler 1:2. Damit ist
    /// U = Rohwert / 1024 * 2,5 V * 2 = Rohwert * 5 / 1024.
    /// </para>
    /// <para>
    /// Zur Probe: Rohwert 619 ergibt 3,0225 V, also 1,007 V je Zelle bei drei Zellen in Reihe.
    /// Eine AG13 gilt unterhalb von etwa 1,1 V als erschoepft. Der Wert passt damit zu der
    /// Beobachtung, dass Uhrzeit und Speicherinhalt dieses Geraets nach dem Abziehen verloren
    /// gehen.
    /// </para>
    /// <para>
    /// Die vorherige Fassung rechnete mit 1/128 und zeigte fuer denselben Rohwert 4,84 V an,
    /// also eine volle Batterie. Das war hergeleitet, nicht belegt, und es war falsch.
    /// </para>
    /// </remarks>
    public const double SupplyVoltagePerCount = AdcReferenceVoltage * SupplyDividerRatio / AdcSteps;

    /// <summary>True, solange der Umrechnungsfaktor nicht durch eine Messung bestaetigt ist.</summary>
    public const bool SupplyVoltageScaleIsProvisional = false;

    /// <summary>Abtastintervall. Gegen drei Aufnahmen mit Start-/Endzeitstempel auf die Sekunde bestätigt.</summary>
    public const double SampleIntervalSeconds = 0.25;

    /// <summary>Nach so vielen Messpunkten schiebt der Logger einen neuen Temperaturwert ein (= 60 s).</summary>
    public const int TemperatureInterval = 240;

    /// <summary>Temperatur und Startemperatur: (roh - 500) / 10 ergibt °C.</summary>
    public const double TemperatureOffset = 500.0;

    public const double TemperatureScale = 10.0;

    /// <summary>Druck: roh / 10 ergibt hPa.</summary>
    public const double PressureScale = 10.0;

    /// <summary>Skalierung des Beschleunigungssensors (LIS3DH, ±16 g, 10 bit linksbündig).</summary>
    public const double AccelerationScaleG = 0.048;

    /// <summary>Anzahl Bits, um die der 16-Bit-Rohwert nach rechts geschoben wird.</summary>
    public const int AccelerationShift = 6;

    /// <summary>
    /// Spezifizierter Messbereich des Sensors. Werte ab hier gelten als am Anschlag und werden
    /// gekennzeichnet: "16 g" und "mindestens 16 g" sind für eine Materialbeurteilung nicht dasselbe.
    /// </summary>
    public const double AccelerationRangeG = 16.0;

    /// <summary>Plausibler Druckbereich in hPa – dient der Erkennung von Rasterversatz.</summary>
    public const double MinPlausiblePressureHpa = 250.0;

    public const double MaxPlausiblePressureHpa = 1100.0;

    /// <summary>Plausibler Temperaturbereich in °C.</summary>
    public const double MinPlausibleTemperatureC = -60.0;

    public const double MaxPlausibleTemperatureC = 90.0;
}
