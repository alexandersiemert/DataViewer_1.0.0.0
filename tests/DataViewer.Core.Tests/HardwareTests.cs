using Siemert.DataViewer.Core.Analysis;
using Siemert.DataViewer.Core.Device;
using Siemert.DataViewer.Core.Io;
using Siemert.DataViewer.Core.Model;
using Siemert.DataViewer.Core.Protocol;
using Xunit.Abstractions;

namespace DataViewer.Core.Tests;

/// <summary>
/// Tests gegen einen tatsaechlich angeschlossenen Logger.
/// </summary>
/// <remarks>
/// Diese Tests brauchen Hardware und laufen deshalb nicht im normalen Durchlauf mit. Ausfuehren mit
/// <c>dotnet test --filter Category=Hardware</c>.
/// </remarks>
[Trait("Category", "Hardware")]
public sealed class HardwareTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _out = output;

    /// <summary>
    /// Wartet, bis ein Logger antwortet.
    /// </summary>
    /// <remarks>
    /// Das Warten ist nötig, weil das Gerät einen einmal begonnenen Speicherauszug zu Ende sendet.
    /// Läuft unmittelbar zuvor ein Test, der das Auslesen abbricht, ist der Logger noch einige
    /// Sekunden mit dem Rest beschäftigt und beantwortet in dieser Zeit keinen Ping.
    /// </remarks>
    private static async Task<string> RequireLoggerPortAsync(int timeoutSeconds = 60)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (true)
        {
            IReadOnlyList<SerialPortCandidate> found = await LoggerPortScanner.FindLoggersAsync();
            if (found.Count > 0)
            {
                return found[0].PortName;
            }

            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail(
                    "Es hat innerhalb von " + timeoutSeconds + " s kein Logger geantwortet. " +
                    "Diese Tests brauchen ein Gerät an einer seriellen Schnittstelle.");
            }

            await Task.Delay(1000);
        }
    }

    [Fact]
    public void Anschluesse_werden_mit_USB_Kennung_aufgelistet()
    {
        IReadOnlyList<SerialPortCandidate> ports = LoggerPortScanner.ListPorts();
        Assert.NotEmpty(ports);

        foreach (SerialPortCandidate p in ports)
        {
            _out.WriteLine($"{p.PortName,-8} verdächtig={p.IsLikelyLogger,-5} {p.Description}  [{p.HardwareId}]");
        }

        // Mindestens ein Anschluss muss über die USB-Kennung als möglicher Logger erkannt werden,
        // sonst greift die gezielte Suche nicht und es bliebe nur das Ansprechen aller Ports.
        Assert.Contains(ports, p => p.IsLikelyLogger);
    }

    [Fact]
    public async Task Gezielte_Suche_findet_den_Logger_ohne_fremde_Ports_anzusprechen()
    {
        IReadOnlyList<SerialPortCandidate> found = await LoggerPortScanner.FindLoggersAsync(probeAllPorts: false);
        foreach (SerialPortCandidate p in found)
        {
            _out.WriteLine("gefunden: " + p);
        }

        Assert.NotEmpty(found);
    }

    [Fact]
    public async Task Belegter_Anschluss_wird_bei_der_Suche_nicht_angefasst()
    {
        // Das ist der Fehler, der produktiv einen laufenden Auslesevorgang zerstört hat: ein
        // beliebiges USB-Ereignis löste eine Gerätesuche aus, die den gerade streamenden Port
        // öffnen wollte, scheiterte und ihn daraufhin als "entfernt" behandelte.
        string port = await RequireLoggerPortAsync();

        using var connection = new LoggerConnection(port);
        connection.Open();

        IReadOnlyList<SerialPortCandidate> found = await LoggerPortScanner.FindLoggersAsync(
            probeAllPorts: false,
            portsInUse: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { port });

        Assert.Contains(found, p => string.Equals(p.PortName, port, StringComparison.OrdinalIgnoreCase));
        Assert.True(connection.IsOpen, "Die Verbindung muss die Gerätesuche unbeschadet überstehen.");
    }

    [Fact]
    public async Task Kopfdaten_werden_gelesen()
    {
        string port = await RequireLoggerPortAsync();

        using var connection = new LoggerConnection(port);
        connection.Open();
        DeviceInfo info = await connection.ReadInfoAsync();

        _out.WriteLine($"Modell       : {info.Model} (Kennung {info.ModelCode})");
        _out.WriteLine($"Seriennummer : {info.SerialNumber}");
        _out.WriteLine($"Baudatum     : {info.ProductionDate}");
        _out.WriteLine($"Prüfsumme    : {info.Checksum}");
        _out.WriteLine($"Hersteller   : {HeaderParser.ReadManufacturer(info.RawHeader)}");

        Assert.Equal("SI-TL1", info.Model);
        Assert.True(info.SerialNumber > 0);
        Assert.NotNull(info.ProductionDate);
        Assert.Equal("SIEMERT", HeaderParser.ReadManufacturer(info.RawHeader));
    }

    [Fact]
    public async Task Vollstaendiges_Auslesen_liefert_mehr_Daten_als_das_Geraet_ankuendigt()
    {
        // Kern des Befunds: die angekündigte Adressspanne ist eine Untergrenze. Wer die
        // Übertragung beim Erreichen dieser Menge beendet, schneidet den Strom ab.
        string port = await RequireLoggerPortAsync();

        using var connection = new LoggerConnection(port);
        connection.Open();

        var seen = new List<ReadProgress>();
        var progress = new Progress<ReadProgress>(seen.Add);

        ReadoutResult result = await connection.ReadMemoryAsync(LoggerProtocol.CommandReadAll, progress);

        Assert.NotNull(result.StartAddress);
        Assert.NotNull(result.EndAddress);

        long announced = (result.EndAddress!.Value - result.StartAddress!.Value + 1L) * 128L * 2L;
        long actual = result.RawResponse.Count(char.IsAsciiHexDigit);

        _out.WriteLine($"Dauer            : {result.Duration.TotalSeconds:F1} s");
        _out.WriteLine($"Adressen         : 0x{result.StartAddress:X4} bis 0x{result.EndAddress:X4}");
        _out.WriteLine($"angekündigt      : {announced} Hexzeichen");
        _out.WriteLine($"tatsächlich      : {actual} Hexzeichen");
        _out.WriteLine($"Überschuss       : {actual - announced} Zeichen");
        _out.WriteLine($"Fortschrittsmeld.: {seen.Count}");

        Assert.True(actual >= announced,
            "Das Gerät sendet nie weniger als angekündigt; andernfalls stimmt die Annahme nicht mehr.");
    }

    [Fact]
    public async Task Auslesen_und_Dekodieren_ergibt_plausible_Aufnahmen()
    {
        string port = await RequireLoggerPortAsync();

        using var connection = new LoggerConnection(port);
        connection.Open();

        ReadoutResult result = await connection.ReadMemoryAsync(LoggerProtocol.CommandReadAll);

        // Der Gerätekopf steckt auch in der G-Antwort und wird jetzt ausgewertet statt verworfen.
        Assert.NotNull(result.DeviceInfo);
        _out.WriteLine($"Gerät aus der G-Antwort: {result.DeviceInfo!.DisplayName}");

        DecodeResult decoded = PayloadDecoder.Decode(result.Payload);

        _out.WriteLine($"Aufnahmen: {decoded.Recordings.Count}, Ereignisse: {decoded.Events.Count}");
        foreach (Recording r in decoded.Recordings)
        {
            _out.WriteLine($"  {r.DisplayName,-34} {r.Samples.Count,6} Punkte  " +
                           $"{r.Duration.TotalSeconds,7:F1} s  " +
                           $"Start {r.StartPressureHpa:F1} hPa / {r.StartTemperatureC:F1} °C  " +
                           $"Ende {r.EndPressureHpa:F1} hPa / {r.EndTemperatureC:F1} °C  " +
                           $"vollständig={r.IsComplete}");
        }

        foreach (DecodeMessage m in decoded.Messages)
        {
            _out.WriteLine("  Meldung: " + m);
        }

        Assert.NotEmpty(decoded.Recordings);
        Assert.All(decoded.Recordings, r => Assert.True(r.IsComplete,
            "Nach einem vollständigen Auslesevorgang muss jede Aufnahme ihren Abschlussdatensatz haben."));

        // Die Endwerte müssen skaliert sein - genau hier lag der Fehler der Vorgängerversion.
        Assert.All(decoded.Recordings, r =>
        {
            Assert.InRange(r.EndPressureHpa, 250, 1100);
            Assert.InRange(r.EndTemperatureC, -60, 90);
        });
    }

    [Fact]
    public async Task Einzelbefehle_werden_alle_beantwortet()
    {
        string port = await RequireLoggerPortAsync();

        using var connection = new LoggerConnection(port);
        connection.Open();

        LoggerClock? clock = await connection.ReadClockAsync();
        LoggerLiveReading? live = await connection.ReadLiveAsync();
        LoggerDeviceFacts facts = await connection.ReadFactsAsync();

        _out.WriteLine($"Uhr          : {clock?.Describe() ?? "keine Antwort"}");
        _out.WriteLine($"Momentanwert : {live?.TemperatureC:F1} °C / {live?.PressureHpa:F1} hPa");
        _out.WriteLine($"Beschleunig. : X={live?.AccX:F2} Y={live?.AccY:F2} Z={live?.AccZ:F2}  " +
                       $"Betrag={live?.AccMagnitude:F2} g");
        _out.WriteLine($"Kennung      : {facts.Checksum}");
        _out.WriteLine($"Speicheruml. : {facts.MemoryWraps}");

        Assert.NotNull(clock);
        Assert.NotNull(live);

        // Ein Logger am Schreibtisch: plausible Raumwerte und eine Erdbeschleunigung.
        Assert.InRange(live!.TemperatureC, -20, 60);
        Assert.InRange(live.PressureHpa, 850, 1100);
        Assert.InRange(live.AccMagnitude, 0.7, 1.3);

        // Die Kennung ist geraetefest und darf nie null sein.
        Assert.True(facts.Checksum > 0);
    }

    [Fact]
    public async Task Uhr_laesst_sich_stellen_und_zuruecklesen()
    {
        // Schreibt die Uhr des Geraets. Unkritisch: der Wert ist die richtige Ortszeit, und der
        // Test prueft genau die eine Sache, die kein Schreibtischtest zeigen kann - ob das Geraet
        // den selbst gebauten Stellbefehl annimmt.
        string port = await RequireLoggerPortAsync();

        using var connection = new LoggerConnection(port);
        connection.Open();

        LoggerClock? before = await connection.ReadClockAsync();
        _out.WriteLine($"vorher : {before?.Describe() ?? "keine Antwort"}");

        var steps = new List<TimeSpan>();
        DateTime target = await connection.SetClockAsync(new Progress<TimeSpan>(steps.Add));

        LoggerClock? after = await connection.ReadClockAsync();
        _out.WriteLine($"gestellt auf: {target:dd.MM.yyyy HH:mm:ss}");
        _out.WriteLine($"nachher: {after?.Describe() ?? "keine Antwort"}");
        _out.WriteLine($"Countdown-Meldungen: {steps.Count}");

        Assert.NotNull(after);
        Assert.True(after!.IsSet, "Nach dem Stellen muss das Geraet ein Datum fuehren.");
        Assert.Equal(target.Date, after.Date!.Value.Date);

        // Der Befehl geht auf dem Minutenwechsel hinaus, das Zuruecklesen dauert danach einen
        // Moment. Mehr als eine Minute Abstand waere ein Fehler im Feldaufbau des Befehls.
        TimeSpan drift = (after.Date.Value.Date + after.TimeOfDay) - target;
        _out.WriteLine($"Abweichung: {drift.TotalSeconds:F1} s");
        Assert.InRange(Math.Abs(drift.TotalSeconds), 0, 59);
    }

    [Fact]
    public async Task Einzelne_Aufnahme_vom_Geraet_laesst_sich_ausschneiden_und_zuruecklesen()
    {
        // Der Weg, den eine Sprungdatei beim Kunden nimmt: auslesen, eine Aufnahme einzeln
        // speichern, weitergeben, wieder oeffnen. Dabei darf sich kein Messwert aendern.
        string port = await RequireLoggerPortAsync();

        using var connection = new LoggerConnection(port);
        connection.Open();

        ReadoutResult result = await connection.ReadMemoryAsync(LoggerProtocol.CommandReadAll);
        DecodeResult data = PayloadDecoder.Decode(result.Payload);

        Assert.NotEmpty(data.Recordings);
        Recording original = data.Recordings.OrderByDescending(r => r.Samples.Count).First();

        _out.WriteLine($"Auszug            : {data.RawPayload.Length} Hexzeichen, " +
                       $"{data.Recordings.Count} Aufnahmen, {data.Events.Count} Ereignisse");
        _out.WriteLine($"Umfeld            : {data.RawContext.Length} Hexzeichen " +
                       $"({100.0 * data.RawContext.Length / data.RawPayload.Length:F1} %)");
        _out.WriteLine($"gewaehlte Aufnahme: {original.DisplayName}, {original.Samples.Count} Messpunkte");
        _out.WriteLine($"Rohausschnitt     : ab Zeichen {original.RawOffset}, " +
                       $"{original.RawSegment.Length} Zeichen");

        // Der Ausschnitt muss woertlich im Auszug stehen - sonst waere er neu erzeugt.
        Assert.Equal(
            data.RawPayload.Substring(original.RawOffset, original.RawSegment.Length),
            original.RawSegment);

        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + SdvLogFile.Extension);
        try
        {
            SdvLogFile.SaveRecording(file, result.DeviceInfo, original, data,
                AltitudeReference.Standard, "Hardwaretest", "COM-Auslesevorgang");

            var info = new FileInfo(file);
            _out.WriteLine($"Dateigroesse      : {info.Length / 1024.0:F1} kB");

            SdvDocument loaded = SdvLogFile.Load(file);

            Assert.True(loaded.IntegrityVerified, loaded.IntegrityMessage);
            Assert.True(loaded.Manifest.IsExcerpt);

            Recording again = Assert.Single(loaded.Data.Recordings);
            Assert.Equal(original.Samples.Count, again.Samples.Count);
            Assert.Equal(original.RawSegment, again.RawSegment);
            Assert.Equal(data.Events.Count, loaded.Data.Events.Count);

            // Jeder Messpunkt einzeln, nicht nur die Anzahl.
            Assert.Equal(original.Samples.Select(s => s.PressureHpa), again.Samples.Select(s => s.PressureHpa));
            Assert.Equal(original.Samples.Select(s => s.AccZ), again.Samples.Select(s => s.AccZ));

            _out.WriteLine($"zurueckgelesen    : {again.Samples.Count} Messpunkte, " +
                           $"{loaded.Data.Events.Count} Ereignisse, Pruefsummen bestaetigt");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Ein_offener_Anschluss_bedeutet_noch_keine_Anmeldung()
    {
        // Die USB-Schnittstelle sitzt im Adapter. Ein offener Anschluss sagt deshalb nichts
        // darueber aus, ob ueberhaupt ein Logger daran steckt - erst die Antwort auf das '*'.
        string port = await RequireLoggerPortAsync();

        using var connection = new LoggerConnection(port);
        connection.Open();

        Assert.True(connection.IsOpen);
        Assert.False(connection.IsReady, "Nach dem Oeffnen darf noch keine Anmeldung gelten.");

        Assert.True(await connection.HandshakeAsync(), "Das Geraet hat den Ping nicht beantwortet.");
        Assert.True(connection.IsReady);

        _out.WriteLine("Anmeldung am Geraet erfolgreich.");
    }

    [Fact]
    public async Task Verbindung_heilt_sich_auch_ohne_Zutun_des_Aufrufers()
    {
        // Am Geraet gemessen: Nach 'B' wird der Logger nicht stumm, er antwortet auf einen
        // Befehl zuerst mit "?" und danach nur noch mit dessen Echo. Auf Stille zu warten waere
        // deshalb das falsche Merkmal gewesen - entscheidend ist, ob Nutzlast zurueckkommt.
        //
        // Hier wird bewusst roh abgemeldet, ohne der Verbindung Gelegenheit zu geben, ihren
        // Zustand nachzufuehren. Selbst dann muss der naechste Befehl eine Uhrzeit liefern.
        string port = await RequireLoggerPortAsync();

        using var connection = new LoggerConnection(port);
        connection.Open();
        Assert.True(await connection.HandshakeAsync());

        Assert.NotNull(LoggerCommands.ParseClock(
            await connection.SendSimpleAsync(LoggerCommands.ReadClock)));

        await connection.SendSimpleAsync(LoggerCommands.EndCommunication);

        LoggerClock? recovered = LoggerCommands.ParseClock(
            await connection.SendSimpleAsync(LoggerCommands.ReadClock));

        Assert.NotNull(recovered);
        _out.WriteLine("nach rohem Abmelden wiederhergestellt: " + recovered!.Describe());
    }

    [Fact]
    public async Task Nach_dem_Abmelden_meldet_sich_die_Verbindung_von_selbst_neu_an()
    {
        // 'B' beendet die Kommunikation - danach nimmt das Geraet keinen Befehl mehr an, bis ein
        // neues '*' kommt. Dieselbe Lage entsteht, wenn der Logger zwischendurch vom Adapter
        // abgezogen und wieder aufgesteckt wird; das kann der Rechner nicht sehen, weil die
        // USB-Schnittstelle im Adapter sitzt. Der Ablauf hier ist die pruefbare Entsprechung.
        string port = await RequireLoggerPortAsync();

        using var connection = new LoggerConnection(port);
        connection.Open();

        Assert.True(await connection.HandshakeAsync());
        LoggerClock? before = await connection.ReadClockAsync();
        Assert.NotNull(before);

        await connection.EndCommunicationAsync();
        Assert.False(connection.IsReady, "Nach 'B' darf die Anmeldung nicht mehr gelten.");

        // Ohne erneute Anmeldung kaeme hier keine Uhrzeit zurueck. Die Verbindung muss das
        // selbst erledigen, ohne dass der Anwender etwas davon merkt.
        LoggerClock? after = await connection.ReadClockAsync();

        Assert.NotNull(after);
        Assert.True(connection.IsReady);

        _out.WriteLine($"vor dem Abmelden    : {before!.Describe()}");
        _out.WriteLine($"nach Wiederanmeldung: {after!.Describe()}");
    }


    [Fact]
    public async Task Fehlender_Anschluss_wird_verstaendlich_gemeldet()
    {
        // Ohne Uebersetzung steht hier eine rohe Systemmeldung ueber einen "Dateinamen". Der
        // Anwender muss lesen koennen, dass der Adapter fehlt - nicht der Logger.
        using var connection = new LoggerConnection("COM254");

        LoggerProtocolException ex = await Assert.ThrowsAsync<LoggerProtocolException>(
            async () => await connection.ReadClockAsync());

        _out.WriteLine(ex.Message);

        Assert.Contains("Adapter", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(connection.IsReady);
    }

    [Fact]
    public async Task Ohne_neue_Aufnahmen_antwortet_das_Geraet_sofort_und_ohne_Daten()
    {
        // 'S' liefert alles seit dem letzten Auslesen. Liegt nichts vor, kommt nichts - das ist
        // eine gueltige Auskunft. Frueher wurde daraufhin ein zweiter Versuch gestartet, weil
        // eine leere Antwort als Stoerung galt; der Anwender sah dabei einen Ladevorgang, der
        // nichts zu laden hatte.
        string port = await RequireLoggerPortAsync();

        using var connection = new LoggerConnection(port);
        connection.Open();

        // Erst alles holen, damit danach nichts Neues mehr offen ist.
        await connection.ReadMemoryAsync(LoggerProtocol.CommandReadAll);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        ReadoutResult again = await connection.ReadMemoryAsync(LoggerProtocol.CommandReadNew);
        watch.Stop();

        DecodeResult decoded = PayloadDecoder.Decode(again.Payload);

        _out.WriteLine($"Dauer     : {watch.Elapsed.TotalSeconds:F1} s");
        _out.WriteLine($"Nutzdaten : {again.Payload.Count(char.IsAsciiHexDigit)} Hexzeichen");
        _out.WriteLine($"Aufnahmen : {decoded.Recordings.Count}");

        Assert.Empty(decoded.Recordings);

        // Ohne den zweiten Versuch bleibt es bei einem Durchlauf der Ruhephase.
        Assert.True(watch.Elapsed.TotalSeconds < 12,
            $"Eine leere Antwort darf nicht {watch.Elapsed.TotalSeconds:F1} s dauern.");
    }

    [Fact]
    public async Task Live_mitschreiben_ergibt_eine_speicherbare_Aufnahme()
    {
        // Der ganze Weg: anmelden, ein paar Sekunden mitschreiben, speichern, wieder oeffnen.
        // Gespeichert werden die unveraenderten Antworten des Geraets, nicht die ausgewerteten
        // Werte - dieselbe Regel wie beim Auslesen des Speichers.
        string port = await RequireLoggerPortAsync();

        using var connection = new LoggerConnection(port);
        connection.Open();
        Assert.True(await connection.HandshakeAsync());

        DeviceInfo info = await connection.ReadInfoAsync();
        var recorder = new LiveRecorder(1.0) { Device = info };

        for (int i = 0; i < 5; i++)
        {
            string env = await connection.SendSimpleAsync(LoggerCommands.ReadEnvironment);
            string acc = await connection.SendSimpleAsync(LoggerCommands.ReadAcceleration);

            Assert.True(recorder.Add(env, acc, DateTime.Now), $"Takt {i + 1} war nicht auswertbar.");
        }

        Recording live = recorder.BuildRecording()!;

        _out.WriteLine($"Messpunkte : {live.Samples.Count}");
        _out.WriteLine($"Takt       : {live.SampleIntervalSeconds:F2} s");
        _out.WriteLine($"Druck      : {live.Samples[0].PressureHpa:F1} hPa");
        _out.WriteLine($"Temperatur : {live.Samples[0].TemperatureC:F1} Grad C");
        _out.WriteLine($"Beschl.    : {live.Samples[0].AccMagnitude:F2} g");
        _out.WriteLine($"Mitschrift : {recorder.ToRawLog().Length} Zeichen");

        Assert.Equal(5, live.Samples.Count);
        Assert.InRange(live.Samples[0].PressureHpa, 850, 1100);
        Assert.InRange(live.Samples[0].TemperatureC, -20, 60);
        Assert.InRange(live.SampleIntervalSeconds, 0.2, 5.0);

        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + SdvLogFile.Extension);
        try
        {
            SdvLogFile.Save(file, info, recorder.BuildResult(), AltitudeReference.Standard, "Hardwaretest");
            SdvDocument loaded = SdvLogFile.Load(file);

            Assert.True(loaded.IntegrityVerified);
            Assert.Equal(SdvLogFile.KindLive, loaded.Manifest.Kind);

            Recording again = Assert.Single(loaded.Data.Recordings);

            Assert.Equal(live.Samples.Select(x => x.PressureHpa), again.Samples.Select(x => x.PressureHpa));
            Assert.Equal(live.Samples.Select(x => x.AccZ), again.Samples.Select(x => x.AccZ));

            _out.WriteLine($"gespeichert und zurueckgelesen: {again.Samples.Count} Messpunkte, Pruefsumme bestaetigt");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Auslesen_laesst_sich_abbrechen()
    {
        string port = await RequireLoggerPortAsync();

        using var connection = new LoggerConnection(port);
        connection.Open();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await connection.ReadMemoryAsync(LoggerProtocol.CommandReadAll, null, cts.Token));

        _out.WriteLine("Abbruch hat gegriffen.");
    }
}


