using Siemert.DataViewer.Core.Analysis;

namespace DataViewer.Core.Tests;

/// <summary>
/// Tests der Glättungsverfahren.
/// </summary>
/// <remarks>
/// Geprüft werden die Eigenschaften, auf die es bei Messdaten ankommt: keine Zeitverschiebung,
/// keine Verfälschung des Mittelwerts, und bei Savitzky-Golay die definierende Eigenschaft,
/// dass Polynome bis zum gewählten Grad unverändert durchgehen.
/// </remarks>
public sealed class SmoothingTests
{
    private const double Dt = 0.25;

    private static double[] Ramp(int n, double a, double b) =>
        [.. Enumerable.Range(0, n).Select(i => a + (b * i * Dt))];

    // ------------------------------------------------------------------ Savitzky-Golay

    [Fact]
    public void SavitzkyGolay_laesst_eine_Gerade_unveraendert()
    {
        // Die definierende Eigenschaft: Polynome bis Grad 2 gehen exakt durch.
        double[] line = Ramp(200, 100, -3.5);
        double[] smoothed = Smoothing.SavitzkyGolay(line, Dt, 3.0);

        for (int i = 0; i < line.Length; i++)
        {
            Assert.Equal(line[i], smoothed[i], 9);
        }
    }

    [Fact]
    public void SavitzkyGolay_laesst_eine_Parabel_unveraendert()
    {
        double[] parabola = [.. Enumerable.Range(0, 200).Select(i => 5.0 + (2.0 * i * Dt) - (0.75 * i * Dt * i * Dt))];
        double[] smoothed = Smoothing.SavitzkyGolay(parabola, Dt, 4.0);

        for (int i = 0; i < parabola.Length; i++)
        {
            Assert.Equal(parabola[i], smoothed[i], 8);
        }
    }

    [Fact]
    public void SavitzkyGolay_haelt_eine_Spitze_besser_als_der_Mittelwert()
    {
        // Der eigentliche Grund für das Verfahren: Der gleitende Mittelwert drückt eine
        // Öffnungsspitze systematisch nach unten, Savitzky-Golay deutlich weniger.
        var peak = new double[201];
        for (int i = 0; i < peak.Length; i++)
        {
            double d = (i - 100) * Dt;
            peak[i] = 10.0 * Math.Exp(-(d * d) / 0.5);
        }

        double sg = Smoothing.SavitzkyGolay(peak, Dt, 2.0)[100];
        double ma = Signal.MovingAverage(peak, Dt, 2.0)[100];

        Assert.True(sg > ma, $"Savitzky-Golay {sg:F2} muss die Spitze besser halten als {ma:F2}.");
        Assert.True(sg > 0.8 * peak[100], $"Die Spitze darf nicht auf {sg:F2} von {peak[100]:F2} einbrechen.");
    }

    [Fact]
    public void Die_Gewichte_summieren_sich_zu_eins()
    {
        // Andernfalls würde ein konstantes Signal verstärkt oder gedämpft.
        foreach ((int left, int right) in new[] { (4, 4), (0, 4), (4, 0), (1, 3), (2, 6) })
        {
            double[]? w = Smoothing.Weights(left, right, 2);
            Assert.NotNull(w);
            Assert.Equal(1.0, w!.Sum(), 9);
        }
    }

    // ------------------------------------------------------------------ Gemeinsame Eigenschaften

    [Theory]
    [InlineData(SmoothingMethod.MovingAverage)]
    [InlineData(SmoothingMethod.SavitzkyGolay)]
    [InlineData(SmoothingMethod.Gaussian)]
    [InlineData(SmoothingMethod.Median)]
    [InlineData(SmoothingMethod.Spline)]
    public void Ein_konstantes_Signal_bleibt_konstant(SmoothingMethod method)
    {
        double[] flat = [.. Enumerable.Repeat(42.0, 120)];
        double[] smoothed = Smoothing.Apply(flat, Dt, 2.0, method);

        Assert.All(smoothed, v => Assert.Equal(42.0, v, 9));
    }

    [Theory]
    [InlineData(SmoothingMethod.MovingAverage)]
    [InlineData(SmoothingMethod.SavitzkyGolay)]
    [InlineData(SmoothingMethod.Gaussian)]
    [InlineData(SmoothingMethod.Median)]
    [InlineData(SmoothingMethod.Spline)]
    public void Eine_symmetrische_Spitze_wandert_nicht(SmoothingMethod method)
    {
        // Phasenfreiheit. Ein nachlaufendes Fenster verschöbe die Öffnungsstelle.
        var peak = new double[201];
        for (int i = 0; i < peak.Length; i++)
        {
            double d = (i - 100) * Dt;
            peak[i] = Math.Exp(-(d * d) / 2.0);
        }

        double[] smoothed = Smoothing.Apply(peak, Dt, 1.5, method);

        // Der Median erzeugt eine Plateauspitze aus mehreren gleichen Werten. Massgeblich ist
        // deshalb die Mitte aller Hoechstwerte, nicht der erste davon.
        double max = smoothed.Max();
        int[] top = [.. Enumerable.Range(0, smoothed.Length).Where(i => smoothed[i] >= max - 1e-12)];
        double centre = (top[0] + top[^1]) / 2.0;

        Assert.InRange(centre, 99.0, 101.0);
    }

    [Theory]
    [InlineData(SmoothingMethod.MovingAverage)]
    [InlineData(SmoothingMethod.SavitzkyGolay)]
    [InlineData(SmoothingMethod.Gaussian)]
    [InlineData(SmoothingMethod.Median)]
    [InlineData(SmoothingMethod.Spline)]
    public void Ohne_Fensterbreite_wird_nichts_veraendert(SmoothingMethod method)
    {
        double[] values = Ramp(50, 0, 1);
        Assert.Equal(values, Smoothing.Apply(values, Dt, 0, method));
    }

    [Theory]
    [InlineData(SmoothingMethod.MovingAverage)]
    [InlineData(SmoothingMethod.SavitzkyGolay)]
    [InlineData(SmoothingMethod.Gaussian)]
    [InlineData(SmoothingMethod.Median)]
    [InlineData(SmoothingMethod.Spline)]
    public void Fehlende_Werte_bleiben_fehlend(SmoothingMethod method)
    {
        double[] values = [.. Ramp(60, 10, 0.5)];
        values[30] = double.NaN;

        double[] smoothed = Smoothing.Apply(values, Dt, 2.0, method);

        Assert.True(double.IsNaN(smoothed[30]), "Ein fehlender Wert darf nicht erfunden werden.");
        Assert.All(smoothed.Where((_, i) => i != 30), v => Assert.False(double.IsNaN(v)));
    }

    [Theory]
    [InlineData(SmoothingMethod.MovingAverage)]
    [InlineData(SmoothingMethod.SavitzkyGolay)]
    [InlineData(SmoothingMethod.Gaussian)]
    [InlineData(SmoothingMethod.Spline)]
    public void Der_Mittelwert_bleibt_erhalten(SmoothingMethod method)
    {
        var rng = new Random(1234);
        double[] noisy = [.. Enumerable.Range(0, 400).Select(_ => 20.0 + (rng.NextDouble() - 0.5))];

        double[] smoothed = Smoothing.Apply(noisy, Dt, 2.0, method);

        Assert.Equal(noisy.Average(), smoothed.Average(), 2);
    }

    // ------------------------------------------------------------------ Rauschen und Ausreisser

    [Theory]
    [InlineData(SmoothingMethod.MovingAverage)]
    [InlineData(SmoothingMethod.SavitzkyGolay)]
    [InlineData(SmoothingMethod.Gaussian)]
    [InlineData(SmoothingMethod.Spline)]
    public void Rauschen_wird_deutlich_gesenkt(SmoothingMethod method)
    {
        var rng = new Random(7);
        double[] noisy = [.. Enumerable.Range(0, 2000).Select(_ => rng.NextDouble() - 0.5)];

        double[] smoothed = Smoothing.Apply(noisy, Dt, 3.0, method);

        double before = StandardDeviation(noisy);
        double after = StandardDeviation(smoothed);

        Assert.True(after < before / 2.0,
            $"Streuung nur von {before:F3} auf {after:F3} gesenkt.");
    }

    [Fact]
    public void Der_Median_entfernt_einen_einzelnen_Ausreisser_vollstaendig()
    {
        // Genau dafür ist er da: Die Quantisierungsstufen des Drucksensors erzeugen einzelne
        // Ausreißer, die ein linearer Filter über das ganze Fenster verschmiert.
        double[] values = [.. Enumerable.Repeat(5.0, 61)];
        values[30] = 500.0;

        double[] median = Smoothing.Median(values, Dt, 2.0);
        double[] average = Signal.MovingAverage(values, Dt, 2.0);

        Assert.Equal(5.0, median[30], 9);
        Assert.Equal(5.0, median[28], 9);

        // Der Mittelwert zieht den Ausreißer über das gesamte Fenster.
        Assert.True(average[28] > 5.5, "Der gleitende Mittelwert verteilt den Ausreißer.");
    }

    // ------------------------------------------------------------------ Glaettender Spline

    /// <summary>
    /// Der Spline glaettet bei gleicher Fensterangabe deutlich staerker.
    /// </summary>
    /// <remarks>
    /// Nachgemessen an weissem Rauschen, Fenster 3 s: In der Mitte der Reihe bleibt beim
    /// gleitenden Mittelwert eine Streuung von 0,021, beim Spline 0,010. Am Rand sind beide
    /// gleichauf bei 0,037.
    /// </remarks>
    [Fact]
    public void Der_Spline_glaettet_in_der_Mitte_deutlich_staerker()
    {
        double[] noisy = Noise(600, seed: 99);

        double[] spline = Smoothing.Apply(noisy, Dt, 3.0, SmoothingMethod.Spline);
        double[] average = Smoothing.Apply(noisy, Dt, 3.0, SmoothingMethod.MovingAverage);

        double splineMiddle = StandardDeviation([.. spline.Skip(290).Take(20)]);
        double averageMiddle = StandardDeviation([.. average.Skip(290).Take(20)]);

        Assert.True(splineMiddle < averageMiddle * 0.7,
            $"Spline {splineMiddle:F4} gegen Mittelwert {averageMiddle:F4}.");
    }

    /// <summary>
    /// Am Rand glaetten alle Verfahren schwaecher, der Spline aber nicht schlechter.
    /// </summary>
    /// <remarks>
    /// Bei den Fensterverfahren liegt das am verkuerzten Fenster. Beim Spline daran, dass der
    /// erste und der letzte Punkt in nur einer einzigen zweiten Differenz vorkommen und deshalb
    /// weniger gebunden sind. Im Verhaeltnis zur eigenen Mitte ist der Spline dadurch sogar am
    /// schwaechsten - absolut ist sein Rand jedoch so ruhig wie der des Mittelwerts.
    /// </remarks>
    [Fact]
    public void Der_Rand_des_Splines_ist_nicht_unruhiger_als_beim_Mittelwert()
    {
        double[] noisy = Noise(600, seed: 99);

        double splineEdge = StandardDeviation([.. Smoothing.Apply(noisy, Dt, 3.0, SmoothingMethod.Spline).Take(20)]);
        double averageEdge = StandardDeviation([.. Smoothing.Apply(noisy, Dt, 3.0, SmoothingMethod.MovingAverage).Take(20)]);

        Assert.True(splineEdge <= averageEdge * 1.2,
            $"Spline {splineEdge:F4} gegen Mittelwert {averageEdge:F4}.");
    }

    [Fact]
    public void Der_Spline_laesst_eine_Gerade_unveraendert()
    {
        // Bestraft werden zweite Differenzen. Eine Gerade hat keine, geht also exakt durch -
        // gleich wie stark geglaettet wird. Eine gleichmaessige Sinkrate bleibt damit erhalten.
        double[] line = Ramp(300, 500, -12.0);

        foreach (double window in new[] { 1.0, 5.0, 30.0 })
        {
            double[] smoothed = Smoothing.Apply(line, Dt, window, SmoothingMethod.Spline);

            for (int i = 0; i < line.Length; i++)
            {
                Assert.Equal(line[i], smoothed[i], 6);
            }
        }
    }

    [Fact]
    public void Mehr_Gewicht_glaettet_staerker()
    {
        var rng = new Random(5);
        double[] noisy = [.. Enumerable.Range(0, 400).Select(_ => rng.NextDouble() - 0.5)];

        double weak = StandardDeviation(SmoothingSpline.Apply(noisy, 1.0));
        double strong = StandardDeviation(SmoothingSpline.Apply(noisy, 10000.0));

        Assert.True(strong < weak, $"Stark {strong:F4} muss ruhiger sein als schwach {weak:F4}.");
    }

    [Fact]
    public void Ohne_Gewicht_bleibt_die_Reihe_unveraendert()
    {
        double[] values = Ramp(50, 3, 2);
        Assert.Equal(values, SmoothingSpline.Apply(values, 0));
    }

    [Fact]
    public void Sehr_kurze_Reihen_werden_nicht_angefasst()
    {
        // Unter vier Punkten gibt es keine zweite Differenz, die sich bestrafen liesse.
        double[] three = [1.0, 5.0, 2.0];
        Assert.Equal(three, SmoothingSpline.Apply(three, 100.0));
    }

    [Fact]
    public void Das_Gewicht_waechst_mit_der_vierten_Potenz_der_Fensterbreite()
    {
        // Herleitung siehe SmoothingSpline.LambdaForWindow. Verdoppelte Fensterbreite bedeutet
        // sechzehnfaches Gewicht.
        double small = SmoothingSpline.LambdaForWindow(0.25, 2.0);
        double large = SmoothingSpline.LambdaForWindow(0.25, 4.0);

        Assert.Equal(16.0, large / small, 3);
    }

    private static double[] Noise(int count, int seed)
    {
        var rng = new Random(seed);
        return [.. Enumerable.Range(0, count).Select(_ => rng.NextDouble() - 0.5)];
    }

    private static double StandardDeviation(IReadOnlyList<double> values)
    {
        double mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
    }
}
