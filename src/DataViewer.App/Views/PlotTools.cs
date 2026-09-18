using Siemert.DataViewer.Core.Model;

namespace Siemert.DataViewer.App.Views;

/// <summary>Welches Werkzeug gerade aktiv ist.</summary>
public enum PlotTool
{
    None,
    Measure,
    Crosshair,
    Marker
}

/// <summary>Kennzeichnet eine Kurve im Diagramm.</summary>
public enum SeriesKind
{
    Altitude,
    Speed,
    Temperature,
    AccMagnitude,
    AccX,
    AccY,
    AccZ
}

/// <summary>Eine Kurve mit allem, was zum Zeichnen und Ablesen gebraucht wird.</summary>
public sealed record SeriesData
{
    public required SeriesKind Kind { get; init; }

    public required string Label { get; init; }

    public required string Unit { get; init; }

    /// <summary>Anzuzeigende Werte, bereits in der gewählten Einheit und ggf. geglättet.</summary>
    public required double[] Values { get; init; }

    /// <summary>Ungeglättete Werte in derselben Einheit. Statistik wird immer hierauf gerechnet.</summary>
    public required double[] RawValues { get; init; }

    public required bool Visible { get; init; }

    /// <summary>Nachkommastellen für die Anzeige.</summary>
    public required int Digits { get; init; }
}

/// <summary>Ablesewerte des Fadenkreuzes an einer Zeitposition.</summary>
public sealed class CrosshairReadout
{
    public required double TimeSeconds { get; init; }

    public required IReadOnlyList<(SeriesKind Kind, string Label, string Value, string Unit)> Values { get; init; }
}

/// <summary>Statistik einer Kurve über den gewählten Zeitbereich.</summary>
public sealed record SeriesStatistics
{
    public required SeriesKind Kind { get; init; }

    public required string Label { get; init; }

    public required string Unit { get; init; }

    public required double At1 { get; init; }

    public required double At2 { get; init; }

    public required double Min { get; init; }

    public required double Max { get; init; }

    public required double Delta { get; init; }

    public required double Average { get; init; }

    public required int Digits { get; init; }

    /// <summary>
    /// False, wenn die Kurve im Diagramm ausgeblendet ist. Die Zahlen werden trotzdem berechnet:
    /// Wer einen Bereich misst, will alle Messgroessen sehen, auch die gerade nicht gezeichneten.
    /// </summary>
    public required bool Visible { get; init; }

    /// <summary>Nur bei der Hoehe belegt: mittlere Aenderung je Sekunde ueber den Bereich.</summary>
    public double RatePerSecond { get; init; } = double.NaN;
}

/// <summary>
/// Ergebnis des Messcursors.
/// </summary>
/// <remarks>
/// Gegenüber Fassung 1 zwei Unterschiede. Erstens enthält das Ergebnis die <b>Dauer</b> des
/// gewählten Bereichs; sie wurde dort intern für die Geschwindigkeit berechnet, aber nie
/// angezeigt, obwohl die Freifallzeit die zentrale Zahl eines Sprungs ist. Zweitens wird die
/// Statistik immer auf den <b>ungeglätteten</b> Werten gebildet und das auch dazugeschrieben —
/// sonst widersprechen sich Kurve und Zahlenwerte, ohne dass der Anwender erkennen kann, warum.
/// </remarks>
public sealed class MeasureResult
{
    public required double Time1 { get; init; }

    public required double Time2 { get; init; }

    public double Duration => Math.Abs(Time2 - Time1);

    public required int SampleCount { get; init; }

    public required IReadOnlyList<SeriesStatistics> Series { get; init; }

    /// <summary>Mittlere Vertikalgeschwindigkeit über den Bereich, in m/s.</summary>
    public required double MeanVerticalSpeedMs { get; init; }

    /// <summary>True, wenn mindestens eine angezeigte Kurve geglättet dargestellt wird.</summary>
    public required bool SmoothingActive { get; init; }
}

/// <summary>Hilfsfunktionen für die Statistik über einen Indexbereich.</summary>
public static class RangeStatistics
{
    public static SeriesStatistics Compute(SeriesData series, int i1, int i2)
    {
        ArgumentNullException.ThrowIfNull(series);

        double[] v = series.RawValues;
        int lo = Math.Max(0, Math.Min(i1, i2));
        int hi = Math.Min(v.Length - 1, Math.Max(i1, i2));

        double min = double.NaN;
        double max = double.NaN;
        double sum = 0;
        int count = 0;

        for (int i = lo; i <= hi; i++)
        {
            double x = v[i];
            if (double.IsNaN(x) || double.IsInfinity(x))
            {
                continue;
            }

            if (double.IsNaN(min) || x < min)
            {
                min = x;
            }

            if (double.IsNaN(max) || x > max)
            {
                max = x;
            }

            sum += x;
            count++;
        }

        double at1 = Value(v, i1);
        double at2 = Value(v, i2);

        return new SeriesStatistics
        {
            Kind = series.Kind,
            Label = series.Label,
            Unit = series.Unit,
            At1 = at1,
            At2 = at2,
            Min = min,
            Max = max,
            Delta = at2 - at1,
            Average = count > 0 ? sum / count : double.NaN,
            Digits = series.Digits,
            Visible = series.Visible
        };
    }

    private static double Value(double[] v, int i) =>
        v.Length == 0 ? double.NaN : v[Math.Clamp(i, 0, v.Length - 1)];

    /// <summary>Index des Messpunkts, der einer Zeit am nächsten liegt.</summary>
    public static int IndexOfTime(IReadOnlyList<Sample> samples, double seconds)
    {
        if (samples.Count == 0)
        {
            return 0;
        }

        double step = samples.Count > 1 ? samples[1].TimeSeconds - samples[0].TimeSeconds : 1;
        if (step <= 0)
        {
            step = 1;
        }

        int index = (int)Math.Round((seconds - samples[0].TimeSeconds) / step);
        return Math.Clamp(index, 0, samples.Count - 1);
    }
}
