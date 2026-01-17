using System;
using System.Collections.Generic;

namespace DataViewer_1._0._0._0
{
    public class Messreihe
    {
        public DateTime Startzeit { get; set; }
        public double StartTemperatur { get; set; }
        public double StartDruck { get; set; }
        public List<Messdaten> Messungen { get; set; } = new List<Messdaten>();
        public string Status { get; set; }
        public string Spannung { get; set; }
        public DateTime Endzeit { get; set; }
        public double EndTemperatur { get; set; }
        public double EndDruck { get; set; }
    }
}
