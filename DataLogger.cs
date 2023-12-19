using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataViewer_1._0._0._0
{
    public class DataLogger
    {
        public string comPort { get; set; }
        public string checkSum { get; set; }
        public string sensorConstant1 { get; set; }
        public string sensorConstant2 { get; set; }
        public string sensorConstant3 { get; set; }
        public string sensorConstant4 { get; set; }
        public string sensorConstant5 { get; set; }
        public string reserve { get; set; }
        public string serialNumber { get; set; }
        public string id { get; set; }
        public string productionDate { get; set; }
        public string sensorCorrection { get; set; }
        public double[] dataTime { get; set; }
        public double[] dataAltitude { get; set; }
        public double[] dataTemperature { get; set; }
        public double[] dataAcceleration { get; set; }
        public double[] dataAccelerationX { get; set; }
        public double[] dataAccelerationY { get; set; }
        public double[] dataAccelerationZ { get; set; }



        // Konstruktor, Methoden usw. können hier hinzugefügt werden
    }
}
