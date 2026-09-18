using System.Globalization;
using Siemert.DataViewer.Core.Model;

namespace Siemert.DataViewer.App.Services;

/// <summary>
/// Einheitliche Beschriftung einer Aufnahme.
/// </summary>
/// <remarks>
/// Eine Aufnahme muss ohne Rückfrage zuzuordnen sein: welches Gerät, welche Seriennummer,
/// welcher Tag, von wann bis wann. Diese Angaben stehen deshalb überall gleich — in der Liste,
/// über dem Diagramm und in jedem Export. Sie an drei Stellen getrennt zusammenzubauen hat
/// dazu geführt, dass sie an zwei davon fehlten.
/// </remarks>
public static class RecordingLabel
{
    /// <summary>Gerät und Seriennummer, etwa <c>SI-TL1 Nr. 349</c>.</summary>
    public static string Device(DeviceInfo? device) =>
        device is null ? "Unbekanntes Gerät" : $"{device.Model} Nr. {device.SerialNumber}";

    /// <summary>Der Tag der Aufnahme, oder ein Hinweis, dass keiner vorliegt.</summary>
    public static string DateText(Recording recording) =>
        recording.ClockWasSet && recording.StartTime.HasValue
            ? recording.StartTime.Value.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture)
            : "ohne Datum";

    /// <summary>Von wann bis wann, etwa <c>10:48:26 – 10:51:55</c>.</summary>
    public static string TimeSpanText(Recording recording)
    {
        TimeSpan start = recording.StartTimeOfDay;

        // Der Abschlussdatensatz fehlt bei einer abgebrochenen Übertragung. Dann wird die
        // Endzeit aus der Dauer gebildet, statt 00:00:00 zu behaupten.
        TimeSpan end = recording.IsComplete && recording.EndTimeOfDay > TimeSpan.Zero
            ? recording.EndTimeOfDay
            : start + recording.Duration;

        return $"{Clock(start)} bis {Clock(end)}";
    }

    /// <summary>Erste Zeile: Gerät, Seriennummer und Tag.</summary>
    public static string Headline(Recording recording, DeviceInfo? device) =>
        $"{Device(device)} · {DateText(recording)}";

    /// <summary>Vollständige Beschriftung für den Diagrammtitel.</summary>
    public static string Full(Recording recording, DeviceInfo? device) =>
        $"{Device(device)} · {DateText(recording)} · {TimeSpanText(recording)}";

    private static string Clock(TimeSpan t)
    {
        while (t < TimeSpan.Zero)
        {
            t += TimeSpan.FromDays(1);
        }

        return (t - TimeSpan.FromDays(t.Days)).ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture);
    }
}
