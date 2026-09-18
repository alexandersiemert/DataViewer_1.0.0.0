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
using System.Globalization;

namespace DataViewer_1._0._0._0
{
    public class SerialPortManager : IDisposable
    {
        public sealed class LoggerReadProgress
        {
            public LoggerReadProgress(char command, long receivedHexChars, long expectedHexChars, bool isIndeterminate, bool isComplete, bool isError)
            {
                Command = command;
                ReceivedHexChars = receivedHexChars;
                ExpectedHexChars = expectedHexChars;
                IsIndeterminate = isIndeterminate;
                IsComplete = isComplete;
                IsError = isError;
            }

            public char Command { get; }
            public long ReceivedHexChars { get; }
            public long ExpectedHexChars { get; }
            public bool IsIndeterminate { get; }
            public bool IsComplete { get; }
            public bool IsError { get; }
        }

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
        public event Action<string, LoggerReadProgress> LoggerReadProgressChanged;

        private const int LoggerAddressCount = 0x4000;
        private const int LoggerBytesPerAddress = 128;
        private char? activeEchoCommand;
        private bool loggerReadActive;
        private bool loggerHeaderParsed;
        private int loggerPayloadStartIndex = -1;
        private int loggerLastCountedIndex;
        private long loggerExpectedHexChars;
        private long loggerReceivedHexChars;
 
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
                ResetReceiveState();
                serialPort.Write(command);
                PrepareReceiveState(command);
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
                if (activeEchoCommand == null && receivedData.Length > 0)
                {
                    activeEchoCommand = receivedData[0];
                }

                if (activeEchoCommand == 'G' || activeEchoCommand == 'S')
                {
                    if (ProcessLoggerData())
                    {
                        FinishReceive();
                    }
                    return;
                }

                if (receivedData.Length > 0 && expectedPacketCount == 0)
                {
                    char echoCommand = receivedData[0];
                    switch (echoCommand)
                    {
                        case 'I':
                            expectedPacketCount = 2;
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
                        FinishReceive(packets);
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

        private void PrepareReceiveState(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            activeEchoCommand = command[0];
            if (activeEchoCommand == 'G' || activeEchoCommand == 'S')
            {
                loggerReadActive = true;
                loggerHeaderParsed = false;
                loggerPayloadStartIndex = -1;
                loggerLastCountedIndex = 0;
                loggerExpectedHexChars = 0;
                loggerReceivedHexChars = 0;
                NotifyLoggerProgress(activeEchoCommand.Value, 0, 0, true, false, false);
            }
        }

        private bool ProcessLoggerData()
        {
            if (!loggerReadActive)
            {
                return false;
            }

            if (!loggerHeaderParsed)
            {
                if (!TryParseLoggerHeader(out int payloadStartIndex, out long expectedHexChars))
                {
                    return false;
                }

                loggerHeaderParsed = true;
                loggerPayloadStartIndex = payloadStartIndex;
                loggerExpectedHexChars = expectedHexChars;
                loggerReceivedHexChars = CountHexChars(receivedData, payloadStartIndex, receivedData.Length);
                loggerLastCountedIndex = receivedData.Length;
            }
            else if (loggerLastCountedIndex < receivedData.Length)
            {
                loggerReceivedHexChars += CountHexChars(receivedData, loggerLastCountedIndex, receivedData.Length);
                loggerLastCountedIndex = receivedData.Length;
            }

            bool isComplete = loggerExpectedHexChars > 0 && loggerReceivedHexChars >= loggerExpectedHexChars;
            NotifyLoggerProgress(activeEchoCommand ?? '?', loggerReceivedHexChars, loggerExpectedHexChars, loggerExpectedHexChars <= 0, isComplete, false);
            return isComplete;
        }

        private bool TryParseLoggerHeader(out int payloadStartIndex, out long expectedHexChars)
        {
            payloadStartIndex = -1;
            expectedHexChars = 0;

            int line1End = receivedData.IndexOf("\r\n", StringComparison.Ordinal);
            if (line1End < 0)
            {
                return false;
            }

            int line2Start = line1End + 2;
            int line2End = receivedData.IndexOf("\r\n", line2Start, StringComparison.Ordinal);
            if (line2End < 0)
            {
                return false;
            }

            string line1 = receivedData.Substring(0, line1End).Trim();
            string line2 = receivedData.Substring(line2Start, line2End - line2Start).Trim();

            if (!TryParseStartAddress(line1, out int startAddress))
            {
                return false;
            }

            if (!TryParseEndAddress(line2, out int endAddress))
            {
                return false;
            }

            long addressCount = startAddress <= endAddress
                ? (endAddress - startAddress + 1L)
                : (LoggerAddressCount - startAddress) + (endAddress + 1L);

            long bytesExpected = addressCount * LoggerBytesPerAddress;
            expectedHexChars = bytesExpected * 2L;
            payloadStartIndex = line2End + 2;
            return true;
        }

        private static bool TryParseStartAddress(string line, out int address)
        {
            address = 0;
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string trimmed = line.Trim();
            int startIndex = 0;
            if (trimmed.Length >= 5 && (trimmed[0] == 'G' || trimmed[0] == 'S'))
            {
                startIndex = 1;
            }

            if (trimmed.Length < startIndex + 4)
            {
                return false;
            }

            string hex = trimmed.Substring(startIndex, 4);
            return int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
        }

        private static bool TryParseEndAddress(string line, out int address)
        {
            address = 0;
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string trimmed = line.Trim();
            if (trimmed.Length < 4)
            {
                return false;
            }

            string hex = trimmed.Substring(0, 4);
            return int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
        }

        private static long CountHexChars(string data, int startIndex, int endIndex)
        {
            if (string.IsNullOrEmpty(data) || startIndex >= endIndex)
            {
                return 0;
            }

            int safeStart = Math.Max(0, startIndex);
            int safeEnd = Math.Min(data.Length, endIndex);
            long count = 0;
            for (int i = safeStart; i < safeEnd; i++)
            {
                char c = data[i];
                if ((c >= '0' && c <= '9') ||
                    (c >= 'A' && c <= 'F') ||
                    (c >= 'a' && c <= 'f'))
                {
                    count++;
                }
            }

            return count;
        }

        private void NotifyLoggerProgress(char command, long receivedHexChars, long expectedHexChars, bool isIndeterminate, bool isComplete, bool isError)
        {
            LoggerReadProgressChanged?.Invoke(serialPort.PortName, new LoggerReadProgress(command, receivedHexChars, expectedHexChars, isIndeterminate, isComplete, isError));
        }

        private void FinishReceive()
        {
            var packets = receivedData.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None);
            foreach (var packet in packets)
            {
                Debug.WriteLine(packet);
            }
            FinishReceive(packets);
        }

        private void FinishReceive(string[] packets)
        {
            StopDataReception();
            receivedData = "";
            expectedPacketCount = 0;
            activeEchoCommand = null;
            loggerReadActive = false;
            loggerHeaderParsed = false;
            loggerPayloadStartIndex = -1;
            loggerLastCountedIndex = 0;
            loggerExpectedHexChars = 0;
            loggerReceivedHexChars = 0;
            DataReceived?.Invoke(serialPort.PortName, packets);
        }

            // Event wenn Timeout Timer abgelaufen
        private void DataReceiveTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (loggerReadActive && loggerHeaderParsed && loggerReceivedHexChars > 0 && receivedData.EndsWith("\r\n"))
            {
                // Treat timeout after a clean line ending as end-of-data.
                FinishReceive();
                return;
            }

            ShowError("Timeout: " + serialPort.PortName + " not responding.", "COM-Port Error");
            if (loggerReadActive)
            {
                NotifyLoggerProgress(activeEchoCommand ?? '?', loggerReceivedHexChars, loggerExpectedHexChars, loggerExpectedHexChars <= 0, false, true);
            }
            // Timeout Timer anhalten
            ResetReceiveState();
        }

        private void ResetReceiveState()
        {
            receivedData = "";
            expectedPacketCount = 0;
            activeEchoCommand = null;
            loggerReadActive = false;
            loggerHeaderParsed = false;
            loggerPayloadStartIndex = -1;
            loggerLastCountedIndex = 0;
            loggerExpectedHexChars = 0;
            loggerReceivedHexChars = 0;
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
