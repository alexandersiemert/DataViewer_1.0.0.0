namespace Siemert.DataViewer.Core.Model;

/// <summary>
/// Stammdaten eines angeschlossenen Loggers, gelesen aus der Antwort auf den Befehl 'I'.
/// </summary>
public sealed class DeviceInfo
{
    /// <summary>Modellbezeichnung, z. B. "SI-TL1". Bei unbekannter Kennung <see cref="UnknownModel"/>.</summary>
    public required string Model { get; init; }

    /// <summary>Werkskennung aus dem Gerätekopf (zwei Hexzeichen), auch wenn sie unbekannt ist.</summary>
    public required string ModelCode { get; init; }

    /// <summary>Seriennummer als Dezimalzahl.</summary>
    public required int SerialNumber { get; init; }

    /// <summary>Produktionsdatum, sofern im Kopf gültig hinterlegt.</summary>
    public DateOnly? ProductionDate { get; init; }

    /// <summary>Prüfsumme aus dem Gerätekopf, byteweise getauscht dargestellt.</summary>
    public required string Checksum { get; init; }

    /// <summary>Der vollständige Gerätekopf als Hexzeichenkette – für Diagnose und Archivierung.</summary>
    public required string RawHeader { get; init; }

    /// <summary>COM-Port, über den das Gerät gelesen wurde. Bei Dateien der Dateiname.</summary>
    public string? Source { get; set; }

    public const string UnknownModel = "Unbekannt";

    public string DisplayName => $"{Model} Nr. {SerialNumber}";

    public override string ToString() => DisplayName;
}
