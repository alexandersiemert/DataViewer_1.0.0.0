using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace DataViewer_1._0._0._0
{
    [XmlRoot("SiemertDataViewerLog")]
    public class DataViewerLogFile
    {
        public string FileVersion { get; set; }
        public LoggerMetadata Logger { get; set; } = new LoggerMetadata();

        [XmlArray("Recordings")]
        [XmlArrayItem("Recording")]
        public List<Recording> Recordings { get; set; } = new List<Recording>();
    }

    public class LoggerMetadata
    {
        public string Model { get; set; }
        public string SerialNumber { get; set; }
        public string ProductionDate { get; set; }
        public string Checksum { get; set; }
    }

    public class Recording
    {
        public DateTime Startzeit { get; set; }
        public double StartTemperatur { get; set; }
        public double StartDruck { get; set; }
        public string Status { get; set; }
        public string Spannung { get; set; }
        public DateTime Endzeit { get; set; }
        public double EndTemperatur { get; set; }
        public double EndDruck { get; set; }

        [XmlArray("Measurements")]
        [XmlArrayItem("Measurement")]
        public List<Measurement> Measurements { get; set; } = new List<Measurement>();
    }

    public class Measurement
    {
        public DateTime Zeit { get; set; }
        public double Druck { get; set; }
        public double Hoehe { get; set; }
        public double BeschleunigungX { get; set; }
        public double BeschleunigungY { get; set; }
        public double BeschleunigungZ { get; set; }
        public double Temperatur { get; set; }
    }
}
