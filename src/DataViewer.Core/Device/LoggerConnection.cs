using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using Siemert.DataViewer.Core.Model;
using Siemert.DataViewer.Core.Protocol;

namespace Siemert.DataViewer.Core.Device;

public sealed record ReadProgress(
    string Phase,
    long CharsReceived,
    long? EstimatedTotal,
    TimeSpan Elapsed,
    TimeSpan? EstimatedRemaining)
{
    /// <summary>
    /// Fortschritt 0..1, oder null solange keine Schaetzung moeglich ist. Der Wert wird bei 0,99
    /// gedeckelt, weil die vom Geraet angekuendigte Laenge nur eine Untergrenze ist.
    /// </summary>
    public double? Fraction => EstimatedTotal is > 0
        ? Math.Min(0.99, (double)CharsReceived / EstimatedTotal.Value)
        : null;
}

/// <summary>Ergebnis eines Auslesevorgangs, vor jeder Auswertung.</summary>
public sealed class ReadoutResult
{
    /// <summary>Die vollstaendige Antwort des Geraets, unveraendert.</summary>
    public required string RawResponse { get; init; }

    /// <summary>Nur die Nutzdaten (ohne Echo, Adressen und Geraetekopf).</summary>
    public required string Payload { get; init; }

    /// <summary>Der Geraetekopf, sofern die Antwort einen enthielt.</summary>
    public DeviceInfo? DeviceInfo { get; init; }

    public int? StartAddress { get; init; }

    public int? EndAddress { get; init; }

    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Serielle Verbindung zu einem SI-TL1.
/// </summary>
/// <remarks>
/// Gegenueber der Vorgaengerversion sind drei Dinge grundlegend anders:
/// <list type="number">
/// <item>
/// <b>Das Ende der Uebertragung wird an der Funkstille erkannt, nicht an der angekuendigten
/// Laenge.</b> Das Geraet meldet in den ersten beiden Zeilen eine Adressspanne, sendet aber
/// regelmaessig mehr Daten als darin angekuendigt - gemessen zwischen 18 und 500 Byte. Wer die
/// Uebertragung beim Erreichen der angekuendigten Menge beendet, schneidet den Strom ab. Im
/// schlimmsten beobachteten Fall fehlte dadurch die komplette juengste Aufnahme, ohne jede
/// Meldung. Die angekuendigte Laenge dient hier nur noch der Fortschrittsanzeige.
/// </item>
/// <item>
/// <b>Der Empfangspuffer ist ein StringBuilder.</b> Vorher wurde mit <c>s += ...</c> in einen
/// String angehaengt; bei mehreren Megabyte ergibt das quadratischen Aufwand.
/// </item>
/// <item>
/// <b>Abbrechbar.</b> Jeder Auslesevorgang laesst sich jederzeit beenden.
/// </item>
/// </list>
/// </remarks>
public sealed class LoggerConnection : IDisposable
{
    /// <summary>So lange muss Funkstille herrschen, damit die Uebertragung als beendet gilt.</summary>
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(2000);

    /// <summary>So lange wird auf das erste Zeichen gewartet.</summary>
    public static readonly TimeSpan FirstByteTimeout = TimeSpan.FromSeconds(5);

    private const int PollIntervalMs = 20;

    /// <summary>Antwortzeichen des Geraets auf den Ping.</summary>
    private const string PingResponse = "?";

    /// <summary>Anzahl Anmeldeversuche.</summary>
    private const int HandshakeAttempts = 3;

    /// <summary>Wartezeit auf die Antwort je Anmeldeversuch.</summary>
    private const int HandshakeReplyDelayMs = 250;

    private readonly SerialPort _port;
    private readonly List<string> _portErrors = [];
    private bool _disposed;

    /// <summary>Das Geraet hat sich mit '?' gemeldet und nimmt Befehle an.</summary>
    private bool _ready;

    public LoggerConnection(string portName)
    {
        PortName = portName;
        _port = new SerialPort(portName, LoggerProtocol.BaudRate, Parity.None, 8, StopBits.One)
        {
            ReadBufferSize = 1 << 20,
            WriteBufferSize = 4096,
            ReadTimeout = 1000,
            WriteTimeout = 1000
        };

        _port.ErrorReceived += OnErrorReceived;
    }

    public string PortName { get; }

    public bool IsOpen => !_disposed && _port.IsOpen;

    /// <summary>
    /// Wahr, wenn sich das Geraet in dieser Sitzung bereits mit '?' gemeldet hat.
    /// </summary>
    /// <remarks>
    /// Ein offener Anschluss bedeutet <b>nicht</b>, dass ein Logger daran haengt: Die
    /// USB-Schnittstelle sitzt im Adapter, nicht im Geraet. Wird der Logger vom Adapter
    /// abgezogen, bleibt der Anschluss am Rechner bestehen und es gibt kein USB-Ereignis.
    /// Erst die Antwort auf den Ping sagt etwas darueber aus, ob wirklich ein Geraet da ist.
    /// </remarks>
    public bool IsReady => IsOpen && _ready;

    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_port.IsOpen)
        {
            try
            {
                _port.Open();
            }
            catch (IOException ex)
            {
                // Ohne Uebersetzung steht hier eine rohe Systemmeldung, und der Anwender sucht
                // den Fehler am Logger - dabei fehlt der Adapter, in dem die USB-Schnittstelle
                // sitzt.
                throw new LoggerProtocolException(
                    $"Der Anschluss {PortName} ist nicht vorhanden. Bitte prüfen, ob der " +
                    "USB-Adapter am Rechner steckt.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new LoggerProtocolException(
                    $"Der Anschluss {PortName} wird bereits von einem anderen Programm verwendet.", ex);
            }
        }

        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
        _portErrors.Clear();

        // Ein frisch geoeffneter Anschluss sagt nichts ueber das Geraet aus - die Anmeldung
        // steht noch aus.
        _ready = false;
    }

    /// <summary>
    /// Meldet sich beim Geraet an: sendet '*' und wartet auf '?'.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ohne diese Anmeldung antwortet der Logger auf keinen Befehl. Sie gilt nur, solange das
    /// Geraet am Adapter steckt: Wird es abgezogen und wieder aufgesteckt, ist sie verfallen
    /// und muss wiederholt werden. Der Rechner bekommt davon nichts mit, weil die
    /// USB-Schnittstelle im Adapter sitzt und dort bleibt - der Anschluss verschwindet also
    /// nicht und es gibt kein Geraeteereignis, an dem sich das erkennen liesse.
    /// </para>
    /// <para>
    /// Mehrere Versuche, weil das Geraet nach einem abgebrochenen Auslesevorgang noch eine
    /// Weile weitersendet und den Ping in dieser Zeit nicht beantwortet.
    /// </para>
    /// </remarks>
    public async Task<bool> HandshakeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_port.IsOpen)
        {
            Open();
        }

        _ready = false;

        for (int attempt = 0; attempt < HandshakeAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
            _port.Write(LoggerProtocol.CommandPing);

            await Task.Delay(HandshakeReplyDelayMs, cancellationToken).ConfigureAwait(false);

            if (_port.ReadExisting().IndexOf(PingResponse, StringComparison.Ordinal) >= 0)
            {
                _ready = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>Stellt sicher, dass das Geraet angemeldet ist, und meldet es sonst an.</summary>
    private async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (_ready)
        {
            return;
        }

        if (!await HandshakeAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new LoggerTimeoutException(NotAnsweringMessage(PortName));
        }
    }

    /// <summary>Verstaendliche Meldung, wenn sich kein Geraet meldet.</summary>
    public static string NotAnsweringMessage(string portName) =>
        $"Der Logger an {portName} meldet sich nicht. Die Verbindung zum Rechner ist dabei nicht " +
        "das Problem - die USB-Schnittstelle sitzt im Adapter, nicht im Gerät. Bitte prüfen, ob " +
        "der Logger fest im Adapter steckt. Wurde er zwischendurch abgezogen, genügt es, ihn " +
        "wieder aufzustecken und den Vorgang zu wiederholen.";

    public void Close()
    {
        if (!_disposed && _port.IsOpen)
        {
            _port.Close();
        }
    }

    private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs e) =>
        _portErrors.Add(e.EventType.ToString());

    /// <summary>Liest den Geraetekopf (Befehl 'I').</summary>
    public async Task<DeviceInfo> ReadInfoAsync(CancellationToken cancellationToken = default)
    {
        string response = await TransferAsync(LoggerProtocol.CommandInfo, null, cancellationToken).ConfigureAwait(false);

        string first = response.Split(["\r\n"], StringSplitOptions.None).FirstOrDefault() ?? string.Empty;
        if (!HeaderParser.TryParse(first, out DeviceInfo? info, out string? error))
        {
            throw new LoggerProtocolException(error ?? "Der Gerätekopf konnte nicht gelesen werden.");
        }

        info!.Source = PortName;
        return info;
    }

    /// <summary>
    /// Sendet einen Einzelbefehl und gibt die Antwort zurueck.
    /// </summary>
    public async Task<string> SendSimpleAsync(string command, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_port.IsOpen)
        {
            Open();
        }

        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);

        string answer = await SendOnceAsync(command, cancellationToken).ConfigureAwait(false);
        if (HasPayload(answer, command))
        {
            return answer;
        }

        // Keine Nutzlast trotz gueltig geglaubter Anmeldung. Der haeufigste Grund: Der Logger
        // wurde zwischendurch vom Adapter getrennt und wieder aufgesteckt. Der Rechner sieht
        // das nicht, weil die USB-Schnittstelle im Adapter sitzt und der Anschluss bestehen
        // bleibt. Also neu anmelden und den Befehl einmal wiederholen.
        _ready = false;
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        return await SendOnceAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Prueft, ob eine Antwort ueberhaupt Nutzdaten enthaelt.
    /// </summary>
    /// <remarks>
    /// Das ist noetig, weil das Geraet im abgemeldeten Zustand <b>nicht schweigt</b>: Es
    /// antwortet mit '?' oder wiederholt nur den Befehl. Am Geraet gemessen - nach 'B' liefert
    /// ein 'L' zuerst "?" und danach nur noch "L". Auf Stille zu warten waere also das falsche
    /// Merkmal; es kommt darauf an, ob nach Echo, Zeilenende und '?' noch etwas uebrig bleibt.
    /// </remarks>
    private static bool HasPayload(string answer, string command)
    {
        // 'B' beendet die Kommunikation und liefert bauartbedingt nur sein Echo. Eine
        // Wiederholung wuerde sich dafuer neu anmelden, nur um sich sofort wieder abzumelden.
        if (string.Equals(command, LoggerCommands.EndCommunication, StringComparison.Ordinal))
        {
            return true;
        }

        string rest = answer.Replace("\r", string.Empty)
                            .Replace("\n", string.Empty)
                            .Replace("?", string.Empty)
                            .Trim();

        if (rest.StartsWith(command, StringComparison.OrdinalIgnoreCase))
        {
            rest = rest[command.Length..];
        }

        return rest.Trim().Length > 0;
    }

    private async Task<string> SendOnceAsync(string command, CancellationToken cancellationToken)
    {
        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
        _port.Write(command);

        var sw = Stopwatch.StartNew();
        var buffer = new StringBuilder();
        TimeSpan lastData = TimeSpan.Zero;

        while (sw.Elapsed < FirstByteTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string chunk = _port.ReadExisting();
            if (chunk.Length > 0)
            {
                buffer.Append(chunk);
                lastData = sw.Elapsed;
            }
            else if (buffer.Length > 0 && sw.Elapsed - lastData > TimeSpan.FromMilliseconds(300))
            {
                break;
            }

            await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(false);
        }

        return buffer.ToString();
    }

    /// <summary>Liest Uhrzeit und Datum des Geraets (Befehl 'L').</summary>
    public async Task<LoggerClock?> ReadClockAsync(CancellationToken cancellationToken = default) =>
        LoggerCommands.ParseClock(await SendSimpleAsync(LoggerCommands.ReadClock, cancellationToken).ConfigureAwait(false));

    /// <summary>Liest Temperatur, Druck und Beschleunigung als Momentanwerte (Befehle 'D' und 'M').</summary>
    public async Task<LoggerLiveReading?> ReadLiveAsync(CancellationToken cancellationToken = default)
    {
        var env = LoggerCommands.ParseEnvironment(
            await SendSimpleAsync(LoggerCommands.ReadEnvironment, cancellationToken).ConfigureAwait(false));
        var acc = LoggerCommands.ParseAcceleration(
            await SendSimpleAsync(LoggerCommands.ReadAcceleration, cancellationToken).ConfigureAwait(false));

        if (env is null || acc is null)
        {
            return null;
        }

        return new LoggerLiveReading(env.Value.TemperatureC, env.Value.PressureHpa,
            acc.Value.X, acc.Value.Y, acc.Value.Z);
    }

    /// <summary>Liest Geraetekennung und Anzahl der Speicherumlaeufe (Befehle 'C' und 'R').</summary>
    public async Task<LoggerDeviceFacts> ReadFactsAsync(CancellationToken cancellationToken = default)
    {
        int checksum = LoggerCommands.ParseNumber(
            await SendSimpleAsync(LoggerCommands.ReadChecksum, cancellationToken).ConfigureAwait(false),
            LoggerCommands.ReadChecksum) ?? 0;

        int wraps = LoggerCommands.ParseNumber(
            await SendSimpleAsync(LoggerCommands.ReadMemoryWraps, cancellationToken).ConfigureAwait(false),
            LoggerCommands.ReadMemoryWraps) ?? 0;

        return new LoggerDeviceFacts(checksum, wraps);
    }

    /// <summary>
    /// Stellt Uhr und Kalender des Geraets (Befehl 'U').
    /// </summary>
    /// <remarks>
    /// Der Befehl wird erst im Augenblick des naechsten vollen Minutenwechsels abgesetzt, weil das
    /// Geraet die Sekunden dabei auf null setzt. Der Hersteller hebt genau das hervor.
    /// </remarks>
    public async Task<DateTime> SetClockAsync(
        IProgress<TimeSpan>? countdown = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_port.IsOpen)
        {
            Open();
        }

        // Vor dem Warten anmelden, damit ein fehlendes Geraet sofort auffaellt und nicht erst
        // nach einer Minute Countdown.
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);

        DateTime target = await LoggerCommands.WaitForFullMinuteAsync(countdown, cancellationToken)
            .ConfigureAwait(false);

        // Waehrend des Wartens kann der Logger abgezogen worden sein - noch einmal vergewissern.
        _ready = false;
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);

        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
        _port.Write(LoggerCommands.BuildSetClock(target));

        // Dem Geraet Zeit zum Uebernehmen geben, danach zurueckgelesen und geprueft.
        await Task.Delay(400, cancellationToken).ConfigureAwait(false);

        LoggerClock? check = await ReadClockAsync(cancellationToken).ConfigureAwait(false);
        if (check is null || !check.IsSet)
        {
            throw new LoggerProtocolException(
                "Das Geraet hat die Uhrzeit nicht uebernommen. Bitte Verbindung pruefen und erneut versuchen.");
        }

        return target;
    }

    /// <summary>Beendet die Kommunikation ordentlich (Befehl 'B').</summary>
    public async Task EndCommunicationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_ready || !_port.IsOpen)
            {
                // Nicht angemeldet - es gibt nichts zu beenden.
                return;
            }

            // Bewusst ohne die Wiederholung aus SendSimpleAsync: 'B' liefert bauartbedingt nur
            // sein Echo zurueck. Eine Wiederholung wuerde sich hier neu anmelden, nur um sich
            // sofort wieder abzumelden.
            await SendOnceAsync(LoggerCommands.EndCommunication, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Beim Aufraeumen unerheblich - das Geraet beendet die Kommunikation ohnehin,
            // sobald es vom Adapter getrennt wird.
        }
        finally
        {
            // Nach 'B' ist die Anmeldung verbraucht; der naechste Befehl braucht wieder ein '*'.
            _ready = false;
        }
    }

    /// <summary>Liest den Speicher (Befehl 'G' oder 'W' fuer alles, 'S' fuer die neuen Daten).</summary>
    public async Task<ReadoutResult> ReadMemoryAsync(
        string command,
        IProgress<ReadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        string response = await TransferAsync(command, progress, cancellationToken).ConfigureAwait(false);
        sw.Stop();

        string[] lines = response.Split(["\r\n"], StringSplitOptions.None);

        int? start = null;
        int? end = null;
        DeviceInfo? info = null;
        int payloadLine = 0;

        if (lines.Length > 0 && TryParseAddress(lines[0], out int s))
        {
            start = s;
            payloadLine = 1;
        }

        if (lines.Length > payloadLine && TryParseAddress(lines[payloadLine], out int e))
        {
            end = e;
            payloadLine++;
        }

        // Der naechste Block ist der Geraetekopf - der wurde frueher einfach weggeworfen,
        // obwohl er Modell, Seriennummer und Produktionsdatum enthaelt.
        if (lines.Length > payloadLine &&
            lines[payloadLine].Length == LoggerProtocol.HeaderHexLength &&
            !lines[payloadLine].Contains(LoggerProtocol.MarkerRecordingStart, StringComparison.Ordinal))
        {
            if (HeaderParser.TryParse(lines[payloadLine], out DeviceInfo? parsed, out _))
            {
                info = parsed;
                info!.Source = PortName;
            }

            payloadLine++;
        }

        var payload = new StringBuilder();
        for (int i = payloadLine; i < lines.Length; i++)
        {
            payload.Append(lines[i]);
        }

        return new ReadoutResult
        {
            RawResponse = response,
            Payload = payload.ToString(),
            DeviceInfo = info,
            StartAddress = start,
            EndAddress = end,
            Duration = sw.Elapsed
        };
    }

    private async Task<string> TransferAsync(
        string command,
        IProgress<ReadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_port.IsOpen)
        {
            Open();
        }

        // Hat sich das Geraet in diesem Aufruf gerade erst gemeldet, dann steht fest, dass es da
        // ist. Eine leere Antwort ist dann keine Stoerung, sondern die Auskunft "nichts
        // vorhanden" - so antwortet 'S', wenn seit dem letzten Auslesen nichts aufgezeichnet
        // wurde. In dem Fall waere ein zweiter Versuch nur verlorene Zeit.
        bool justSignedIn = !_ready;
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);

        string response;

        try
        {
            response = await TransferOnceAsync(command, progress, cancellationToken).ConfigureAwait(false);

            if (HasPayload(response, command) || justSignedIn)
            {
                return response;
            }
        }
        catch (LoggerTimeoutException)
        {
            // Kein einziges Zeichen empfangen.
            response = string.Empty;
        }

        // Echo oder '?' statt Daten - oder gar nichts. Der haeufigste Grund dafuer ist nicht ein
        // defektes Kabel, sondern ein Logger, der zwischendurch vom Adapter getrennt und wieder
        // aufgesteckt wurde: Der Anschluss am Rechner bleibt dabei bestehen, die Anmeldung am
        // Geraet ist aber verfallen. Es sind keine Nutzdaten angekommen, ein zweiter Versuch
        // kann also nichts durcheinanderbringen.
        _ready = false;

        if (!await HandshakeAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new LoggerTimeoutException(NotAnsweringMessage(PortName));
        }

        return await TransferOnceAsync(command, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> TransferOnceAsync(
        string command,
        IProgress<ReadProgress>? progress,
        CancellationToken cancellationToken)
    {
        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();

        var buffer = new StringBuilder(1 << 16);
        var sw = Stopwatch.StartNew();

        _port.Write(command);

        TimeSpan lastData = sw.Elapsed;
        long? estimatedTotal = null;
        bool headerSeen = false;
        string phase = "Verbindung wird aufgebaut";

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // Das Gerät kennt keinen Abbruchbefehl und sendet weiter, bis es fertig ist.
                // Wir lesen den Rest deshalb weg und verwerfen ihn. Täten wir das nicht, würde der
                // nächste Befehl in den Restdaten untergehen und das Gerät schiene defekt.
                await DrainAsync(progress).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            string chunk;
            try
            {
                chunk = _port.ReadExisting();
            }
            catch (InvalidOperationException ex)
            {
                throw new LoggerProtocolException(
                    $"Die Verbindung zu {PortName} wurde während der Übertragung unterbrochen.", ex);
            }

            if (chunk.Length > 0)
            {
                buffer.Append(chunk);
                lastData = sw.Elapsed;
                phase = "Daten werden gelesen";

                if (!headerSeen && estimatedTotal is null)
                {
                    estimatedTotal = TryEstimateTotal(buffer, out headerSeen);
                }

                progress?.Report(BuildProgress(phase, buffer.Length, estimatedTotal, sw.Elapsed));
            }
            else
            {
                TimeSpan quiet = sw.Elapsed - lastData;

                if (buffer.Length == 0)
                {
                    if (sw.Elapsed > FirstByteTimeout)
                    {
                        throw new LoggerTimeoutException(
                            $"Das Gerät an {PortName} antwortet nicht. Bitte Verbindung und Einschaltzustand prüfen.");
                    }
                }
                else if (quiet >= QuietPeriod)
                {
                    break;
                }
            }

            await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(false);
        }

        sw.Stop();
        progress?.Report(new ReadProgress("Fertig", buffer.Length, buffer.Length, sw.Elapsed, TimeSpan.Zero));

        if (_portErrors.Count > 0)
        {
            throw new LoggerProtocolException(
                "Auf der seriellen Schnittstelle sind Übertragungsfehler aufgetreten (" +
                string.Join(", ", _portErrors.Distinct()) +
                "). Die Daten sind möglicherweise unvollständig.");
        }

        return buffer.ToString();
    }

    /// <summary>
    /// Höchstdauer, die nach einem Abbruch auf das Leerlaufen der Leitung gewartet wird.
    /// </summary>
    /// <remarks>
    /// Großzügig bemessen, weil das Gerät einen einmal begonnenen Speicherauszug vollständig
    /// sendet. Ein vorzeitiges Schließen des Anschlusses würde den Logger für die nächsten
    /// Sekunden scheinbar tot wirken lassen: Er sendet dann noch Daten, während die Anwendung
    /// bereits auf eine Antwort wartet.
    /// </remarks>
    public static readonly TimeSpan DrainLimit = TimeSpan.FromSeconds(90);

    /// <summary>Liest verbleibende Daten weg und verwirft sie, bis die Leitung ruhig ist.</summary>
    private async Task DrainAsync(IProgress<ReadProgress>? progress)
    {
        if (!_port.IsOpen)
        {
            return;
        }

        var sw = Stopwatch.StartNew();
        TimeSpan lastData = sw.Elapsed;
        long discarded = 0;

        while (sw.Elapsed < DrainLimit)
        {
            string chunk;
            try
            {
                chunk = _port.ReadExisting();
            }
            catch (InvalidOperationException)
            {
                return;
            }

            if (chunk.Length > 0)
            {
                discarded += chunk.Length;
                lastData = sw.Elapsed;
                progress?.Report(new ReadProgress(
                    "Abbruch, das Gerät sendet den Rest noch zu Ende",
                    discarded, null, sw.Elapsed, null));
            }
            else if (sw.Elapsed - lastData >= QuietPeriod)
            {
                return;
            }

            await Task.Delay(PollIntervalMs).ConfigureAwait(false);
        }
    }

    private static ReadProgress BuildProgress(string phase, long received, long? total, TimeSpan elapsed)
    {
        TimeSpan? remaining = null;
        if (total is > 0 && received > 0 && elapsed > TimeSpan.Zero)
        {
            double rate = received / elapsed.TotalSeconds;
            if (rate > 0)
            {
                double left = Math.Max(0, total.Value - received) / rate;
                remaining = TimeSpan.FromSeconds(left);
            }
        }

        return new ReadProgress(phase, received, total, elapsed, remaining);
    }

    /// <summary>
    /// Schaetzt die Gesamtmenge aus den ersten beiden Zeilen. Nur fuer die Anzeige - das Ende der
    /// Uebertragung wird daraus ausdruecklich nicht abgeleitet.
    /// </summary>
    private static long? TryEstimateTotal(StringBuilder buffer, out bool headerSeen)
    {
        headerSeen = false;
        string s = buffer.ToString();

        int first = s.IndexOf("\r\n", StringComparison.Ordinal);
        if (first < 0)
        {
            return null;
        }

        int second = s.IndexOf("\r\n", first + 2, StringComparison.Ordinal);
        if (second < 0)
        {
            return null;
        }

        headerSeen = true;

        if (!TryParseAddress(s[..first], out int start) ||
            !TryParseAddress(s[(first + 2)..second], out int end))
        {
            return null;
        }

        long blocks = start <= end ? end - start + 1L : end + 1L;
        return (blocks * 128L * 2L) + second + 2L;
    }

    private static bool TryParseAddress(string line, out int address)
    {
        address = 0;
        string t = line.Trim();
        if (t.Length >= 5 && char.IsLetter(t[0]))
        {
            t = t[1..];
        }

        return t.Length >= 4 &&
               int.TryParse(t.AsSpan(0, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _port.ErrorReceived -= OnErrorReceived;
            if (_port.IsOpen)
            {
                _port.Close();
            }
        }
        catch (IOException)
        {
            // Beim Abziehen des Kabels kann das Schliessen fehlschlagen - unerheblich.
        }
        finally
        {
            _port.Dispose();
        }
    }
}

public class LoggerProtocolException : Exception
{
    public LoggerProtocolException(string message) : base(message) { }

    public LoggerProtocolException(string message, Exception inner) : base(message, inner) { }
}

public sealed class LoggerTimeoutException : LoggerProtocolException
{
    public LoggerTimeoutException(string message) : base(message) { }
}
