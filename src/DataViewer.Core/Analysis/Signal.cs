namespace Siemert.DataViewer.Core.Analysis;

/// <summary>
/// Signalverarbeitung fuer Messreihen mit konstanter Abtastrate.
/// </summary>
public static class Signal
{
    /// <summary>
    /// Gleitender Mittelwert ueber ein <b>zentriertes</b> Zeitfenster.
    /// </summary>
    /// <remarks>
    /// Ein nachlaufendes Fenster - wie es frueher verwendet wurde - verschiebt die gesamte Kurve
    /// um die halbe Fensterbreite nach hinten. Bei 2 s Glaettung und 55 m/s Sinkrate sind das
    /// 55 m Hoehenversatz und eine Sekunde Zeitversatz genau an der Oeffnungsstelle, also dort,
    /// wo der Anwender ablesen will. Zentriert ist der Versatz null.
    /// </remarks>
    public static double[] MovingAverage(IReadOnlyList<double> values, double sampleIntervalSeconds, double windowSeconds)
    {
        ArgumentNullException.ThrowIfNull(values);
        int n = values.Count;
        var result = new double[n];
        if (n == 0)
        {
            return result;
        }

        if (windowSeconds <= 0 || sampleIntervalSeconds <= 0)
        {
            for (int i = 0; i < n; i++)
            {
                result[i] = values[i];
            }

            return result;
        }

        int half = Math.Max(1, (int)Math.Round(windowSeconds / sampleIntervalSeconds / 2.0));

        // Praefixsummen, damit die Laufzeit unabhaengig von der Fensterbreite bleibt.
        var prefix = new double[n + 1];
        var count = new int[n + 1];
        for (int i = 0; i < n; i++)
        {
            bool valid = !double.IsNaN(values[i]) && !double.IsInfinity(values[i]);
            prefix[i + 1] = prefix[i] + (valid ? values[i] : 0.0);
            count[i + 1] = count[i] + (valid ? 1 : 0);
        }

        for (int i = 0; i < n; i++)
        {
            // Wo nichts gemessen wurde, darf auch nichts stehen. Vorher wurde an dieser Stelle
            // aus den Nachbarwerten ein Wert gebildet - bei der Temperatur, die erst ab dem
            // ersten Einschub vorliegt, entstand dadurch am Anfang der Aufnahme eine erfundene
            // Kurve.
            if (double.IsNaN(values[i]))
            {
                result[i] = double.NaN;
                continue;
            }

            int lo = Math.Max(0, i - half);
            int hi = Math.Min(n - 1, i + half);
            int c = count[hi + 1] - count[lo];
            result[i] = c > 0 ? (prefix[hi + 1] - prefix[lo]) / c : double.NaN;
        }

        return result;
    }

    /// <summary>
    /// Erste Ableitung ueber eine gleitende lineare Regression (Savitzky-Golay, Grad 1).
    /// </summary>
    /// <remarks>
    /// Eine einfache Differenz zweier Nachbarpunkte verstaerkt das Messrauschen um den Faktor
    /// 1/dt. Bei 0,1 hPa Druckaufloesung und 4 Hz sind das bereits ueber 1 m/s allein aus der
    /// Quantisierung, mit realem Sensorrauschen ein Vielfaches davon. Die Regression ueber ein
    /// Fenster von N Punkten senkt das Rauschen um den Faktor sqrt(12/(N*(N*N-1)))*N.
    /// </remarks>
    /// <param name="values">Messwerte, aequidistant abgetastet.</param>
    /// <param name="sampleIntervalSeconds">Abtastintervall in Sekunden.</param>
    /// <param name="windowSamples">Fensterbreite in Messpunkten; wird auf ungerade aufgerundet, Minimum 3.</param>
    public static double[] Derivative(IReadOnlyList<double> values, double sampleIntervalSeconds, int windowSamples = 9)
    {
        ArgumentNullException.ThrowIfNull(values);
        int n = values.Count;
        var result = new double[n];
        if (n < 2 || sampleIntervalSeconds <= 0)
        {
            return result;
        }

        int w = Math.Max(3, windowSamples);
        if (w % 2 == 0)
        {
            w++;
        }

        int half = w / 2;

        for (int i = 0; i < n; i++)
        {
            int lo = Math.Max(0, i - half);
            int hi = Math.Min(n - 1, i + half);

            // Regression y = a + b*k, gesucht ist b. k ist der Index relativ zur Fenstermitte.
            double mean = (lo + hi) / 2.0;
            double sxx = 0;
            double sxy = 0;
            int c = 0;
            double ySum = 0;

            for (int k = lo; k <= hi; k++)
            {
                double v = values[k];
                if (double.IsNaN(v) || double.IsInfinity(v))
                {
                    continue;
                }

                ySum += v;
                c++;
            }

            if (c < 2)
            {
                result[i] = double.NaN;
                continue;
            }

            double yMean = ySum / c;
            for (int k = lo; k <= hi; k++)
            {
                double v = values[k];
                if (double.IsNaN(v) || double.IsInfinity(v))
                {
                    continue;
                }

                double dx = k - mean;
                sxx += dx * dx;
                sxy += dx * (v - yMean);
            }

            result[i] = sxx > 0 ? sxy / sxx / sampleIntervalSeconds : double.NaN;
        }

        return result;
    }

    /// <summary>
    /// Theoretisches Rauschen der Ableitung bei gegebenem Messrauschen - fuer die Dokumentation
    /// der Fehlergrenzen und zur Wahl der Fensterbreite.
    /// </summary>
    public static double DerivativeNoise(double valueNoise, double sampleIntervalSeconds, int windowSamples)
    {
        int w = Math.Max(3, windowSamples);
        if (w % 2 == 0)
        {
            w++;
        }

        return valueNoise * Math.Sqrt(12.0 / (w * ((double)w * w - 1.0))) / sampleIntervalSeconds;
    }

    public static double Median(IEnumerable<double> values)
    {
        double[] sorted = values.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToArray();
        if (sorted.Length == 0)
        {
            return double.NaN;
        }

        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
