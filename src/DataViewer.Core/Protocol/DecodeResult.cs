using Siemert.DataViewer.Core.Model;

namespace Siemert.DataViewer.Core.Protocol;

public enum DecodeSeverity
{
    Info,
    Warning,
    Error
}

public sealed record DecodeMessage(DecodeSeverity Severity, string Text)
{
    public override string ToString() => $"[{Severity}] {Text}";
}

/// <summary>
/// Ergebnis der Dekodierung eines Rohdatenstroms. Enthält bewusst auch die Probleme:
/// Eine Aufnahme darf nie stillschweigend verschwinden oder stillschweigend gekürzt werden.
/// </summary>
public sealed class DecodeResult
{
    public IReadOnlyList<Recording> Recordings { get; init; } = Array.Empty<Recording>();

    public IReadOnlyList<DeviceEvent> Events { get; init; } = Array.Empty<DeviceEvent>();

    public IReadOnlyList<DecodeMessage> Messages { get; init; } = Array.Empty<DecodeMessage>();

    /// <summary>Rohdatenstrom, aus dem dieses Ergebnis entstand – wird mitarchiviert.</summary>
    public string RawPayload { get; init; } = string.Empty;

    /// <summary>
    /// Alles, was im Auszug <b>ausserhalb</b> der Aufnahmen steht: Geraetekopf und
    /// Betriebsereignisse.
    /// </summary>
    /// <remarks>
    /// Mengenmaessig ist das wenig – in einem gemessenen Auszug unter einem Prozent –
    /// inhaltlich aber die gesamte Betriebshistorie des Geraets. Es wird unveraendert
    /// uebernommen, damit es einer einzeln gespeicherten Aufnahme beigelegt werden kann.
    /// </remarks>
    public string RawContext { get; init; } = string.Empty;

    /// <summary>
    /// Geraet, sofern es sich aus den Daten selbst ergibt.
    /// </summary>
    /// <remarks>
    /// Beim Auslesen steckt der Kopf in der Antwort des Geraets und wird dort ausgewertet. Eine
    /// Live-Mitschrift fuehrt ihn in ihrer eigenen Kopfzeile mit.
    /// </remarks>
    public Model.DeviceInfo? Device { get; init; }

    public bool HasErrors => Messages.Any(m => m.Severity == DecodeSeverity.Error);

    public bool HasWarnings => Messages.Any(m => m.Severity == DecodeSeverity.Warning);

    public static DecodeResult Empty { get; } = new();
}
