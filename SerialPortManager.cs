using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;
using System.Windows;
using System.Timers;

namespace DataViewer_1._0._0._0
{
    public class SerialPortManager
    {
        //Neuen SerialPort anlegen
        private SerialPort serialPort;

        //String für das Abspeichern des Empfangspuffers
        private string receivedData = "";

        //Timer für Timeout bei Datenempfang
        private System.Timers.Timer dataReceiveTimer;

        //Verlinkung zum DataReceived Event der Instanz des SerialPorts im Main Code mit Übergabe der complete Message
        public event Action<string> DataReceived;
 
        //Initialisiere SerialPort
        public SerialPortManager(string portName)
        {
            serialPort = new SerialPort(portName, 38400, Parity.None, 8, StopBits.One);
            serialPort.DataReceived += new SerialDataReceivedEventHandler(SerialPort_DataReceived);
            //Timer für Timeout anlegen
            dataReceiveTimer = new System.Timers.Timer(5000); // 5000 Millisekunden (5 Sekunden)
            dataReceiveTimer.Elapsed += DataReceiveTimer_Elapsed;
        }

        //Get COM-Port Name
        public string GetPortName()
        {
            return serialPort.PortName;
        }

        //SerialPort öffnen
        public void OpenPort()
        {
            try
            {
                serialPort.Open();
            }
            catch
            {
                MessageBox.Show("An error has occurred: " + serialPort.PortName + " not found", "COM-Port Error", MessageBoxButton.OKCancel, MessageBoxImage.Error);
            }
        }

        //Abfrage ob Port offen
        public bool IsOpen()
        {
            return serialPort.IsOpen;
        }

        //COM-Port schließen
        public void ClosePort()
        {
            if (serialPort.IsOpen)
            {
                serialPort.Close();
            }
        }

        //Befehl senden
        public void SendCommand(string command)
        {
            try
            {
                serialPort.Write(command);
                StartDataReception();
            }
            catch
            {
                MessageBox.Show("An error has occurred: " + serialPort.PortName + " not open", "COM-Port Error", MessageBoxButton.OKCancel, MessageBoxImage.Error);
            }        
        }

        //Event wenn Daten empfangen wurden
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                // Daten vom COM-Port lesen und an den empfangenen Datenstring anhängen
                receivedData += serialPort.ReadExisting();
                // Timeout Stoppen
                StopDataReception();
                // Timeout wieder starten für nächsten Datenblock
                StartDataReception();
                // Überprüfen, ob die Daten mit CR LF enden
                if (receivedData.EndsWith("\r\n"))
                {
                    // Stoppe Timeout Timer bei komplettem Datenempfang
                    StopDataReception();
                    // Speichere das Datenpaket ohne CR LF am Ende ab
                    string completeMessage = receivedData.Substring(0, receivedData.Length - "\r\n".Length);
                    Debug.WriteLine(completeMessage);
                    // Zurücksetzen des empfangenen Datenstrings
                    receivedData = "";
                    // Verlinke das komplette Datenpaket an das Data Received Event der jeweiligen Instanz des SerialPorts
                    DataReceived?.Invoke(completeMessage);
                }
            }
            catch(Exception ex)
            {
                MessageBox.Show("An error has occurred: " + serialPort.PortName + ": " + ex.Message, "COM-Port Error", MessageBoxButton.OKCancel, MessageBoxImage.Error);
            }
        }

        // Starten Sie den Timer, wenn Sie auf Daten warten
        private void StartDataReception()
        {
            dataReceiveTimer.Start(); //Info: Der Timer fängt immer wieder bei der usprünglich festgelegten Zeit an
        }

        // Stoppen Sie den Timer, wenn Daten empfangen wurden oder ein Timeout erreicht wurde
        private void StopDataReception()
        {
            dataReceiveTimer.Stop();
        }

            // Event wenn Timeout Timer abgelaufen
            private void DataReceiveTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            MessageBox.Show("Timeout: " + serialPort.PortName + " not responding.", "COM-Port Error", MessageBoxButton.OKCancel, MessageBoxImage.Error);
            // Timeout Timer anhalten
            StopDataReception();
        }

    }
}
