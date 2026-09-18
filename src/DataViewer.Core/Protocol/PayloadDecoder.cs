using System.Globalization;
using System.Text;
using Siemert.DataViewer.Core.Model;

namespace Siemert.DataViewer.Core.Protocol;

/// <summary>
/// Dekodiert den Rohdatenstrom des SI-TL1.
/// </summary>
/// <remarks>
/// Der Scanner laeuft strikt auf dem 4-Zeichen-Raster des Protokolls. Das ist der wesentliche
/// Unterschied zur frueheren Umsetzung, die den Strom mit Split("AAAA") zerlegt hat:
/// Ein Bitmuster AAAA, das zufaellig ueber zwei benachbarte Felder hinweg entsteht, haette dort
/// eine Aufnahme mitten auseinandergeschnitten und die Bruchstuecke als neue Aufnahmen
/// fehlgedeutet. Auf dem Raster kann das nicht passieren.
/// Ebenso wird das Ende der Messdaten (FFFF) nur an Feldgrenzen geprueft, an denen ein Druckwert
/// stehen muesste - ein Beschleunigungswert FFFF (= -0,048 g) ist ein normaler Messwert und darf
/// die Aufnahme nicht beenden.
/// </remarks>
public static class PayloadDecoder
{
    public static DecodeResult Decode(string? rawPayload)
    {
        var messages = new List<DecodeMessage>();
        var recordings = new List<Recording>();
        var events = new List<DeviceEvent>();

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            messages.Add(new DecodeMessage(DecodeSeverity.Error, "Es wurden keine Daten empfangen."));
            return new DecodeResult { Messages = messages };
        }

        string data = Sanitize(rawPayload, out int dropped);
        if (dropped > 0)
        {
            messages.Add(new DecodeMessage(DecodeSeverity.Info,
                dropped + " Zeichen im Datenstrom waren keine Hexzeichen und wurden uebergangen."));
        }

        int origin = data.IndexOf(LoggerProtocol.MarkerRecordingStart, StringComparison.Ordinal);
        if (origin < 0)
        {
            // Kein Abbruch: Ein Auszug kann ausschliesslich Geraetekopf und Betriebsereignisse
            // enthalten - etwa das Umfeld, das einer einzeln gespeicherten Aufnahme beiliegt.
            // Die Ereignisse sind dann sehr wohl auswertbar.
            bool hasEvents =
                data.Contains(LoggerProtocol.MarkerConnected, StringComparison.Ordinal) ||
                data.Contains(LoggerProtocol.MarkerDisconnected, StringComparison.Ordinal);

            if (!hasEvents)
            {
                messages.Add(new DecodeMessage(DecodeSeverity.Error,
                    "In den Daten ist weder eine Aufnahme noch ein Betriebsereignis enthalten."));
                return new DecodeResult { Messages = messages, RawPayload = data, RawContext = data };
            }

            origin = 0;
        }
        else if (origin % LoggerProtocol.Alignment != 0)
        {
            messages.Add(new DecodeMessage(DecodeSeverity.Warning,
                "Der Datenstrom beginnt um " + (origin % LoggerProtocol.Alignment) +
                " Zeichen versetzt. Das deutet auf einen Uebertragungsfehler hin; die Auswertung " +
                "wird auf die gefundene Kennung ausgerichtet."));
        }

        int pos = origin;
        int skipped = 0;

        // Anfang und Ende jeder Aufnahme, um daraus das Umfeld zu bestimmen.
        var spans = new List<(int Start, int End)>();

        while (pos + LoggerProtocol.Alignment <= data.Length)
        {
            ReadOnlySpan<char> marker = data.AsSpan(pos, LoggerProtocol.Alignment);

            if (marker.SequenceEqual(LoggerProtocol.MarkerRecordingStart))
            {
                int recordingStart = pos;
                Recording? rec = ReadRecording(data, ref pos, messages, recordings.Count + 1);
                if (rec is not null)
                {
                    recordings.Add(rec);
                    spans.Add((recordingStart, pos));
                }

                continue;
            }

            if (marker.SequenceEqual(LoggerProtocol.MarkerConnected))
            {
                if (TryReadEvent(data, pos, DeviceEventKind.ConnectedToPc, out DeviceEvent? on))
                {
                    events.Add(on!);
                }

                pos += LoggerProtocol.Alignment + LoggerProtocol.ConnectedBodyHexLength;
                continue;
            }

            if (marker.SequenceEqual(LoggerProtocol.MarkerDisconnected))
            {
                if (TryReadEvent(data, pos, DeviceEventKind.DisconnectedFromPc, out DeviceEvent? off))
                {
                    events.Add(off!);
                }

                pos += LoggerProtocol.Alignment + LoggerProtocol.DisconnectedBodyHexLength;
                continue;
            }

            skipped += LoggerProtocol.Alignment;
            pos += LoggerProtocol.Alignment;
        }

        if (skipped > 0)
        {
            messages.Add(new DecodeMessage(DecodeSeverity.Info,
                (skipped / 2) + " Byte zwischen den Datensaetzen konnten keiner Kennung zugeordnet werden."));
        }

        if (recordings.Count == 0)
        {
            messages.Add(new DecodeMessage(DecodeSeverity.Warning,
                "Der Speicher enthaelt Betriebsereignisse, aber keine auswertbare Aufnahme."));
        }

        return new DecodeResult
        {
            Recordings = recordings,
            Events = events,
            Messages = messages,
            RawPayload = data,
            RawContext = BuildContext(data, spans)
        };
    }

    /// <summary>
    /// Sammelt alles, was ausserhalb der Aufnahmen steht.
    /// </summary>
    /// <remarks>
    /// Das Ergebnis besteht ausschliesslich aus unveraenderten Stuecken des Auszugs - es wird
    /// nichts umgeschrieben, nur das Innere der Aufnahmen herausgenommen. Uebrig bleiben
    /// Geraetekopf und Betriebsereignisse.
    /// </remarks>
    private static string BuildContext(string data, List<(int Start, int End)> spans)
    {
        if (spans.Count == 0)
        {
            return data;
        }

        var sb = new StringBuilder(data.Length - spans.Sum(s => s.End - s.Start));
        int cursor = 0;

        foreach ((int start, int end) in spans)
        {
            if (start > cursor)
            {
                sb.Append(data, cursor, start - cursor);
            }

            cursor = Math.Max(cursor, end);
        }

        if (cursor < data.Length)
        {
            sb.Append(data, cursor, data.Length - cursor);
        }

        return sb.ToString();
    }

    private static Recording? ReadRecording(string data, ref int pos, List<DecodeMessage> messages, int number)
    {
        int segmentStart = pos;
        pos += LoggerProtocol.Alignment;

        if (pos + LoggerProtocol.RecordingHeaderHexLength > data.Length)
        {
            messages.Add(new DecodeMessage(DecodeSeverity.Warning,
                "Aufnahme " + number + " bricht bereits im Kopf ab und wird verworfen."));
            pos = data.Length;
            return null;
        }

        string head = data.Substring(pos, LoggerProtocol.RecordingHeaderHexLength);
        pos += LoggerProtocol.RecordingHeaderHexLength;

        TimestampCodec.TryDecode(head.AsSpan(2, TimestampCodec.HexLength),
            out DateTime? startTime, out TimeSpan startTod, out bool clockSet);

        double startTemp = DecodeTemperature(head.AsSpan(16, 4));
        double startPressure = DecodePressure(head.AsSpan(20, 4));

        var samples = new List<Sample>();
        var warnings = new List<string>();
        double temperature = startTemp;
        bool temperatureHeld = false;
        int sampleIndex = 0;
        int temperatureInserts = 0;
        int resyncCount = 0;
        int skippedChars = 0;
        int cadenceDeviations = 0;
        bool endFound = false;

        // Der Logger schiebt nach je 240 Messungen einen Temperaturwert ein. Diese Kadenz ist die
        // Fuehrungsgroesse; der Wertebereich dient nur der Gegenpruefe. Beides zusammen, weil
        // keines allein genuegt: Reines Zaehlen bricht, wenn das Geraet einmal aus dem Takt
        // geraet (beobachtet unmittelbar nach einem Stromereignis), und reine Wertpruefung wuerde
        // einen verfaelschten Temperaturwert uebersehen.
        int nextInsertAfter = LoggerProtocol.TemperatureInterval - 1;

        while (pos + LoggerProtocol.SampleHexLength <= data.Length)
        {
            // Ende der Messdaten. FFFF ist weder ein moeglicher Druck- noch Temperaturwert und
            // an dieser Stelle daher eindeutig.
            if (data.AsSpan(pos, LoggerProtocol.Alignment).SequenceEqual(LoggerProtocol.MarkerRecordingEnd))
            {
                endFound = true;
                pos += LoggerProtocol.Alignment;
                break;
            }

            FieldKind kind = Classify(data, pos);

            if (sampleIndex == nextInsertAfter && kind != FieldKind.Temperature)
            {
                // An dieser Stelle muesste ein Temperaturwert stehen. Steht dort keiner, ist der
                // Datenstrom an dieser Stelle nicht regelkonform - das wird gemeldet, nicht
                // stillschweigend ueberspielt.
                cadenceDeviations++;
            }

            if (kind == FieldKind.Pressure)
            {
                double pressure = DecodePressure(data.AsSpan(pos, 4));
                (double ax, bool sx) = DecodeAcceleration(data.AsSpan(pos + 4, 4));
                (double ay, bool sy) = DecodeAcceleration(data.AsSpan(pos + 8, 4));
                (double az, bool sz) = DecodeAcceleration(data.AsSpan(pos + 12, 4));

                samples.Add(new Sample
                {
                    TimeSeconds = sampleIndex * LoggerProtocol.SampleIntervalSeconds,
                    PressureHpa = pressure,
                    TemperatureC = temperature,
                    TemperatureHeld = temperatureHeld,
                    AccX = ax,
                    AccY = ay,
                    AccZ = az,
                    AccSaturated = sx || sy || sz
                });

                temperatureHeld = true;
                sampleIndex++;
                pos += LoggerProtocol.SampleHexLength;
                continue;
            }

            if (kind == FieldKind.Temperature)
            {
                if (sampleIndex != nextInsertAfter)
                {
                    cadenceDeviations++;
                }

                temperature = DecodeTemperature(data.AsSpan(pos, 4));
                temperatureHeld = false;
                temperatureInserts++;
                nextInsertAfter = sampleIndex + LoggerProtocol.TemperatureInterval;
                pos += LoggerProtocol.Alignment;
                continue;
            }

            // Weder Druck noch Temperatur: das Raster ist verrutscht. Wir suchen die naechste
            // Stelle, an der die Struktur wieder traegt, statt ab hier Unsinn zu erzeugen.
            int resumed = Resynchronize(data, pos);
            if (resumed < 0)
            {
                break;
            }

            resyncCount++;
            skippedChars += resumed - pos;
            pos = resumed;
        }
        int statusRaw = 0;
        int quarterSecond = 0;
        int supplyVoltage = 0;
        DateTime? endTime = null;
        TimeSpan endTod = TimeSpan.Zero;
        double endTemp = double.NaN;
        double endPressure = double.NaN;
        bool complete = false;

        if (endFound && pos + LoggerProtocol.RecordingTrailerHexLength <= data.Length)
        {
            string tail = data.Substring(pos, LoggerProtocol.RecordingTrailerHexLength);
            pos += LoggerProtocol.RecordingTrailerHexLength;

            // Aufbau laut Herstellerdokumentation:
            // Status(4) VCC(4) Viertelsekunde(2) ss(2) mm(2) hh(2) TT(2) MM(2) JJJJ(4) Temp(4) Druck(4)
            statusRaw = ParseHex(tail.AsSpan(0, 4));
            supplyVoltage = ParseHex(tail.AsSpan(4, 4));
            quarterSecond = ParseHex(tail.AsSpan(8, 2));
            TimestampCodec.TryDecode(tail.AsSpan(10, TimestampCodec.HexLength), out endTime, out endTod, out _);

            // Diese beiden Werte wurden frueher ohne Skalierung uebernommen und landeten als
            // Rohzaehlwerte in den Archivdateien.
            endTemp = DecodeTemperature(tail.AsSpan(24, 4));
            endPressure = DecodePressure(tail.AsSpan(28, 4));
            complete = true;
        }
        else
        {
            warnings.Add("Der Abschlussdatensatz fehlt. Die Aufnahme wurde vermutlich nicht vollstaendig uebertragen.");
            messages.Add(new DecodeMessage(DecodeSeverity.Warning,
                "Aufnahme " + number + " ist unvollstaendig: Die Uebertragung endete vor dem Abschlussdatensatz."));
        }

        if (cadenceDeviations > 0)
        {
            warnings.Add("Der Temperaturwert kam " + cadenceDeviations +
                         " Mal nicht im erwarteten Abstand von " + LoggerProtocol.TemperatureInterval +
                         " Messungen. Das deutet auf eine Unregelmaessigkeit im Geraetespeicher hin, " +
                         "etwa nach einem Stromereignis.");
            messages.Add(new DecodeMessage(DecodeSeverity.Warning,
                "Aufnahme " + number + ": Temperaturtakt " + cadenceDeviations +
                " Mal abweichend (erwartet werden " + LoggerProtocol.TemperatureInterval + " Messungen)."));
        }

        if (resyncCount > 0)
        {
            warnings.Add(resyncCount + " Mal musste die Auswertung neu auf das Datenraster " +
                         "ausgerichtet werden; dabei wurden " + skippedChars +
                         " Zeichen uebergangen. An diesen Stellen koennen einzelne Messpunkte " +
                         "fehlen, und die Zeitangaben danach koennen um Bruchteile einer Sekunde " +
                         "verschoben sein.");
            messages.Add(new DecodeMessage(DecodeSeverity.Warning,
                "Aufnahme " + number + ": " + resyncCount + " Rastersprung/-spruenge im Datenstrom (" +
                skippedChars + " Zeichen). Die Aufnahme ist auswertbar, an den betroffenen Stellen " +
                "aber nicht luekenlos."));
        }

        if (((DeviceStatus)statusRaw).HasFlag(DeviceStatus.LowBattery))
        {
            warnings.Add("Das Geraet hat beim Beenden dieser Aufnahme eine zu geringe " +
                         "Batteriespannung gemeldet. Weitere Aufnahmen koennen unvollstaendig sein.");
            messages.Add(new DecodeMessage(DecodeSeverity.Warning,
                "Aufnahme " + number + ": Das Geraet meldet zu geringe Batteriespannung."));
        }

        if (samples.Count == 0)
        {
            messages.Add(new DecodeMessage(DecodeSeverity.Warning,
                "Aufnahme " + number + " enthaelt keine Messpunkte und wird verworfen."));
            return null;
        }

        int saturated = 0;
        foreach (Sample s in samples)
        {
            if (s.AccSaturated)
            {
                saturated++;
            }
        }

        if (saturated > 0)
        {
            warnings.Add(saturated + " Messpunkte liegen am Anschlag des Beschleunigungssensors (+/-16 g). " +
                         "Der tatsaechliche Spitzenwert kann hoeher gewesen sein.");
            messages.Add(new DecodeMessage(DecodeSeverity.Warning,
                "Aufnahme " + number + ": Beschleunigungssensor war bei " + saturated +
                " Messpunkten am Anschlag."));
        }

        return new Recording
        {
            StartTime = startTime,
            StartTimeOfDay = startTod,
            EndTime = endTime,
            EndTimeOfDay = endTod,
            ClockWasSet = clockSet,
            StartTemperatureC = startTemp,
            StartPressureHpa = startPressure,
            EndTemperatureC = endTemp,
            EndPressureHpa = endPressure,
            StatusRaw = statusRaw,
            SupplyVoltageRaw = supplyVoltage,
            EndQuarterSecond = quarterSecond,
            Samples = samples,
            Warnings = warnings,
            IsComplete = complete,
            RawSegment = data.Substring(segmentStart, pos - segmentStart),
            RawOffset = segmentStart
        };
    }

    private static bool TryReadEvent(string data, int pos, DeviceEventKind kind, out DeviceEvent? result)
    {
        result = null;
        int bodyLength = kind == DeviceEventKind.ConnectedToPc
            ? LoggerProtocol.ConnectedBodyHexLength
            : LoggerProtocol.DisconnectedBodyHexLength;

        int bodyStart = pos + LoggerProtocol.Alignment;
        if (bodyStart + bodyLength > data.Length)
        {
            return false;
        }

        // Aufbau laut Herstellerdokumentation:
        //   CCCC  Status(4) Viertelsekunde(2) ss mm hh TT MM JJJJ Temp(4) Druck(4)
        //   EEEE            Viertelsekunde(2) ss mm hh TT MM JJJJ Temp(4) Druck(4)
        // Der Anschlusssatz fuehrt also ein Statuswort, der Trennsatz nicht.
        int offset = kind == DeviceEventKind.ConnectedToPc ? 6 : 2;
        ReadOnlySpan<char> body = data.AsSpan(bodyStart, bodyLength);

        if (!TimestampCodec.TryDecode(body.Slice(offset, TimestampCodec.HexLength),
                out DateTime? ts, out TimeSpan tod, out bool clockSet))
        {
            return false;
        }

        double temp = DecodeTemperature(body.Slice(offset + 14, 4));
        double pressure = DecodePressure(body.Slice(offset + 18, 4));

        if (temp < LoggerProtocol.MinPlausibleTemperatureC || temp > LoggerProtocol.MaxPlausibleTemperatureC ||
            pressure < LoggerProtocol.MinPlausiblePressureHpa || pressure > LoggerProtocol.MaxPlausiblePressureHpa)
        {
            return false;
        }

        int status = kind == DeviceEventKind.ConnectedToPc ? ParseHex(body[..4]) : 0;

        result = new DeviceEvent
        {
            Kind = kind,
            Timestamp = ts,
            TimeOfDay = tod,
            ClockWasSet = clockSet,
            TemperatureC = temp,
            PressureHpa = pressure,
            RawOffset = pos,
            Status = (DeviceStatus)status,
            QuarterSecond = ParseHex(body.Slice(offset - 2, 2))
        };

        return true;
    }

    private enum FieldKind
    {
        Pressure,
        Temperature,
        Unknown
    }

    /// <summary>
    /// Bestimmt, was an einer Feldgrenze steht.
    /// </summary>
    /// <remarks>
    /// Das geht eindeutig, weil sich die Rohwertebereiche nicht ueberschneiden: ein Druck von
    /// 250 bis 1100 hPa liegt roh zwischen 2500 und 11000, eine Temperatur von -60 bis +90 °C
    /// zwischen -100 und 1400.
    /// <para>
    /// Frueher wurde stattdessen mitgezaehlt und nach je 240 Messpunkten ein Temperaturwert
    /// erwartet. Das trifft nicht immer zu: In einer am Geraet aufgezeichneten Messreihe kam der
    /// erste Einschub erst nach 250 Messpunkten. Der starr eingeschobene Zaehlschritt verschob
    /// dort das gesamte weitere Raster um vier Zeichen, wodurch Beschleunigungswerte als Druecke
    /// gelesen wurden - die Hoehe sprang dadurch zwischen -18.000 und +27.000 Metern.
    /// </para>
    /// </remarks>
    private static FieldKind Classify(string data, int pos)
    {
        if (pos + 4 > data.Length)
        {
            return FieldKind.Unknown;
        }

        int raw = ParseHex(data.AsSpan(pos, 4));

        double pressure = raw / LoggerProtocol.PressureScale;
        if (pressure >= LoggerProtocol.MinPlausiblePressureHpa &&
            pressure <= LoggerProtocol.MaxPlausiblePressureHpa)
        {
            return FieldKind.Pressure;
        }

        double temperature = (raw - LoggerProtocol.TemperatureOffset) / LoggerProtocol.TemperatureScale;
        if (temperature >= LoggerProtocol.MinPlausibleTemperatureC &&
            temperature <= LoggerProtocol.MaxPlausibleTemperatureC)
        {
            return FieldKind.Temperature;
        }

        return FieldKind.Unknown;
    }

    /// <summary>
    /// Sucht nach einem Rasterverlust die naechste Stelle, an der die Struktur wieder traegt.
    /// </summary>
    /// <remarks>
    /// Ein einzelner plausibler Druckwert genuegt als Beleg nicht, weil eine Beschleunigung von
    /// etwa 7,5 g denselben Rohwertebereich trifft. Deshalb wird gefordert, dass ab der neuen
    /// Stelle mehrere Messpunkte hintereinander aufgehen.
    /// </remarks>
    private static int Resynchronize(string data, int pos)
    {
        const int RequiredAnchors = 3;

        for (int p = pos + LoggerProtocol.Alignment;
             p + (RequiredAnchors * LoggerProtocol.SampleHexLength) <= data.Length;
             p += LoggerProtocol.Alignment)
        {
            bool ok = true;
            int probe = p;

            for (int k = 0; k < RequiredAnchors; k++)
            {
                // Ein Temperatureinschub darf zwischen den Messpunkten stehen.
                if (Classify(data, probe) == FieldKind.Temperature)
                {
                    probe += LoggerProtocol.Alignment;
                }

                if (Classify(data, probe) != FieldKind.Pressure)
                {
                    ok = false;
                    break;
                }

                probe += LoggerProtocol.SampleHexLength;
            }

            if (ok)
            {
                return p;
            }
        }

        return -1;
    }

    /// <summary>Wandelt einen 16-Bit-Rohwert in g um und meldet, ob der Messbereich erreicht wurde.</summary>
    public static (double Value, bool Saturated) DecodeAcceleration(ReadOnlySpan<char> hex)
    {
        int raw = ParseHex(hex);
        short raw16 = unchecked((short)raw);
        int raw10 = raw16 >> LoggerProtocol.AccelerationShift;
        double g = Math.Round(raw10 * LoggerProtocol.AccelerationScaleG, 3);
        return (g, Math.Abs(g) >= LoggerProtocol.AccelerationRangeG);
    }

    public static double DecodeTemperature(ReadOnlySpan<char> hex) =>
        (ParseHex(hex) - LoggerProtocol.TemperatureOffset) / LoggerProtocol.TemperatureScale;

    public static double DecodePressure(ReadOnlySpan<char> hex) =>
        ParseHex(hex) / LoggerProtocol.PressureScale;

    private static int ParseHex(ReadOnlySpan<char> hex) =>
        int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v) ? v : 0;

    /// <summary>Entfernt alles, was kein Hexzeichen ist (Zeilenumbrueche, Leerzeichen, Stoerzeichen).</summary>
    public static string Sanitize(string input, out int dropped)
    {
        var sb = new StringBuilder(input.Length);
        dropped = 0;
        foreach (char c in input)
        {
            if (c >= '0' && c <= '9')
            {
                sb.Append(c);
            }
            else if (c >= 'A' && c <= 'F')
            {
                sb.Append(c);
            }
            else if (c >= 'a' && c <= 'f')
            {
                sb.Append(char.ToUpperInvariant(c));
            }
            else if (!char.IsWhiteSpace(c))
            {
                dropped++;
            }
        }

        return sb.ToString();
    }
}
