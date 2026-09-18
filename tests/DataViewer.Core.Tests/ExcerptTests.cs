using System.IO.Compression;
using Siemert.DataViewer.Core.Analysis;
using Siemert.DataViewer.Core.Io;
using Siemert.DataViewer.Core.Model;
using Siemert.DataViewer.Core.Protocol;

namespace DataViewer.Core.Tests;

/// <summary>
/// Tests zur einzeln gespeicherten Aufnahme.
/// </summary>
/// <remarks>
/// Der springende Punkt dieser Dateien ist, dass sie den <b>unveraenderten</b> Rohausschnitt
/// enthalten und nicht die ausgewerteten Werte. Nur so bleibt eine einzeln weitergegebene
/// Aufnahme neu auswertbar, wenn ein Fehler in der Auswertung behoben wird. Genau daran ist die
/// Vorgaengerversion gescheitert. Die Tests pruefen deshalb vor allem eines: dass nichts
/// umgeschrieben wird.
/// </remarks>
public sealed class ExcerptTests
{
    private static string LoadPayload(string file = "readout_sn349_G.txt")
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", file);
        string[] lines = File.ReadAllText(path)
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();

        int i = lines[0].StartsWith('*') ? 1 : 0;
        return string.Join(string.Empty, lines.Skip(i + 3));
    }

    private static DecodeResult Decode() => PayloadDecoder.Decode(LoadPayload());

    // ------------------------------------------------------------------ Der Ausschnitt ist woertlich

    [Fact]
    public void Jede_Aufnahme_kennt_ihre_Stelle_im_Auszug()
    {
        DecodeResult r = Decode();
        Assert.NotEmpty(r.Recordings);

        foreach (Recording rec in r.Recordings)
        {
            // Der entscheidende Nachweis: der Ausschnitt ist zeichengenau derselbe Text wie im
            // Auszug an der vermerkten Stelle - nicht neu erzeugt.
            Assert.NotEqual(string.Empty, rec.RawSegment);
            Assert.Equal(
                r.RawPayload.Substring(rec.RawOffset, rec.RawSegment.Length),
                rec.RawSegment);

            Assert.StartsWith(LoggerProtocol.MarkerRecordingStart, rec.RawSegment, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Ausschnitte_ueberschneiden_sich_nicht()
    {
        DecodeResult r = Decode();
        Recording[] ordered = [.. r.Recordings.OrderBy(x => x.RawOffset)];

        for (int i = 1; i < ordered.Length; i++)
        {
            int previousEnd = ordered[i - 1].RawOffset + ordered[i - 1].RawSegment.Length;
            Assert.True(ordered[i].RawOffset >= previousEnd,
                "Aufnahme " + i + " beginnt vor dem Ende der vorhergehenden.");
        }
    }

    // ------------------------------------------------------------------ Das Umfeld

    [Fact]
    public void Umfeld_enthaelt_die_Betriebshistorie_aber_keine_Aufnahme()
    {
        DecodeResult r = Decode();

        Assert.NotEqual(string.Empty, r.RawContext);
        Assert.DoesNotContain(LoggerProtocol.MarkerRecordingStart, r.RawContext, StringComparison.Ordinal);

        // Aus dem Umfeld allein muessen sich dieselben Ereignisse ergeben wie aus dem Auszug.
        DecodeResult only = PayloadDecoder.Decode(r.RawContext);
        Assert.Empty(only.Recordings);
        Assert.Equal(r.Events.Count, only.Events.Count);
        Assert.NotEmpty(only.Events);
    }

    [Fact]
    public void Umfeld_und_Ausschnitte_ergeben_zusammen_den_ganzen_Auszug()
    {
        // Es geht nichts verloren und es kommt nichts hinzu: die Laengen muessen aufgehen.
        DecodeResult r = Decode();
        int inRecordings = r.Recordings.Sum(x => x.RawSegment.Length);

        Assert.Equal(r.RawPayload.Length, inRecordings + r.RawContext.Length);
    }

    [Fact]
    public void Ein_Auszug_ohne_Aufnahme_ist_kein_Fehler()
    {
        // Frueher brach die Auswertung hier mit einem Fehler ab. Das Umfeld einer einzeln
        // gespeicherten Aufnahme sieht aber genau so aus.
        DecodeResult r = PayloadDecoder.Decode(Decode().RawContext);

        Assert.False(r.HasErrors);
        Assert.NotEmpty(r.Events);
    }

    // ------------------------------------------------------------------ Speichern und wieder laden

    [Fact]
    public void Einzelne_Aufnahme_uebersteht_Speichern_und_Laden_unveraendert()
    {
        DecodeResult data = Decode();
        Recording original = data.Recordings.OrderByDescending(x => x.Samples.Count).First();
        var reference = new AltitudeReference { Mode = AltitudeMode.GroundZero };

        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + SdvLogFile.Extension);
        try
        {
            SdvLogFile.SaveRecording(file, null, original, data, reference, "Test", "auszug.lgd");
            SdvDocument loaded = SdvLogFile.Load(file);

            Assert.True(loaded.IntegrityVerified);
            Assert.True(loaded.Manifest.IsExcerpt);

            Recording again = Assert.Single(loaded.Data.Recordings);

            Assert.Equal(original.Samples.Count, again.Samples.Count);
            Assert.Equal(original.StartTimeOfDay, again.StartTimeOfDay);
            Assert.Equal(original.EndTimeOfDay, again.EndTimeOfDay);
            Assert.Equal(original.StatusRaw, again.StatusRaw);
            Assert.Equal(original.IsComplete, again.IsComplete);
            Assert.Equal(original.StartPressureHpa, again.StartPressureHpa, 3);
            Assert.Equal(original.EndPressureHpa, again.EndPressureHpa, 3);
            Assert.Equal(original.EndTemperatureC, again.EndTemperatureC, 3);
            Assert.Equal(original.RawSegment, again.RawSegment);

            // Jeder einzelne Messpunkt, nicht nur die Anzahl.
            Assert.Equal(
                original.Samples.Select(s => s.PressureHpa),
                again.Samples.Select(s => s.PressureHpa));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Einzelne_Aufnahme_nimmt_die_Betriebshistorie_mit()
    {
        DecodeResult data = Decode();
        Recording original = data.Recordings[0];

        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + SdvLogFile.Extension);
        try
        {
            SdvLogFile.SaveRecording(file, null, original, data, AltitudeReference.Standard, "Test");
            SdvDocument loaded = SdvLogFile.Load(file);

            Assert.Equal(data.Events.Count, loaded.Data.Events.Count);
            Assert.NotEmpty(loaded.Data.Events);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Einzelne_Aufnahme_weist_ihre_Herkunft_nach()
    {
        DecodeResult data = Decode();
        Recording original = data.Recordings[1];

        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + SdvLogFile.Extension);
        try
        {
            SdvLogFile.SaveRecording(file, null, original, data, AltitudeReference.Standard,
                "Test", "readout_sn349_G.txt");

            SdvManifest m = SdvLogFile.Load(file).Manifest;

            Assert.True(m.IsExcerpt);
            Assert.Equal("readout_sn349_G.txt", m.ExcerptSourceName);
            Assert.Equal(original.RawOffset, m.ExcerptOffset);
            Assert.Equal(original.RawSegment.Length, m.ExcerptLength);

            // Damit laesst sich spaeter belegen, aus welchem Auslesevorgang der Schnitt stammt.
            Assert.NotNull(m.ExcerptSourceSha256);
            Assert.NotEqual(m.RawSha256, m.ExcerptSourceSha256);
            Assert.NotNull(m.ContextSha256);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Veraendertes_Umfeld_wird_erkannt()
    {
        DecodeResult data = Decode();

        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + SdvLogFile.Extension);
        try
        {
            SdvLogFile.SaveRecording(file, null, data.Recordings[0], data, AltitudeReference.Standard, "Test");

            using (ZipArchive zip = ZipFile.Open(file, ZipArchiveMode.Update))
            {
                ZipArchiveEntry entry = zip.GetEntry("context.hex")!;
                using Stream s = entry.Open();
                s.SetLength(0);
                using var w = new StreamWriter(s);
                w.Write("CCCC00000000000000000000000000000000");
            }

            SdvDocument loaded = SdvLogFile.Load(file);

            Assert.False(loaded.IntegrityVerified);
            Assert.NotNull(loaded.IntegrityMessage);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Aufnahme_ohne_Rohausschnitt_wird_nicht_stillschweigend_gespeichert()
    {
        // So sieht eine Aufnahme aus einer alten Datei aus, die nur ausgewertete Werte enthielt.
        // Sie einzeln zu speichern hiesse, ausgewertete Werte als Rohdaten auszugeben.
        var withoutSegment = new Recording { Samples = [new Sample()] };
        var data = new DecodeResult { Recordings = [withoutSegment] };

        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + SdvLogFile.Extension);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            SdvLogFile.SaveRecording(file, null, withoutSegment, data, AltitudeReference.Standard, "Test"));

        Assert.Contains("Rohausschnitt", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(file));
    }
}
