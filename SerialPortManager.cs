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
        private readonly object receiveLock = new object();

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
            string[] packetsToDispatch = null;

            try
            {
                lock (receiveLock)
                {
                    // Read from port and append to buffer
                    receivedData += serialPort.ReadExisting();
                    StopDataReception();
                    StartDataReception();

                    if (receivedData.Length > 0 && expectedPacketCount == 0)
                    {
                        expectedPacketCount = GetExpectedPacketCount(receivedData[0]);
                        if (expectedPacketCount == 0)
                        {
                            Debug.WriteLine($"Unknown echo command: {receivedData[0]}");
                            ResetReceiveStateLocked();
                            return;
                        }
                    }

                    if (!receivedData.EndsWith("\r\n", StringComparison.Ordinal))
                    {
                        return;
                    }

                    var packets = receivedData.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var packet in packets)
                    {
                        Debug.WriteLine(packet);
                    }
                    if (expectedPacketCount > 0 && packets.Length >= expectedPacketCount)
                    {
                        StopDataReception();
                        ResetReceiveStateLocked();
                        packetsToDispatch = packets;
                    }
                }
            }
            catch (Exception ex)
            {
                ResetReceiveState();
                ShowError("An error has occurred: " + serialPort.PortName + ": " + ex.Message, "COM-Port Error");
            }

            if (packetsToDispatch != null)
            {
                DataReceived?.Invoke(serialPort.PortName, packetsToDispatch);
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
            lock (receiveLock)
            {
                ResetReceiveStateLocked();
            }
        }

        private void ResetReceiveStateLocked()
        {
            receivedData = "";
            expectedPacketCount = 0;
            StopDataReception();
        }

        private static int GetExpectedPacketCount(char echoCommand)
        {
            switch (echoCommand)
            {
                case 'I':
                    return 2;
                case 'S':
                case 'G':
                    return 4;
                default:
                    return 0;
            }
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

