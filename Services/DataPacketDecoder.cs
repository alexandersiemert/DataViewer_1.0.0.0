using System;
using System.Collections.Generic;
using System.Globalization;

namespace DataViewer_1._0._0._0
{
    public class DataPacketDecoder
    {
        public List<Messreihe> Decode(string datenpaket)
        {
            List<Messreihe> messreihen = new List<Messreihe>();
            if (string.IsNullOrEmpty(datenpaket))
            {
                return messreihen;
            }

            int splitStartIndex = datenpaket.IndexOf("AAAA", StringComparison.Ordinal);
            if (splitStartIndex < 0)
            {
                return messreihen;
            }

            if (splitStartIndex > 0)
            {
                datenpaket = datenpaket.Substring(splitStartIndex);
            }

            string[] einzelneReihen = datenpaket.Split(new[] { "AAAA" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string reihe in einzelneReihen)
            {
                if (string.IsNullOrEmpty(reihe) || reihe.Length < 24)
                {
                    continue;
                }

                try
                {
                    Messreihe messreihe = DecodeSeries(reihe);
                    if (messreihe != null)
                    {
                        messreihen.Add(messreihe);
                    }
                }
                catch
                {
                    // Skip malformed series.
                }
            }

            return messreihen;
        }

        private Messreihe DecodeSeries(string reihe)
        {
            string anfangsdaten = reihe.Substring(0, 24);

            Messreihe messreihe = new Messreihe
            {
                Startzeit = ParseDatumUndZeit(anfangsdaten.Substring(2, 14)),
                StartTemperatur = (HexZuDouble(anfangsdaten.Substring(16, 4)) - 500) / 10,
                StartDruck = HexZuDouble(anfangsdaten.Substring(20, 4)) / 10
            };

            int messdatenStartIndex = 24;
            int messdatenEndIndex = reihe.IndexOf("FFFF", StringComparison.Ordinal);
            if (messdatenEndIndex < 0 || messdatenEndIndex <= messdatenStartIndex)
            {
                return null;
            }

            string messdaten = reihe.Substring(messdatenStartIndex, messdatenEndIndex - messdatenStartIndex);

            double temperatur = messreihe.StartTemperatur;
            int tempCounter = 1;
            int timeCounter = 0;

            for (int i = 0; i + 15 < messdaten.Length; i += 16)
            {
                if (tempCounter >= 240)
                {
                    tempCounter = 0;

                    if (i + 19 >= messdaten.Length)
                    {
                        break;
                    }

                    temperatur = (HexZuDouble(messdaten.Substring(i, 4)) - 500) / 10;
                    i += 4;
                }

                if (i + 15 >= messdaten.Length)
                {
                    break;
                }

                Messdaten daten = new Messdaten
                {
                    Zeit = messreihe.Startzeit.AddMilliseconds(250 * timeCounter),
                    Druck = HexZuDouble(messdaten.Substring(i, 4)) / 10,
                    Hoehe = Math.Round((288.15 / 0.0065) * (1 - ((HexZuDouble(messdaten.Substring(i, 4)) / 10) / 1013.25)) * 0.190294957, 2),
                    BeschleunigungX = CalculateAccelerationFromHex(messdaten.Substring(i + 4, 4)),
                    BeschleunigungY = CalculateAccelerationFromHex(messdaten.Substring(i + 8, 4)),
                    BeschleunigungZ = CalculateAccelerationFromHex(messdaten.Substring(i + 12, 4)),
                    Temperatur = temperatur
                };

                messreihe.Messungen.Add(daten);
                tempCounter++;
                timeCounter++;
            }

            string abschlussdaten = reihe.Substring(messdatenEndIndex + 4);
            if (abschlussdaten.Length >= 32)
            {
                messreihe.Status = abschlussdaten.Substring(0, 4);
                messreihe.Spannung = abschlussdaten.Substring(4, 4);
                messreihe.Endzeit = ParseDatumUndZeit(abschlussdaten.Substring(10, 14));
                messreihe.EndTemperatur = HexZuDouble(abschlussdaten.Substring(24, 4));
                messreihe.EndDruck = HexZuDouble(abschlussdaten.Substring(28, 4));
            }

            return messreihe;
        }

        private DateTime ParseDatumUndZeit(string hexDatumZeit)
        {
            // Ensure hexDatumZeit length is valid before parsing.
            if (hexDatumZeit.Length != 14)
            {
                throw new ArgumentException("Invalid length for date and time.");
            }

            int sekunden = HexZuInt(hexDatumZeit.Substring(0, 2));
            int minuten = HexZuInt(hexDatumZeit.Substring(2, 2));
            int stunden = HexZuInt(hexDatumZeit.Substring(4, 2));
            int tag = int.Parse(hexDatumZeit.Substring(6, 2));
            int monat = int.Parse(hexDatumZeit.Substring(8, 2));
            int jahr = int.Parse(hexDatumZeit.Substring(10, 4));

            // Validierung der Datumswerte
            if (jahr < 1 || jahr > 9999 || monat < 1 || monat > 12 || tag < 1 || tag > DateTime.DaysInMonth(jahr, monat))
            {
                return new DateTime(1900, 1, 1, stunden, minuten, sekunden);
            }
            return new DateTime(jahr, monat, tag, stunden, minuten, sekunden);
        }

        private int HexZuInt(string hex)
        {
            return int.Parse(hex, NumberStyles.HexNumber);
        }

        private double HexZuDouble(string hex)
        {
            long intValue = Convert.ToInt64(hex, 16);
            return (double)intValue;
        }

        private double CalculateAccelerationFromHex(string hexData)
        {
            double acceleration;

            if (hexData.Length > 4)
            {
                throw new ArgumentException("Der Hexadezimal-String darf maximal vier Zeichen lang sein.");
            }

            // Konvertiere Hexadezimal-String in Integer (10-Bit Zweierkomplement)
            int rawValue = Convert.ToInt32(hexData, 16);

            if (rawValue <= 32767)
            {
                acceleration = rawValue / 2048.0;
                return Math.Round(acceleration, 3);
            }

            acceleration = (rawValue - 65535.0) / 2048.0;
            return Math.Round(acceleration, 3);
        }
    }
}
