using Siemert.DataViewer.App.Views;
using Siemert.DataViewer.Core.Analysis;
using Siemert.DataViewer.Core.Model;

namespace DataViewer.Core.Tests;

/// <summary>
/// Tests der Messcursor-Statistik.
/// </summary>
/// <remarks>
/// In Fassung 1 war diese Rechnung ungeprüft, und sie enthielt einen Widerspruch: Die Kurve wurde
/// geglättet gezeichnet, die Zahlen daneben aber aus den Rohwerten gebildet, ohne dass das
/// irgendwo stand. Bei zwei Sekunden Glättung zeigte die Kurve dann eine Spitze von 1,9 g,
/// während im Messfeld 10,2 g stand. Hier wird festgeschrieben, dass die Statistik immer die
/// Rohwerte nimmt - und die Oberfläche sagt es dazu.
/// </remarks>
public sealed class MeasureToolTests
{
    private static SeriesData BuildSeries(double[] raw, double[]? shown = null) => new()
    {
        Kind = SeriesKind.AccMagnitude,
        Label = "Beschleunigung",
        Unit = "g",
        Values = shown ?? raw,
        RawValues = raw,
        Visible = true,
        Digits = 2
    };

    [Fact]
    public void Statistik_ueber_den_gesamten_Bereich()
    {
        double[] raw = [1, 3, 2, 8, 4, 2];
        SeriesStatistics s = RangeStatistics.Compute(BuildSeries(raw), 0, 5);

        Assert.Equal(1, s.At1);
        Assert.Equal(2, s.At2);
        Assert.Equal(1, s.Min);
        Assert.Equal(8, s.Max);
        Assert.Equal(1, s.Delta);
        Assert.Equal(20.0 / 6.0, s.Average, 6);
    }

    [Fact]
    public void Bereich_darf_rueckwaerts_aufgezogen_werden()
    {
        double[] raw = [1, 3, 2, 8, 4, 2];

        SeriesStatistics forward = RangeStatistics.Compute(BuildSeries(raw), 1, 4);
        SeriesStatistics backward = RangeStatistics.Compute(BuildSeries(raw), 4, 1);

        // Min, Max und Mittelwert hängen nicht von der Ziehrichtung ab.
        Assert.Equal(forward.Min, backward.Min);
        Assert.Equal(forward.Max, backward.Max);
        Assert.Equal(forward.Average, backward.Average, 6);

        // Die Differenz schon - sie ist Wert am Ende minus Wert am Anfang.
        Assert.Equal(-backward.Delta, forward.Delta, 6);
    }

    [Fact]
    public void Statistik_nimmt_immer_die_Rohwerte_nie_die_geglaettete_Kurve()
    {
        // Eine Spitze von 10 g, die durch Glättung auf knapp 2 g gedrückt wird.
        double[] raw = new double[41];
        for (int i = 0; i < raw.Length; i++)
        {
            raw[i] = 1.0;
        }

        raw[20] = 10.0;

        double[] smoothed = Signal.MovingAverage(raw, 0.25, 2.0);

        Assert.True(smoothed[20] < 4.0, "Die Glättung muss die Spitze sichtbar dämpfen.");

        SeriesStatistics s = RangeStatistics.Compute(BuildSeries(raw, smoothed), 0, raw.Length - 1);

        Assert.Equal(10.0, s.Max, 6);
    }

    [Fact]
    public void Ungueltige_Werte_verfaelschen_den_Mittelwert_nicht()
    {
        double[] raw = [2, double.NaN, 4];
        SeriesStatistics s = RangeStatistics.Compute(BuildSeries(raw), 0, 2);

        Assert.Equal(3.0, s.Average, 6);
        Assert.Equal(2, s.Min);
        Assert.Equal(4, s.Max);
    }

    [Fact]
    public void Indexe_ausserhalb_des_Bereichs_werden_begrenzt()
    {
        double[] raw = [5, 6, 7];
        SeriesStatistics s = RangeStatistics.Compute(BuildSeries(raw), -10, 99);

        Assert.Equal(5, s.At1);
        Assert.Equal(7, s.At2);
        Assert.Equal(6.0, s.Average, 6);
    }

    [Fact]
    public void Zeit_wird_auf_den_naechsten_Messpunkt_gerundet()
    {
        var samples = new List<Sample>();
        for (int i = 0; i < 100; i++)
        {
            samples.Add(new Sample { TimeSeconds = i * 0.25 });
        }

        Assert.Equal(0, RangeStatistics.IndexOfTime(samples, 0.0));
        Assert.Equal(4, RangeStatistics.IndexOfTime(samples, 1.0));
        Assert.Equal(4, RangeStatistics.IndexOfTime(samples, 1.1));
        Assert.Equal(5, RangeStatistics.IndexOfTime(samples, 1.2));

        // Ausserhalb der Aufnahme wird auf die Ränder begrenzt.
        Assert.Equal(0, RangeStatistics.IndexOfTime(samples, -50));
        Assert.Equal(99, RangeStatistics.IndexOfTime(samples, 999));
    }
}
