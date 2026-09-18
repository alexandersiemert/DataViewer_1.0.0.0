namespace Siemert.DataViewer.Core.Analysis;

/// <summary>
/// Glättender Spline nach Whittaker und Henderson.
/// </summary>
/// <remarks>
/// <para>
/// Anders als alle Fensterverfahren arbeitet dieses nicht lokal, sondern legt <b>eine</b> Kurve
/// durch die gesamte Messreihe. Gesucht wird die Folge z, die
/// </para>
/// <para>
/// <c>Summe (y_i - z_i)^2 + lambda * Summe (z_{i-1} - 2 z_i + z_{i+1})^2</c>
/// </para>
/// <para>
/// kleinstmöglich macht. Der erste Teil zieht die Kurve zu den Messwerten, der zweite bestraft
/// Krümmung. Das ist die diskrete Fassung des kubischen glättenden Splines; für lambda gegen
/// null entsteht die Messreihe selbst, für lambda gegen unendlich eine Gerade.
/// </para>
/// <para>
/// Das Verfahren ist <b>exakt für Geraden</b>: Bestraft werden zweite Differenzen, und eine
/// Gerade hat keine. Eine gleichmäßige Sinkrate geht deshalb unverändert durch, gleich wie
/// stark geglättet wird. Bei gleicher Fensterangabe glättet es in der Mitte der Reihe
/// erheblich stärker als ein Rechteckfenster; nachgemessen an weissem Rauschen bleibt dort
/// etwa die halbe Streuung.
/// </para>
/// <para>
/// Was es <b>nicht</b> leistet: Auch hier ist der Rand schwächer geglättet als die Mitte. Der
/// Grund ist ein anderer als bei den Fensterverfahren — der erste und der letzte Punkt kommen
/// in nur einer einzigen zweiten Differenz vor und sind deshalb weniger gebunden. Im
/// Verhältnis zur eigenen Mitte ist der Rand hier sogar am unruhigsten; absolut ist er so
/// ruhig wie beim gleitenden Mittelwert gleicher Breite.
/// </para>
/// <para>
/// Die Normalgleichung <c>(W + lambda * D'D) z = W y</c> ist symmetrisch, positiv definit und
/// hat Bandbreite 2. Sie wird mit einer Bandcholeskyzerlegung in linearer Zeit gelöst; für
/// 5000 Messpunkte sind das wenige Millisekunden.
/// </para>
/// </remarks>
public static class SmoothingSpline
{
    /// <summary>
    /// Glättet eine Messreihe.
    /// </summary>
    /// <param name="values">Messwerte. <c>NaN</c> ist zulässig und wird als fehlend behandelt.</param>
    /// <param name="lambda">Gewicht der Krümmungsstrafe. Größer heißt glatter.</param>
    public static double[] Apply(IReadOnlyList<double> values, double lambda)
    {
        ArgumentNullException.ThrowIfNull(values);

        int n = values.Count;

        // Unter vier Punkten gibt es keine zweite Differenz, die sich bestrafen liesse.
        if (n < 4 || lambda <= 0)
        {
            return [.. values];
        }

        // Fehlende Werte bekommen Gewicht null: Der Spline ueberbrueckt sie, ohne dass sie das
        // Ergebnis verfaelschen.
        var weight = new double[n];
        var target = new double[n];
        int known = 0;

        for (int i = 0; i < n; i++)
        {
            bool valid = !double.IsNaN(values[i]) && !double.IsInfinity(values[i]);
            weight[i] = valid ? 1.0 : 0.0;
            target[i] = valid ? values[i] : 0.0;
            known += valid ? 1 : 0;
        }

        if (known < 4)
        {
            return [.. values];
        }

        // Bandmatrix A = W + lambda * D'D, D bildet zweite Differenzen.
        var d0 = new double[n];
        var d1 = new double[n];
        var d2 = new double[n];

        for (int i = 0; i < n; i++)
        {
            d0[i] = weight[i];
        }

        // Jede Zeile von D hat die Eintraege 1, -2, 1 an den Stellen k, k+1, k+2.
        double[] row = [1.0, -2.0, 1.0];

        for (int k = 0; k + 2 < n; k++)
        {
            for (int a = 0; a < 3; a++)
            {
                for (int b = a; b < 3; b++)
                {
                    double value = lambda * row[a] * row[b];

                    switch (b - a)
                    {
                        case 0:
                            d0[k + a] += value;
                            break;
                        case 1:
                            d1[k + a] += value;
                            break;
                        default:
                            d2[k + a] += value;
                            break;
                    }
                }
            }
        }

        double[]? solved = SolveBanded(d0, d1, d2, target, n);

        if (solved is null)
        {
            return [.. values];
        }

        // Wo nichts gemessen wurde, steht auch nichts - dieselbe Regel wie bei den anderen
        // Verfahren. Der Spline koennte dort einen Wert liefern, aber er waere erfunden.
        for (int i = 0; i < n; i++)
        {
            if (weight[i] == 0.0)
            {
                solved[i] = double.NaN;
            }
        }

        return solved;
    }

    /// <summary>
    /// Löst ein symmetrisches, positiv definites Bandsystem der Bandbreite 2.
    /// </summary>
    /// <remarks>
    /// Cholesky in Bandform: A = L * L'. Gespeichert werden nur Hauptdiagonale und die beiden
    /// Nebendiagonalen; die Zerlegung bleibt in derselben Bandbreite. Gibt <c>null</c> zurück,
    /// wenn das System nicht positiv definit ist - dann wird nicht geraten, sondern die
    /// ungeglättete Reihe zurückgegeben.
    /// </remarks>
    private static double[]? SolveBanded(double[] d0, double[] d1, double[] d2, double[] rhs, int n)
    {
        var l0 = new double[n];
        var l1 = new double[n];
        var l2 = new double[n];

        for (int i = 0; i < n; i++)
        {
            double sum = d0[i];

            if (i >= 1)
            {
                sum -= l1[i - 1] * l1[i - 1];
            }

            if (i >= 2)
            {
                sum -= l2[i - 2] * l2[i - 2];
            }

            if (sum <= 0)
            {
                return null;
            }

            l0[i] = Math.Sqrt(sum);

            if (i + 1 < n)
            {
                double off = d1[i];
                if (i >= 1)
                {
                    off -= l2[i - 1] * l1[i - 1];
                }

                l1[i] = off / l0[i];
            }

            if (i + 2 < n)
            {
                l2[i] = d2[i] / l0[i];
            }
        }

        // Vorwaertseinsetzen L y = rhs
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = rhs[i];

            if (i >= 1)
            {
                sum -= l1[i - 1] * y[i - 1];
            }

            if (i >= 2)
            {
                sum -= l2[i - 2] * y[i - 2];
            }

            y[i] = sum / l0[i];
        }

        // Rueckwaertseinsetzen L' z = y
        var z = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = y[i];

            if (i + 1 < n)
            {
                sum -= l1[i] * z[i + 1];
            }

            if (i + 2 < n)
            {
                sum -= l2[i] * z[i + 2];
            }

            z[i] = sum / l0[i];
        }

        return z;
    }

    /// <summary>
    /// Rechnet eine Fensterbreite in das Krümmungsgewicht um.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Damit die eingestellte Fensterbreite bei allen Verfahren ungefähr dasselbe bewirkt, wird
    /// lambda so gewählt, dass die Grenzfrequenz der des gleitenden Mittelwerts gleicher Breite
    /// entspricht.
    /// </para>
    /// <para>
    /// Der Frequenzgang dieses Verfahrens ist <c>H(w) = 1 / (1 + 16 * lambda * sin^4(w/2))</c>.
    /// Halbe Leistung liegt bei <c>sin(w/2) = (1 / (16 lambda))^(1/4)</c>. Ein Rechteckfenster
    /// aus N Punkten hat seinen entsprechenden Punkt bei etwa <c>w = 2,78 / N</c>. Gleichsetzen
    /// und nach lambda auflösen ergibt <c>lambda = N^4 / 59,7</c>.
    /// </para>
    /// </remarks>
    public static double LambdaForWindow(double sampleIntervalSeconds, double windowSeconds)
    {
        if (sampleIntervalSeconds <= 0 || windowSeconds <= 0)
        {
            return 0;
        }

        double samples = Math.Max(2.0, windowSeconds / sampleIntervalSeconds);
        return samples * samples * samples * samples / 59.7;
    }
}
