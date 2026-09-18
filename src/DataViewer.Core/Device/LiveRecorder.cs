using Siemert.DataViewer.Core.Model;
using Siemert.DataViewer.Core.Protocol;

namespace Siemert.DataViewer.Core.Device;

/// <summary>
/// Schreibt eine Aufzeichnung live am Rechner mit.
/// </summary>
/// <remarks>
/// <para>
/// Das Gerät wird im festgelegten Takt nach Temperatur und Druck (<c>D</c>) sowie
/// Beschleunigung (<c>M</c>) gefragt. Die Antworten werden unverändert mitgeschrieben; die
/// Auswertung geschieht daraus.
/// </para>
/// <para>
/// Der Takt ist ein Sollwert, kein Versprechen: Das Gerät antwortet, wann es antwortet. Als
/// Zeitstempel eines Messpunkts gilt deshalb die tatsächliche Ortszeit der Abfrage, nicht
/// Takt mal Nummer. Für Ableitung und Glättung wird anschließend der gemessene mittlere
/// Abstand verwendet, nicht der Sollwert.
/// </para>
/// </remarks>
public sealed class LiveRecorder(double intervalSeconds = 1.0)
{
    private readonly List<LiveLog.Entry> _entries = [];
    private readonly List<Sample> _samples = [];

    /// <summary>Angestrebter Abstand zweier Abfragen in Sekunden.</summary>
    public double IntervalSeconds { get; } = intervalSeconds > 0 ? intervalSeconds : 1.0;

    /// <summary>Ortszeit der ersten Abfrage.</summary>
    public DateTime StartedAt { get; private set; }

    /// <summary>Gerät, von dem die Werte stammen.</summary>
    public DeviceInfo? Device { get; set; }

    /// <summary>Bisher aufgezeichnete Messpunkte.</summary>
    public IReadOnlyList<Sample> Samples => _samples;

    public int Count => _samples.Count;

    /// <summary>Dauer der bisherigen Aufzeichnung.</summary>
    public TimeSpan Duration => _samples.Count == 0
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds(_samples[^1].TimeSeconds);

    /// <summary>
    /// Nimmt einen Abfragetakt auf.
    /// </summary>
    /// <param name="environment">Rohantwort auf <c>D</c>.</param>
    /// <param name="acceleration">Rohantwort auf <c>M</c>.</param>
    /// <param name="at">Ortszeit der Abfrage.</param>
    /// <returns>True, wenn sich beide Antworten zerlegen ließen.</returns>
    public bool Add(string environment, string acceleration, DateTime at)
    {
        (double TemperatureC, double PressureHpa)? env = LoggerCommands.ParseEnvironment(environment);
        (double X, double Y, double Z)? acc = LoggerCommands.ParseAcceleration(acceleration);

        if (env is null || acc is null)
        {
            return false;
        }

        if (_samples.Count == 0)
        {
            StartedAt = at;
        }

        double seconds = (at - StartedAt).TotalSeconds;

        _entries.Add(new LiveLog.Entry(seconds, environment, acceleration));
        _samples.Add(new Sample
        {
            TimeSeconds = seconds,
            PressureHpa = env.Value.PressureHpa,
            TemperatureC = env.Value.TemperatureC,
            AccX = acc.Value.X,
            AccY = acc.Value.Y,
            AccZ = acc.Value.Z
        });

        return true;
    }

    /// <summary>
    /// Der tatsächlich erreichte mittlere Abstand zweier Messpunkte.
    /// </summary>
    /// <remarks>
    /// Maßgeblich für Ableitung und Glättung. Bleibt das Gerät einmal länger stumm, ist der
    /// Sollwert falsch und dieser Wert richtig.
    /// </remarks>
    public double MeasuredIntervalSeconds => _samples.Count < 2
        ? IntervalSeconds
        : _samples[^1].TimeSeconds / (_samples.Count - 1);

    /// <summary>Die Mitschrift als Text, so wie sie gespeichert wird.</summary>
    public string ToRawLog() => LiveLog.Write(_entries, Device, StartedAt, IntervalSeconds);

    /// <summary>
    /// Baut aus dem bisherigen Stand eine Aufnahme.
    /// </summary>
    /// <remarks>
    /// Wird auch während der Aufzeichnung aufgerufen, damit sich der Verlauf mitverfolgen
    /// lässt. Das Ergebnis ist jedes Mal vollständig neu gebildet und unabhängig vom vorigen.
    /// </remarks>
    public Recording? BuildRecording()
    {
        if (_samples.Count == 0)
        {
            return null;
        }

        return new Recording
        {
            StartTime = StartedAt,
            StartTimeOfDay = StartedAt.TimeOfDay,
            EndTime = StartedAt.AddSeconds(_samples[^1].TimeSeconds),
            EndTimeOfDay = StartedAt.TimeOfDay + TimeSpan.FromSeconds(_samples[^1].TimeSeconds),
            ClockWasSet = true,
            StartPressureHpa = _samples[0].PressureHpa,
            StartTemperatureC = _samples[0].TemperatureC,
            EndPressureHpa = _samples[^1].PressureHpa,
            EndTemperatureC = _samples[^1].TemperatureC,
            Samples = [.. _samples],
            IsComplete = true,
            IsLive = true,
            SampleIntervalSeconds = MeasuredIntervalSeconds,
            RawSegment = ToRawLog(),
            Warnings = ["Live am Rechner mitgeschrieben, nicht aus dem Gerätespeicher gelesen."]
        };
    }

    /// <summary>Das Ergebnis als vollständiges Auswerteergebnis.</summary>
    public DecodeResult BuildResult()
    {
        Recording? recording = BuildRecording();

        if (recording is null)
        {
            return DecodeResult.Empty;
        }

        return new DecodeResult
        {
            Recordings = [recording],
            RawPayload = ToRawLog(),
            Device = Device,
            Messages =
            [
                new DecodeMessage(DecodeSeverity.Info,
                    _samples.Count + " Messpunkte live mitgeschrieben.")
            ]
        };
    }
}
