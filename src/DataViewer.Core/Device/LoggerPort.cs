using System.IO.Ports;
using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Siemert.DataViewer.Core.Device;

/// <summary>Ein serieller Anschluss mit den Angaben, die zur Bewertung noetig sind.</summary>
public sealed record SerialPortCandidate(string PortName, string Description, string? HardwareId)
{
    /// <summary>
    /// True, wenn hinter dem Anschluss eine USB-Bruecke steckt, wie sie im Logger verbaut ist.
    /// Solche Anschluesse werden zuerst geprueft.
    /// </summary>
    public bool IsLikelyLogger =>
        HardwareId is not null &&
        LoggerPortScanner.KnownBridgeIds.Any(id => HardwareId.Contains(id, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => string.IsNullOrWhiteSpace(Description) ? PortName : $"{PortName}. {Description}";
}

/// <summary>
/// Sucht angeschlossene Logger.
/// </summary>
/// <remarks>
/// Die frühere Umsetzung hat jeden COM-Port des Rechners geoeffnet und ein '*' hineingeschrieben.
/// Das ist riskant: An einem COM-Port koennen Messgeraete mit SCPI-Befehlssatz haengen (dort ist
/// '*' das Praefix der Standardbefehle, ein stehengebliebenes Zeichen macht aus dem naechsten
/// "RST" ein "*RST" und setzt das Geraet zurueck), Funkgeraete mit CAT-Steuerung, 3D-Drucker und
/// Mikrocontroller, die beim Oeffnen des Ports ueber DTR neu starten, oder Bluetooth-Anschluesse,
/// deren Oeffnen sekundenlang blockiert.
/// Deshalb werden Anschluesse zuerst ueber ihre USB-Kennung gefiltert und nur passende
/// tatsaechlich angesprochen. Alle uebrigen werden nur auf ausdruecklichen Wunsch geprueft.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class LoggerPortScanner
{
    /// <summary>USB-Kennungen der im Logger verbauten Schnittstellenwandler.</summary>
    public static readonly string[] KnownBridgeIds =
    [
        "VID_10C4&PID_EA60", // Silicon Labs CP2102/CP210x - im SI-TL1 verbaut
        "VID_10C4&PID_EA70",
        "VID_0403"           // FTDI, fuer aeltere Geraetestaende
    ];

    /// <summary>Antwortzeichen des Loggers auf den Ping.</summary>
    private const char PingResponse = '?';

    public static IReadOnlyList<SerialPortCandidate> ListPorts()
    {
        var byName = new Dictionary<string, SerialPortCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (string p in SerialPort.GetPortNames())
        {
            byName[p] = new SerialPortCandidate(p, string.Empty, null);
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");

            foreach (ManagementBaseObject mo in searcher.Get())
            {
                string? name = mo["Name"] as string;
                if (name is null)
                {
                    continue;
                }

                Match m = Regex.Match(name, @"\((COM\d+)\)");
                if (!m.Success)
                {
                    continue;
                }

                string port = m.Groups[1].Value;
                string description = Regex.Replace(name, @"\s*\(COM\d+\)\s*$", string.Empty);
                string? hwid = mo["PNPDeviceID"] as string ?? mo["DeviceID"] as string;
                byName[port] = new SerialPortCandidate(port, description, hwid);
            }
        }
        catch (ManagementException)
        {
            // Ohne WMI bleibt die einfache Liste - dann wird eben ohne Beschreibung gearbeitet.
        }

        return byName.Values
            .OrderByDescending(c => c.IsLikelyLogger)
            .ThenBy(c => c.PortName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Sucht Anschluesse, an denen ein Logger antwortet.
    /// </summary>
    /// <param name="probeAllPorts">
    /// Wenn false (Vorgabe), werden nur Anschluesse mit passender USB-Kennung angesprochen.
    /// </param>
    /// <param name="portsInUse">Anschluesse, die gerade gelesen werden und nicht angefasst werden duerfen.</param>
    /// <param name="cancellationToken">Bricht die Suche ab.</param>
    public static async Task<IReadOnlyList<SerialPortCandidate>> FindLoggersAsync(
        bool probeAllPorts = false,
        IReadOnlySet<string>? portsInUse = null,
        CancellationToken cancellationToken = default)
    {
        var found = new List<SerialPortCandidate>();

        foreach (SerialPortCandidate candidate in ListPorts())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Ein laufender Auslesevorgang darf niemals gestoert werden.
            if (portsInUse is not null && portsInUse.Contains(candidate.PortName))
            {
                found.Add(candidate);
                continue;
            }

            if (!probeAllPorts && !candidate.IsLikelyLogger)
            {
                continue;
            }

            if (await RespondsAsync(candidate.PortName, cancellationToken).ConfigureAwait(false))
            {
                found.Add(candidate);
            }
        }

        return found;
    }

    /// <summary>Anzahl Ping-Versuche je Anschluss.</summary>
    private const int PingAttempts = 3;

    /// <summary>
    /// Wie oft das Oeffnen des Anschlusses wiederholt wird, bevor er als nicht verfuegbar gilt.
    /// </summary>
    /// <remarks>
    /// Windows gibt einen seriellen Anschluss nach dem Schliessen nicht augenblicklich frei.
    /// Faellt ein Suchlauf kurz nach einem Zugriff an, scheitert das Oeffnen - und der Logger
    /// verschwand dadurch kurzzeitig aus der Geraeteliste, obwohl er unveraendert angesteckt war.
    /// </remarks>
    private const int OpenAttempts = 2;

    private static async Task<bool> RespondsAsync(string portName, CancellationToken cancellationToken)
    {
        for (int open = 0; open < OpenAttempts; open++)
        {
            if (open > 0)
            {
                // Dem Treiber Zeit geben, den Anschluss freizugeben.
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            }

            bool? outcome = await TryRespondsAsync(portName, cancellationToken).ConfigureAwait(false);
            if (outcome.HasValue)
            {
                return outcome.Value;
            }
        }

        return false;
    }

    /// <summary>
    /// Ein Anspruchversuch. <c>null</c> bedeutet: Der Anschluss liess sich nicht oeffnen -
    /// das ist kein Urteil ueber das Geraet und darf wiederholt werden.
    /// </summary>
    private static async Task<bool?> TryRespondsAsync(string portName, CancellationToken cancellationToken)
    {
        try
        {
            using var port = new SerialPort(portName, Protocol.LoggerProtocol.BaudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 500,
                WriteTimeout = 500
            };

            port.Open();

            // Mehrere Versuche, weil das Geraet nach einem abgebrochenen Auslesevorgang noch eine
            // Weile weitersendet und den Ping in dieser Zeit nicht beantwortet.
            for (int attempt = 0; attempt < PingAttempts; attempt++)
            {
                port.DiscardInBuffer();
                port.DiscardOutBuffer();
                port.Write(Protocol.LoggerProtocol.CommandPing);

                await Task.Delay(250, cancellationToken).ConfigureAwait(false);

                if (port.ReadExisting().IndexOf(PingResponse) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            // Anschluss (noch) nicht zu oeffnen - das sagt nichts ueber das Geraet aus.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // Von einem anderen Zugriff belegt. Ebenfalls kein Urteil ueber das Geraet.
            return null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
