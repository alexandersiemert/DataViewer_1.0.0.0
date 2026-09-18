using Siemert.DataViewer.Core.Model;
using Siemert.DataViewer.Core.Protocol;

namespace DataViewer.Core.Tests;

/// <summary>
/// Diese Tests laufen gegen einen echten Auslesevorgang des Geraets mit der Seriennummer 349
/// (Datei Data/readout_sn349_G.txt, aufgezeichnet an COM3). Die Sollwerte stammen nicht aus dem
/// Quelltext, sondern aus der unabhaengigen Auswertung des Rohdatenstroms.
/// </summary>
public sealed class DecoderTests
{
    private static string LoadPayload(string file)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", file);
        string[] lines = File.ReadAllText(path)
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();

        // Die Nutzdaten beginnen nach Echo, Endadresse und Geraetekopf. Ein evtl. vorhandenes
        // Ping-Echo ("*?") wird uebersprungen.
        int i = 0;
        if (lines[i].StartsWith('*'))
        {
            i++;
        }

        return string.Join(string.Empty, lines.Skip(i + 3));
    }

    private static DecodeResult Decode(string file = "readout_sn349_G.txt") =>
        PayloadDecoder.Decode(LoadPayload(file));

    [Fact]
    public void Findet_alle_drei_Aufnahmen()
    {
        DecodeResult r = Decode();
        Assert.Equal(3, r.Recordings.Count);
        Assert.False(r.HasErrors);
    }

    [Theory]
    [InlineData(0, 5360)]
    [InlineData(1, 980)]
    [InlineData(2, 200)]
    public void Messpunktzahl_stimmt(int index, int expected)
    {
        DecodeResult r = Decode();
        Assert.Equal(expected, r.Recordings[index].Samples.Count);
    }

    [Fact]
    public void Aufnahmen_sind_vollstaendig_abgeschlossen()
    {
        DecodeResult r = Decode();
        Assert.All(r.Recordings, rec => Assert.True(rec.IsComplete));
    }

    [Fact]
    public void Endtemperatur_und_Enddruck_sind_skaliert()
    {
        // Das war der Fehler der Vorgaengerversion: die Rohzaehlwerte 787 bzw. 9824 wurden
        // ungeteilt uebernommen und landeten so in jeder Archivdatei.
        Recording first = Decode().Recordings[0];
        Assert.Equal(28.7, first.EndTemperatureC, 1);
        Assert.Equal(982.4, first.EndPressureHpa, 1);

        Recording last = Decode().Recordings[2];
        Assert.Equal(19.9, last.EndTemperatureC, 1);
        Assert.Equal(988.9, last.EndPressureHpa, 1);
    }

    [Fact]
    public void Starttemperatur_und_Startdruck_stimmen()
    {
        Recording first = Decode().Recordings[0];
        Assert.Equal(18.4, first.StartTemperatureC, 1);
        Assert.Equal(988.3, first.StartPressureHpa, 1);
    }

    [Fact]
    public void Ungestellte_Loggeruhr_erfindet_kein_Datum()
    {
        DecodeResult r = Decode();

        // Aufnahmen 1 und 2 stammen von einem Geraet mit ungestellter Uhr. Frueher wurde daraus
        // "01.01.1900" - ein Datum, das der Anwender nicht einordnen kann.
        Assert.False(r.Recordings[0].ClockWasSet);
        Assert.Null(r.Recordings[0].StartTime);
        Assert.Equal(new TimeSpan(0, 8, 34), r.Recordings[0].StartTimeOfDay);

        Assert.False(r.Recordings[1].ClockWasSet);
        Assert.Equal(new TimeSpan(23, 46, 55), r.Recordings[1].StartTimeOfDay);
    }

    [Fact]
    public void Gestellte_Loggeruhr_liefert_vollen_Zeitstempel()
    {
        Recording third = Decode().Recordings[2];
        Assert.True(third.ClockWasSet);
        Assert.Equal(new DateTime(2026, 2, 2, 21, 48, 48), third.StartTime);
        Assert.Equal(new DateTime(2026, 2, 2, 21, 49, 37), third.EndTime);
    }

    [Fact]
    public void Dauer_aus_Messpunkten_passt_zu_den_Zeitstempeln()
    {
        // Der entscheidende Beleg fuer das Abtastintervall von 250 ms: die aus der Anzahl der
        // Messpunkte berechnete Dauer muss zur Differenz der Zeitstempel passen.
        foreach (Recording rec in Decode().Recordings)
        {
            TimeSpan byTimestamp = rec.EndTimeOfDay - rec.StartTimeOfDay;
            if (byTimestamp < TimeSpan.Zero)
            {
                byTimestamp += TimeSpan.FromDays(1);
            }

            double delta = Math.Abs(byTimestamp.TotalSeconds - rec.Duration.TotalSeconds);
            Assert.True(delta <= 1.5,
                $"Dauer weicht ab: Zeitstempel {byTimestamp.TotalSeconds} s, Messpunkte {rec.Duration.TotalSeconds} s");
        }
    }

    [Fact]
    public void Temperatureinschuebe_liegen_auf_dem_Raster()
    {
        Recording first = Decode().Recordings[0];

        // Nach jedem Einschub ist TemperatureHeld false; die Abstaende muessen 240 betragen.
        var marks = first.Samples
            .Select((s, i) => (s, i))
            .Where(t => !t.s.TemperatureHeld && t.i > 0)
            .Select(t => t.i)
            .ToList();

        Assert.NotEmpty(marks);
        for (int k = 1; k < marks.Count; k++)
        {
            Assert.Equal(LoggerProtocol.TemperatureInterval, marks[k] - marks[k - 1]);
        }
    }

    [Fact]
    public void Betriebsereignisse_werden_erkannt()
    {
        DecodeResult r = Decode();
        Assert.Equal(19, r.Events.Count);
        Assert.Equal(10, r.Events.Count(e => e.Kind == DeviceEventKind.ConnectedToPc));
        Assert.Equal(9, r.Events.Count(e => e.Kind == DeviceEventKind.DisconnectedFromPc));

        // Der letzte Einschaltsatz gehoert zu genau diesem Auslesevorgang.
        DeviceEvent last = r.Events[^1];
        Assert.Equal(DeviceEventKind.ConnectedToPc, last.Kind);
        Assert.True(last.ClockWasSet);
    }

    [Fact]
    public void Batteriespannung_und_Statusfeld_werden_uebernommen()
    {
        DecodeResult r = Decode();
        // Aufbau laut Herstellerdokumentation: Status(4) VCC(4) Viertelsekunde(2) ...
        Assert.Equal(619, r.Recordings[0].SupplyVoltageRaw);
        Assert.Equal(618, r.Recordings[2].SupplyVoltageRaw);
        Assert.All(r.Recordings, rec => Assert.Equal(0, rec.StatusRaw));
        Assert.All(r.Recordings, rec => Assert.False(rec.LowBattery));
        Assert.All(r.Recordings, rec => Assert.InRange(rec.EndQuarterSecond, 0, 3));
    }

    [Fact]
    public void Ruhender_Logger_misst_eine_Erdbeschleunigung()
    {
        // Das Geraet lag zu Beginn der ersten Aufnahme still. Wenn die Bitentpackung und die
        // Skalierung stimmen, muss der Betrag des Beschleunigungsvektors nahe 1 g liegen.
        Sample first = Decode().Recordings[0].Samples[0];
        Assert.InRange(first.AccMagnitude, 0.9, 1.1);
    }

    [Fact]
    public void Keine_Saettigung_in_dieser_Bodenaufzeichnung()
    {
        Assert.All(Decode().Recordings, rec => Assert.False(rec.HasSaturatedSamples));
    }

    [Fact]
    public void Aeltere_Aufzeichnung_desselben_Geraets_liefert_dieselben_Aufnahmen()
    {
        // Der frueher aufgenommene Mitschnitt ist ein Praefix des spaeteren. Die Dekodierung
        // muss darauf identische Aufnahmen liefern.
        DecodeResult older = Decode("readout_sn349_earlier.txt");
        Assert.Equal(3, older.Recordings.Count);
        Assert.Equal(5360, older.Recordings[0].Samples.Count);
        Assert.Equal(980, older.Recordings[1].Samples.Count);
        Assert.Equal(200, older.Recordings[2].Samples.Count);
    }

    [Fact]
    public void Abgeschnittene_Uebertragung_wird_gemeldet_statt_verschwiegen()
    {
        // Genau dieser Fall trat produktiv auf: die Uebertragung endete vorzeitig, und die
        // letzte Aufnahme verschwand kommentarlos aus der Liste.
        string payload = LoadPayload("readout_sn349_G.txt");
        string truncated = payload[..(payload.Length - 400)];

        DecodeResult r = PayloadDecoder.Decode(truncated);

        Assert.Equal(3, r.Recordings.Count);
        Assert.False(r.Recordings[2].IsComplete);
        Assert.Contains(r.Messages, m => m.Severity == DecodeSeverity.Warning && m.Text.Contains("unvollst"));
    }

    [Fact]
    public void Beschleunigungswert_FFFF_beendet_keine_Aufnahme()
    {
        // FFFF ist als Beschleunigungswert (-0,048 g) ein voellig normaler Messwert. Nur an der
        // Position eines Druckwerts bedeutet es das Ende der Messdaten.
        (double value, bool saturated) = PayloadDecoder.DecodeAcceleration("FFFF");
        Assert.Equal(-0.048, value, 3);
        Assert.False(saturated);
    }

    [Fact]
    public void Saettigung_wird_erkannt()
    {
        // 0x7FC0 entspricht dem oberen Anschlag des 10-Bit-Werts.
        (double value, bool saturated) = PayloadDecoder.DecodeAcceleration("7FC0");
        Assert.True(saturated);
        Assert.True(value >= 16.0);
    }

    [Fact]
    public void Rohdaten_mit_Stoerzeichen_werden_bereinigt()
    {
        string clean = PayloadDecoder.Sanitize("AA AA\r\n00ff\tZZ", out int dropped);
        Assert.Equal("AAAA00FF", clean);
        Assert.Equal(2, dropped);
    }
}
