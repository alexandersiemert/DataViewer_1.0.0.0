using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Siemert.DataViewer.Core.Analysis;
using Siemert.DataViewer.Core.Device;
using Siemert.DataViewer.Core.Model;
using Siemert.DataViewer.Core.Protocol;

namespace Siemert.DataViewer.Core.Io;

/// <summary>Kopfdaten einer Archivdatei.</summary>
public sealed class SdvManifest
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = SdvLogFile.CurrentFormatVersion;

    [JsonPropertyName("createdUtc")]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; set; } = "SIEMERT DataViewer";

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = string.Empty;

    [JsonPropertyName("deviceModel")]
    public string? DeviceModel { get; set; }

    [JsonPropertyName("deviceSerial")]
    public int? DeviceSerial { get; set; }

    [JsonPropertyName("deviceProductionDate")]
    public string? DeviceProductionDate { get; set; }

    [JsonPropertyName("deviceChecksum")]
    public string? DeviceChecksum { get; set; }

    [JsonPropertyName("deviceRawHeader")]
    public string? DeviceRawHeader { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>SHA-256 ueber die Rohdaten - macht nachtraegliche Veraenderung erkennbar.</summary>
    [JsonPropertyName("rawSha256")]
    public string? RawSha256 { get; set; }

    [JsonPropertyName("altitudeMode")]
    public string AltitudeMode { get; set; } = nameof(Analysis.AltitudeMode.GroundZero);

    [JsonPropertyName("qnhHpa")]
    public double QnhHpa { get; set; } = Atmosphere.StandardPressureHpa;

    [JsonPropertyName("stationElevationM")]
    public double StationElevationM { get; set; }

    [JsonPropertyName("sampleIntervalSeconds")]
    public double SampleIntervalSeconds { get; set; } = LoggerProtocol.SampleIntervalSeconds;

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// Wahr, wenn die Datei nur eine einzelne Aufnahme aus einem groesseren Auszug enthaelt.
    /// </summary>
    [JsonPropertyName("isExcerpt")]
    public bool IsExcerpt { get; set; }

    /// <summary>Name des Auszugs, aus dem der Ausschnitt stammt.</summary>
    [JsonPropertyName("excerptSourceName")]
    public string? ExcerptSourceName { get; set; }

    /// <summary>Pruefsumme des vollstaendigen Auszugs, aus dem geschnitten wurde.</summary>
    [JsonPropertyName("excerptSourceSha256")]
    public string? ExcerptSourceSha256 { get; set; }

    /// <summary>Lage des Ausschnitts im vollstaendigen Auszug, in Hexzeichen.</summary>
    [JsonPropertyName("excerptOffset")]
    public int? ExcerptOffset { get; set; }

    /// <summary>Laenge des Ausschnitts in Hexzeichen.</summary>
    [JsonPropertyName("excerptLength")]
    public int? ExcerptLength { get; set; }

    /// <summary>Pruefsumme des beigelegten Umfelds (Geraetekopf und Betriebsereignisse).</summary>
    [JsonPropertyName("contextSha256")]
    public string? ContextSha256 { get; set; }

    /// <summary>
    /// Art der Rohdaten: <c>speicher</c> fuer einen Geraeteauszug, <c>live</c> fuer eine am
    /// Rechner mitgeschriebene Aufzeichnung.
    /// </summary>
    /// <remarks>
    /// Beide sind Rohdaten, aber in verschiedenen Formaten und mit verschiedenen Zerlegern.
    /// Ohne diese Angabe muesste das Format beim Oeffnen erraten werden.
    /// </remarks>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = SdvLogFile.KindMemory;
}

/// <summary>Inhalt einer geladenen Archivdatei.</summary>
public sealed class SdvDocument
{
    public required SdvManifest Manifest { get; init; }

    public DeviceInfo? Device { get; init; }

    public required DecodeResult Data { get; init; }

    public required AltitudeReference Reference { get; init; }

    /// <summary>False, wenn die Pruefsumme der Rohdaten nicht zum Manifest passt.</summary>
    public bool IntegrityVerified { get; init; } = true;

    public string? IntegrityMessage { get; init; }
}

/// <summary>
/// Archivformat <c>.sdvlog</c>.
/// </summary>
/// <remarks>
/// Das Format ist ein ZIP-Behaelter mit <c>manifest.json</c>, den unveraenderten Rohdaten
/// <c>raw.hex</c> und deren SHA-256-Pruefsumme.
/// <para>
/// Der wichtigste Unterschied zur Vorgaengerversion: es werden die <b>Rohdaten</b> archiviert,
/// nicht nur das Auswertungsergebnis. Damit gilt zweierlei. Erstens laesst sich jede kuenftige
/// Korrektur an der Auswertung - etwa an der Hoehenformel oder am Bezugsdruck - rueckwirkend auf
/// alle Altdaten anwenden. Zweitens ist ueber die Pruefsumme nachweisbar, dass an den Messwerten
/// nichts veraendert wurde; das alte, offene XML war frei editierbar.
/// </para>
/// Dateien der Version 1 (XML) werden weiterhin gelesen.
/// </remarks>
public static class SdvLogFile
{
    public const int CurrentFormatVersion = 2;

    public const string Extension = ".sdvlog";

    private const string ManifestEntry = "manifest.json";
    private const string RawEntry = "raw.hex";
    private const string ChecksumEntry = "raw.sha256";

    /// <summary>Geraetekopf und Betriebsereignisse des Auszugs, unveraendert.</summary>
    private const string ContextEntry = "context.hex";

    /// <summary>Rohdaten aus dem Geraetespeicher.</summary>
    public const string KindMemory = "speicher";

    /// <summary>Live am Rechner mitgeschriebene Rohdaten.</summary>
    public const string KindLive = "live";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void Save(
        string path,
        DeviceInfo? device,
        DecodeResult data,
        AltitudeReference reference,
        string appVersion,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(reference);

        string raw = data.RawPayload ?? string.Empty;
        string hash = Sha256(raw);

        var manifest = new SdvManifest
        {
            AppVersion = appVersion,
            DeviceModel = device?.Model,
            DeviceSerial = device?.SerialNumber,
            DeviceProductionDate = device?.ProductionDate?.ToString("yyyy-MM-dd"),
            DeviceChecksum = device?.Checksum,
            DeviceRawHeader = device?.RawHeader,
            Source = device?.Source,
            RawSha256 = hash,
            Kind = LiveLog.IsLiveLog(raw) ? KindLive : KindMemory,
            AltitudeMode = reference.Mode.ToString(),
            QnhHpa = reference.QnhHpa,
            StationElevationM = reference.StationElevationM,
            Notes = notes
        };

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        Write(zip, ManifestEntry, JsonSerializer.Serialize(manifest, JsonOptions));
        Write(zip, RawEntry, raw);
        Write(zip, ChecksumEntry, hash);
    }

    /// <summary>
    /// Speichert eine einzelne Aufnahme als eigene Datei.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Gespeichert wird der <b>unveraenderte Rohausschnitt</b> dieser Aufnahme, zeichengenau aus
    /// dem Auszug herausgeschnitten - keine ausgewerteten Werte. Die Datei bleibt dadurch neu
    /// auswertbar, wenn sich die Auswertung spaeter aendert oder ein Fehler darin behoben wird.
    /// Genau daran ist die Vorgaengerversion gescheitert: Was einmal falsch dekodiert gespeichert
    /// wurde, blieb falsch.
    /// </para>
    /// <para>
    /// Beigelegt wird ausserdem das Umfeld des Auszugs - Geraetekopf und Betriebsereignisse -
    /// sowie die Pruefsumme des vollstaendigen Auszugs und die Lage des Ausschnitts darin. Damit
    /// laesst sich jederzeit nachweisen, aus welchem Auslesevorgang die Aufnahme stammt.
    /// </para>
    /// </remarks>
    public static void SaveRecording(
        string path,
        DeviceInfo? device,
        Recording recording,
        DecodeResult source,
        AltitudeReference reference,
        string appVersion,
        string? sourceName = null,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(reference);

        if (string.IsNullOrEmpty(recording.RawSegment))
        {
            throw new InvalidOperationException(
                "Für diese Aufnahme liegt kein Rohausschnitt vor. Sie stammt aus einer älteren " +
                "Datei, die nur ausgewertete Werte enthält, und lässt sich deshalb nicht einzeln " +
                "speichern. Bitte den zugehörigen Auszug erneut öffnen.");
        }

        string raw = recording.RawSegment;
        string context = source.RawContext ?? string.Empty;

        var manifest = new SdvManifest
        {
            AppVersion = appVersion,
            DeviceModel = device?.Model,
            DeviceSerial = device?.SerialNumber,
            DeviceProductionDate = device?.ProductionDate?.ToString("yyyy-MM-dd"),
            DeviceChecksum = device?.Checksum,
            DeviceRawHeader = device?.RawHeader,
            Source = device?.Source,
            RawSha256 = Sha256(raw),
            Kind = LiveLog.IsLiveLog(raw) ? KindLive : KindMemory,
            AltitudeMode = reference.Mode.ToString(),
            QnhHpa = reference.QnhHpa,
            StationElevationM = reference.StationElevationM,
            Notes = notes,
            IsExcerpt = true,
            ExcerptSourceName = sourceName,
            ExcerptSourceSha256 = Sha256(source.RawPayload ?? string.Empty),
            ExcerptOffset = recording.RawOffset,
            ExcerptLength = raw.Length,
            ContextSha256 = context.Length > 0 ? Sha256(context) : null
        };

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        Write(zip, ManifestEntry, JsonSerializer.Serialize(manifest, JsonOptions));
        Write(zip, RawEntry, raw);
        Write(zip, ChecksumEntry, manifest.RawSha256!);

        if (context.Length > 0)
        {
            Write(zip, ContextEntry, context);
        }
    }

    public static SdvDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (IsLegacyXml(path))
        {
            return LoadLegacyXml(path);
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        ZipArchiveEntry manifestEntry = zip.GetEntry(ManifestEntry)
            ?? throw new InvalidDataException("Die Datei enthält kein Manifest und ist kein gültiges .sdvlog-Archiv.");

        SdvManifest manifest = JsonSerializer.Deserialize<SdvManifest>(Read(manifestEntry), JsonOptions)
            ?? throw new InvalidDataException("Das Manifest der Datei ist beschädigt.");

        if (manifest.FormatVersion > CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Die Datei wurde mit einer neueren Programmversion erstellt (Formatversion {manifest.FormatVersion}). " +
                "Bitte den DataViewer aktualisieren.");
        }

        ZipArchiveEntry rawEntry = zip.GetEntry(RawEntry)
            ?? throw new InvalidDataException("Die Datei enthält keine Rohdaten.");

        string raw = Read(rawEntry);

        bool verified = true;
        string? integrityMessage = null;
        if (!string.IsNullOrEmpty(manifest.RawSha256))
        {
            string actual = Sha256(raw);
            if (!string.Equals(actual, manifest.RawSha256, StringComparison.OrdinalIgnoreCase))
            {
                verified = false;
                integrityMessage =
                    "Die Prüfsumme der Rohdaten stimmt nicht mit dem Manifest überein. " +
                    "Die Datei wurde nach dem Speichern verändert oder ist beschädigt.";
            }
        }
        else
        {
            verified = false;
            integrityMessage = "Die Datei enthält keine Prüfsumme; ihre Unversehrtheit lässt sich nicht bestätigen.";
        }

        // Eine Live-Mitschrift hat ein eigenes Format und einen eigenen Zerleger. Die Kennung im
        // Manifest entscheidet; zusaetzlich wird der Inhalt geprueft, damit eine von Hand
        // veraenderte Kennung nicht zum falschen Zerleger fuehrt.
        DecodeResult data = manifest.Kind == KindLive || LiveLog.IsLiveLog(raw)
            ? LiveLog.Parse(raw)
            : PayloadDecoder.Decode(raw);

        // Bei einer einzeln gespeicherten Aufnahme liegt das Umfeld des Auszugs bei. Daraus
        // stammen die Betriebsereignisse; Aufnahmen enthaelt es nicht.
        ZipArchiveEntry? contextEntry = zip.GetEntry(ContextEntry);
        if (contextEntry is not null)
        {
            string context = Read(contextEntry);

            if (!string.IsNullOrEmpty(manifest.ContextSha256) &&
                !string.Equals(Sha256(context), manifest.ContextSha256, StringComparison.OrdinalIgnoreCase))
            {
                verified = false;
                integrityMessage = (integrityMessage is null ? string.Empty : integrityMessage + " ") +
                    "Die Prüfsumme des beigelegten Geräteumfelds stimmt nicht mit dem Manifest überein.";
            }

            DecodeResult surroundings = PayloadDecoder.Decode(context);

            data = new DecodeResult
            {
                Recordings = data.Recordings,
                Events = surroundings.Events,
                Messages = data.Messages,
                RawPayload = data.RawPayload,
                RawContext = context
            };
        }

        DeviceInfo? device = null;
        if (!string.IsNullOrEmpty(manifest.DeviceRawHeader) &&
            HeaderParser.TryParse(manifest.DeviceRawHeader, out DeviceInfo? parsed, out _))
        {
            device = parsed;
            device!.Source = manifest.Source ?? Path.GetFileName(path);
        }

        var reference = new AltitudeReference
        {
            Mode = Enum.TryParse(manifest.AltitudeMode, out AltitudeMode mode) ? mode : AltitudeMode.GroundZero,
            QnhHpa = manifest.QnhHpa,
            StationElevationM = manifest.StationElevationM,
            GroundPressureHpa = data.Recordings.Count > 0 && !double.IsNaN(data.Recordings[0].EndPressureHpa)
                ? data.Recordings[0].EndPressureHpa
                : null
        };

        return new SdvDocument
        {
            Manifest = manifest,
            Device = device,
            Data = data,
            Reference = reference,
            IntegrityVerified = verified,
            IntegrityMessage = integrityMessage
        };
    }

    private static bool IsLegacyXml(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> head = stackalloc byte[5];
        int read = fs.Read(head);
        if (read < 2)
        {
            return false;
        }

        // ZIP beginnt mit "PK"; alles andere behandeln wir als das alte XML.
        return !(head[0] == 'P' && head[1] == 'K');
    }

    /// <summary>
    /// Liest eine Datei der Version 1. Diese Dateien enthalten keine Rohdaten, sondern nur das
    /// damalige Auswertungsergebnis - und in den Endwerten die bekannten unskalierten Rohzaehlwerte.
    /// </summary>
    private static SdvDocument LoadLegacyXml(string path)
    {
        XDocument doc = XDocument.Load(path);
        XElement? root = doc.Root;

        var manifest = new SdvManifest
        {
            FormatVersion = 1,
            DeviceModel = root?.Element("Logger")?.Element("Model")?.Value,
            DeviceChecksum = root?.Element("Logger")?.Element("Checksum")?.Value,
            Source = Path.GetFileName(path),
            AltitudeMode = nameof(Analysis.AltitudeMode.PressureAltitude),
            Notes = "Aus einer Datei der Formatversion 1 übernommen. Diese Dateien enthalten keine " +
                    "Rohdaten; Endtemperatur und Enddruck waren dort unskaliert gespeichert."
        };

        if (int.TryParse(root?.Element("Logger")?.Element("SerialNumber")?.Value, out int sn))
        {
            manifest.DeviceSerial = sn;
        }

        var recordings = new List<Recording>();

        foreach (XElement rec in root?.Element("Recordings")?.Elements("Recording") ?? [])
        {
            var samples = new List<Sample>();
            int index = 0;
            foreach (XElement m in rec.Element("Measurements")?.Elements("Measurement") ?? [])
            {
                samples.Add(new Sample
                {
                    TimeSeconds = index * LoggerProtocol.SampleIntervalSeconds,
                    PressureHpa = ParseDouble(m.Element("Druck")?.Value),
                    TemperatureC = ParseDouble(m.Element("Temperatur")?.Value),
                    AccX = ParseDouble(m.Element("BeschleunigungX")?.Value),
                    AccY = ParseDouble(m.Element("BeschleunigungY")?.Value),
                    AccZ = ParseDouble(m.Element("BeschleunigungZ")?.Value),
                    TemperatureHeld = true
                });
                index++;
            }

            if (samples.Count == 0)
            {
                continue;
            }

            DateTime? start = ParseDate(rec.Element("Startzeit")?.Value);
            DateTime? end = ParseDate(rec.Element("Endzeit")?.Value);
            bool clockSet = start.HasValue && start.Value.Year > 1901;

            // Die Endwerte aus Version-1-Dateien sind Rohzaehlwerte. Wir rechnen sie zurueck,
            // wenn sie eindeutig in diesem Wertebereich liegen.
            double endTemp = ParseDouble(rec.Element("EndTemperatur")?.Value);
            double endPressure = ParseDouble(rec.Element("EndDruck")?.Value);
            if (endTemp > 200)
            {
                endTemp = (endTemp - LoggerProtocol.TemperatureOffset) / LoggerProtocol.TemperatureScale;
            }

            if (endPressure > 2000)
            {
                endPressure /= LoggerProtocol.PressureScale;
            }

            recordings.Add(new Recording
            {
                StartTime = clockSet ? start : null,
                StartTimeOfDay = start?.TimeOfDay ?? TimeSpan.Zero,
                EndTime = clockSet ? end : null,
                EndTimeOfDay = end?.TimeOfDay ?? TimeSpan.Zero,
                ClockWasSet = clockSet,
                StartTemperatureC = ParseDouble(rec.Element("StartTemperatur")?.Value),
                StartPressureHpa = ParseDouble(rec.Element("StartDruck")?.Value),
                EndTemperatureC = endTemp,
                EndPressureHpa = endPressure,
                StatusRaw = int.TryParse(rec.Element("Status")?.Value,
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out int legacyStatus) ? legacyStatus : 0,
                Samples = samples,
                IsComplete = true,
                Warnings = ["Aus einer Datei der Formatversion 1 übernommen, ohne Rohdaten."]
            });
        }

        var data = new DecodeResult
        {
            Recordings = recordings,
            Messages =
            [
                new DecodeMessage(DecodeSeverity.Info,
                    "Datei der Formatversion 1 geladen. Sie enthält keine Rohdaten, daher sind " +
                    "Höhenbezug und Sättigungserkennung nachträglich nicht mehr korrigierbar. " +
                    "Beim Speichern wird das aktuelle Format verwendet.")
            ]
        };

        return new SdvDocument
        {
            Manifest = manifest,
            Device = null,
            Data = data,
            Reference = AltitudeReference.Standard,
            IntegrityVerified = false,
            IntegrityMessage = "Dateien der Formatversion 1 enthalten keine Prüfsumme."
        };
    }

    private static double ParseDouble(string? s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double v)
            ? v
            : double.NaN;

    private static DateTime? ParseDate(string? s) =>
        DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out DateTime v)
            ? v
            : null;

    private static void Write(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream s = entry.Open();
        using var w = new StreamWriter(s, new UTF8Encoding(false));
        w.Write(content);
    }

    private static string Read(ZipArchiveEntry entry)
    {
        using Stream s = entry.Open();
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    public static string Sha256(string content)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash);
    }
}
