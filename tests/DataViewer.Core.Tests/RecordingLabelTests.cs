using Siemert.DataViewer.App.Services;
using Siemert.DataViewer.Core.Model;

namespace DataViewer.Core.Tests;

/// <summary>
/// Tests der Aufnahmebeschriftung.
/// </summary>
/// <remarks>
/// Geraet, Seriennummer, Tag und Zeitspanne standen in der Vorgaengerversion ueberall dabei und
/// waren in der Ueberarbeitung auf "10:48:26" zusammengeschrumpft. Damit liess sich eine
/// weitergereichte Auswertung nicht mehr zuordnen.
/// </remarks>
public sealed class RecordingLabelTests
{
    private static DeviceInfo Device() => new()
    {
        Model = "SI-TL1",
        ModelCode = "TL1",
        SerialNumber = 349,
        Checksum = "B910",
        RawHeader = string.Empty
    };

    private static Recording Complete() => new()
    {
        ClockWasSet = true,
        StartTime = new DateTime(2026, 9, 18, 10, 48, 26),
        StartTimeOfDay = new TimeSpan(10, 48, 26),
        EndTimeOfDay = new TimeSpan(10, 51, 55),
        IsComplete = true,
        Samples = [new Sample(), new Sample()]
    };

    [Fact]
    public void Ueberschrift_nennt_Geraet_Seriennummer_und_Tag()
    {
        string headline = RecordingLabel.Headline(Complete(), Device());

        Assert.Contains("SI-TL1", headline, StringComparison.Ordinal);
        Assert.Contains("349", headline, StringComparison.Ordinal);
        Assert.Contains("18.09.2026", headline, StringComparison.Ordinal);
    }

    [Fact]
    public void Zeitspanne_nennt_Anfang_und_Ende()
    {
        Assert.Equal("10:48:26 bis 10:51:55", RecordingLabel.TimeSpanText(Complete()));
    }

    [Fact]
    public void Vollstaendige_Beschriftung_enthaelt_alles()
    {
        string full = RecordingLabel.Full(Complete(), Device());

        Assert.Contains("SI-TL1 Nr. 349", full, StringComparison.Ordinal);
        Assert.Contains("18.09.2026", full, StringComparison.Ordinal);
        Assert.Contains("10:48:26 bis 10:51:55", full, StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_gestellte_Uhr_wird_kein_Datum_erfunden()
    {
        var recording = new Recording
        {
            ClockWasSet = false,
            StartTimeOfDay = new TimeSpan(0, 40, 31),
            EndTimeOfDay = new TimeSpan(0, 44, 0),
            IsComplete = true,
            Samples = [new Sample()]
        };

        string full = RecordingLabel.Full(recording, Device());

        Assert.Contains("ohne Datum", full, StringComparison.Ordinal);
        Assert.Contains("00:40:31", full, StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_Abschlussdatensatz_wird_die_Endzeit_aus_der_Dauer_gebildet()
    {
        // Bei abgebrochener Uebertragung fehlt der Abschluss. Dann darf dort nicht 00:00:00
        // stehen - das waere eine falsche Angabe, keine fehlende.
        var recording = new Recording
        {
            ClockWasSet = true,
            StartTime = new DateTime(2026, 9, 18, 12, 0, 0),
            StartTimeOfDay = new TimeSpan(12, 0, 0),
            EndTimeOfDay = TimeSpan.Zero,
            IsComplete = false,
            Samples = [.. Enumerable.Range(0, 41).Select(_ => new Sample())]
        };

        string span = RecordingLabel.TimeSpanText(recording);

        Assert.StartsWith("12:00:00", span, StringComparison.Ordinal);
        Assert.DoesNotContain("00:00:00", span, StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_Geraet_wird_nichts_erfunden()
    {
        Assert.Contains("Unbekanntes Gerät", RecordingLabel.Headline(Complete(), null), StringComparison.Ordinal);
    }
}
