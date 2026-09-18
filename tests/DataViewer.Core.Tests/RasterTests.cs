using Siemert.DataViewer.Core.Analysis;
using Siemert.DataViewer.Core.Model;
using Siemert.DataViewer.Core.Protocol;

namespace DataViewer.Core.Tests;

/// <summary>
/// Tests gegen einen Auslesevorgang, dessen vierte Aufnahme das Datenraster verlaesst.
/// </summary>
/// <remarks>
/// <para>
/// Diese Aufnahme (Data/readout_sn349_mit_rastersprung.txt) hat einen Fehler aufgedeckt, den
/// beide bisherigen Programmfassungen hatten: Der Temperaturwert, den der Logger zwischen die
/// Messpunkte schiebt, wurde nicht am Wert erkannt, sondern <b>mitgezaehlt</b> - nach je 240
/// Messpunkten wurde ein Einschub angenommen.
/// </para>
/// <para>
/// In dieser Aufnahme kam der erste Einschub aber erst nach 250 Messpunkten. Der starr
/// eingeschobene Zaehlschritt verschob dadurch das gesamte weitere Raster um vier Zeichen, sodass
/// Beschleunigungswerte als Druecke gelesen wurden. Die berechnete Hoehe sprang daraufhin
/// zwischen -18.874 m und +27.421 m.
/// </para>
/// <para>
/// Die Loesung nutzt aus, dass sich die Rohwertebereiche nicht ueberschneiden: Druck 250 bis
/// 1100 hPa liegt roh bei 2500 bis 11000, Temperatur -60 bis +90 °C bei -100 bis 1400.
/// </para>
/// </remarks>
public sealed class RasterTests
{
    private const string FileWithShift = "readout_sn349_mit_rastersprung.txt";

    private static DecodeResult Decode(string file)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", file);
        string[] lines = File.ReadAllText(path).Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();

        int i = lines[0].StartsWith('*') ? 1 : 0;
        return PayloadDecoder.Decode(string.Join(string.Empty, lines.Skip(i + 3)));
    }

    [Fact]
    public void Alle_vier_Aufnahmen_werden_gefunden()
    {
        DecodeResult r = Decode(FileWithShift);
        Assert.Equal(4, r.Recordings.Count);
        Assert.False(r.HasErrors);
    }

    [Theory]
    [InlineData(0, 5360)]
    [InlineData(1, 980)]
    [InlineData(2, 200)]
    [InlineData(3, 2640)]
    public void Messpunktzahl_stimmt_auch_bei_unregelmaessigem_Raster(int index, int expected)
    {
        Assert.Equal(expected, Decode(FileWithShift).Recordings[index].Samples.Count);
    }

    [Fact]
    public void Hoehe_bleibt_physikalisch_moeglich()
    {
        // Der eigentliche Regressionstest. Vor der Korrektur ergab diese Aufnahme Hoehen
        // zwischen -18.874 m und +27.421 m.
        Recording rec = Decode(FileWithShift).Recordings[3];
        var reference = new AltitudeReference
        {
            Mode = AltitudeMode.GroundZero,
            GroundPressureHpa = rec.EndPressureHpa
        };

        double min = double.MaxValue;
        double max = double.MinValue;
        foreach (Sample s in rec.Samples)
        {
            double h = reference.ToAltitudeMeters(s.PressureHpa, s.TemperatureC);
            min = Math.Min(min, h);
            max = Math.Max(max, h);
        }

        Assert.InRange(max - min, 0, 200);
    }

    [Fact]
    public void Druck_bleibt_im_plausiblen_Bereich()
    {
        foreach (Recording rec in Decode(FileWithShift).Recordings)
        {
            Assert.All(rec.Samples, s => Assert.InRange(s.PressureHpa,
                LoggerProtocol.MinPlausiblePressureHpa, LoggerProtocol.MaxPlausiblePressureHpa));
        }
    }

    [Fact]
    public void Temperatur_bleibt_im_plausiblen_Bereich()
    {
        foreach (Recording rec in Decode(FileWithShift).Recordings)
        {
            Assert.All(rec.Samples, s => Assert.InRange(s.TemperatureC,
                LoggerProtocol.MinPlausibleTemperatureC, LoggerProtocol.MaxPlausibleTemperatureC));
        }
    }

    [Fact]
    public void Keine_riesigen_Spruenge_zwischen_benachbarten_Messpunkten()
    {
        // Bei 4 Hz sind mehr als 40 m Hoehenaenderung zwischen zwei Messpunkten (= 160 m/s)
        // physikalisch nicht darstellbar und waren immer ein Zeichen fuer Rasterverlust.
        Recording rec = Decode(FileWithShift).Recordings[3];
        var reference = new AltitudeReference { Mode = AltitudeMode.PressureAltitude };

        double previous = reference.ToAltitudeMeters(rec.Samples[0].PressureHpa);
        double worst = 0;

        for (int i = 1; i < rec.Samples.Count; i++)
        {
            double h = reference.ToAltitudeMeters(rec.Samples[i].PressureHpa);
            worst = Math.Max(worst, Math.Abs(h - previous));
            previous = h;
        }

        Assert.True(worst < 60, $"Groesster Sprung zwischen zwei Messpunkten: {worst:F1} m");
    }

    [Fact]
    public void Rastersprung_wird_gemeldet_statt_verschwiegen()
    {
        // Die vierte Aufnahme enthaelt eine Unregelmaessigkeit von vier Zeichen, die das
        // dokumentierte Format nicht erklaert. Sie wird ausgewertet - aber der Anwender erfaehrt
        // davon, weil an dieser Stelle Messpunkte fehlen koennen.
        DecodeResult r = Decode(FileWithShift);
        Assert.Contains(r.Messages, m =>
            m.Severity == DecodeSeverity.Warning && m.Text.Contains("Rastersprung", StringComparison.Ordinal));

        Assert.Contains(r.Recordings[3].Warnings, w => w.Contains("raster", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Regelmaessige_Aufnahmen_brauchen_keine_Neuausrichtung()
    {
        // Die ersten drei Aufnahmen derselben Datei sind regelmaessig und duerfen keine
        // Warnung ausloesen - sonst waere die Erkennung zu empfindlich eingestellt.
        DecodeResult r = Decode(FileWithShift);

        for (int i = 0; i < 3; i++)
        {
            Assert.DoesNotContain(r.Recordings[i].Warnings, w => w.Contains("raster", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Temperatur_wird_am_Wert_erkannt_nicht_am_Zaehler()
    {
        // Aufnahme 4 hat ihren ersten Einschub nach 250 statt nach 239 Messpunkten. Trotzdem
        // muessen plausible Temperaturen ankommen.
        Recording rec = Decode(FileWithShift).Recordings[3];
        var temps = rec.Samples.Select(s => s.TemperatureC).Distinct().ToList();

        Assert.True(temps.Count > 1, "Es muessen mehrere Temperaturwerte eingeschoben worden sein.");
        Assert.All(temps, t => Assert.InRange(t, 0, 60));
    }
}
