namespace Siemert.DataViewer.Core.Analysis;

/// <summary>Verfahren zur Glättung einer Messreihe.</summary>
public enum SmoothingMethod
{
    /// <summary>Gleitender Mittelwert über ein zentriertes Fenster (Rechteckfenster).</summary>
    MovingAverage = 0,

    /// <summary>Gleitende Polynomregression nach Savitzky und Golay.</summary>
    SavitzkyGolay = 1,

    /// <summary>Gauß-gewichtetes Fenster.</summary>
    Gaussian = 2,

    /// <summary>Gleitender Median.</summary>
    Median = 3,

    /// <summary>Glättender Spline nach Whittaker und Henderson.</summary>
    Spline = 4
}

/// <summary>
/// Glättungsverfahren für äquidistant abgetastete Messreihen.
/// </summary>
/// <remarks>
/// <para>
/// Alle Verfahren hier sind <b>phasenfrei</b>: Das Fenster liegt symmetrisch um den
/// betrachteten Punkt, die geglättete Kurve wird also weder nach vorn noch nach hinten
/// verschoben. Ein nachlaufendes Fenster verschöbe bei 2 s Glättung und 55 m/s Sinkrate die
/// Öffnungsstelle um 55 m.
/// </para>
/// <para>
/// Zur Wahl des Verfahrens, in der Sprache der Signaltheorie:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Gleitender Mittelwert.</b> Ein Rechteckfenster. Sein Frequenzgang ist eine
/// Spaltfunktion mit Nebenzipfeln von nur 13 dB Dämpfung, und er senkt echte Spitzen
/// systematisch ab. Für eine Öffnungsspitze, die man beurteilen will, ist das die
/// ungünstigste Wahl. Rauschen senkt er um den Faktor Wurzel(N).
/// </item>
/// <item>
/// <b>Savitzky-Golay.</b> Legt in jedes Fenster ein Polynom vom Grad d nach der Methode der
/// kleinsten Quadrate und nimmt dessen Wert in der Fenstermitte. Polynome bis Grad d gehen
/// dadurch unverändert durch; Höhe und Lage von Spitzen bleiben weitgehend erhalten. Das ist
/// der übliche Standard für Messreihen, bei denen es auf Extremwerte ankommt.
/// </item>
/// <item>
/// <b>Gauß.</b> Der Frequenzgang ist wieder eine Gaußkurve, also ohne Nebenzipfel und ohne
/// Überschwingen. Glättet am gleichmäßigsten, dämpft Spitzen aber wie der Mittelwert.
/// </item>
/// <item>
/// <b>Median.</b> Kein linearer Filter. Entfernt einzelne Ausreißer vollständig, statt sie
/// über das Fenster zu verteilen, und lässt Sprünge stehen. Sinnvoll gegen die
/// Quantisierungsstufen des Drucksensors.
/// </item>
/// <item>
/// <b>Spline.</b> Kein Fensterverfahren: Es wird eine einzige Kurve durch die ganze Reihe
/// gelegt, die zwischen Nähe zu den Messwerten und geringer Krümmung abwägt. Dadurch gibt es
/// keine Randbehandlung, und eine gleichmäßige Sinkrate geht unverändert durch. Siehe
/// <see cref="SmoothingSpline"/>.
/// </item>
/// </list>
/// </remarks>
public static class Smoothing
{
    /// <summary>Grad des Polynoms bei Savitzky-Golay.</summary>
    public const int PolynomialDegree = 2;

    /// <summary>
    /// Glättet eine Messreihe mit dem gewählten Verfahren.
    /// </summary>
    /// <param name="values">Messwerte, äquidistant abgetastet. <c>NaN</c> ist zulässig.</param>
    /// <param name="sampleIntervalSeconds">Abtastintervall in Sekunden.</param>
    /// <param name="windowSeconds">Fensterbreite in Sekunden. Null oder kleiner lässt die Reihe unverändert.</param>
    /// <param name="method">Das Verfahren.</param>
    public static double[] Apply(
        IReadOnlyList<double> values,
        double sampleIntervalSeconds,
        double windowSeconds,
        SmoothingMethod method)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (windowSeconds <= 0 || sampleIntervalSeconds <= 0 || values.Count == 0)
        {
            return [.. values];
        }

        return method switch
        {
            SmoothingMethod.SavitzkyGolay => SavitzkyGolay(values, sampleIntervalSeconds, windowSeconds),
            SmoothingMethod.Gaussian => Gaussian(values, sampleIntervalSeconds, windowSeconds),
            SmoothingMethod.Median => Median(values, sampleIntervalSeconds, windowSeconds),
            SmoothingMethod.Spline => SmoothingSpline.Apply(
                values, SmoothingSpline.LambdaForWindow(sampleIntervalSeconds, windowSeconds)),
            _ => Signal.MovingAverage(values, sampleIntervalSeconds, windowSeconds)
        };
    }

    /// <summary>Halbe Fensterbreite in Messpunkten, mindestens 1.</summary>
    private static int HalfWidth(double sampleIntervalSeconds, double windowSeconds) =>
        Math.Max(1, (int)Math.Round(windowSeconds / sampleIntervalSeconds / 2.0));

    // ------------------------------------------------------------------ Savitzky-Golay

    /// <summary>
    /// Gleitende Polynomregression.
    /// </summary>
    /// <remarks>
    /// Am Rand wird das Fenster nicht gespiegelt und nicht fortgesetzt, sondern verkürzt. Ein
    /// gespiegelter Rand würde dort eine Symmetrie behaupten, die in den Messdaten nicht
    /// vorkommt; ein fortgesetzter Rand würde den Randwert verdoppeln.
    /// </remarks>
    public static double[] SavitzkyGolay(
        IReadOnlyList<double> values,
        double sampleIntervalSeconds,
        double windowSeconds,
        int degree = PolynomialDegree)
    {
        int n = values.Count;
        var result = new double[n];
        int half = HalfWidth(sampleIntervalSeconds, windowSeconds);

        // Für einen Polynomgrad d braucht es mindestens d+1 Stützstellen.
        if (2 * half + 1 < degree + 1)
        {
            return [.. values];
        }

        // Für Fenster voller Breite sind die Gewichte überall gleich und werden einmal berechnet.
        double[]? interior = Weights(half, half, degree);
        var cache = new Dictionary<(int, int), double[]?>();

        for (int i = 0; i < n; i++)
        {
            if (double.IsNaN(values[i]))
            {
                result[i] = double.NaN;
                continue;
            }

            int left = Math.Min(half, i);
            int right = Math.Min(half, n - 1 - i);

            bool complete = true;
            for (int k = -left; k <= right; k++)
            {
                if (double.IsNaN(values[i + k]))
                {
                    complete = false;
                    break;
                }
            }

            if (!complete)
            {
                // Lücken im Fenster: über die vorhandenen Punkte mitteln, statt zu raten.
                result[i] = MeanOfValid(values, i - left, i + right);
                continue;
            }

            double[]? w;
            if (left == half && right == half)
            {
                w = interior;
            }
            else if (!cache.TryGetValue((left, right), out w))
            {
                w = Weights(left, right, degree);
                cache[(left, right)] = w;
            }

            if (w is null)
            {
                result[i] = MeanOfValid(values, i - left, i + right);
                continue;
            }

            double sum = 0;
            for (int k = 0; k < w.Length; k++)
            {
                sum += w[k] * values[i - left + k];
            }

            result[i] = sum;
        }

        return result;
    }

    /// <summary>
    /// Gewichte, mit denen sich der Wert des ausgleichenden Polynoms in der Fenstermitte als
    /// gewichtete Summe der Messwerte ergibt.
    /// </summary>
    /// <remarks>
    /// Für Stützstellen t = -left … +right und den Ansatz p(t) = Summe a_j t^j gilt nach der
    /// Methode der kleinsten Quadrate a = (A^T A)^-1 A^T y. Gesucht ist p(0) = a_0, also die
    /// erste Zeile von (A^T A)^-1 A^T. Die Normalmatrix ist nur (d+1) mal (d+1) gross und wird
    /// direkt geloest.
    /// </remarks>
    internal static double[]? Weights(int left, int right, int degree)
    {
        int m = left + right + 1;
        if (m < degree + 1)
        {
            return null;
        }

        // Normalmatrix: N[j,k] = Summe t^(j+k)
        int size = degree + 1;
        var normal = new double[size, size];

        for (int j = 0; j < size; j++)
        {
            for (int k = 0; k < size; k++)
            {
                double sum = 0;
                for (int t = -left; t <= right; t++)
                {
                    sum += Math.Pow(t, j + k);
                }

                normal[j, k] = sum;
            }
        }

        // Gesucht: c = N^-1 * e_0
        double[]? c = SolveFirstUnit(normal, size);
        if (c is null)
        {
            return null;
        }

        var w = new double[m];
        for (int index = 0; index < m; index++)
        {
            int t = index - left;
            double sum = 0;
            for (int j = 0; j < size; j++)
            {
                sum += c[j] * Math.Pow(t, j);
            }

            w[index] = sum;
        }

        return w;
    }

    /// <summary>Löst <c>matrix * x = e_0</c> mit Gauß-Elimination und Spaltenpivotisierung.</summary>
    private static double[]? SolveFirstUnit(double[,] matrix, int size)
    {
        var a = new double[size, size + 1];
        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                a[i, j] = matrix[i, j];
            }

            a[i, size] = i == 0 ? 1.0 : 0.0;
        }

        for (int col = 0; col < size; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < size; row++)
            {
                if (Math.Abs(a[row, col]) > Math.Abs(a[pivot, col]))
                {
                    pivot = row;
                }
            }

            if (Math.Abs(a[pivot, col]) < 1e-12)
            {
                return null;
            }

            if (pivot != col)
            {
                for (int j = col; j <= size; j++)
                {
                    (a[col, j], a[pivot, j]) = (a[pivot, j], a[col, j]);
                }
            }

            for (int row = 0; row < size; row++)
            {
                if (row == col)
                {
                    continue;
                }

                double factor = a[row, col] / a[col, col];
                for (int j = col; j <= size; j++)
                {
                    a[row, j] -= factor * a[col, j];
                }
            }
        }

        var x = new double[size];
        for (int i = 0; i < size; i++)
        {
            x[i] = a[i, size] / a[i, i];
        }

        return x;
    }

    // ------------------------------------------------------------------ Gauß

    /// <summary>
    /// Gauß-gewichtetes Fenster.
    /// </summary>
    /// <remarks>
    /// Die Standardabweichung wird so gewählt, dass die Rauschunterdrückung derselben
    /// Fensterbreite beim gleitenden Mittelwert entspricht: Für ein Rechteckfenster aus N
    /// Punkten ist die Summe der quadrierten Gewichte 1/N, für einen Gauß mit sigma Punkten
    /// 1/(2*sigma*Wurzel(pi)). Gleichsetzen ergibt sigma = N / (2*Wurzel(pi)). Dadurch sind die
    /// beiden Verfahren bei gleicher eingestellter Fensterbreite vergleichbar.
    /// </remarks>
    public static double[] Gaussian(IReadOnlyList<double> values, double sampleIntervalSeconds, double windowSeconds)
    {
        int n = values.Count;
        var result = new double[n];

        int half = HalfWidth(sampleIntervalSeconds, windowSeconds);
        double samples = (2 * half) + 1;
        double sigma = samples / (2.0 * Math.Sqrt(Math.PI));

        // Abschneiden bei drei Standardabweichungen; darüber hinaus trägt der Kern nichts bei.
        int reach = Math.Max(1, (int)Math.Ceiling(3.0 * sigma));
        var kernel = new double[(2 * reach) + 1];

        for (int k = -reach; k <= reach; k++)
        {
            kernel[k + reach] = Math.Exp(-(k * k) / (2.0 * sigma * sigma));
        }

        for (int i = 0; i < n; i++)
        {
            if (double.IsNaN(values[i]))
            {
                result[i] = double.NaN;
                continue;
            }

            double sum = 0;
            double weight = 0;

            for (int k = -reach; k <= reach; k++)
            {
                int j = i + k;
                if (j < 0 || j >= n || double.IsNaN(values[j]))
                {
                    continue;
                }

                double w = kernel[k + reach];
                sum += w * values[j];
                weight += w;
            }

            // Am Rand wird über die tatsächlich vorhandenen Gewichte normiert, sonst fiele die
            // Kurve dort zur Null hin ab.
            result[i] = weight > 0 ? sum / weight : double.NaN;
        }

        return result;
    }

    // ------------------------------------------------------------------ Median

    /// <summary>
    /// Gleitender Median über ein zentriertes Fenster.
    /// </summary>
    /// <remarks>
    /// Kein linearer Filter: Ein einzelner Ausreißer verschwindet vollständig, statt sich über
    /// das Fenster zu verteilen. Sprünge bleiben als Sprünge stehen.
    /// </remarks>
    public static double[] Median(IReadOnlyList<double> values, double sampleIntervalSeconds, double windowSeconds)
    {
        int n = values.Count;
        var result = new double[n];
        int half = HalfWidth(sampleIntervalSeconds, windowSeconds);

        var buffer = new List<double>((2 * half) + 1);

        for (int i = 0; i < n; i++)
        {
            if (double.IsNaN(values[i]))
            {
                result[i] = double.NaN;
                continue;
            }

            buffer.Clear();
            int lo = Math.Max(0, i - half);
            int hi = Math.Min(n - 1, i + half);

            for (int j = lo; j <= hi; j++)
            {
                if (!double.IsNaN(values[j]))
                {
                    buffer.Add(values[j]);
                }
            }

            if (buffer.Count == 0)
            {
                result[i] = double.NaN;
                continue;
            }

            buffer.Sort();
            int mid = buffer.Count / 2;

            result[i] = buffer.Count % 2 == 1
                ? buffer[mid]
                : (buffer[mid - 1] + buffer[mid]) / 2.0;
        }

        return result;
    }

    private static double MeanOfValid(IReadOnlyList<double> values, int from, int to)
    {
        double sum = 0;
        int count = 0;

        for (int i = from; i <= to; i++)
        {
            if (!double.IsNaN(values[i]))
            {
                sum += values[i];
                count++;
            }
        }

        return count > 0 ? sum / count : double.NaN;
    }
}
