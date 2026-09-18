using Siemert.DataViewer.Core.Analysis;
using Siemert.DataViewer.Core.Io;
using Siemert.DataViewer.Core.Model;
using Siemert.DataViewer.Core.Protocol;
using Siemert.DataViewer.Core.Units;

namespace DataViewer.Core.Tests;

public sealed class AtmosphereTests
{
    [Fact]
    public void Normatmosphaere_liefert_null_Meter_beim_Normdruck()
    {
        Assert.Equal(0.0, Atmosphere.AltitudeMeters(1013.25, 1013.25), 6);
    }

    [Fact]
    public void Hoehenformel_trifft_bekannte_Stuetzstellen()
    {
        // Stuetzstellen der Normatmosphaere, Toleranz 2 m.
        Assert.InRange(Atmosphere.AltitudeMeters(500, 1013.25), 5573, 5577);
        Assert.InRange(Atmosphere.AltitudeMeters(850, 1013.25), 1455, 1459);
        Assert.InRange(Atmosphere.AltitudeMeters(700, 1013.25), 3010, 3014);
    }

    [Fact]
    public void Falsches_QNH_verschiebt_die_Hoehe_erheblich()
    {
        // Genau dieser Fehler steckte in der Vorgaengerversion: der Bezugsdruck war fest
        // auf 1013,25 hPa verdrahtet.
        const double pressureAloft = 700.0;

        double standard = Atmosphere.AltitudeMeters(pressureAloft, 1013.25);
        double lowPressureDay = Atmosphere.AltitudeMeters(pressureAloft, 985.0);

        double error = Math.Abs(standard - lowPressureDay);
        Assert.True(error > 200, $"Erwartet wurde ein Fehler von mehreren hundert Metern, gemessen {error:F0} m.");
    }

    [Fact]
    public void GroundZero_macht_den_QNH_Fehler_weitgehend_unwirksam()
    {
        // Kernaussage: ein QNH-Fehler wirkt im Wesentlichen wie ein Druckversatz und faellt
        // heraus, wenn man gegen den tatsaechlichen Bodendruck rechnet.
        const double groundPressure = 985.0;
        const double aloft = 700.0;

        var correct = new AltitudeReference { Mode = AltitudeMode.GroundZero, GroundPressureHpa = groundPressure };
        double trueAgl = correct.ToAltitudeMeters(aloft);

        // Selbst wenn jemand ein falsches QNH einstellt, bleibt GroundZero davon unberuehrt.
        var stillCorrect = new AltitudeReference
        {
            Mode = AltitudeMode.GroundZero,
            GroundPressureHpa = groundPressure,
            QnhHpa = 1040
        };

        Assert.Equal(trueAgl, stillCorrect.ToAltitudeMeters(aloft), 6);

        // Gegenprobe: gegen die Normatmosphaere liegt dasselbe Sample deutlich daneben.
        double pressureAltitude = AltitudeReference.Standard.ToAltitudeMeters(aloft);
        Assert.True(Math.Abs(pressureAltitude - trueAgl) > 200);
    }

    [Fact]
    public void Hoehenbezug_wird_immer_benannt()
    {
        foreach (AltitudeMode mode in Enum.GetValues<AltitudeMode>())
        {
            var r = new AltitudeReference { Mode = mode, GroundPressureHpa = 1000 };
            Assert.False(string.IsNullOrWhiteSpace(r.Describe()));
            Assert.False(string.IsNullOrWhiteSpace(r.ShortLabel));
        }
    }

    [Fact]
    public void Kalte_Luft_laesst_die_unkorrigierte_Hoehe_zu_hoch_erscheinen()
    {
        double indicated = 3000;
        double cold = Atmosphere.ApplyTemperatureCorrection(indicated, -20);
        Assert.True(cold < indicated);
    }
}

public sealed class SignalTests
{
    [Fact]
    public void Gleitender_Mittelwert_ist_zentriert_und_verschiebt_nicht()
    {
        // Eine Rampe muss durch eine zentrierte Mittelung unveraendert bleiben. Ein
        // nachlaufendes Fenster - wie frueher verwendet - wuerde sie nach hinten verschieben.
        var ramp = Enumerable.Range(0, 200).Select(i => (double)i).ToArray();
        double[] smoothed = Signal.MovingAverage(ramp, 0.25, 2.0);

        for (int i = 20; i < 180; i++)
        {
            Assert.Equal(ramp[i], smoothed[i], 6);
        }
    }

    [Fact]
    public void Ableitung_trifft_eine_bekannte_Steigung()
    {
        // Hoehe faellt mit 50 m/s bei 4 Hz.
        const double dt = 0.25;
        var altitude = Enumerable.Range(0, 200).Select(i => 4000.0 - (50.0 * i * dt)).ToArray();

        double[] v = Signal.Derivative(altitude, dt, 9);

        for (int i = 10; i < 190; i++)
        {
            Assert.Equal(-50.0, v[i], 3);
        }
    }

    [Fact]
    public void Regression_daempft_Rauschen_deutlich_staerker_als_die_einfache_Differenz()
    {
        double single = Signal.DerivativeNoise(0.83, 0.25, 3);
        double window9 = Signal.DerivativeNoise(0.83, 0.25, 9);

        Assert.True(window9 < single / 3.0,
            $"Erwartet wurde eine deutliche Daempfung, gemessen {single:F2} -> {window9:F2} m/s");
    }
}

public sealed class FileRoundTripTests
{
    private static DecodeResult SampleData()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "readout_sn349_G.txt");
        string[] lines = File.ReadAllText(path).Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToArray();
        int i = lines[0].StartsWith('*') ? 1 : 0;
        return PayloadDecoder.Decode(string.Join(string.Empty, lines.Skip(i + 3)));
    }

    [Fact]
    public void Speichern_und_Laden_erhaelt_alle_Aufnahmen()
    {
        DecodeResult data = SampleData();
        var reference = new AltitudeReference { Mode = AltitudeMode.GroundZero, GroundPressureHpa = 982.4 };
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + SdvLogFile.Extension);

        try
        {
            SdvLogFile.Save(file, null, data, reference, "Test");
            SdvDocument loaded = SdvLogFile.Load(file);

            Assert.True(loaded.IntegrityVerified);
            Assert.Equal(data.Recordings.Count, loaded.Data.Recordings.Count);
            Assert.Equal(data.Events.Count, loaded.Data.Events.Count);
            Assert.Equal(
                data.Recordings.Select(r => r.Samples.Count),
                loaded.Data.Recordings.Select(r => r.Samples.Count));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Nachtraegliche_Veraenderung_der_Rohdaten_wird_erkannt()
    {
        DecodeResult data = SampleData();
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + SdvLogFile.Extension);

        try
        {
            SdvLogFile.Save(file, null, data, AltitudeReference.Standard, "Test");

            // Rohdaten im Archiv manipulieren.
            using (var zip = System.IO.Compression.ZipFile.Open(file, System.IO.Compression.ZipArchiveMode.Update))
            {
                System.IO.Compression.ZipArchiveEntry entry = zip.GetEntry("raw.hex")!;
                string content;
                using (var r = new StreamReader(entry.Open()))
                {
                    content = r.ReadToEnd();
                }

                entry.Delete();
                System.IO.Compression.ZipArchiveEntry fresh = zip.CreateEntry("raw.hex");
                using var w = new StreamWriter(fresh.Open());
                w.Write(content.Replace("269B", "2600"));
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
}

public sealed class CsvTests
{
    private static Recording Sample()
    {
        var samples = new List<Sample>();
        for (int i = 0; i < 10; i++)
        {
            samples.Add(new Sample
            {
                TimeSeconds = i * 0.25,
                PressureHpa = 1000 - i,
                TemperatureC = 12.5,
                AccX = 0.1,
                AccY = 0.2,
                AccZ = -0.98
            });
        }

        return new Recording { Samples = samples, IsComplete = true, ClockWasSet = false };
    }

    [Fact]
    public void Internationales_Format_nutzt_Punkt_und_Komma_unabhaengig_vom_Rechner()
    {
        string csv = CsvExporter.Build(Sample(), null, AltitudeReference.Standard,
            UnitSystem.Metric, CsvFlavor.International, "Test");

        Assert.Contains("1000.0,", csv);

        // Nur die Datenzeilen pruefen - die Kopfzeilen sind Fliesstext und duerfen Satzzeichen
        // enthalten.
        string[] dataLines = csv.Split('\n').Where(l => l.Length > 0 && !l.StartsWith('#')).ToArray();
        Assert.All(dataLines, l => Assert.DoesNotContain(";", l));
    }

    [Fact]
    public void Excel_Format_nutzt_Semikolon_und_kuendigt_es_an()
    {
        string csv = CsvExporter.Build(Sample(), null, AltitudeReference.Standard,
            UnitSystem.Metric, CsvFlavor.GermanExcel, "Test");

        Assert.StartsWith("sep=;", csv);
        Assert.Contains("1000,0;", csv);
    }

    [Fact]
    public void Kopfzeilen_dokumentieren_den_Hoehenbezug()
    {
        string csv = CsvExporter.Build(Sample(), null, AltitudeReference.Standard,
            UnitSystem.Metric, CsvFlavor.International, "Test");

        Assert.Contains("# Hoehenbezug:", csv);
        Assert.Contains("Druckhöhe", csv);
    }
}

public sealed class JumpAnalyzerTests
{
    [Fact]
    public void Bodenaufzeichnung_wird_nicht_als_Sprung_ausgegeben()
    {
        // Die echten Testdaten enthalten keinen Sprung. Der Analysator darf dann keine
        // Oeffnungshoehe erfinden.
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "readout_sn349_G.txt");
        string[] lines = File.ReadAllText(path).Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToArray();
        int i = lines[0].StartsWith('*') ? 1 : 0;
        DecodeResult data = PayloadDecoder.Decode(string.Join(string.Empty, lines.Skip(i + 3)));

        foreach (Recording rec in data.Recordings)
        {
            JumpMetrics m = JumpAnalyzer.Analyze(rec, AltitudeReference.Standard);
            Assert.False(m.JumpDetected);
            Assert.Null(m.DeploymentAltitudeM);
            Assert.Contains("Kein Sprungprofil", m.Note);
        }
    }

    [Fact]
    public void Kuenstliches_Sprungprofil_wird_erkannt()
    {
        var samples = new List<Sample>();
        double t = 0;

        void Add(double altitude, double g)
        {
            // Hoehe -> Druck umrechnen (Umkehrung der barometrischen Formel).
            double ratio = 1.0 - (altitude * Atmosphere.LapseRateKPerM / Atmosphere.StandardTemperatureK);
            double p = Atmosphere.StandardPressureHpa * Math.Pow(ratio, 1.0 / Atmosphere.Exponent);
            samples.Add(new Sample
            {
                TimeSeconds = t,
                PressureHpa = p,
                TemperatureC = 10,
                AccX = 0,
                AccY = 0,
                AccZ = g
            });
            t += 0.25;
        }

        // 20 s am Boden, dann Steigflug auf 4000 m, 40 s Freifall, Oeffnung, Schirmfahrt.
        for (int i = 0; i < 80; i++) { Add(0, 1.0); }
        for (int i = 0; i < 400; i++) { Add(i * 10.0, 1.0); }
        double alt = 4000;
        while (alt > 1100) { alt -= 55 * 0.25; Add(alt, 0.05); }
        for (int i = 0; i < 12; i++) { alt -= 20 * 0.25; Add(alt, 6.0); }
        while (alt > 0) { alt -= 5 * 0.25; Add(Math.Max(0, alt), 1.0); }
        for (int i = 0; i < 40; i++) { Add(0, 1.0); }

        var rec = new Recording { Samples = samples, IsComplete = true };
        JumpMetrics m = JumpAnalyzer.Analyze(rec, AltitudeReference.Standard);

        Assert.True(m.JumpDetected);
        Assert.NotNull(m.DeploymentAltitudeM);
        Assert.NotNull(m.FreefallSeconds);
        Assert.InRange(m.MaxDescentRateMs, 45, 65);
        Assert.InRange(m.DeploymentAltitudeM!.Value, 900, 1300);
        Assert.True(m.MaxOpeningG > 3);
    }
}

public sealed class ReferenceZeroTests
{
    /// <summary>
    /// Der Nullpunkt muss genau dort liegen, wo er angekuendigt ist.
    /// </summary>
    /// <remarks>
    /// Ein Bezug, der um einen konstanten Betrag danebenliegt, faellt beim Ablesen einer
    /// Oeffnungshoehe nicht auf und verfaelscht sie trotzdem vollstaendig.
    /// </remarks>
    [Fact]
    public void Bezug_auf_das_Ende_setzt_den_Enddruck_auf_null()
    {
        var reference = new AltitudeReference
        {
            Mode = AltitudeMode.GroundZero,
            GroundPressureHpa = 982.4,
            StartPressureHpa = 800.0
        };

        Assert.Equal(0.0, reference.ToAltitudeMeters(982.4), 6);
        Assert.True(reference.ToAltitudeMeters(800.0) > 0, "Geringerer Druck muss hoeher liegen.");
    }

    [Fact]
    public void Bezug_auf_den_Anfang_setzt_den_Anfangsdruck_auf_null()
    {
        var reference = new AltitudeReference
        {
            Mode = AltitudeMode.StartZero,
            GroundPressureHpa = 982.4,
            StartPressureHpa = 800.0
        };

        Assert.Equal(0.0, reference.ToAltitudeMeters(800.0), 6);
        Assert.True(reference.ToAltitudeMeters(982.4) < 0, "Hoeherer Druck muss tiefer liegen.");
    }

    /// <summary>
    /// Der Bezugsdruck verschiebt den Nullpunkt nicht nur, er veraendert auch den Massstab.
    /// </summary>
    /// <remarks>
    /// Die barometrische Hoehenformel ist nicht linear: h = (T0/L) * (1 - (p/p_ref)^k). Ein
    /// anderer Bezugsdruck skaliert das Ergebnis deshalb mit. Eine Hoehendifferenz haengt damit
    /// vom gewaehlten Bezug ab. Bei realistischen Bezugsdruecken bleibt der Effekt klein - hier
    /// nachgemessen: 1013,25 gegen 1000,0 hPa ergibt ueber 2900 m Unterschied 7,3 m, also
    /// 0,25 %. Der Test haelt diese Groessenordnung fest, damit sie nicht unbemerkt waechst.
    /// </remarks>
    [Fact]
    public void Der_Bezugsdruck_veraendert_Hoehendifferenzen_nur_geringfuegig()
    {
        var a = new AltitudeReference { Mode = AltitudeMode.GroundZero, GroundPressureHpa = 1013.25 };
        var b = new AltitudeReference { Mode = AltitudeMode.GroundZero, GroundPressureHpa = 1000.0 };

        double spanA = a.ToAltitudeMeters(700.0) - a.ToAltitudeMeters(1000.0);
        double spanB = b.ToAltitudeMeters(700.0) - b.ToAltitudeMeters(1000.0);

        Assert.InRange(Math.Abs(spanA - spanB) / spanA, 0.0, 0.01);
    }

    [Fact]
    public void Der_Nullpunkt_verschiebt_sich_um_den_erwarteten_Betrag()
    {
        // Beide Bezuege zeigen dieselbe Luftsaeule, nur mit unterschiedlichem Nullpunkt. Die
        // Reihenfolge der Werte muss deshalb bei beiden gleich bleiben.
        var toEnd = new AltitudeReference
        {
            Mode = AltitudeMode.GroundZero, GroundPressureHpa = 1000.0, StartPressureHpa = 700.0
        };

        var toStart = new AltitudeReference
        {
            Mode = AltitudeMode.StartZero, GroundPressureHpa = 1000.0, StartPressureHpa = 700.0
        };

        double[] pressures = [1000.0, 950.0, 850.0, 700.0, 500.0];

        for (int i = 1; i < pressures.Length; i++)
        {
            Assert.True(toEnd.ToAltitudeMeters(pressures[i]) > toEnd.ToAltitudeMeters(pressures[i - 1]));
            Assert.True(toStart.ToAltitudeMeters(pressures[i]) > toStart.ToAltitudeMeters(pressures[i - 1]));
        }
    }

    [Fact]
    public void Ohne_Anfangsdruck_wird_gegen_die_Normatmosphaere_gerechnet()
    {
        var reference = new AltitudeReference { Mode = AltitudeMode.StartZero, StartPressureHpa = null };

        Assert.Equal(0.0, reference.ToAltitudeMeters(Atmosphere.StandardPressureHpa), 6);
    }

    [Fact]
    public void Der_Bezug_benennt_sich_selbst()
    {
        var reference = new AltitudeReference { Mode = AltitudeMode.StartZero, StartPressureHpa = 987.6 };

        Assert.Contains("987,6", reference.Describe(), StringComparison.Ordinal);
        Assert.Contains("Beginn", reference.ShortLabel, StringComparison.Ordinal);
    }
}

public sealed class SupplyVoltageTests
{
    /// <summary>
    /// Die vom Hersteller genannte Rechnung, an seinem eigenen Zahlenbeispiel geprueft.
    /// </summary>
    /// <remarks>
    /// U = Rohwert / 1024 * 2,5 V * 2. Fuer den Rohwert 619 ergibt das 3,0225 V. Die frueher
    /// hier verwendete Naeherung 1/128 lieferte fuer denselben Rohwert 4,84 V und haette eine
    /// leere Batterie als voll ausgewiesen.
    /// </remarks>
    [Fact]
    public void Der_Rohwert_619_ergibt_3_0225_Volt()
    {
        double volts = 619 * LoggerProtocol.SupplyVoltagePerCount;
        Assert.Equal(3.0225, volts, 4);
    }

    [Fact]
    public void Die_Formel_entspricht_der_Herstellerangabe()
    {
        foreach (int raw in new[] { 0, 1, 256, 619, 1023 })
        {
            double expected = raw / 1024.0 * 2.5 * 2.0;
            Assert.Equal(expected, raw * LoggerProtocol.SupplyVoltagePerCount, 9);
        }
    }

    [Fact]
    public void Der_Vollausschlag_liegt_bei_fuenf_Volt()
    {
        // 10 Bit, 2,5 V Referenz, Teiler 1:2. Mehr kann der Umsetzer nicht darstellen.
        Assert.Equal(5.0, 1024 * LoggerProtocol.SupplyVoltagePerCount, 6);
    }

    [Fact]
    public void Drei_Zellen_bei_3_Volt_gelten_als_erschoepft()
    {
        // 3,02 V auf drei Zellen sind 1,007 V je Zelle. Eine AG13 ist damit am Ende.
        var recording = new Recording { SupplyVoltageRaw = 619, Samples = [new Sample()] };

        Assert.Equal(3.0225, recording.SupplyVoltage, 4);
        Assert.True(recording.BatteryFraction < 0.1,
            $"Ladezustand {recording.BatteryFraction:P0} ist fuer 1,01 V je Zelle zu hoch.");
    }

    [Fact]
    public void Der_Faktor_gilt_nicht_mehr_als_vorlaeufig()
    {
        Assert.False(LoggerProtocol.SupplyVoltageScaleIsProvisional);
    }
}

public sealed class TimestampTests
{
    /// <summary>
    /// Der Beweis, dass das Datum dezimal und nicht hexadezimal kodiert ist.
    /// </summary>
    /// <remarks>
    /// Kopfdaten einer Aufnahme des Geraets mit der Seriennummer 349. Der Auszug wurde am
    /// 17.09.2026 geholt, und genau dieses Datum steht im Zeitstempel. Waere das Feld
    /// hexadezimal zu lesen, ergaebe 0x17 den Tag 23 - ein Datum, das es zum Zeitpunkt des
    /// Auslesens noch gar nicht gab.
    /// </remarks>
    [Fact]
    public void Das_Datum_wird_dezimal_gelesen()
    {
        Assert.True(TimestampCodec.TryDecode("0B291717092026", out DateTime? stamp, out TimeSpan tod, out bool set));

        Assert.True(set);
        Assert.Equal(new DateTime(2026, 9, 17), stamp!.Value.Date);
        Assert.Equal(new TimeSpan(23, 41, 11), tod);
    }

    /// <summary>
    /// Die Uhrzeit ist umgekehrt hexadezimal kodiert.
    /// </summary>
    /// <remarks>
    /// Belegt durch den Minutenwert 0x2E aus einem Mitschnitt: Als Dezimalziffern waere 'E'
    /// keine gueltige Ziffer.
    /// </remarks>
    [Fact]
    public void Die_Uhrzeit_wird_hexadezimal_gelesen()
    {
        Assert.True(TimestampCodec.TryDecode("372E1700000000", out _, out TimeSpan tod, out bool set));

        Assert.Equal(new TimeSpan(23, 46, 55), tod);
        Assert.False(set);
    }

    /// <summary>
    /// Ein Datum aus Nullen bedeutet: Die Uhr des Geraets war nicht gestellt.
    /// </summary>
    /// <remarks>
    /// Hier wird bewusst kein Ersatzdatum geliefert. Die Vorgaengerversion setzte in diesem Fall
    /// den 01.01.1900 ein; das sah nach einer Angabe aus, war aber keine.
    /// </remarks>
    [Fact]
    public void Ein_Datum_aus_Nullen_ist_kein_Datum()
    {
        Assert.True(TimestampCodec.TryDecode("22080000000000", out DateTime? stamp, out TimeSpan tod, out bool set));

        Assert.Null(stamp);
        Assert.False(set);
        Assert.Equal(new TimeSpan(0, 8, 34), tod);
    }

    [Fact]
    public void Ein_gueltiges_Datum_wird_erkannt()
    {
        // Dieselbe Aufnahme wie oben, aber mit gestellter Uhr: 02.02.2026 um 21:48:48.
        Assert.True(TimestampCodec.TryDecode("30301502022026", out DateTime? stamp, out TimeSpan tod, out bool set));

        Assert.True(set);
        Assert.Equal(new DateTime(2026, 2, 2, 21, 48, 48), stamp);
        Assert.Equal(new TimeSpan(21, 48, 48), tod);
    }

    [Theory]
    [InlineData("22080032012026")]   // Tag 32
    [InlineData("22080001132026")]   // Monat 13
    [InlineData("22080001021926")]   // Jahr 1926
    public void Unplausible_Daten_werden_verworfen_statt_umgedeutet(string hex)
    {
        Assert.True(TimestampCodec.TryDecode(hex, out DateTime? stamp, out _, out bool set));

        Assert.Null(stamp);
        Assert.False(set);
    }

    [Fact]
    public void Eine_unplausible_Uhrzeit_macht_den_ganzen_Stempel_ungueltig()
    {
        // 0x3C = 60 Sekunden gibt es nicht. Dann stimmt an dieser Stelle etwas grundsaetzlich
        // nicht, und es wird auch keine Uhrzeit behauptet.
        Assert.False(TimestampCodec.TryDecode("3C080017092026", out _, out _, out _));
    }
}
