using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace DataViewer_1._0._0._0
{
    // Statische Klasse, weil ich möchte keine Instanzen, es gibt nur einen Manager und fertisch :-)
    public static class DataLoggerManager
    {
        // Erstelle ein Dictionary welche Datenlogger enthalten soll, um diese Später über den Namen des COM Ports wiederfinden zu können
        public static Dictionary<string, DataLogger> dataLoggers = new Dictionary<string, DataLogger>();

        //Füge einen neuen Logger hinzu oder überschreibe bereits vorhandenen Logger, die Kennung ist der COM-Port Name
        public static void AddLogger(DataLogger dataLogger, string port)
        {
            dataLoggers[port] = dataLogger;
        }

        // Entferne Logger aus dem Dictionary durch Angabe des COM-Ports
        public static bool RemoveLogger(string port)
        {
            return dataLoggers.Remove(port);
        }

        //Gebe einen Logger zurück nach Vorgabe des COM-Port Namens
        public static DataLogger GetLogger(string port)
        {
            if (dataLoggers.TryGetValue(port, out DataLogger dataLogger))
            {
                // Wenn port Index gefunden, gebe dataLogger zurück
                return dataLogger;
            }
            else
            {
                // Gebe null zurück, wenn port Index nicht gefunden wurde
                return null;
            }
        }

        // Gebe alle im Dictionary enthaltenen Logger zurück
        public static IEnumerable<DataLogger> GetAllLoggers()
        {
            return dataLoggers.Values;
        }

        // Alle Einträge im Dictionary löschen
        public static void ClearLoggers()
        {
            dataLoggers.Clear();
        }
    }
}
