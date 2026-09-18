using System.Globalization;
using System.Text;
using Siemert.DataViewer.Core.Analysis;
using Siemert.DataViewer.Core.Model;
using Siemert.DataViewer.Core.Protocol;
using Siemert.DataViewer.Core.Units;

namespace Siemert.DataViewer.Core.Io;

public enum CsvFlavor
{
    /// <summary>
    /// Punkt als Dezimaltrennzeichen, Komma als Feldtrennzeichen. Auf jedem Rechner identisch
    /// lesbar und von Auswerteprogrammen direkt verarbeitbar.
    /// </summary>
    International,

    /// <summary>
    /// Semikolon als Feldtrennzeichen, Komma als Dezimaltrennzeichen, mit <c>sep=;</c> in der
    /// ersten Zeile. Fuer den Doppelklick in einem deutschsprachigen Excel.
    /// </summary>
    GermanExcel
}

/// <summary>
/// CSV-Ausgabe einer Aufnahme.
/// </summary>
/// <remarks>
/// Die Vorgaengerversion hat mit <c>CultureInfo.CurrentCulture</c> geschrieben. Dadurch hing es
/// vom Rechner ab, ob Werte "988.30" oder "988,30" und Felder mit Komma oder Semikolon getrennt
/// waren - und in der Datei stand nirgends, welche Variante vorliegt. Zwischen zwei Standorten
/// ausgetauscht war so keine Datei zuverlaessig lesbar. Deshalb wird das Format hier ausdruecklich
/// gewaehlt und im Kopf dokumentiert.
/// </remarks>
public static class CsvExporter
{
    public static string Build(
        Recording recording,
        DeviceInfo? device,
        AltitudeReference reference,
        UnitSystem units,
        CsvFlavor flavor,
        string appVersion)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(reference);

        CultureInfo culture = flavor == CsvFlavor.International
            ? CultureInfo.InvariantCulture
            : CultureInfo.GetCultureInfo("de-DE");

        string sep = flavor == CsvFlavor.International ? "," : ";";
        var sb = new StringBuilder();

        if (flavor == CsvFlavor.GermanExcel)
        {
            sb.Append("sep=").Append(sep).Append('\n');
        }

        string altUnit = UnitConverter.AltitudeUnitLabel(units);
        string tempUnit = units == UnitSystem.Metric ? "C" : "F";

        // Kopfzeilen als Kommentar. Sie dokumentieren den Messbezug, ohne den eine Hoehenangabe
        // nicht interpretierbar ist.
        AppendComment(sb, "SIEMERT DataViewer " + appVersion);
        AppendComment(sb, "Geraet: " + (device?.DisplayName ?? "unbekannt"));
        AppendComment(sb, "Aufnahme: " + recording.DisplayName);
        AppendComment(sb, "Messpunkte: " + recording.Samples.Count.ToString(CultureInfo.InvariantCulture) +
                          " bei " + recording.SampleIntervalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s Abtastintervall");
        AppendComment(sb, "Hoehenbezug: " + reference.Describe());
        AppendComment(sb, "Hoeheneinheit: " + altUnit + ", Temperatur: " + tempUnit + ", Beschleunigung: g");
        AppendComment(sb, "Zeitbasis: Sekunden seit Aufnahmebeginn" +
                          (recording.ClockWasSet ? "; Spalte Zeitstempel ist Loggerzeit" : "; die Loggeruhr war NICHT gestellt"));
        if (recording.HasSaturatedSamples)
        {
            AppendComment(sb, "WARNUNG: Der Beschleunigungssensor war zeitweise am Anschlag (+/-16 g). " +
                              "Betroffene Zeilen sind in der Spalte Saettigung mit 1 gekennzeichnet.");
        }

        if (!recording.IsComplete)
        {
            AppendComment(sb, "WARNUNG: Diese Aufnahme ist unvollstaendig uebertragen worden.");
        }

        string[] header =
        [
            "Zeit_s",
            recording.ClockWasSet ? "Zeitstempel" : "Uhrzeit_Logger",
            "Druck_hPa",
            "Hoehe_" + altUnit,
            "Vertikalgeschwindigkeit_m_s",
            "Temperatur_" + tempUnit,
            "AccX_g",
            "AccY_g",
            "AccZ_g",
            "AccBetrag_g",
            "Saettigung"
        ];

        sb.Append(string.Join(sep, header)).Append('\n');

        var altitudes = new double[recording.Samples.Count];
        for (int i = 0; i < recording.Samples.Count; i++)
        {
            altitudes[i] = reference.ToAltitudeMeters(recording.Samples[i].PressureHpa, recording.Samples[i].TemperatureC);
        }

        double[] vSpeed = Signal.Derivative(altitudes, recording.SampleIntervalSeconds, JumpAnalyzer.DerivativeWindow);

        for (int i = 0; i < recording.Samples.Count; i++)
        {
            Sample s = recording.Samples[i];

            string stamp = recording.ClockWasSet && recording.StartTime.HasValue
                ? recording.StartTime.Value.AddSeconds(s.TimeSeconds).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                : (recording.StartTimeOfDay + TimeSpan.FromSeconds(s.TimeSeconds)).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

            sb.Append(s.TimeSeconds.ToString("F2", culture)).Append(sep);
            sb.Append(stamp).Append(sep);
            sb.Append(s.PressureHpa.ToString("F1", culture)).Append(sep);
            sb.Append(UnitConverter.Altitude(altitudes[i], units).ToString("F1", culture)).Append(sep);
            sb.Append(Fmt(vSpeed[i], culture)).Append(sep);
            sb.Append(UnitConverter.Temperature(s.TemperatureC, units).ToString("F1", culture)).Append(sep);
            sb.Append(s.AccX.ToString("F3", culture)).Append(sep);
            sb.Append(s.AccY.ToString("F3", culture)).Append(sep);
            sb.Append(s.AccZ.ToString("F3", culture)).Append(sep);
            sb.Append(s.AccMagnitude.ToString("F3", culture)).Append(sep);
            sb.Append(s.AccSaturated ? '1' : '0').Append('\n');
        }

        return sb.ToString();
    }

    public static void Save(
        string path,
        Recording recording,
        DeviceInfo? device,
        AltitudeReference reference,
        UnitSystem units,
        CsvFlavor flavor,
        string appVersion)
    {
        string content = Build(recording, device, reference, units, flavor, appVersion);

        // BOM, damit Excel die Umlaute in den Kopfzeilen richtig anzeigt.
        File.WriteAllText(path, content, new UTF8Encoding(flavor == CsvFlavor.GermanExcel));
    }

    private static void AppendComment(StringBuilder sb, string text) =>
        sb.Append("# ").Append(text).Append('\n');

    private static string Fmt(double v, CultureInfo c) =>
        double.IsNaN(v) || double.IsInfinity(v) ? string.Empty : v.ToString("F2", c);
}
