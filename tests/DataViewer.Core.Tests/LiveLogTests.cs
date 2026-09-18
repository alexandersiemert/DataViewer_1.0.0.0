using Siemert.DataViewer.Core.Analysis;
using Siemert.DataViewer.Core.Device;
using Siemert.DataViewer.Core.Io;
using Siemert.DataViewer.Core.Model;
using Siemert.DataViewer.Core.Protocol;

namespace DataViewer.Core.Tests;

/// <summary>
/// Tests der Live-Mitschrift.
/// </summary>
/// <remarks>
/// Der springende Punkt ist derselbe wie beim Auslesen des Gerätespeichers: Gespeichert wird,
/// was das Gerät gesendet hat, nicht das, was das Programm daraus gemacht hat. Die Tests prüfen
/// deshalb vor allem, dass die Antworten wörtlich in der Datei stehen und beim Öffnen mit
/// denselben Funktionen wieder zerlegt werden.
/// </remarks>
public sealed class LiveLogTests
{
    // Echte Antworten des Geräts mit der Seriennummer 349.
    private const string Env = "D00755 10030";
    private const string Acc = "M00400500FF00";

    private static LiveRecorder Recorded(int count, double interval = 1.0)
    {
        var recorder = new LiveRecorder(interval);
        var t0 = new DateTime(2026, 9, 18, 12, 0, 0);

        for (int i = 0; i < count; i++)
        {
            Assert.True(recorder.Add(Env, Acc, t0.AddSeconds(i * interval)));
        }

        return recorder;
    }

    // ------------------------------------------------------------------ Mitschrift

    [Fact]
    public void Die_Antworten_des_Geraets_stehen_woertlich_in_der_Mitschrift()
    {
        string log = Recorded(3).ToRawLog();

        Assert.StartsWith(LiveLog.Magic, log, StringComparison.Ordinal);
        Assert.Contains(Env, log, StringComparison.Ordinal);
        Assert.Contains(Acc, log, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Mitschrift_wird_als_solche_erkannt()
    {
        Assert.True(LiveLog.IsLiveLog(Recorded(2).ToRawLog()));
        Assert.False(LiveLog.IsLiveLog("AAAA0039000000000000"));
        Assert.False(LiveLog.IsLiveLog(null));
    }

    [Fact]
    public void Die_Mitschrift_laesst_sich_wieder_auswerten()
    {
        LiveRecorder recorder = Recorded(5);
        DecodeResult parsed = LiveLog.Parse(recorder.ToRawLog());

        Recording recording = Assert.Single(parsed.Recordings);

        Assert.Equal(5, recording.Samples.Count);
        Assert.True(recording.IsLive);
        Assert.Equal(25.5, recording.Samples[0].TemperatureC, 1);
        Assert.Equal(1003.0, recording.Samples[0].PressureHpa, 1);
    }

    [Fact]
    public void Die_ausgewerteten_Werte_stimmen_mit_der_Aufzeichnung_ueberein()
    {
        // Der Weg über die Datei darf keinen Messwert verändern.
        LiveRecorder recorder = Recorded(8);

        Recording direct = recorder.BuildRecording()!;
        Recording viaFile = LiveLog.Parse(recorder.ToRawLog()).Recordings[0];

        Assert.Equal(direct.Samples.Select(s => s.PressureHpa), viaFile.Samples.Select(s => s.PressureHpa));
        Assert.Equal(direct.Samples.Select(s => s.TemperatureC), viaFile.Samples.Select(s => s.TemperatureC));
        Assert.Equal(direct.Samples.Select(s => s.AccZ), viaFile.Samples.Select(s => s.AccZ));
        Assert.Equal(direct.Samples.Select(s => s.TimeSeconds), viaFile.Samples.Select(s => s.TimeSeconds));
    }

    [Fact]
    public void Unbrauchbare_Takte_werden_uebergangen_und_gemeldet()
    {
        // Geraten wird nichts: Ein Takt, dessen Antwort sich nicht zerlegen lässt, fällt weg.
        string log = Recorded(3).ToRawLog() + "4,000;Dxxxx yyyyy;Mzzzz\n";

        DecodeResult parsed = LiveLog.Parse(log);

        Assert.Equal(3, parsed.Recordings[0].Samples.Count);
        Assert.Contains(parsed.Messages, m => m.Severity == DecodeSeverity.Warning);
    }

    // ------------------------------------------------------------------ Abtastintervall

    [Fact]
    public void Der_gemessene_Takt_geht_in_die_Aufnahme_ein()
    {
        // Sonst rechnete die Sinkrate mit 4 Hz, obwohl im Sekundentakt gemessen wurde - jede
        // abgeleitete Geschwindigkeit wäre um den Faktor 4 falsch.
        Recording recording = Recorded(10, interval: 1.0).BuildRecording()!;

        Assert.Equal(1.0, recording.SampleIntervalSeconds, 3);
        Assert.NotEqual(LoggerProtocol.SampleIntervalSeconds, recording.SampleIntervalSeconds);
    }

    [Fact]
    public void Ein_unregelmaessiger_Takt_wird_gemittelt_statt_behauptet()
    {
        // Das Gerät antwortet, wann es antwortet. Maßgeblich ist der tatsächliche Abstand.
        var recorder = new LiveRecorder(1.0);
        var t0 = new DateTime(2026, 9, 18, 12, 0, 0);

        recorder.Add(Env, Acc, t0);
        recorder.Add(Env, Acc, t0.AddSeconds(1.4));
        recorder.Add(Env, Acc, t0.AddSeconds(2.6));
        recorder.Add(Env, Acc, t0.AddSeconds(4.0));

        Assert.Equal(4.0 / 3.0, recorder.MeasuredIntervalSeconds, 3);
    }

    [Fact]
    public void Ohne_Messpunkte_entsteht_keine_Aufnahme()
    {
        Assert.Null(new LiveRecorder().BuildRecording());
        Assert.Empty(new LiveRecorder().BuildResult().Recordings);
    }

    // ------------------------------------------------------------------ Speichern und Laden

    [Fact]
    public void Eine_Live_Aufnahme_uebersteht_Speichern_und_Laden()
    {
        LiveRecorder recorder = Recorded(12);
        DecodeResult data = recorder.BuildResult();

        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + SdvLogFile.Extension);
        try
        {
            SdvLogFile.Save(file, null, data, AltitudeReference.Standard, "Test");
            SdvDocument loaded = SdvLogFile.Load(file);

            Assert.True(loaded.IntegrityVerified);
            Assert.Equal(SdvLogFile.KindLive, loaded.Manifest.Kind);

            Recording again = Assert.Single(loaded.Data.Recordings);

            Assert.True(again.IsLive);
            Assert.Equal(12, again.Samples.Count);
            Assert.Equal(1.0, again.SampleIntervalSeconds, 3);

            Assert.Equal(
                data.Recordings[0].Samples.Select(s => s.PressureHpa),
                again.Samples.Select(s => s.PressureHpa));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Eine_Live_Datei_wird_nicht_als_Speicherauszug_gelesen()
    {
        // Beide Formate sind Rohdaten, brauchen aber verschiedene Zerleger. Wird das verwechselt,
        // entstehen entweder keine oder unsinnige Messwerte.
        LiveRecorder recorder = Recorded(6);

        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + SdvLogFile.Extension);
        try
        {
            SdvLogFile.Save(file, null, recorder.BuildResult(), AltitudeReference.Standard, "Test");
            SdvDocument loaded = SdvLogFile.Load(file);

            Assert.False(loaded.Data.HasErrors);
            Assert.Equal(6, loaded.Data.Recordings[0].Samples.Count);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Eine_Live_Aufnahme_traegt_ihren_Hinweis_mit()
    {
        // Wer eine solche Datei später öffnet, muss erkennen können, dass sie nicht aus dem
        // Gerätespeicher stammt.
        Recording recording = Recorded(4).BuildRecording()!;

        Assert.Contains(recording.Warnings, w => w.Contains("Live", StringComparison.OrdinalIgnoreCase));
    }
}
