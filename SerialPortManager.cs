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
    public class SerialPortManager : IDisposable
    {
        //Neuen SerialPort anlegen
        private SerialPort serialPort;

        //String für das Abspeichern des Empfangspuffers
        private string receivedData = "";

        //Variable für Anzahl der zu erwartenden Datenpakete
        private int expectedPacketCount=0;

        //Timer für Timeout bei Datenempfang
        private System.Timers.Timer dataReceiveTimer;

        //Verlinkung zum DataReceived Event der Instanz des SerialPorts im Main Code mit Übergabe der complete Message
        public event Action<string, string[]> DataReceived;
 
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
                if (serialPort.IsOpen)
                {
                    return;
                }
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

        //Event wenn Daten vollständig empfangen wurden
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

                // Bestimmen des Echo-Befehls und Setzen der erwarteten Paketzahl
                if (receivedData.Length > 0 && expectedPacketCount == 0)
                {
                    char echoCommand = receivedData[0];
                    switch (echoCommand)
                    {
                        case 'I':
                            expectedPacketCount = 2;
                            break;
                        case 'S':
                            expectedPacketCount = 4;
                            break;
                        case 'G':
                            expectedPacketCount = 4;
                            break;
                        default:
                            // Umgang mit unbekanntem Echo-Befehl
                            break;
                    }
                }

                // Überprüfen, ob die Daten mit CR LF enden
                if (receivedData.EndsWith("\r\n"))
                {
                    var packets = receivedData.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None);
                    foreach (var packet in packets)
                    {
                        Debug.WriteLine(packet);
                    }
                    if (packets.Length >= expectedPacketCount) // -1, weil das letzte Element leer sein könnte
                    {
                        // Stoppe Timeout Timer bei komplettem Datenempfang
                        StopDataReception();
                        // Zurücksetzen des empfangenen Datenstrings
                        receivedData = "";
                        // Zurücksetzen für den nächsten Empfang
                        expectedPacketCount = 0;
                        // Verlinke das komplette Datenpaket an das Data Received Event der jeweiligen Instanz des SerialPorts
                        DataReceived?.Invoke(serialPort.PortName, packets);
                    }
                }
            }
            catch (Exception ex)
            {
                ResetReceiveState();
                ShowError("An error has occurred: " + serialPort.PortName + ": " + ex.Message, "COM-Port Error");
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
            ShowError("Timeout: " + serialPort.PortName + " not responding.", "COM-Port Error");
            // Timeout Timer anhalten
            ResetReceiveState();
        }

        private void ResetReceiveState()
        {
            receivedData = "";
            expectedPacketCount = 0;
            StopDataReception();
        }

        private void ShowError(string message, string title)
        {
            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Error);
                });
            }
            else
            {
                MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Error);
            }
        }

        public void Dispose()
        {
            if (serialPort != null)
            {
                serialPort.DataReceived -= SerialPort_DataReceived;
                if (serialPort.IsOpen)
                {
                    serialPort.Close();
                }
                serialPort.Dispose();
                serialPort = null;
            }

            if (dataReceiveTimer != null)
            {
                dataReceiveTimer.Stop();
                dataReceiveTimer.Dispose();
                dataReceiveTimer = null;
            }
        }

    }
}
