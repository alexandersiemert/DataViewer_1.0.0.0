using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataViewer_1._0._0._0
{
    public class ComPortChecker
    {
        public static List<string> FindValidPorts()
        {
            List<string> responsivePorts = new List<string>();

            string[] portNames = SerialPort.GetPortNames();

            foreach (string portName in portNames)
            {
                using (SerialPort port = new SerialPort(portName))
                {
                    try
                    {
                        port.BaudRate = 38400; // Baudrate auf 38400 setzen
                        port.DataBits = 8;     // Datenbits auf 8 setzen
                        port.Parity = Parity.None; // Keine Parität
                        port.StopBits = StopBits.One; // 1 Stopbit

                        port.Open();
                        port.Write("*"); // Send '*' to the COM port
                        System.Threading.Thread.Sleep(100); // Wait for the response (adjust as needed)
                        string response = port.ReadExisting();

                        if (response.Contains("?"))
                        {
                            Debug.WriteLine($"Aktiver COM-Port: {portName}");
                            responsivePorts.Add(portName);


                        }
                    }
                    finally
                    {
                        port.Close();
                    }
                }
            }

            return responsivePorts;
        }
    }
}
