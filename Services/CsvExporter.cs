using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DataViewer_1._0._0._0
{
    public static class CsvExporter
    {
        public static void ExportSeriesToCsv(Messreihe series, string filePath)
        {
            if (series == null)
            {
                throw new ArgumentNullException(nameof(series));
            }

            StringBuilder csv = new StringBuilder();
            csv.AppendLine("Timestamp;Pressure_hPa;Altitude_m;Temperature_C;AccX_g;AccY_g;AccZ_g;AccAbs_g");

            foreach (Messdaten data in series.Messungen)
            {
                double accAbs = Math.Sqrt(
                    (data.BeschleunigungX * data.BeschleunigungX) +
                    (data.BeschleunigungY * data.BeschleunigungY) +
                    (data.BeschleunigungZ * data.BeschleunigungZ));

                csv.Append(data.Zeit.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                csv.Append(';');
                csv.Append(data.Druck.ToString("F2", CultureInfo.InvariantCulture));
                csv.Append(';');
                csv.Append(data.Hoehe.ToString("F2", CultureInfo.InvariantCulture));
                csv.Append(';');
                csv.Append(data.Temperatur.ToString("F2", CultureInfo.InvariantCulture));
                csv.Append(';');
                csv.Append(data.BeschleunigungX.ToString("F3", CultureInfo.InvariantCulture));
                csv.Append(';');
                csv.Append(data.BeschleunigungY.ToString("F3", CultureInfo.InvariantCulture));
                csv.Append(';');
                csv.Append(data.BeschleunigungZ.ToString("F3", CultureInfo.InvariantCulture));
                csv.Append(';');
                csv.AppendLine(accAbs.ToString("F3", CultureInfo.InvariantCulture));
            }

            File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
        }
    }
}
