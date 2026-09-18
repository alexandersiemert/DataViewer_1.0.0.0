using Siemert.DataViewer.Core.Device;

namespace DataViewer.Core.Tests;

/// <summary>
/// Tests der Einzelbefehle jenseits des Auslesens.
/// </summary>
/// <remarks>
/// Die verwendeten Antworten stammen aus einem Mitschnitt am Geraet mit der Seriennummer 349.
/// </remarks>
public sealed class CommandTests
{
    // ------------------------------------------------------------------ L: Uhrzeit lesen

    [Fact]
    public void Uhrzeit_ohne_gestelltes_Datum_wird_erkannt()
    {
        // Echte Antwort eines Geraets, dessen Uhr nach Batteriewechsel nie gestellt wurde.
        LoggerClock? clock = LoggerCommands.ParseClock("L1F280000000000");

        Assert.NotNull(clock);
        Assert.False(clock!.IsSet);
        Assert.Null(clock.Date);
        Assert.Equal(new TimeSpan(0, 40, 31), clock.TimeOfDay);
    }

    [Fact]
    public void Uhrzeit_mit_Datum_wird_vollstaendig_zerlegt()
    {
        // Zeit hexadezimal (ss mm hh), Datum dezimal (TT MM JJJJ) - so mischt es das Geraet.
        // 0x2D = 45 s, 0x11 = 17 min, 0x0E = 14 h.
        LoggerClock? clock = LoggerCommands.ParseClock("L2D110E17092026");

        Assert.NotNull(clock);
        Assert.True(clock!.IsSet);
        Assert.Equal(new TimeSpan(14, 17, 45), clock.TimeOfDay);
        Assert.Equal(new DateTime(2026, 9, 17), clock.Date!.Value.Date);
    }

    [Fact]
    public void Unbrauchbare_Antwort_auf_L_liefert_keine_Uhrzeit()
    {
        Assert.Null(LoggerCommands.ParseClock(string.Empty));
        Assert.Null(LoggerCommands.ParseClock("L12"));
    }

    // ------------------------------------------------------------------ U: Uhr stellen

    [Fact]
    public void Stellbefehl_mischt_Zeit_hexadezimal_und_Datum_dezimal()
    {
        string command = LoggerCommands.BuildSetClock(new DateTime(2026, 9, 17, 14, 18, 0));

        // U + Minute hex (18 -> 12) + Stunde hex (14 -> 0E) + TT MM JJJJ dezimal.
        Assert.Equal("U120E17092026", command);
    }

    [Fact]
    public void Stellbefehl_fuellt_einstellige_Werte_auf()
    {
        // Ohne fuehrende Nullen verschoebe sich das gesamte Feldraster im Geraet.
        string command = LoggerCommands.BuildSetClock(new DateTime(2026, 1, 2, 3, 4, 0));

        Assert.Equal("U04030201" + "2026", command);
        Assert.Equal(13, command.Length);
    }

    [Fact]
    public void Stellbefehl_hat_immer_dieselbe_Laenge()
    {
        // Jede Minute eines vollen Tages, dazu jeder Monatserste - die Laenge darf nie springen.
        var start = new DateTime(2026, 3, 1, 0, 0, 0);
        for (int minute = 0; minute < 24 * 60; minute++)
        {
            Assert.Equal(13, LoggerCommands.BuildSetClock(start.AddMinutes(minute)).Length);
        }
    }

    [Fact]
    public void Gestellte_Uhr_laesst_sich_wieder_einlesen()
    {
        // Hin und zurueck: was gesendet wird, muss das Geraet genauso zurueckmelden koennen.
        var when = new DateTime(2026, 12, 31, 23, 59, 0);

        string command = LoggerCommands.BuildSetClock(when);
        string echo = "L00" + command[1..];   // Geraet meldet dieselben Felder, Sekunden auf null

        LoggerClock? clock = LoggerCommands.ParseClock(echo);

        Assert.NotNull(clock);
        Assert.True(clock!.IsSet);
        Assert.Equal(new TimeSpan(23, 59, 0), clock.TimeOfDay);
        Assert.Equal(when.Date, clock.Date!.Value.Date);
    }

    // ------------------------------------------------------------------ D: Temperatur und Druck

    [Fact]
    public void Momentanwerte_von_Temperatur_und_Druck_werden_skaliert()
    {
        // Echte Antwort: 25,5 Grad Celsius bei 1003,0 hPa.
        (double TemperatureC, double PressureHpa)? env = LoggerCommands.ParseEnvironment("D00755 10030");

        Assert.NotNull(env);
        Assert.Equal(25.5, env!.Value.TemperatureC, 1);
        Assert.Equal(1003.0, env.Value.PressureHpa, 1);
    }

    [Fact]
    public void Unbrauchbare_Antwort_auf_D_liefert_keine_Werte()
    {
        Assert.Null(LoggerCommands.ParseEnvironment("D00755"));
        Assert.Null(LoggerCommands.ParseEnvironment("Dxxxxx yyyyy"));
    }

    // ------------------------------------------------------------------ M: Beschleunigung

    [Fact]
    public void Ruhendes_Geraet_meldet_eine_Erdbeschleunigung()
    {
        // Echte Antwort eines flach liegenden Geraets. Der Betrag muss 1 g ergeben - genau daran
        // scheitert die handschriftlich notierte Formel "Wert / 2048", die nur 0,64 g liefert.
        (double X, double Y, double Z)? acc = LoggerCommands.ParseAcceleration("M00400500FF00");

        Assert.NotNull(acc);

        double magnitude = Math.Sqrt((acc!.Value.X * acc.Value.X) +
                                     (acc.Value.Y * acc.Value.Y) +
                                     (acc.Value.Z * acc.Value.Z));

        Assert.InRange(magnitude, 0.85, 1.15);
    }

    [Fact]
    public void Unbrauchbare_Antwort_auf_M_liefert_keine_Werte()
    {
        Assert.Null(LoggerCommands.ParseAcceleration("M0040"));
    }

    // ------------------------------------------------------------------ C und R: Kenndaten

    [Fact]
    public void Geraetekennung_und_Speicherumlaeufe_werden_gelesen()
    {
        Assert.Equal(47376, LoggerCommands.ParseNumber("C47376", LoggerCommands.ReadChecksum));
        Assert.Equal(0, LoggerCommands.ParseNumber("R00000", LoggerCommands.ReadMemoryWraps));
    }

    [Fact]
    public void Antwort_ohne_Zahl_liefert_keinen_Wert()
    {
        Assert.Null(LoggerCommands.ParseNumber("C", LoggerCommands.ReadChecksum));
        Assert.Null(LoggerCommands.ParseNumber("Cabc", LoggerCommands.ReadChecksum));
    }

    // ------------------------------------------------------------------ Warten auf den Minutenwechsel

    [Fact]
    public async Task Warten_endet_auf_einer_vollen_Minute()
    {
        // Der Hersteller schreibt das eigens vor: das Geraet setzt die Sekunden beim Stellen auf
        // null. Der Zielzeitpunkt muss deshalb sekundengenau auf einer Minute liegen.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var seen = new List<TimeSpan>();
        var countdown = new Progress<TimeSpan>(seen.Add);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await LoggerCommands.WaitForFullMinuteAsync(countdown, cts.Token));

        // Die Restzeit muss gemeldet werden, damit die Oberflaeche einen Countdown zeigen kann.
        Assert.NotEmpty(seen);
        Assert.All(seen, left => Assert.InRange(left.TotalSeconds, 0, 60));
    }
}
