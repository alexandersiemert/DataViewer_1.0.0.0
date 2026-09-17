using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DataViewer_1._0._0._0
{
    /// <summary>
    /// Schlankes, abhängigkeitsfreies Datei-Logging für Support/Fehlersuche.
    /// Schreibt nach %LocalAppData%\SIEMERT\DataViewer\logs\dataviewer-yyyyMMdd.log.
    /// Fehler beim Loggen dürfen die Anwendung niemals stören.
    /// </summary>
    public static class Logger
    {
        private static readonly object SyncRoot = new object();
        private static string logDirectory;

        public static string LogDirectory
        {
            get
            {
                if (logDirectory == null)
                {
                    logDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "SIEMERT", "DataViewer", "logs");
                }

                return logDirectory;
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public static void Error(string message, Exception ex = null)
        {
            Write("ERROR", message, ex);
        }

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                string directory = LogDirectory;
                Directory.CreateDirectory(directory);
                string file = Path.Combine(directory, "dataviewer-" + DateTime.Now.ToString("yyyyMMdd") + ".log");

                StringBuilder sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.Append(" [").Append(level).Append("] ");
                sb.Append(message);
                if (ex != null)
                {
                    sb.AppendLine();
                    sb.Append(ex);
                }

                lock (SyncRoot)
                {
                    File.AppendAllText(file, sb.ToString() + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception logEx)
            {
                // Logging darf niemals zum Absturz führen.
                Debug.WriteLine("Logger failed: " + logEx.Message);
            }
        }
    }
}
