namespace Siemert.DataViewer.Core.Model;

/// <summary>
/// Ein Messpunkt. Die Zeit ist bewusst relativ zum Aufnahmebeginn gespeichert, weil die
/// Loggeruhr nicht immer gestellt ist; die Wanduhrzeit ergibt sich erst aus
/// <see cref="Recording.StartTime"/> und ist dann optional.
/// </summary>
public readonly struct Sample
{
    /// <summary>Sekunden seit Aufnahmebeginn.</summary>
    public double TimeSeconds { get; init; }

    /// <summary>Luftdruck in hPa.</summary>
    public double PressureHpa { get; init; }

    /// <summary>Temperatur in °C. Der Logger liefert nur alle 60 s einen neuen Wert; dazwischen wird gehalten.</summary>
    public double TemperatureC { get; init; }

    /// <summary>Beschleunigung X in g.</summary>
    public double AccX { get; init; }

    /// <summary>Beschleunigung Y in g.</summary>
    public double AccY { get; init; }

    /// <summary>Beschleunigung Z in g.</summary>
    public double AccZ { get; init; }

    /// <summary>True, wenn mindestens eine Achse den Messbereich des Sensors erreicht hat.</summary>
    public bool AccSaturated { get; init; }

    /// <summary>True, wenn die Temperatur aus dem letzten Einschub gehalten und nicht neu gemessen wurde.</summary>
    public bool TemperatureHeld { get; init; }

    /// <summary>Betrag des Beschleunigungsvektors in g.</summary>
    public double AccMagnitude => Math.Sqrt((AccX * AccX) + (AccY * AccY) + (AccZ * AccZ));
}
