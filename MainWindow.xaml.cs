using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Management;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.Plottables.Interactive;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace DataViewer_1._0._0._0
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        enum UnitMode
        {
            Metric,
            Imperial
        }

        private enum ChartStylePreset
        {
            Light,
            Dark,
            Slate
        }

        Scatter pltAlt, pltTemp, pltAcc, pltAccX, pltAccY, pltAccZ;

        ScottPlot.AxisPanels.RightAxis yAxisTemp, yAxisAcc;

        //XY-Datenarray für Höhe
        double[] xh, yh;
        double[] yhRaw;

        //XY-Datenarray für Temperatur
        double[] xt, yt;
        double[] ytRaw;

        //XY-Datenarray für Beschleunigung
        double[] xa, ya; //Betrag 3-Achsen
        double[] xax, yax; //Beschleunigung X-Achse
        double[] xay, yay; //Beschleunigung Y-Achse
        double[] xaz, yaz; //Beschleunigung Z-Achse

        InteractiveHorizontalSpan measuringSpan;

        InteractiveVerticalLine crosshairX;
        InteractiveHorizontalLine crosshairAlt;
        HorizontalLine crosshairTemp, crosshairAcc, crosshairAltLabel;
        VerticalLine crosshairXLabel;

        InteractiveMarker marker;
        double? lastMeasuringX1;
        double? lastMeasuringX2;
        bool isDateTimeXAxis = false;
        Marker hoverMarker;
        ScottPlot.Plottables.Text hoverLabel;
        string hoverSeries;
        double? hoverX;
        double? hoverY;
        const float HoverSnapDistance = 15f;
        const int HoverUpdateIntervalMs = 33;
        long lastHoverUpdateMs;
        readonly Stopwatch hoverStopwatch = Stopwatch.StartNew();
        const float PlotTitleFontSize = 20f;
        const float AxisLabelFontSize = 18f;
        const float AxisTickFontSize = 16f;
        const float LegendFontSize = 16f;
        const float HoverTooltipFontSize = 16f;
        const double MaxSmoothingSeconds = 300;

        const double FeetPerMeter = 3.28083989501312;
        const double CelsiusToFahrenheitScale = 9.0 / 5.0;
        UnitMode currentUnitMode = UnitMode.Metric;
        ChartStylePreset currentChartStyle = ChartStylePreset.Light;
        bool showGrid = true;
        bool axisLabelColorsFollowSeries = true;

        ScottPlot.Color altitudeColor = ScottPlot.Color.FromHex("#1B1B1B");
        ScottPlot.Color temperatureColor = ScottPlot.Color.FromHex("#D32F2F");
        ScottPlot.Color accAbsColor = ScottPlot.Color.FromHex("#CC79A7");
        ScottPlot.Color accXColor = ScottPlot.Color.FromHex("#0072B2");
        ScottPlot.Color accYColor = ScottPlot.Color.FromHex("#009E73");
        ScottPlot.Color accZColor = ScottPlot.Color.FromHex("#E69F00");

        ScottPlot.Color axisLabelAltColor = ScottPlot.Color.FromHex("#1B1B1B");
        ScottPlot.Color axisLabelTempColor = ScottPlot.Color.FromHex("#D32F2F");
        ScottPlot.Color axisLabelAccColor = ScottPlot.Color.FromHex("#CC79A7");

        ScottPlot.Color figureBackgroundColor = ScottPlot.Color.FromHex("#FFFFFF");
        ScottPlot.Color dataBackgroundColor = ScottPlot.Color.FromHex("#FFFFFF");
        ScottPlot.Color axisColor = ScottPlot.Color.FromHex("#2E2E2E");
        ScottPlot.Color gridMajorLineColor = ScottPlot.Color.FromHex("#E0E0E0");
        ScottPlot.Color legendBackgroundColor = ScottPlot.Color.FromHex("#FFFFFF");
        ScottPlot.Color legendFontColor = ScottPlot.Color.FromHex("#2E2E2E");
        ScottPlot.Color legendOutlineColor = ScottPlot.Color.FromHex("#C8C8C8");

        float altitudeLineWidth = 2.2f;
        float temperatureLineWidth = 1.0f;
        float accAbsLineWidth = 1.0f;
        float accXLineWidth = 1.0f;
        float accYLineWidth = 1.0f;
        float accZLineWidth = 1.0f;

        ScottPlot.LinePattern altitudeLinePattern = ScottPlot.LinePattern.Solid;
        ScottPlot.LinePattern temperatureLinePattern = ScottPlot.LinePattern.Solid;
        ScottPlot.LinePattern accAbsLinePattern = ScottPlot.LinePattern.Solid;
        ScottPlot.LinePattern accXLinePattern = ScottPlot.LinePattern.Solid;
        ScottPlot.LinePattern accYLinePattern = ScottPlot.LinePattern.Solid;
        ScottPlot.LinePattern accZLinePattern = ScottPlot.LinePattern.Solid;

        double smoothingAltSeconds = 0;
        double smoothingTempSeconds = 0;
        double smoothingAccAbsSeconds = 0;
        double smoothingAccXSeconds = 0;
        double smoothingAccYSeconds = 0;
        double smoothingAccZSeconds = 0;

        bool buttonAltUpPressed = false;
        bool buttonAltDownPressed = false;
        bool buttonTempUpPressed = false;
        bool buttonTempDownPressed = false;
        bool buttonAccUpPressed = false;
        bool buttonAccDownPressed = false;
        bool showAltitude = true;
        bool showTemperature = true;
        bool showAccAbs = true;
        bool showAccX = true;
        bool showAccY = true;
        bool showAccZ = true;
        private readonly Dictionary<string, SerialPortManager> serialPortManagers = new Dictionary<string, SerialPortManager>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Messreihe>> measurementSeriesByPort = new Dictionary<string, List<Messreihe>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> filePortNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string currentPortName;
        private int currentSeriesIndex = -1;
        private ManagementEventWatcher deviceArrivalWatcher;
        private ManagementEventWatcher deviceRemovalWatcher;
        private DispatcherTimer deviceRefreshTimer;
        private bool isRefreshingDevices;
        private const int DeviceRefreshDebounceMs = 750;
        private System.Windows.Input.Cursor previousOverrideCursor;
        private int busyCursorDepth;

        //Timer für Achsen Limit Button
        DispatcherTimer timer;
        //Zeit bis Button in Automatik geht
        const double passiveTime = 0.5;
        //Intervall für inkrementieren wenn Button Automatik Aktiv
        const double activeTime = 0.05;

        //Indexe für Array für Berechnung mit Messcursor für z.b. Min/Max Werte und Durchschnitt und so
        int indexCursor1, indexCursor2;

        //Klasse für Min/Max Werte
        public class minMax
        {
            public double min { get; set; }
            public double max { get; set; }
        }
        //Variablen mit Min/Max Werten
        minMax minMaxAlt = new minMax();
        minMax minMaxTemp = new minMax();
        minMax minMaxAcc = new minMax();
        minMax minMaxAccX = new minMax();
        minMax minMaxAccY = new minMax();
        minMax minMaxAccZ = new minMax();

        //Variablen für COM-Port Sachen
        List<string> validPorts = new List<string>();

        //Serial Port Manager 

        //Klassen für Messwerte
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

        public class Messdaten
        {
            public DateTime Zeit { get; set; }
            public double Druck { get; set; }
            public double Hoehe { get; set; }
            public double BeschleunigungX { get; set; }
            public double BeschleunigungY { get; set; }
            public double BeschleunigungZ { get; set; }
            public double Temperatur { get; set; } // Optional
        }

        //Variablen für Messreihen 

        public MainWindow()
        {
            InitializeComponent();

            //Timer initialisieren
            InitTimer();

            UpdateUnitMenuChecks();
            UpdateUnitTextBlocks();
            UpdateSmoothingTextBoxes();
            SetPlotVisibility(false);
            UpdateChartStyleMenuChecks();
            SetChartStylePreset(currentChartStyle);
            ApplyChartStyle();

            //TreeView initialisieren für DeviceList
            TreeViewManager.Initialize(deviceListTreeView);
            TreeViewManager.RequestCommand += HandlePortCommand;
            TreeViewManager.SeriesToggleChanged += TreeViewManager_SeriesToggleChanged;
            TreeViewManager.SetSeriesStates(showAltitude, showTemperature, showAccAbs, showAccX, showAccY, showAccZ);

            // Prepare right axes without plotting startup data.
            yAxisTemp = WpfPlot1.Plot.Axes.AddRightAxis();
            yAxisAcc = WpfPlot1.Plot.Axes.AddRightAxis();

            ApplySeriesVisibility();
            UpdateAxisLabelText();
            ApplyPlotFontSizes();

            ConfigurePlotInteractions();

            WpfPlot1.Plot.Legend.IsVisible = false;


            /****************EventHandler registrieren******************/

            //Plot mit Maus verschieben
            WpfPlot1.MouseMove += WpfPlot1_MouseMove;
            WpfPlot1.MouseLeave += WpfPlot1_MouseLeave;
            //Plot zoomen
            WpfPlot1.MouseWheel += WpfPlot1_MouseWheel;

            // Daten Plotten aus anderen Klassen heraus
            TreeViewManager.TriggerPlotData += TriggerPlotData;


            //Textboxen f?r Messungen leeren, weil da stehen sonst Fragezeichen drin und war zu faul das zu ?ndern :-D
            ClearMeasuringTextBoxes();

            //Plot neu zeichnen
            WpfPlot1.Refresh();

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeDeviceMonitoring();
            await RefreshDeviceListAsync(false);
        }

        private bool TryGetCurrentSeries(out Messreihe series, out string portName, out int seriesIndex)
        {
            series = null;
            portName = currentPortName;
            seriesIndex = currentSeriesIndex;

            if (string.IsNullOrWhiteSpace(portName) || seriesIndex < 0)
            {
                return false;
            }

            if (!measurementSeriesByPort.TryGetValue(portName, out List<Messreihe> seriesList) || seriesList == null)
            {
                return false;
            }

            if (seriesIndex >= seriesList.Count)
            {
                return false;
            }

            series = seriesList[seriesIndex];
            return series != null;
        }

        private void ExportSeriesToCsv(Messreihe series, string filePath, UnitMode exportUnits)
        {
            if (series == null)
            {
                throw new ArgumentNullException(nameof(series));
            }

            CultureInfo culture = CultureInfo.CurrentCulture;
            string separator = culture.TextInfo.ListSeparator;
            StringBuilder csv = new StringBuilder();
            string altitudeHeader = exportUnits == UnitMode.Metric ? "Altitude_m" : "Altitude_ft";
            string temperatureHeader = exportUnits == UnitMode.Metric ? "Temperature_C" : "Temperature_F";
            csv.AppendLine(string.Join(separator, new[]
            {
                "Timestamp",
                "Pressure_hPa",
                altitudeHeader,
                temperatureHeader,
                "AccX_g",
                "AccY_g",
                "AccZ_g",
                "AccAbs_g"
            }));

            foreach (Messdaten data in series.Messungen)
            {
                double accAbs = Math.Sqrt(
                    (data.BeschleunigungX * data.BeschleunigungX) +
                    (data.BeschleunigungY * data.BeschleunigungY) +
                    (data.BeschleunigungZ * data.BeschleunigungZ));

                double altitude = ConvertAltitude(data.Hoehe, UnitMode.Metric, exportUnits);
                double temperature = ConvertTemperature(data.Temperatur, UnitMode.Metric, exportUnits);

                csv.Append(data.Zeit.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                csv.Append(separator);
                csv.Append(data.Druck.ToString("F2", culture));
                csv.Append(separator);
                csv.Append(altitude.ToString("F2", culture));
                csv.Append(separator);
                csv.Append(temperature.ToString("F2", culture));
                csv.Append(separator);
                csv.Append(data.BeschleunigungX.ToString("F3", culture));
                csv.Append(separator);
                csv.Append(data.BeschleunigungY.ToString("F3", culture));
                csv.Append(separator);
                csv.Append(data.BeschleunigungZ.ToString("F3", culture));
                csv.Append(separator);
                csv.AppendLine(accAbs.ToString("F3", culture));
            }

            File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
        }

        private DataViewerLogFile BuildLogFile(DataLogger logger, IEnumerable<Messreihe> series)
        {
            DataViewerLogFile logFile = new DataViewerLogFile
            {
                FileVersion = "1.0",
                Logger = new LoggerMetadata
                {
                    Model = logger?.id,
                    SerialNumber = logger?.serialNumber,
                    ProductionDate = logger?.productionDate,
                    Checksum = logger?.checkSum
                }
            };

            if (series != null)
            {
                foreach (Messreihe entry in series)
                {
                    logFile.Recordings.Add(ConvertToRecording(entry));
                }
            }

            return logFile;
        }

        private static Recording ConvertToRecording(Messreihe series)
        {
            Recording record = new Recording();
            if (series == null)
            {
                return record;
            }

            record.Startzeit = series.Startzeit;
            record.StartTemperatur = series.StartTemperatur;
            record.StartDruck = series.StartDruck;
            record.Status = series.Status;
            record.Spannung = series.Spannung;
            record.Endzeit = series.Endzeit;
            record.EndTemperatur = series.EndTemperatur;
            record.EndDruck = series.EndDruck;

            if (series.Messungen != null)
            {
                foreach (Messdaten entry in series.Messungen)
                {
                    record.Measurements.Add(ConvertToMeasurement(entry));
                }
            }

            return record;
        }

        private static Measurement ConvertToMeasurement(Messdaten data)
        {
            Measurement measurement = new Measurement();
            if (data == null)
            {
                return measurement;
            }

            measurement.Zeit = data.Zeit;
            measurement.Druck = data.Druck;
            measurement.Hoehe = data.Hoehe;
            measurement.BeschleunigungX = data.BeschleunigungX;
            measurement.BeschleunigungY = data.BeschleunigungY;
            measurement.BeschleunigungZ = data.BeschleunigungZ;
            measurement.Temperatur = data.Temperatur;
            return measurement;
        }

        private static Messreihe ConvertToMessreihe(Recording record)
        {
            Messreihe series = new Messreihe();
            if (record == null)
            {
                return series;
            }

            series.Startzeit = record.Startzeit;
            series.StartTemperatur = record.StartTemperatur;
            series.StartDruck = record.StartDruck;
            series.Status = record.Status;
            series.Spannung = record.Spannung;
            series.Endzeit = record.Endzeit;
            series.EndTemperatur = record.EndTemperatur;
            series.EndDruck = record.EndDruck;

            if (record.Measurements != null)
            {
                foreach (Measurement entry in record.Measurements)
                {
                    series.Messungen.Add(ConvertToMessdaten(entry));
                }
            }

            return series;
        }

        private static Messdaten ConvertToMessdaten(Measurement data)
        {
            Messdaten measurement = new Messdaten();
            if (data == null)
            {
                return measurement;
            }

            measurement.Zeit = data.Zeit;
            measurement.Druck = data.Druck;
            measurement.Hoehe = data.Hoehe;
            measurement.BeschleunigungX = data.BeschleunigungX;
            measurement.BeschleunigungY = data.BeschleunigungY;
            measurement.BeschleunigungZ = data.BeschleunigungZ;
            measurement.Temperatur = data.Temperatur;
            return measurement;
        }

        private void SaveLogFile(DataViewerLogFile logFile, string filePath)
        {
            if (logFile == null)
            {
                throw new ArgumentNullException(nameof(logFile));
            }

            XmlSerializer serializer = new XmlSerializer(typeof(DataViewerLogFile));
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = Encoding.UTF8
            };

            using (XmlWriter writer = XmlWriter.Create(filePath, settings))
            {
                serializer.Serialize(writer, logFile);
            }
        }

        private DataViewerLogFile LoadLogFile(string filePath)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(DataViewerLogFile));
            using (FileStream stream = File.OpenRead(filePath))
            {
                return serializer.Deserialize(stream) as DataViewerLogFile;
            }
        }

        private static string CreateFilePortName(string filePath)
        {
            string baseName = System.IO.Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "File";
            }

            return $"FILE:{baseName}:{Guid.NewGuid():N}";
        }

        private string BuildLogFileName(DataLogger logger, string suffix)
        {
            string model = SanitizeFileName(logger?.id ?? "UnknownModel");
            string serialNumber = SanitizeFileName(logger?.serialNumber ?? "UnknownSerial");
            return $"{model}_{serialNumber}_{suffix}.sdvlog";
        }

        private bool TryGetSelectedSeries(out string portName, out int index)
        {
            portName = null;
            index = -1;

            if (deviceListTreeView == null)
            {
                return false;
            }

            if (!(deviceListTreeView.SelectedItem is TreeViewItem selectedItem))
            {
                return false;
            }

            dynamic tag = selectedItem.Tag;
            if (tag == null)
            {
                return false;
            }

            if (string.Equals(tag.ItemType, "SubItem", StringComparison.OrdinalIgnoreCase))
            {
                portName = tag.PortName;
                TreeViewItem parentItem = LogicalTreeHelper.GetParent(selectedItem) as TreeViewItem;
                if (parentItem != null)
                {
                    index = parentItem.Items.IndexOf(selectedItem);
                }
            }

            return !string.IsNullOrWhiteSpace(portName) && index >= 0;
        }

        private bool TryGetSelectedLogger(out string portName)
        {
            portName = null;

            if (deviceListTreeView == null)
            {
                return false;
            }

            if (!(deviceListTreeView.SelectedItem is TreeViewItem selectedItem))
            {
                return false;
            }

            dynamic tag = selectedItem.Tag;
            if (tag == null)
            {
                return false;
            }

            if (string.Equals(tag.ItemType, "SubItem", StringComparison.OrdinalIgnoreCase))
            {
                portName = tag.PortName;
                return !string.IsNullOrWhiteSpace(portName);
            }

            if (string.Equals(tag.ItemType, "ComPort", StringComparison.OrdinalIgnoreCase))
            {
                portName = tag.PortName ?? tag.Name;
                return !string.IsNullOrWhiteSpace(portName);
            }

            return false;
        }

        private ContextMenu CreateFileItemContextMenu(string portName)
        {
            ContextMenu contextMenu = new ContextMenu();
            MenuItem closeItem = new MenuItem { Header = "Close" };
            closeItem.Click += (s, e) => CloseFileLogger(portName);
            contextMenu.Items.Add(closeItem);
            return contextMenu;
        }

        private void CloseFileLogger(string portName)
        {
            if (string.IsNullOrWhiteSpace(portName) || !filePortNames.Contains(portName))
            {
                return;
            }

            filePortNames.Remove(portName);
            measurementSeriesByPort.Remove(portName);
            DataLoggerManager.RemoveLogger(portName);
            TreeViewManager.RemoveTreeViewItem(portName);

            if (string.Equals(currentPortName, portName, StringComparison.OrdinalIgnoreCase))
            {
                currentPortName = null;
                currentSeriesIndex = -1;
                ClearPlotState();
                ClearDeviceInfo();
            }
        }

        private void ClearDeviceInfo()
        {
            textBoxModel.Text = "-";
            textBoxSerialNumber.Text = "-";
            textBoxProductionDate.Text = "-";
            textBoxChecksum.Text = "-";
        }

        private void ClearPlotState()
        {
            SetPlotVisibility(false);
            WpfPlot1.Plot.Clear();
            WpfPlot1.Plot.Axes.Remove(ScottPlot.Edge.Right);
            pltAlt = null;
            pltTemp = null;
            pltAcc = null;
            pltAccX = null;
            pltAccY = null;
            pltAccZ = null;
            yAxisTemp = null;
            yAxisAcc = null;

            measuringSpan = null;
            lastMeasuringX1 = null;
            lastMeasuringX2 = null;
            crosshairX = null;
            crosshairAlt = null;
            crosshairTemp = null;
            crosshairAcc = null;
            crosshairAltLabel = null;
            crosshairXLabel = null;
            marker = null;
            hoverMarker = null;
            hoverLabel = null;
            hoverSeries = null;
            hoverX = null;
            hoverY = null;

            ClearMeasuringTextBoxes();
            ClearCrosshairTextBoxes();
            UpdateAxisLabelText();
            WpfPlot1.Refresh();
        }

        private void SetPlotVisibility(bool hasData)
        {
            WpfPlot1.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetAxisFontSizes(ScottPlot.IAxis axis, float labelSize, float tickSize)
        {
            if (axis == null)
            {
                return;
            }

            axis.Label.FontSize = labelSize;
            axis.TickLabelStyle.FontSize = tickSize;
        }

        private void ApplyPlotFontSizes()
        {
            if (WpfPlot1?.Plot?.Axes == null)
            {
                return;
            }

            if (WpfPlot1.Plot.Axes.Title != null)
            {
                WpfPlot1.Plot.Axes.Title.Label.FontSize = PlotTitleFontSize;
            }

            SetAxisFontSizes(WpfPlot1.Plot.Axes.Bottom, AxisLabelFontSize, AxisTickFontSize);
            SetAxisFontSizes(WpfPlot1.Plot.Axes.Left, AxisLabelFontSize, AxisTickFontSize);
            SetAxisFontSizes(yAxisTemp, AxisLabelFontSize, AxisTickFontSize);
            SetAxisFontSizes(yAxisAcc, AxisLabelFontSize, AxisTickFontSize);

            if (WpfPlot1.Plot.Legend != null)
            {
                WpfPlot1.Plot.Legend.FontSize = LegendFontSize;
            }

            if (hoverLabel != null)
            {
                hoverLabel.LabelFontSize = HoverTooltipFontSize;
            }
        }

        private void SetChartStylePreset(ChartStylePreset preset)
        {
            currentChartStyle = preset;
            switch (preset)
            {
                case ChartStylePreset.Dark:
                    figureBackgroundColor = ScottPlot.Color.FromHex("#1E1E1E");
                    dataBackgroundColor = ScottPlot.Color.FromHex("#1E1E1E");
                    axisColor = ScottPlot.Color.FromHex("#E6E6E6");
                    gridMajorLineColor = ScottPlot.Color.FromHex("#3A3A3A");
                    legendBackgroundColor = ScottPlot.Color.FromHex("#2A2A2A");
                    legendFontColor = ScottPlot.Color.FromHex("#E6E6E6");
                    legendOutlineColor = ScottPlot.Color.FromHex("#4D4D4D");
                    break;
                case ChartStylePreset.Slate:
                    figureBackgroundColor = ScottPlot.Color.FromHex("#F2F4F7");
                    dataBackgroundColor = ScottPlot.Color.FromHex("#F7F9FC");
                    axisColor = ScottPlot.Color.FromHex("#2B3645");
                    gridMajorLineColor = ScottPlot.Color.FromHex("#D3DAE4");
                    legendBackgroundColor = ScottPlot.Color.FromHex("#FFFFFF");
                    legendFontColor = ScottPlot.Color.FromHex("#2B3645");
                    legendOutlineColor = ScottPlot.Color.FromHex("#C5CFDB");
                    break;
                default:
                    figureBackgroundColor = ScottPlot.Color.FromHex("#FFFFFF");
                    dataBackgroundColor = ScottPlot.Color.FromHex("#FFFFFF");
                    axisColor = ScottPlot.Color.FromHex("#2E2E2E");
                    gridMajorLineColor = ScottPlot.Color.FromHex("#E0E0E0");
                    legendBackgroundColor = ScottPlot.Color.FromHex("#FFFFFF");
                    legendFontColor = ScottPlot.Color.FromHex("#2E2E2E");
                    legendOutlineColor = ScottPlot.Color.FromHex("#C8C8C8");
                    break;
            }
        }

        private bool IsSmoothingActive()
        {
            return smoothingAltSeconds > 0 ||
                smoothingTempSeconds > 0 ||
                smoothingAccAbsSeconds > 0 ||
                smoothingAccXSeconds > 0 ||
                smoothingAccYSeconds > 0 ||
                smoothingAccZSeconds > 0;
        }

        private void PushBusyCursor()
        {
            if (busyCursorDepth == 0)
            {
                previousOverrideCursor = Mouse.OverrideCursor;
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            }

            busyCursorDepth++;
        }

        private void PopBusyCursor()
        {
            if (busyCursorDepth <= 0)
            {
                return;
            }

            busyCursorDepth--;
            if (busyCursorDepth == 0)
            {
                Mouse.OverrideCursor = previousOverrideCursor;
                previousOverrideCursor = null;
            }
        }

        private void ApplyChartStyle()
        {
            if (WpfPlot1?.Plot == null)
            {
                return;
            }

            ScottPlot.PlotStyle style = new ScottPlot.PlotStyle
            {
                Palette = ScottPlot.Palette.Default,
                FigureBackgroundColor = figureBackgroundColor,
                DataBackgroundColor = dataBackgroundColor,
                AxisColor = axisColor,
                GridMajorLineColor = gridMajorLineColor,
                LegendBackgroundColor = legendBackgroundColor,
                LegendFontColor = legendFontColor,
                LegendOutlineColor = legendOutlineColor
            };

            WpfPlot1.Plot.SetStyle(style);
            ApplyGridVisibility();
            ApplySeriesAxisColors();
            ApplyPlotFontSizes();
            UpdateAxisLabelText();
            WpfPlot1.Refresh();
        }

        private void ApplyGridVisibility()
        {
            if (WpfPlot1?.Plot == null)
            {
                return;
            }

            if (showGrid)
            {
                WpfPlot1.Plot.ShowGrid();
            }
            else
            {
                WpfPlot1.Plot.HideGrid();
            }
        }

        private void ApplySeriesColors(bool refresh = true)
        {
            if (pltAlt != null)
            {
                pltAlt.Color = altitudeColor;
            }

            if (pltTemp != null)
            {
                pltTemp.Color = temperatureColor;
            }

            if (pltAcc != null)
            {
                pltAcc.Color = accAbsColor;
            }

            if (pltAccX != null)
            {
                pltAccX.Color = accXColor;
            }

            if (pltAccY != null)
            {
                pltAccY.Color = accYColor;
            }

            if (pltAccZ != null)
            {
                pltAccZ.Color = accZColor;
            }

            if (axisLabelColorsFollowSeries)
            {
                SyncAxisLabelColorsToSeries();
            }

            ApplySeriesAxisColors();
            if (refresh)
            {
                WpfPlot1.Refresh();
            }
        }

        private void ApplySeriesLineStyles(bool refresh = true)
        {
            if (pltAlt != null)
            {
                pltAlt.LineWidth = altitudeLineWidth;
                pltAlt.LinePattern = altitudeLinePattern;
            }

            if (pltTemp != null)
            {
                pltTemp.LineWidth = temperatureLineWidth;
                pltTemp.LinePattern = temperatureLinePattern;
            }

            if (pltAcc != null)
            {
                pltAcc.LineWidth = accAbsLineWidth;
                pltAcc.LinePattern = accAbsLinePattern;
            }

            if (pltAccX != null)
            {
                pltAccX.LineWidth = accXLineWidth;
                pltAccX.LinePattern = accXLinePattern;
            }

            if (pltAccY != null)
            {
                pltAccY.LineWidth = accYLineWidth;
                pltAccY.LinePattern = accYLinePattern;
            }

            if (pltAccZ != null)
            {
                pltAccZ.LineWidth = accZLineWidth;
                pltAccZ.LinePattern = accZLinePattern;
            }

            if (refresh)
            {
                WpfPlot1.Refresh();
            }
        }

        private void RefreshPlotNow()
        {
            WpfPlot1.Refresh();
            WpfPlot1.InvalidateVisual();
            Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        }

        private void ResetSeriesColors()
        {
            altitudeColor = ScottPlot.Color.FromHex("#1B1B1B");
            temperatureColor = ScottPlot.Color.FromHex("#D32F2F");
            accAbsColor = ScottPlot.Color.FromHex("#CC79A7");
            accXColor = ScottPlot.Color.FromHex("#0072B2");
            accYColor = ScottPlot.Color.FromHex("#009E73");
            accZColor = ScottPlot.Color.FromHex("#E69F00");

            if (axisLabelColorsFollowSeries)
            {
                SyncAxisLabelColorsToSeries();
            }
        }

        private void ResetSeriesLineStyles()
        {
            altitudeLineWidth = 2.2f;
            temperatureLineWidth = 1.0f;
            accAbsLineWidth = 1.0f;
            accXLineWidth = 1.0f;
            accYLineWidth = 1.0f;
            accZLineWidth = 1.0f;

            altitudeLinePattern = ScottPlot.LinePattern.Solid;
            temperatureLinePattern = ScottPlot.LinePattern.Solid;
            accAbsLinePattern = ScottPlot.LinePattern.Solid;
            accXLinePattern = ScottPlot.LinePattern.Solid;
            accYLinePattern = ScottPlot.LinePattern.Solid;
            accZLinePattern = ScottPlot.LinePattern.Solid;
        }

        private void SyncAxisLabelColorsToSeries()
        {
            axisLabelAltColor = altitudeColor;
            axisLabelTempColor = temperatureColor;
            axisLabelAccColor = accAbsColor;
        }

        private bool TryPickColor(ScottPlot.Color current, out ScottPlot.Color selected)
        {
            selected = current;
            using (var dialog = new WinForms.ColorDialog())
            {
                dialog.AnyColor = true;
                dialog.FullOpen = true;
                dialog.SolidColorOnly = true;
                dialog.Color = System.Drawing.Color.FromArgb(current.A, current.R, current.G, current.B);

                if (dialog.ShowDialog() != WinForms.DialogResult.OK)
                {
                    return false;
                }

                selected = ScottPlot.Color.FromARGB(dialog.Color.ToArgb());
                return true;
            }
        }

        private bool TryPickLineWidth(float current, out float selected)
        {
            selected = current;
            using (var form = new WinForms.Form())
            using (var widthInput = new WinForms.NumericUpDown())
            using (var okButton = new WinForms.Button())
            using (var cancelButton = new WinForms.Button())
            using (var label = new WinForms.Label())
            {
                form.Text = "Line Width";
                form.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
                form.StartPosition = WinForms.FormStartPosition.CenterScreen;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new System.Drawing.Size(220, 120);

                label.Text = "Width (px)";
                label.AutoSize = true;
                label.Location = new System.Drawing.Point(12, 15);

                widthInput.Minimum = 0.1M;
                widthInput.Maximum = 10M;
                widthInput.DecimalPlaces = 2;
                widthInput.Increment = 0.1M;
                widthInput.Value = (decimal)current;
                widthInput.Location = new System.Drawing.Point(12, 40);
                widthInput.Width = 80;

                okButton.Text = "OK";
                okButton.DialogResult = WinForms.DialogResult.OK;
                okButton.Location = new System.Drawing.Point(35, 75);

                cancelButton.Text = "Cancel";
                cancelButton.DialogResult = WinForms.DialogResult.Cancel;
                cancelButton.Location = new System.Drawing.Point(120, 75);

                form.Controls.Add(label);
                form.Controls.Add(widthInput);
                form.Controls.Add(okButton);
                form.Controls.Add(cancelButton);
                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;

                if (form.ShowDialog() != WinForms.DialogResult.OK)
                {
                    return false;
                }

                selected = (float)widthInput.Value;
                return true;
            }
        }

        private double[] GetSmoothedSeries(double[] xs, double[] ys, double windowSeconds)
        {
            if (xs == null || ys == null || xs.Length == 0 || xs.Length != ys.Length || windowSeconds <= 0)
            {
                return ys;
            }

            double windowDays = windowSeconds / 86400.0;
            double[] smoothed = new double[ys.Length];
            int start = 0;
            double sum = 0;
            int count = 0;

            for (int i = 0; i < ys.Length; i++)
            {
                double value = ys[i];
                bool valid = !double.IsNaN(value) && !double.IsInfinity(value);
                if (valid)
                {
                    sum += value;
                    count++;
                }

                while (start < i && (xs[i] - xs[start]) > windowDays)
                {
                    double startValue = ys[start];
                    if (!double.IsNaN(startValue) && !double.IsInfinity(startValue))
                    {
                        sum -= startValue;
                        count--;
                    }
                    start++;
                }

                smoothed[i] = count > 0 ? (sum / count) : double.NaN;
            }

            return smoothed;
        }


        private ScottPlot.Color GetColorForTag(string tag)
        {
            switch (tag)
            {
                case "FigureBackground":
                    return figureBackgroundColor;
                case "DataBackground":
                    return dataBackgroundColor;
                case "GridMajor":
                    return gridMajorLineColor;
                case "AxisColor":
                    return axisColor;
                case "LegendBackground":
                    return legendBackgroundColor;
                case "LegendText":
                    return legendFontColor;
                case "LegendOutline":
                    return legendOutlineColor;
                case "SeriesAlt":
                    return altitudeColor;
                case "SeriesTemp":
                    return temperatureColor;
                case "SeriesAccAbs":
                    return accAbsColor;
                case "SeriesAccX":
                    return accXColor;
                case "SeriesAccY":
                    return accYColor;
                case "SeriesAccZ":
                    return accZColor;
                case "AxisLabelAlt":
                    return axisLabelAltColor;
                case "AxisLabelTemp":
                    return axisLabelTempColor;
                case "AxisLabelAcc":
                    return axisLabelAccColor;
                default:
                    return ScottPlot.Color.FromHex("#000000");
            }
        }

        private void ApplyColorByTag(string tag, ScottPlot.Color color)
        {
            switch (tag)
            {
                case "FigureBackground":
                    figureBackgroundColor = color;
                    ApplyChartStyle();
                    break;
                case "DataBackground":
                    dataBackgroundColor = color;
                    ApplyChartStyle();
                    break;
                case "GridMajor":
                    gridMajorLineColor = color;
                    ApplyChartStyle();
                    break;
                case "AxisColor":
                    axisColor = color;
                    ApplyChartStyle();
                    break;
                case "LegendBackground":
                    legendBackgroundColor = color;
                    ApplyChartStyle();
                    break;
                case "LegendText":
                    legendFontColor = color;
                    ApplyChartStyle();
                    break;
                case "LegendOutline":
                    legendOutlineColor = color;
                    ApplyChartStyle();
                    break;
                case "SeriesAlt":
                    altitudeColor = color;
                    ApplySeriesColors();
                    break;
                case "SeriesTemp":
                    temperatureColor = color;
                    ApplySeriesColors();
                    break;
                case "SeriesAccAbs":
                    accAbsColor = color;
                    ApplySeriesColors();
                    break;
                case "SeriesAccX":
                    accXColor = color;
                    ApplySeriesColors();
                    break;
                case "SeriesAccY":
                    accYColor = color;
                    ApplySeriesColors();
                    break;
                case "SeriesAccZ":
                    accZColor = color;
                    ApplySeriesColors();
                    break;
                case "AxisLabelAlt":
                    axisLabelAltColor = color;
                    ApplySeriesAxisColors();
                    WpfPlot1.Refresh();
                    break;
                case "AxisLabelTemp":
                    axisLabelTempColor = color;
                    ApplySeriesAxisColors();
                    WpfPlot1.Refresh();
                    break;
                case "AxisLabelAcc":
                    axisLabelAccColor = color;
                    ApplySeriesAxisColors();
                    WpfPlot1.Refresh();
                    break;
            }
        }

        private float GetLineWidthForTag(string tag)
        {
            switch (tag)
            {
                case "LineWidthAlt":
                    return altitudeLineWidth;
                case "LineWidthTemp":
                    return temperatureLineWidth;
                case "LineWidthAccAbs":
                    return accAbsLineWidth;
                case "LineWidthAccX":
                    return accXLineWidth;
                case "LineWidthAccY":
                    return accYLineWidth;
                case "LineWidthAccZ":
                    return accZLineWidth;
                default:
                    return 1.0f;
            }
        }

        private void ApplyLineWidthByTag(string tag, float width)
        {
            switch (tag)
            {
                case "LineWidthAlt":
                    altitudeLineWidth = width;
                    break;
                case "LineWidthTemp":
                    temperatureLineWidth = width;
                    break;
                case "LineWidthAccAbs":
                    accAbsLineWidth = width;
                    break;
                case "LineWidthAccX":
                    accXLineWidth = width;
                    break;
                case "LineWidthAccY":
                    accYLineWidth = width;
                    break;
                case "LineWidthAccZ":
                    accZLineWidth = width;
                    break;
                default:
                    return;
            }

            ApplySeriesLineStyles();
        }

        private bool TryGetLinePattern(string name, out ScottPlot.LinePattern pattern)
        {
            switch (name)
            {
                case "Solid":
                    pattern = ScottPlot.LinePattern.Solid;
                    return true;
                case "Dashed":
                    pattern = ScottPlot.LinePattern.Dashed;
                    return true;
                case "DenselyDashed":
                    pattern = ScottPlot.LinePattern.DenselyDashed;
                    return true;
                case "Dotted":
                    pattern = ScottPlot.LinePattern.Dotted;
                    return true;
                default:
                    pattern = ScottPlot.LinePattern.Solid;
                    return false;
            }
        }

        private void ApplyLinePatternByTag(string tag, ScottPlot.LinePattern pattern)
        {
            switch (tag)
            {
                case "LinePatternAlt":
                    altitudeLinePattern = pattern;
                    break;
                case "LinePatternTemp":
                    temperatureLinePattern = pattern;
                    break;
                case "LinePatternAccAbs":
                    accAbsLinePattern = pattern;
                    break;
                case "LinePatternAccX":
                    accXLinePattern = pattern;
                    break;
                case "LinePatternAccY":
                    accYLinePattern = pattern;
                    break;
                case "LinePatternAccZ":
                    accZLinePattern = pattern;
                    break;
                default:
                    return;
            }

            ApplySeriesLineStyles();
        }

        private void ApplySeriesAxisColors()
        {
            if (WpfPlot1?.Plot?.Axes == null)
            {
                return;
            }

            ScottPlot.Color altAxisColor = axisLabelColorsFollowSeries ? altitudeColor : axisLabelAltColor;
            ScottPlot.Color tempAxisColor = axisLabelColorsFollowSeries ? temperatureColor : axisLabelTempColor;
            ScottPlot.Color accAxisColor = axisLabelColorsFollowSeries ? accAbsColor : axisLabelAccColor;

            if (WpfPlot1.Plot.Axes.Left is ScottPlot.AxisPanels.LeftAxis leftAxis)
            {
                leftAxis.LabelFontColor = altAxisColor;
            }

            if (yAxisTemp != null)
            {
                yAxisTemp.LabelFontColor = tempAxisColor;
            }

            if (yAxisAcc != null)
            {
                yAxisAcc.LabelFontColor = accAxisColor;
            }
        }

        private void ApplySeriesVisibility()
        {
            if (pltAlt != null) pltAlt.IsVisible = showAltitude;
            if (pltTemp != null) pltTemp.IsVisible = showTemperature;
            if (pltAcc != null) pltAcc.IsVisible = showAccAbs;
            if (pltAccX != null) pltAccX.IsVisible = showAccX;
            if (pltAccY != null) pltAccY.IsVisible = showAccY;
            if (pltAccZ != null) pltAccZ.IsVisible = showAccZ;

            if (WpfPlot1?.Plot?.Legend != null)
            {
                WpfPlot1.Plot.Legend.ShowItemsFromHiddenPlottables = false;
            }
        }

        //################################################################################################################################
        //                                                   FUNKTIONEN
        //################################################################################################################################

        private void ResetPlotTools()
        {
            if (toggleButtonMeasuringCursor != null) toggleButtonMeasuringCursor.IsChecked = false;
            if (toggleButtonCrosshair != null) toggleButtonCrosshair.IsChecked = false;
            if (toggleButtonMarker != null) toggleButtonMarker.IsChecked = false;
            if (toggleButtonLegend != null) toggleButtonLegend.IsChecked = false;
        }

        // Daten plotten
        private void PlotData(List<Messreihe> _messreihe, int index)
        {
            if (_messreihe == null || _messreihe.Count == 0 || index < 0 || index >= _messreihe.Count)
            {
                SetPlotVisibility(false);
                MessageBox.Show("No data to plot.");
                return;
            }

            ResetPlotTools();

            SetPlotVisibility(true);
            WpfPlot1.Plot.Clear();
            WpfPlot1.Plot.Axes.Remove(ScottPlot.Edge.Right);
            yAxisTemp = null;
            yAxisAcc = null;
            hoverMarker = null;
            hoverLabel = null;
            hoverSeries = null;
            hoverX = null;
            hoverY = null;
            yAxisTemp = WpfPlot1.Plot.Axes.AddRightAxis();
            yAxisAcc = WpfPlot1.Plot.Axes.AddRightAxis();
            ApplyChartStyle();

            bool showBusy = IsSmoothingActive();
            if (showBusy)
            {
                PushBusyCursor();
            }

            try
            {
                //Plot Titel festlegen
                WpfPlot1.Plot.Title(BuildPlotTitle(currentPortName, _messreihe[index]));

                List<DateTime> timeList = new List<DateTime>();
                List<double> pressureList = new List<double>();
                List<double> heightList = new List<double>();
                List<double> accXList = new List<double>();
                List<double> accYList = new List<double>();
                List<double> accZList = new List<double>();
                List<double> tempList = new List<double>();

                foreach (Messdaten messdaten in _messreihe[index].Messungen)
                {
                    timeList.Add(messdaten.Zeit);
                    pressureList.Add(messdaten.Druck);
                    heightList.Add(messdaten.Hoehe);
                    accXList.Add(messdaten.BeschleunigungX);
                    accYList.Add(messdaten.BeschleunigungY);
                    accZList.Add(messdaten.BeschleunigungZ);
                    tempList.Add(messdaten.Temperatur);
                }

                // Konvertieren der Liste in ein Array
                DateTime[] dateTimeArray = timeList.ToArray();
                double[] timeArray = new double[timeList.Count];

                for (int i = 0; i < dateTimeArray.Length; i++)
                {
                    timeArray[i] = dateTimeArray[i].ToOADate();
                }

                double[] pressureArray = pressureList.ToArray();
                double[] heightArray = heightList.ToArray();
                double[] accXArray = accXList.ToArray();
                double[] accYArray = accYList.ToArray();
                double[] accZArray = accZList.ToArray();
                double[] tempArray = tempList.ToArray();
                double[] accAbsArray = new double[accXArray.Length];

                for (int i = 0; i < accAbsArray.Length; i++)
                {
                    accAbsArray[i] = Math.Sqrt((accXArray[i] * accXArray[i]) + (accYArray[i] * accYArray[i]) + (accZArray[i] * accZArray[i]));
                }

                // Schreiben der Werte in den public main Bereich um die Arrays au?erhalb dieser Methode verf?gbar zu machen (z.B. f?r Cursor Berechnungen)
                yhRaw = heightArray;
                ytRaw = tempArray;
                ApplyUnitsToDisplayData();

                xh = timeArray;
                xa = timeArray;
                xt = timeArray;
                ya = accAbsArray;
                xax = timeArray;
                xay = timeArray;
                xaz = timeArray;
                yax = accXArray;
                yay = accYArray;
                yaz = accZArray;

                //Aktiviere DateTime Format f?r die X-Achse
                WpfPlot1.Plot.Axes.DateTimeTicksBottom();
                isDateTimeXAxis = true;

                //Daten f?r H?he zum Plot WpfPlot1 (in XAML definiert) hinzuf?gen
                double[] yhPlot = GetSmoothedSeries(timeArray, yh, smoothingAltSeconds);
                double[] ytPlot = GetSmoothedSeries(timeArray, yt, smoothingTempSeconds);
                double[] yaPlot = GetSmoothedSeries(timeArray, ya, smoothingAccAbsSeconds);
                double[] yaxPlot = GetSmoothedSeries(xax, yax, smoothingAccXSeconds);
                double[] yayPlot = GetSmoothedSeries(xay, yay, smoothingAccYSeconds);
                double[] yazPlot = GetSmoothedSeries(xaz, yaz, smoothingAccZSeconds);

                pltAlt = AddAltitudeScatter(timeArray, yhPlot);

                //Daten f?r Temperatur zum Plot WpfPlot1 (in XAML definiert) hinzuf?gen
                pltTemp = AddTemperatureScatter(timeArray, ytPlot);

                //Daten f?r Beschleunigung zum Plot WpfPlot1 (in XAML definiert) hinzuf?gen
                pltAcc = AddAccelerationScatter(timeArray, yaPlot, FormatLegendText("Acc Abs", smoothingAccAbsSeconds), accAbsColor, true);
                pltAccX = AddAccelerationScatter(xax, yaxPlot, FormatLegendText("Acc X", smoothingAccXSeconds), accXColor, false);
                pltAccY = AddAccelerationScatter(xay, yayPlot, FormatLegendText("Acc Y", smoothingAccYSeconds), accYColor, false);
                pltAccZ = AddAccelerationScatter(xaz, yazPlot, FormatLegendText("Acc Z", smoothingAccZSeconds), accZColor, false);

                ApplySeriesColors(false);
                ApplySeriesLineStyles(false);
                ApplySeriesVisibility();
                WpfPlot1.Plot.Axes.AutoScale();

                UpdateAxisLabelText();
                ApplyPlotFontSizes();

                WpfPlot1.Plot.Legend.IsVisible = false;

                RefreshAxisTextBoxes();
                WpfPlot1.Refresh();
            }
            finally
            {
                if (showBusy)
                {
                    PopBusyCursor();
                }
            }
        }

        private string BuildPlotTitle(string portName, Messreihe series)
        {
            string title = null;
            string model = null;
            string serialNumber = null;

            if (!string.IsNullOrWhiteSpace(portName))
            {
                DataLogger logger = DataLoggerManager.GetLogger(portName);
                if (logger != null)
                {
                    model = logger.id;
                    serialNumber = logger.serialNumber;
                }
            }

            if (!string.IsNullOrWhiteSpace(model) && !string.IsNullOrWhiteSpace(serialNumber))
            {
                title = $"{model} No. {serialNumber}";
            }
            else if (!string.IsNullOrWhiteSpace(model))
            {
                title = model;
            }
            else if (!string.IsNullOrWhiteSpace(serialNumber))
            {
                title = $"Serial {serialNumber}";
            }

            if (series != null)
            {
                string startText = series.Startzeit.ToString();
                if (!string.IsNullOrWhiteSpace(startText))
                {
                    title = string.IsNullOrWhiteSpace(title) ? startText : $"{title} - {startText}";
                }
            }

            return string.IsNullOrWhiteSpace(title) ? "DataViewer" : title;
        }

        private Scatter AddAltitudeScatter(double[] xs, double[] ys)
        {
            Scatter scatter = WpfPlot1.Plot.Add.Scatter(xs, ys);
            scatter.LegendText = FormatLegendText("Altitude", smoothingAltSeconds);
            scatter.Axes = new ScottPlot.Axes { XAxis = WpfPlot1.Plot.Axes.Bottom, YAxis = WpfPlot1.Plot.Axes.Left };
            scatter.MarkerSize = 0;
            scatter.Color = altitudeColor;

            return scatter;
        }

        private Scatter AddTemperatureScatter(double[] xs, double[] ys)
        {
            if (yAxisTemp == null)
            {
                yAxisTemp = WpfPlot1.Plot.Axes.AddRightAxis();
            }

            Scatter scatter = WpfPlot1.Plot.Add.Scatter(xs, ys);
            scatter.LegendText = FormatLegendText("Temperature", smoothingTempSeconds);
            scatter.Axes = new ScottPlot.Axes { XAxis = WpfPlot1.Plot.Axes.Bottom, YAxis = yAxisTemp };
            scatter.MarkerSize = 0;
            scatter.Color = temperatureColor;
            return scatter;
        }

        private Scatter AddAccelerationScatter(double[] xs, double[] ys, string legendText, ScottPlot.Color color, bool setAxisColor)
        {
            if (yAxisAcc == null)
            {
                yAxisAcc = WpfPlot1.Plot.Axes.AddRightAxis();
            }

            Scatter scatter = WpfPlot1.Plot.Add.Scatter(xs, ys);
            scatter.LegendText = legendText;
            scatter.Axes = new ScottPlot.Axes { XAxis = WpfPlot1.Plot.Axes.Bottom, YAxis = yAxisAcc };
            scatter.MarkerSize = 0;
            scatter.Color = color;
            return scatter;
        }

        private void UpdateAxisLabelText()
        {
            WpfPlot1.Plot.YLabel($"Altitude [{GetAltitudeUnitLabel()}]");

            if (yAxisTemp != null)
            {
                yAxisTemp.LabelText = $"Temperature [{GetTemperatureUnitLabel()}]";
            }

            if (yAxisAcc != null)
            {
                yAxisAcc.LabelText = "Acceleration [g]";
            }
        }

        private void RebuildScatterPlots()
        {
            if (pltAlt != null)
            {
                WpfPlot1.Plot.Remove(pltAlt);
                pltAlt = null;
            }

            if (pltTemp != null)
            {
                WpfPlot1.Plot.Remove(pltTemp);
                pltTemp = null;
            }

            if (pltAcc != null)
            {
                WpfPlot1.Plot.Remove(pltAcc);
                pltAcc = null;
            }

            if (pltAccX != null)
            {
                WpfPlot1.Plot.Remove(pltAccX);
                pltAccX = null;
            }

            if (pltAccY != null)
            {
                WpfPlot1.Plot.Remove(pltAccY);
                pltAccY = null;
            }

            if (pltAccZ != null)
            {
                WpfPlot1.Plot.Remove(pltAccZ);
                pltAccZ = null;
            }

            if (xh == null || yh == null || yt == null || ya == null)
            {
                return;
            }

            bool showBusy = IsSmoothingActive();
            if (showBusy)
            {
                PushBusyCursor();
            }

            try
            {
                double[] yhPlot = GetSmoothedSeries(xh, yh, smoothingAltSeconds);
                double[] ytPlot = GetSmoothedSeries(xh, yt, smoothingTempSeconds);
                double[] yaPlot = GetSmoothedSeries(xh, ya, smoothingAccAbsSeconds);
                double[] yaxPlot = GetSmoothedSeries(xax, yax, smoothingAccXSeconds);
                double[] yayPlot = GetSmoothedSeries(xay, yay, smoothingAccYSeconds);
                double[] yazPlot = GetSmoothedSeries(xaz, yaz, smoothingAccZSeconds);

                pltAlt = AddAltitudeScatter(xh, yhPlot);
                pltTemp = AddTemperatureScatter(xh, ytPlot);
                pltAcc = AddAccelerationScatter(xh, yaPlot, FormatLegendText("Acc Abs", smoothingAccAbsSeconds), accAbsColor, true);
                if (xax != null && yax != null)
                {
                    pltAccX = AddAccelerationScatter(xax, yaxPlot, FormatLegendText("Acc X", smoothingAccXSeconds), accXColor, false);
                }
                if (xay != null && yay != null)
                {
                    pltAccY = AddAccelerationScatter(xay, yayPlot, FormatLegendText("Acc Y", smoothingAccYSeconds), accYColor, false);
                }
                if (xaz != null && yaz != null)
                {
                    pltAccZ = AddAccelerationScatter(xaz, yazPlot, FormatLegendText("Acc Z", smoothingAccZSeconds), accZColor, false);
                }
                ApplySeriesColors(false);
                ApplySeriesLineStyles(false);
                ApplySeriesVisibility();
                UpdateAxisLabelText();
                ApplyPlotFontSizes();
            }
            finally
            {
                if (showBusy)
                {
                    PopBusyCursor();
                }
            }
        }

        private void UpdateUnitTextBlocks()
        {
            string altUnit = GetAltitudeUnitLabel();
            string tempUnit = GetTemperatureUnitLabel();
            string speedUnit = GetAltitudeSpeedUnitLabel();

            if (textBlockAltAxisMaxUnit != null) textBlockAltAxisMaxUnit.Text = altUnit;
            if (textBlockAltAxisMinUnit != null) textBlockAltAxisMinUnit.Text = altUnit;
            if (textBlockTempAxisMaxUnit != null) textBlockTempAxisMaxUnit.Text = tempUnit;
            if (textBlockTempAxisMinUnit != null) textBlockTempAxisMinUnit.Text = tempUnit;

            if (textBlockMeasAltCursor1Unit != null) textBlockMeasAltCursor1Unit.Text = altUnit;
            if (textBlockMeasAltCursor2Unit != null) textBlockMeasAltCursor2Unit.Text = altUnit;
            if (textBlockMeasAltMinUnit != null) textBlockMeasAltMinUnit.Text = altUnit;
            if (textBlockMeasAltMaxUnit != null) textBlockMeasAltMaxUnit.Text = altUnit;
            if (textBlockMeasAltDeltaUnit != null) textBlockMeasAltDeltaUnit.Text = altUnit;
            if (textBlockMeasAltAverageUnit != null) textBlockMeasAltAverageUnit.Text = altUnit;
            if (textBlockMeasAltSpeedUnit != null) textBlockMeasAltSpeedUnit.Text = speedUnit;

            if (textBlockMeasTempCursor1Unit != null) textBlockMeasTempCursor1Unit.Text = tempUnit;
            if (textBlockMeasTempCursor2Unit != null) textBlockMeasTempCursor2Unit.Text = tempUnit;
            if (textBlockMeasTempMinUnit != null) textBlockMeasTempMinUnit.Text = tempUnit;
            if (textBlockMeasTempMaxUnit != null) textBlockMeasTempMaxUnit.Text = tempUnit;
            if (textBlockMeasTempDeltaUnit != null) textBlockMeasTempDeltaUnit.Text = tempUnit;
            if (textBlockMeasTempAverageUnit != null) textBlockMeasTempAverageUnit.Text = tempUnit;
        }

        private void UpdateSmoothingTextBoxes()
        {
            if (textBoxSmoothAlt != null) textBoxSmoothAlt.Text = FormatSmoothingSeconds(smoothingAltSeconds);
            if (textBoxSmoothTemp != null) textBoxSmoothTemp.Text = FormatSmoothingSeconds(smoothingTempSeconds);
            if (textBoxSmoothAccAbs != null) textBoxSmoothAccAbs.Text = FormatSmoothingSeconds(smoothingAccAbsSeconds);
            if (textBoxSmoothAccX != null) textBoxSmoothAccX.Text = FormatSmoothingSeconds(smoothingAccXSeconds);
            if (textBoxSmoothAccY != null) textBoxSmoothAccY.Text = FormatSmoothingSeconds(smoothingAccYSeconds);
            if (textBoxSmoothAccZ != null) textBoxSmoothAccZ.Text = FormatSmoothingSeconds(smoothingAccZSeconds);
        }

        private string FormatSmoothingSeconds(double seconds)
        {
            if (seconds <= 0)
            {
                return "0";
            }

            double rounded = Math.Round(seconds, 1);
            bool isWhole = Math.Abs(rounded - Math.Round(rounded)) < 0.0001;
            return rounded.ToString(isWhole ? "0" : "0.#", CultureInfo.CurrentCulture);
        }

        private string FormatLegendText(string baseName, double smoothingSeconds)
        {
            if (smoothingSeconds <= 0)
            {
                return baseName;
            }

            return $"{baseName} (AVG {FormatSmoothingSeconds(smoothingSeconds)}s)";
        }

        private bool TryGetSmoothingTarget(
            string tag,
            out double[] xs,
            out double[] ys,
            out Scatter scatter,
            out string legendBase,
            out double seconds)
        {
            xs = null;
            ys = null;
            scatter = null;
            legendBase = null;
            seconds = 0;

            switch (tag)
            {
                case "Alt":
                    xs = xh;
                    ys = yh;
                    scatter = pltAlt;
                    legendBase = "Altitude";
                    seconds = smoothingAltSeconds;
                    return true;
                case "Temp":
                    xs = xt;
                    ys = yt;
                    scatter = pltTemp;
                    legendBase = "Temperature";
                    seconds = smoothingTempSeconds;
                    return true;
                case "AccAbs":
                    xs = xa;
                    ys = ya;
                    scatter = pltAcc;
                    legendBase = "Acc Abs";
                    seconds = smoothingAccAbsSeconds;
                    return true;
                case "AccX":
                    xs = xax;
                    ys = yax;
                    scatter = pltAccX;
                    legendBase = "Acc X";
                    seconds = smoothingAccXSeconds;
                    return true;
                case "AccY":
                    xs = xay;
                    ys = yay;
                    scatter = pltAccY;
                    legendBase = "Acc Y";
                    seconds = smoothingAccYSeconds;
                    return true;
                case "AccZ":
                    xs = xaz;
                    ys = yaz;
                    scatter = pltAccZ;
                    legendBase = "Acc Z";
                    seconds = smoothingAccZSeconds;
                    return true;
                default:
                    return false;
            }
        }

        private void ApplySmoothingToSeries(string tag)
        {
            if (!TryGetSmoothingTarget(tag, out double[] xs, out double[] ys, out Scatter scatter, out string legendBase, out double seconds))
            {
                return;
            }

            if (xs == null || ys == null)
            {
                return;
            }

            bool showBusy = seconds > 0;
            if (showBusy)
            {
                PushBusyCursor();
            }

            try
            {
                double[] smoothed = GetSmoothedSeries(xs, ys, seconds);
                ReplaceSmoothedScatter(tag, xs, smoothed, legendBase, seconds);
                ApplySeriesLineStyles(false);
                ApplySeriesVisibility();
                ApplySeriesAxisColors();
                RefreshPlotNow();
            }
            finally
            {
                if (showBusy)
                {
                    PopBusyCursor();
                }
            }
        }

        private void ReplaceSmoothedScatter(string tag, double[] xs, double[] ys, string legendBase, double seconds)
        {
            switch (tag)
            {
                case "Alt":
                    if (pltAlt != null) WpfPlot1.Plot.Remove(pltAlt);
                    pltAlt = AddAltitudeScatter(xs, ys);
                    break;
                case "Temp":
                    if (pltTemp != null) WpfPlot1.Plot.Remove(pltTemp);
                    pltTemp = AddTemperatureScatter(xs, ys);
                    break;
                case "AccAbs":
                    if (pltAcc != null) WpfPlot1.Plot.Remove(pltAcc);
                    pltAcc = AddAccelerationScatter(xs, ys, FormatLegendText(legendBase, seconds), accAbsColor, true);
                    break;
                case "AccX":
                    if (pltAccX != null) WpfPlot1.Plot.Remove(pltAccX);
                    pltAccX = AddAccelerationScatter(xs, ys, FormatLegendText(legendBase, seconds), accXColor, false);
                    break;
                case "AccY":
                    if (pltAccY != null) WpfPlot1.Plot.Remove(pltAccY);
                    pltAccY = AddAccelerationScatter(xs, ys, FormatLegendText(legendBase, seconds), accYColor, false);
                    break;
                case "AccZ":
                    if (pltAccZ != null) WpfPlot1.Plot.Remove(pltAccZ);
                    pltAccZ = AddAccelerationScatter(xs, ys, FormatLegendText(legendBase, seconds), accZColor, false);
                    break;
            }
        }

        private void SetSmoothingSeconds(string tag, double seconds)
        {
            seconds = ClampSmoothingSeconds(tag, seconds);
            switch (tag)
            {
                case "Alt":
                    smoothingAltSeconds = seconds;
                    break;
                case "Temp":
                    smoothingTempSeconds = seconds;
                    break;
                case "AccAbs":
                    smoothingAccAbsSeconds = seconds;
                    break;
                case "AccX":
                    smoothingAccXSeconds = seconds;
                    break;
                case "AccY":
                    smoothingAccYSeconds = seconds;
                    break;
                case "AccZ":
                    smoothingAccZSeconds = seconds;
                    break;
            }
        }

        private double ClampSmoothingSeconds(string tag, double seconds)
        {
            if (seconds < 0)
            {
                seconds = 0;
            }

            if (seconds > MaxSmoothingSeconds)
            {
                seconds = MaxSmoothingSeconds;
            }

            if (string.Equals(tag, "Temp", StringComparison.OrdinalIgnoreCase) && seconds > 0 && seconds < 60)
            {
                seconds = 60;
            }

            return seconds;
        }

        private void UpdateUnitMenuChecks()
        {
            if (menuItemUnitsMetric != null) menuItemUnitsMetric.IsChecked = currentUnitMode == UnitMode.Metric;
            if (menuItemUnitsImperial != null) menuItemUnitsImperial.IsChecked = currentUnitMode == UnitMode.Imperial;
        }

        private void ConvertAxisLimits(UnitMode from, UnitMode to)
        {
            if (WpfPlot1?.Plot == null)
            {
                return;
            }

            var altLimits = GetAxisLimits(WpfPlot1.Plot.Axes.Left);
            WpfPlot1.Plot.Axes.SetLimitsY(
                ConvertAltitude(altLimits.Bottom, from, to),
                ConvertAltitude(altLimits.Top, from, to),
                WpfPlot1.Plot.Axes.Left);

            if (yAxisTemp != null)
            {
                var tempLimits = GetAxisLimits(yAxisTemp);
                WpfPlot1.Plot.Axes.SetLimitsY(
                    ConvertTemperature(tempLimits.Bottom, from, to),
                    ConvertTemperature(tempLimits.Top, from, to),
                    yAxisTemp);
            }
        }

        private void ConvertCrosshair(UnitMode from, UnitMode to)
        {
            if (crosshairAlt != null)
            {
                crosshairAlt.Y = ConvertAltitude(crosshairAlt.Y, from, to);
            }
        }

        private void SetUnitMode(UnitMode mode)
        {
            if (currentUnitMode == mode)
            {
                UpdateUnitMenuChecks();
                UpdateUnitTextBlocks();
                UpdateAxisLabelText();
                return;
            }

            UnitMode previousMode = currentUnitMode;
            currentUnitMode = mode;

            UpdateUnitMenuChecks();
            UpdateUnitTextBlocks();
            UpdateAxisLabelText();
            ConvertAxisLimits(previousMode, currentUnitMode);
            ConvertCrosshair(previousMode, currentUnitMode);
            ApplyUnitsToDisplayData();
            RebuildScatterPlots();
            UpdateCrosshairState();
            UpdateMeasuringCursor();
            HideHover();
            RefreshAxisTextBoxes();
            RefreshCrosshairTextBoxes();
            RefreshMeasuringTextBoxes();
            WpfPlot1.Refresh();
        }

        private bool TryGetExportUnitMode(out UnitMode exportUnits)
        {
            string currentLabel = currentUnitMode == UnitMode.Metric ? "Metric (m, \u00b0C)" : "Imperial (ft, \u00b0F)";
            string otherLabel = currentUnitMode == UnitMode.Metric ? "Imperial (ft, \u00b0F)" : "Metric (m, \u00b0C)";
            MessageBoxResult result = MessageBox.Show(
                $"Export in {currentLabel}?\nYes = {currentLabel}\nNo = {otherLabel}",
                "Export Units",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);

            if (result == MessageBoxResult.Cancel)
            {
                exportUnits = currentUnitMode;
                return false;
            }

            exportUnits = result == MessageBoxResult.Yes
                ? currentUnitMode
                : (currentUnitMode == UnitMode.Metric ? UnitMode.Imperial : UnitMode.Metric);
            return true;
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unknown";
            }

            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            StringBuilder cleaned = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (invalid.Contains(c))
                {
                    continue;
                }

                cleaned.Append(char.IsWhiteSpace(c) ? '_' : c);
            }

            return cleaned.Length == 0 ? "Unknown" : cleaned.ToString();
        }

        // Timer initialisieren
        private void InitTimer()
        {
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(passiveTime);
            timer.Tick += new EventHandler(Timer_Tick);
        }

        private void InitializeDeviceMonitoring()
        {
            if (deviceRefreshTimer == null)
            {
                deviceRefreshTimer = new DispatcherTimer();
                deviceRefreshTimer.Interval = TimeSpan.FromMilliseconds(DeviceRefreshDebounceMs);
                deviceRefreshTimer.Tick += DeviceRefreshTimer_Tick;
            }

            if (deviceArrivalWatcher != null || deviceRemovalWatcher != null)
            {
                return;
            }

            try
            {
                deviceArrivalWatcher = new ManagementEventWatcher(
                    new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2"));
                deviceRemovalWatcher = new ManagementEventWatcher(
                    new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3"));
                deviceArrivalWatcher.EventArrived += DeviceChange_EventArrived;
                deviceRemovalWatcher.EventArrived += DeviceChange_EventArrived;
                deviceArrivalWatcher.Start();
                deviceRemovalWatcher.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Device monitoring disabled: " + ex.Message);
                deviceArrivalWatcher = null;
                deviceRemovalWatcher = null;
            }
        }

        private void StopDeviceMonitoring()
        {
            if (deviceRefreshTimer != null)
            {
                deviceRefreshTimer.Stop();
                deviceRefreshTimer.Tick -= DeviceRefreshTimer_Tick;
                deviceRefreshTimer = null;
            }

            if (deviceArrivalWatcher != null)
            {
                deviceArrivalWatcher.EventArrived -= DeviceChange_EventArrived;
                deviceArrivalWatcher.Stop();
                deviceArrivalWatcher.Dispose();
                deviceArrivalWatcher = null;
            }

            if (deviceRemovalWatcher != null)
            {
                deviceRemovalWatcher.EventArrived -= DeviceChange_EventArrived;
                deviceRemovalWatcher.Stop();
                deviceRemovalWatcher.Dispose();
                deviceRemovalWatcher = null;
            }
        }

        private void DeviceChange_EventArrived(object sender, EventArrivedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (deviceRefreshTimer == null)
                {
                    return;
                }

                deviceRefreshTimer.Stop();
                deviceRefreshTimer.Start();
            });
        }

        private async void DeviceRefreshTimer_Tick(object sender, EventArgs e)
        {
            deviceRefreshTimer.Stop();
            await RefreshDeviceListAsync(false);
        }

        private void ConfigurePlotInteractions()
        {
            var input = WpfPlot1.UserInputProcessor;
            input.IsEnabled = true;

            var responses = input.UserActionResponses;
            for (int i = responses.Count - 1; i >= 0; i--)
            {
                if (responses[i] is ScottPlot.Interactivity.UserActionResponses.MouseInteractWithPlottables)
                {
                    responses.RemoveAt(i);
                }
            }

            var plottableInteraction = new ScottPlot.Interactivity.UserActionResponses.MouseInteractWithPlottables(
                ScottPlot.Interactivity.StandardMouseButtons.Left);
            responses.Insert(0, plottableInteraction);

            var primaryField = typeof(ScottPlot.Interactivity.UserInputProcessor)
                .GetField("PrimaryResponse", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            primaryField?.SetValue(input, plottableInteraction);
        }

        private ScottPlot.AxisLimits GetAxisLimits(ScottPlot.IYAxis axis)
        {
            return WpfPlot1.Plot.Axes.GetLimits(WpfPlot1.Plot.Axes.Bottom, axis);
        }

        private void ShiftAxis(ScottPlot.IYAxis axis, double fraction)
        {
            var limits = GetAxisLimits(axis);
            double span = limits.Top - limits.Bottom;
            double delta = span * fraction;
            WpfPlot1.Plot.Axes.SetLimitsY(limits.Bottom + delta, limits.Top + delta, axis);
        }

        //Textboxen f?r Achsenlimits aktualisieren
        private void RefreshAxisTextBoxes()
        {
            //Textboxen f?r Achsenlimits aktualisieren
            var altLimits = GetAxisLimits(WpfPlot1.Plot.Axes.Left);
            textBoxAltMax.Text = altLimits.Top.ToString("F2");
            textBoxAltMin.Text = altLimits.Bottom.ToString("F2");

            if (yAxisTemp != null)
            {
                var tempLimits = GetAxisLimits(yAxisTemp);
                textBoxTempMax.Text = tempLimits.Top.ToString("F2");
                textBoxTempMin.Text = tempLimits.Bottom.ToString("F2");
            }

            if (yAxisAcc != null)
            {
                var accLimits = GetAxisLimits(yAxisAcc);
                textBoxAccMax.Text = accLimits.Top.ToString("F2");
                textBoxAccMin.Text = accLimits.Bottom.ToString("F2");
            }
        }

        //Textboxen f?r Crosshair aktualisieren
        private void RefreshCrosshairTextBoxes()
        {
            if (crosshairX == null)
            {
                return;
            }

            //Textboxen f?r Crosshair aktualisieren
            textBoxCrossAlt.Text = InterpolateY(xh, yh, crosshairX.X).ToString("F2");
            textBoxCrossTemp.Text = InterpolateY(xt, yt, crosshairX.X).ToString("F2");
            textBoxCrossAcc.Text = InterpolateY(xa, ya, crosshairX.X).ToString("F2");
            textBoxCrossAccX.Text = InterpolateY(xax, yax, crosshairX.X).ToString("F2");
            textBoxCrossAccY.Text = InterpolateY(xay, yay, crosshairX.X).ToString("F2");
            textBoxCrossAccZ.Text = InterpolateY(xaz, yaz, crosshairX.X).ToString("F2");
        }

        //Textboxen für Crosshair leeren
        private void ClearCrosshairTextBoxes()
        {
            textBoxCrossAlt.Text = double.NaN.ToString();
            textBoxCrossTemp.Text = double.NaN.ToString();
            textBoxCrossAcc.Text = double.NaN.ToString();
            textBoxCrossAccX.Text = double.NaN.ToString();
            textBoxCrossAccY.Text = double.NaN.ToString();
            textBoxCrossAccZ.Text = double.NaN.ToString();
        }

        private void UpdateCrosshairState()
        {
            if (crosshairX == null || crosshairAlt == null)
            {
                return;
            }

            SyncCrosshairAxes();
            RefreshCrosshairTextBoxes();
            UpdateCrosshairLabels();
        }

        private void SyncCrosshairAxes()
        {
            if (crosshairTemp == null || crosshairAcc == null || yAxisTemp == null || yAxisAcc == null)
            {
                return;
            }

            ScottPlot.Pixel pixel = WpfPlot1.Plot.GetPixel(
                new ScottPlot.Coordinates(crosshairX.X, crosshairAlt.Y),
                WpfPlot1.Plot.Axes.Bottom,
                WpfPlot1.Plot.Axes.Left);

            crosshairTemp.Y = WpfPlot1.Plot.GetCoordinates(pixel, WpfPlot1.Plot.Axes.Bottom, yAxisTemp).Y;
            crosshairAcc.Y = WpfPlot1.Plot.GetCoordinates(pixel, WpfPlot1.Plot.Axes.Bottom, yAxisAcc).Y;
        }

        private void UpdateCrosshairLabels()
        {
            if (crosshairTemp != null)
            {
                crosshairTemp.LabelText = crosshairTemp.Y.ToString("F2");
            }

            if (crosshairAcc != null)
            {
                crosshairAcc.LabelText = crosshairAcc.Y.ToString("F2");
            }

            if (crosshairXLabel != null)
            {
                crosshairXLabel.X = crosshairX.X;
                crosshairXLabel.LabelText = FormatXAxisLabel(crosshairX.X);
            }

            if (crosshairAltLabel != null)
            {
                crosshairAltLabel.Y = crosshairAlt.Y;
                crosshairAltLabel.LabelText = crosshairAlt.Y.ToString("F2");
            }
        }

        private bool IsDateTimeXAxis()
        {
            if (isDateTimeXAxis)
            {
                return true;
            }

            var tickGenerator = WpfPlot1?.Plot?.Axes?.Bottom?.TickGenerator;
            if (tickGenerator == null)
            {
                return false;
            }

            return tickGenerator.GetType().Name.IndexOf("DateTime", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string FormatXAxisLabel(double x)
        {
            if (IsDateTimeXAxis())
            {
                return DateTime.FromOADate(x).ToString("G");
            }

            return x.ToString("F2");
        }

        private string GetAltitudeUnitLabel()
        {
            return currentUnitMode == UnitMode.Metric ? "m" : "ft";
        }

        private string GetTemperatureUnitLabel()
        {
            return currentUnitMode == UnitMode.Metric ? "\u00b0C" : "\u00b0F";
        }

        private string GetAltitudeSpeedUnitLabel()
        {
            return currentUnitMode == UnitMode.Metric ? "m/sec" : "ft/sec";
        }

        private double ConvertAltitude(double value, UnitMode from, UnitMode to)
        {
            if (from == to)
            {
                return value;
            }

            return from == UnitMode.Metric
                ? value * FeetPerMeter
                : value / FeetPerMeter;
        }

        private double ConvertTemperature(double value, UnitMode from, UnitMode to)
        {
            if (from == to)
            {
                return value;
            }

            return from == UnitMode.Metric
                ? (value * CelsiusToFahrenheitScale) + 32
                : (value - 32) / CelsiusToFahrenheitScale;
        }

        private double ConvertTemperatureDelta(double value, UnitMode from, UnitMode to)
        {
            if (from == to)
            {
                return value;
            }

            return from == UnitMode.Metric
                ? value * CelsiusToFahrenheitScale
                : value / CelsiusToFahrenheitScale;
        }

        private double[] ConvertAltitudeArray(double[] values)
        {
            if (values == null)
            {
                return null;
            }

            if (currentUnitMode == UnitMode.Metric)
            {
                return values;
            }

            double[] converted = new double[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                converted[i] = values[i] * FeetPerMeter;
            }

            return converted;
        }

        private double[] ConvertTemperatureArray(double[] values)
        {
            if (values == null)
            {
                return null;
            }

            if (currentUnitMode == UnitMode.Metric)
            {
                return values;
            }

            double[] converted = new double[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                converted[i] = (values[i] * CelsiusToFahrenheitScale) + 32;
            }

            return converted;
        }

        private void ApplyUnitsToDisplayData()
        {
            yh = ConvertAltitudeArray(yhRaw);
            yt = ConvertTemperatureArray(ytRaw);
        }

        private string GetSeriesUnit(string seriesName)
        {
            switch (seriesName)
            {
                case "Altitude":
                    return GetAltitudeUnitLabel();
                case "Temperature":
                    return GetTemperatureUnitLabel();
                case "Acc Abs":
                    return "g";
                case "Acc X":
                case "Acc Y":
                case "Acc Z":
                    return "g";
                default:
                    return string.Empty;
            }
        }

        private string FormatHoverLabel(string seriesName, double x, double y)
        {
            string unit = GetSeriesUnit(seriesName);
            string yLabel = string.IsNullOrEmpty(unit)
                ? y.ToString("F2")
                : $"{y:F2} {unit}";

            return $"{seriesName}\nValue: {yLabel}\nTime: {FormatXAxisLabel(x)}";
        }

        private void UpdateHoverLabelPlacement(ScottPlot.Pixel anchorPixel)
        {
            if (hoverLabel == null)
            {
                return;
            }

            double width = WpfPlot1.ActualWidth;
            if (width <= 0)
            {
                return;
            }

            const double edgePadding = 120;
            bool nearRightEdge = anchorPixel.X > (width - edgePadding);
            hoverLabel.Alignment = nearRightEdge ? Alignment.UpperRight : Alignment.UpperLeft;
            hoverLabel.OffsetX = nearRightEdge ? -8 : 8;
        }



        //Textboxen für Messungen aktualisieren
        private void RefreshMeasuringTextBoxes()
        {
            if (measuringSpan == null || xh == null || yh == null || yt == null || ya == null)
            {
                ClearMeasuringTextBoxes();
                return;
            }

            //Textboxen für Messungen aktualisieren
            textBoxMeasAltCursor1.Text = InterpolateY(xh, yh, measuringSpan.X1).ToString("F2");
            textBoxMeasAltCursor2.Text = InterpolateY(xh, yh, measuringSpan.X2).ToString("F2");

            //Textboxen für Messungen aktualisieren
            textBoxMeasTempCursor1.Text = InterpolateY(xh, yt, measuringSpan.X1).ToString("F2");
            textBoxMeasTempCursor2.Text = InterpolateY(xh, yt, measuringSpan.X2).ToString("F2");

            //Textboxen für Messungen aktualisieren
            textBoxMeasAccCursor1.Text = InterpolateY(xh, ya, measuringSpan.X1).ToString("F2");
            textBoxMeasAccCursor2.Text = InterpolateY(xh, ya, measuringSpan.X2).ToString("F2");

            if (xax != null && yax != null)
            {
                textBoxMeasAccCursor1X.Text = InterpolateY(xax, yax, measuringSpan.X1).ToString("F2");
                textBoxMeasAccCursor2X.Text = InterpolateY(xax, yax, measuringSpan.X2).ToString("F2");
            }
            else
            {
                textBoxMeasAccCursor1X.Text = double.NaN.ToString();
                textBoxMeasAccCursor2X.Text = double.NaN.ToString();
            }

            if (xay != null && yay != null)
            {
                textBoxMeasAccCursor1Y.Text = InterpolateY(xay, yay, measuringSpan.X1).ToString("F2");
                textBoxMeasAccCursor2Y.Text = InterpolateY(xay, yay, measuringSpan.X2).ToString("F2");
            }
            else
            {
                textBoxMeasAccCursor1Y.Text = double.NaN.ToString();
                textBoxMeasAccCursor2Y.Text = double.NaN.ToString();
            }

            if (xaz != null && yaz != null)
            {
                textBoxMeasAccCursor1Z.Text = InterpolateY(xaz, yaz, measuringSpan.X1).ToString("F2");
                textBoxMeasAccCursor2Z.Text = InterpolateY(xaz, yaz, measuringSpan.X2).ToString("F2");
            }
            else
            {
                textBoxMeasAccCursor1Z.Text = double.NaN.ToString();
                textBoxMeasAccCursor2Z.Text = double.NaN.ToString();
            }

            /*
             * ################## FREISCHALTEN WENN ES SOWEIT IST #######################################################################################################################
             * 
            //Textboxen für Messungen aktualisieren
            textBoxMeasAccCursor1X.Text = InterpolateY(xax, yax, measuringSpan.X1).ToString("F2");
            textBoxMeasAccCursor2X.Text = InterpolateY(xax, yax, measuringSpan.X2).ToString("F2");

            //Textboxen für Messungen aktualisieren
            textBoxMeasAccCursor1Y.Text = InterpolateY(xay, yay, measuringSpan.X1).ToString("F2");
            textBoxMeasAccCursor2Y.Text = InterpolateY(xay, yay, measuringSpan.X2).ToString("F2");

            //Textboxen für Messungen aktualisieren
            textBoxMeasAccCursor1Z.Text = InterpolateY(xaz, yaz, measuringSpan.X1).ToString("F2");
            textBoxMeasAccCursor2Z.Text = InterpolateY(xaz, yaz, measuringSpan.X2).ToString("F2");
            */
        }

        //Textboxen für Messungen leeren
        private void ClearMeasuringTextBoxes()
        {
            textBoxMeasAltCursor1.Text = double.NaN.ToString();
            textBoxMeasAltCursor2.Text = double.NaN.ToString();
            textBoxMeasAltMin.Text = double.NaN.ToString();
            textBoxMeasAltMax.Text = double.NaN.ToString();
            textBoxMeasAltDelta.Text = double.NaN.ToString();
            textBoxMeasAltAverage.Text = double.NaN.ToString();
            textBoxMeasAltSpeed.Text = double.NaN.ToString();

            textBoxMeasTempCursor1.Text = double.NaN.ToString();
            textBoxMeasTempCursor2.Text = double.NaN.ToString();
            textBoxMeasTempMin.Text = double.NaN.ToString();
            textBoxMeasTempMax.Text = double.NaN.ToString();
            textBoxMeasTempDelta.Text = double.NaN.ToString();
            textBoxMeasTempAverage.Text = double.NaN.ToString();

            textBoxMeasAccCursor1.Text = double.NaN.ToString();
            textBoxMeasAccCursor2.Text = double.NaN.ToString();
            textBoxMeasAccMin.Text = double.NaN.ToString();
            textBoxMeasAccMax.Text = double.NaN.ToString();
            textBoxMeasAccDelta.Text = double.NaN.ToString();
            textBoxMeasAccAverage.Text = double.NaN.ToString();

            textBoxMeasAccCursor1X.Text = double.NaN.ToString();
            textBoxMeasAccCursor2X.Text = double.NaN.ToString();
            textBoxMeasAccMinX.Text = double.NaN.ToString();
            textBoxMeasAccMaxX.Text = double.NaN.ToString();
            textBoxMeasAccDeltaX.Text = double.NaN.ToString();
            textBoxMeasAccAverageX.Text = double.NaN.ToString();

            textBoxMeasAccCursor1Y.Text = double.NaN.ToString();
            textBoxMeasAccCursor2Y.Text = double.NaN.ToString();
            textBoxMeasAccMinY.Text = double.NaN.ToString();
            textBoxMeasAccMaxY.Text = double.NaN.ToString();
            textBoxMeasAccDeltaY.Text = double.NaN.ToString();
            textBoxMeasAccAverageY.Text = double.NaN.ToString();

            textBoxMeasAccCursor1Z.Text = double.NaN.ToString();
            textBoxMeasAccCursor2Z.Text = double.NaN.ToString();
            textBoxMeasAccMinZ.Text = double.NaN.ToString();
            textBoxMeasAccMaxZ.Text = double.NaN.ToString();
            textBoxMeasAccDeltaZ.Text = double.NaN.ToString();
            textBoxMeasAccAverageZ.Text = double.NaN.ToString();
        }

        private void UpdateMeasuringState()
        {
            if (measuringSpan == null)
            {
                return;
            }

            if (lastMeasuringX1.HasValue && lastMeasuringX2.HasValue &&
                measuringSpan.X1 == lastMeasuringX1.Value && measuringSpan.X2 == lastMeasuringX2.Value)
            {
                return;
            }

            lastMeasuringX1 = measuringSpan.X1;
            lastMeasuringX2 = measuringSpan.X2;

            UpdateMeasuringCursor();
        }

        private void UpdateMeasuringCursor()
        {
            if (measuringSpan == null)
            {
                return;
            }

            if (measuringSpan.X1 > measuringSpan.X2)
            {
                double temp = measuringSpan.X1;
                measuringSpan.X1 = measuringSpan.X2;
                measuringSpan.X2 = temp;
            }

            indexCursor1 = FindIndex(xh, measuringSpan.X1);
            indexCursor2 = FindIndex(xh, measuringSpan.X2);

            minMaxAlt = FindMinMax(minMaxAlt, yh, indexCursor1, indexCursor2);
            textBoxMeasAltMin.Text = minMaxAlt.min.ToString("F2");
            textBoxMeasAltMax.Text = minMaxAlt.max.ToString("F2");

            minMaxTemp = FindMinMax(minMaxTemp, yt, indexCursor1, indexCursor2);
            textBoxMeasTempMin.Text = minMaxTemp.min.ToString("F2");
            textBoxMeasTempMax.Text = minMaxTemp.max.ToString("F2");

            minMaxAcc = FindMinMax(minMaxAcc, ya, indexCursor1, indexCursor2);
            textBoxMeasAccMin.Text = minMaxAcc.min.ToString("F2");
            textBoxMeasAccMax.Text = minMaxAcc.max.ToString("F2");

            if (yax != null)
            {
                minMaxAccX = FindMinMax(minMaxAccX, yax, indexCursor1, indexCursor2);
                textBoxMeasAccMinX.Text = minMaxAccX.min.ToString("F2");
                textBoxMeasAccMaxX.Text = minMaxAccX.max.ToString("F2");
            }
            else
            {
                textBoxMeasAccMinX.Text = double.NaN.ToString();
                textBoxMeasAccMaxX.Text = double.NaN.ToString();
            }

            if (yay != null)
            {
                minMaxAccY = FindMinMax(minMaxAccY, yay, indexCursor1, indexCursor2);
                textBoxMeasAccMinY.Text = minMaxAccY.min.ToString("F2");
                textBoxMeasAccMaxY.Text = minMaxAccY.max.ToString("F2");
            }
            else
            {
                textBoxMeasAccMinY.Text = double.NaN.ToString();
                textBoxMeasAccMaxY.Text = double.NaN.ToString();
            }

            if (yaz != null)
            {
                minMaxAccZ = FindMinMax(minMaxAccZ, yaz, indexCursor1, indexCursor2);
                textBoxMeasAccMinZ.Text = minMaxAccZ.min.ToString("F2");
                textBoxMeasAccMaxZ.Text = minMaxAccZ.max.ToString("F2");
            }
            else
            {
                textBoxMeasAccMinZ.Text = double.NaN.ToString();
                textBoxMeasAccMaxZ.Text = double.NaN.ToString();
            }

            UpdateMeasuringCalculations();
            RefreshMeasuringTextBoxes();
        }

        private void UpdateMeasuringCalculations()
        {
            double alt1 = InterpolateY(xh, yh, measuringSpan.X1);
            double alt2 = InterpolateY(xh, yh, measuringSpan.X2);
            textBoxMeasAltDelta.Text = FormatMeasurementValue(alt2 - alt1);
            textBoxMeasAltAverage.Text = FormatMeasurementValue(CalculateAverage(yh, indexCursor1, indexCursor2));
            textBoxMeasAltSpeed.Text = FormatMeasurementValue(CalculateSpeed(alt1, alt2, measuringSpan.X1, measuringSpan.X2));

            double temp1 = InterpolateY(xh, yt, measuringSpan.X1);
            double temp2 = InterpolateY(xh, yt, measuringSpan.X2);
            textBoxMeasTempDelta.Text = FormatMeasurementValue(temp2 - temp1);
            textBoxMeasTempAverage.Text = FormatMeasurementValue(CalculateAverage(yt, indexCursor1, indexCursor2));

            double acc1 = InterpolateY(xh, ya, measuringSpan.X1);
            double acc2 = InterpolateY(xh, ya, measuringSpan.X2);
            textBoxMeasAccDelta.Text = FormatMeasurementValue(acc2 - acc1);
            textBoxMeasAccAverage.Text = FormatMeasurementValue(CalculateAverage(ya, indexCursor1, indexCursor2));

            if (yax != null)
            {
                double accX1 = InterpolateY(xax, yax, measuringSpan.X1);
                double accX2 = InterpolateY(xax, yax, measuringSpan.X2);
                textBoxMeasAccDeltaX.Text = FormatMeasurementValue(accX2 - accX1);
                textBoxMeasAccAverageX.Text = FormatMeasurementValue(CalculateAverage(yax, indexCursor1, indexCursor2));
            }
            else
            {
                textBoxMeasAccDeltaX.Text = double.NaN.ToString();
                textBoxMeasAccAverageX.Text = double.NaN.ToString();
            }

            if (yay != null)
            {
                double accY1 = InterpolateY(xay, yay, measuringSpan.X1);
                double accY2 = InterpolateY(xay, yay, measuringSpan.X2);
                textBoxMeasAccDeltaY.Text = FormatMeasurementValue(accY2 - accY1);
                textBoxMeasAccAverageY.Text = FormatMeasurementValue(CalculateAverage(yay, indexCursor1, indexCursor2));
            }
            else
            {
                textBoxMeasAccDeltaY.Text = double.NaN.ToString();
                textBoxMeasAccAverageY.Text = double.NaN.ToString();
            }

            if (yaz != null)
            {
                double accZ1 = InterpolateY(xaz, yaz, measuringSpan.X1);
                double accZ2 = InterpolateY(xaz, yaz, measuringSpan.X2);
                textBoxMeasAccDeltaZ.Text = FormatMeasurementValue(accZ2 - accZ1);
                textBoxMeasAccAverageZ.Text = FormatMeasurementValue(CalculateAverage(yaz, indexCursor1, indexCursor2));
            }
            else
            {
                textBoxMeasAccDeltaZ.Text = double.NaN.ToString();
                textBoxMeasAccAverageZ.Text = double.NaN.ToString();
            }
        }

        private void EnsureHoverPlottables()
        {
            if (hoverMarker == null)
            {
                hoverMarker = WpfPlot1.Plot.Add.Marker(0, 0, MarkerShape.OpenCircle, 8);
                hoverMarker.MarkerLineWidth = 2;
                hoverMarker.IsVisible = false;
            }

            if (hoverLabel == null)
            {
                hoverLabel = WpfPlot1.Plot.Add.Text(string.Empty, 0, 0);
                hoverLabel.Alignment = Alignment.UpperLeft;
                hoverLabel.LabelBackgroundColor = Colors.White;
                hoverLabel.LabelBorderColor = Colors.DimGray;
                hoverLabel.LabelBorderWidth = 1;
                hoverLabel.LabelFontSize = HoverTooltipFontSize;
                hoverLabel.OffsetX = 8;
                hoverLabel.OffsetY = -8;
                hoverLabel.IsVisible = false;
            }

            if (hoverMarker != null)
            {
                WpfPlot1.Plot.MoveToFront(hoverMarker);
            }

            if (hoverLabel != null)
            {
                WpfPlot1.Plot.MoveToFront(hoverLabel);
            }
        }

        private void HideHover()
        {
            if (hoverMarker != null)
            {
                hoverMarker.IsVisible = false;
            }

            if (hoverLabel != null)
            {
                hoverLabel.IsVisible = false;
            }

            hoverSeries = null;
            hoverX = null;
            hoverY = null;
        }

        private void UpdateHoverPoint(MouseEventArgs e)
        {
            long nowMs = hoverStopwatch.ElapsedMilliseconds;
            if (nowMs - lastHoverUpdateMs < HoverUpdateIntervalMs)
            {
                return;
            }

            lastHoverUpdateMs = nowMs;

            if (WpfPlot1.Plot.LastRender.Equals(default(ScottPlot.RenderDetails)))
            {
                return;
            }

            EnsureHoverPlottables();

            ScottPlot.Pixel mousePixel = WpfPlot1.GetPlotPixelPosition(e);
            ScottPlot.DataPoint bestPoint = default;
            ScottPlot.IAxes bestAxes = null;
            ScottPlot.Color bestColor = Colors.Transparent;
            string bestSeries = null;
            double bestDistance = double.PositiveInfinity;

            TryUpdateBestCandidate(pltAlt, "Altitude", mousePixel, ref bestPoint, ref bestAxes, ref bestColor, ref bestSeries, ref bestDistance);
            TryUpdateBestCandidate(pltTemp, "Temperature", mousePixel, ref bestPoint, ref bestAxes, ref bestColor, ref bestSeries, ref bestDistance);
            TryUpdateBestCandidate(pltAcc, "Acc Abs", mousePixel, ref bestPoint, ref bestAxes, ref bestColor, ref bestSeries, ref bestDistance);
            TryUpdateBestCandidate(pltAccX, "Acc X", mousePixel, ref bestPoint, ref bestAxes, ref bestColor, ref bestSeries, ref bestDistance);
            TryUpdateBestCandidate(pltAccY, "Acc Y", mousePixel, ref bestPoint, ref bestAxes, ref bestColor, ref bestSeries, ref bestDistance);
            TryUpdateBestCandidate(pltAccZ, "Acc Z", mousePixel, ref bestPoint, ref bestAxes, ref bestColor, ref bestSeries, ref bestDistance);

            if (bestAxes == null)
            {
                if (hoverMarker?.IsVisible == true || hoverLabel?.IsVisible == true)
                {
                    HideHover();
                    WpfPlot1.Refresh();
                }
                return;
            }

            hoverMarker.Axes = bestAxes;
            hoverMarker.X = bestPoint.X;
            hoverMarker.Y = bestPoint.Y;
            hoverMarker.MarkerLineColor = bestColor;
            hoverMarker.MarkerFillColor = bestColor;
            hoverMarker.IsVisible = true;

            hoverLabel.Axes = bestAxes;
            hoverLabel.Location = new ScottPlot.Coordinates(bestPoint.X, bestPoint.Y);
            hoverLabel.LabelText = FormatHoverLabel(bestSeries, bestPoint.X, bestPoint.Y);
            ScottPlot.Pixel anchorPixel = WpfPlot1.Plot.GetPixel(hoverLabel.Location, bestAxes.XAxis, bestAxes.YAxis);
            UpdateHoverLabelPlacement(anchorPixel);
            hoverLabel.IsVisible = true;

            bool changed = hoverSeries != bestSeries || hoverX != bestPoint.X || hoverY != bestPoint.Y;
            hoverSeries = bestSeries;
            hoverX = bestPoint.X;
            hoverY = bestPoint.Y;

            if (changed)
            {
                WpfPlot1.Refresh();
            }
        }

        private void TryUpdateBestCandidate(Scatter scatter, string seriesName, ScottPlot.Pixel mousePixel,
            ref ScottPlot.DataPoint bestPoint, ref ScottPlot.IAxes bestAxes, ref ScottPlot.Color bestColor,
            ref string bestSeries, ref double bestDistance)
        {
            if (scatter == null || !scatter.IsVisible)
            {
                return;
            }

            if (scatter == null || !scatter.IsVisible)
            {
                return;
            }

            ScottPlot.IAxes axes = scatter.Axes;
            if (axes == null)
            {
                return;
            }

            ScottPlot.Coordinates mouseCoords = WpfPlot1.Plot.GetCoordinates(mousePixel, axes.XAxis, axes.YAxis);
            ScottPlot.DataPoint nearest = scatter.GetNearest(mouseCoords, WpfPlot1.Plot.LastRender, HoverSnapDistance);
            if (!nearest.IsReal)
            {
                return;
            }

            ScottPlot.Pixel nearestPixel = WpfPlot1.Plot.GetPixel(nearest.Coordinates, axes.XAxis, axes.YAxis);
            double dx = nearestPixel.X - mousePixel.X;
            double dy = nearestPixel.Y - mousePixel.Y;
            double distance = Math.Sqrt((dx * dx) + (dy * dy));
            if (distance > HoverSnapDistance || distance >= bestDistance)
            {
                return;
            }

            bestPoint = nearest;
            bestAxes = axes;
            bestColor = scatter.Color;
            bestSeries = seriesName;
            bestDistance = distance;
        }


        private void ClearSerialPortManagers()
        {
            foreach (SerialPortManager manager in serialPortManagers.Values)
            {
                manager.DataReceived -= OnDataReceived;
                manager.Dispose();
            }
            serialPortManagers.Clear();
        }

        private void HandlePortCommand(string portName, string command)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                MessageBox.Show("Invalid device selection.");
                return;
            }

            if (!serialPortManagers.TryGetValue(portName, out SerialPortManager manager))
            {
                MessageBox.Show("Device not found: " + portName);
                return;
            }

            manager.OpenPort();
            manager.SendCommand(command);
        }

        private void UpdateDeviceInfo(string portName)
        {
            DataLogger logger = DataLoggerManager.GetLogger(portName);
            if (logger == null)
            {
                return;
            }

            textBoxModel.Text = logger.id ?? "-";
            textBoxSerialNumber.Text = logger.serialNumber ?? "-";
            textBoxProductionDate.Text = logger.productionDate ?? "-";
            textBoxChecksum.Text = logger.checkSum ?? "-";
        }

            // Methode zur linearen Interpolation für den Measuring Cursor um Y-Koordinate des Plots aus X-Koordinate des Messcursors zu bekommen
        private double InterpolateY(double[] xData, double[] yData, double xValue)
        {
            if (xData == null || yData == null || xData.Length == 0 || xData.Length != yData.Length)
            {
                return double.NaN; // Fr?hzeitige R?ckkehr bei ung?ltigen Eingabedaten
            }

            if (xValue <= xData[0])
            {
                return yData[0];
            }

            if (xValue >= xData[xData.Length - 1])
            {
                return yData[yData.Length - 1];
            }

            for (int i = 1; i < xData.Length; i++)
            {
                if (xValue < xData[i])
                {
                    double slope = (yData[i] - yData[i - 1]) / (xData[i] - xData[i - 1]);
                    double yInterpolated = yData[i - 1] + slope * (xValue - xData[i - 1]);

                    return yInterpolated;
                }
            }
            return double.NaN; // X-Wert liegt au?erhalb des Bereichs
        }

        // Methode zur Findung der X-Koordinaten des Messcursors 1 und 2
        private int FindIndex(double[] xData, double xValue)
        {
            if (xData == null || xData.Length == 0)
            {
                return 0;
            }

            if (xValue <= xData[0])
            {
                return 0;
            }

            if (xValue >= xData[xData.Length - 1])
            {
                return xData.Length - 1;
            }

            for (int i = 1; i < xData.Length; i++)
            {
                if (xValue < xData[i])
                {
                    Debug.WriteLine(i);
                    return i;
                }
            }
            return 0; // X-Wert liegt außerhalb des Bereichs
        }

        // Methode zur Findung der Min/Max Werte im gegebenen X-Achsenbereich den der Measuring Cursor abdeckt
        private minMax FindMinMax(minMax minMax, double[] data, int start, int end)
        {
            if (data == null || data.Length == 0)
            {
                minMax.min = double.NaN;
                minMax.max = double.NaN;
                return minMax;
            }

            int safeStart = Math.Max(0, Math.Min(start, data.Length - 1));
            int safeEnd = Math.Max(0, Math.Min(end, data.Length - 1));
            if (safeStart > safeEnd)
            {
                int temp = safeStart;
                safeStart = safeEnd;
                safeEnd = temp;
            }

            minMax.min = data[safeStart];
            minMax.max = data[safeStart];

            for (int i = safeStart; i <= safeEnd; i++)
            {
                if (data[i] < minMax.min)
                {
                    minMax.min = data[i];
                }
                if (data[i] > minMax.max)
                {
                    minMax.max = data[i];
                }
                
            }
            Debug.WriteLine(minMax.min);
            Debug.WriteLine(minMax.max);
            return minMax;
        }

        private double CalculateAverage(double[] data, int start, int end)
        {
            if (data == null || data.Length == 0)
            {
                return double.NaN;
            }

            int safeStart = Math.Max(0, Math.Min(start, end));
            int safeEnd = Math.Min(data.Length - 1, Math.Max(start, end));
            if (safeStart > safeEnd)
            {
                return double.NaN;
            }

            double sum = 0;
            int count = 0;
            for (int i = safeStart; i <= safeEnd; i++)
            {
                sum += data[i];
                count++;
            }

            return count > 0 ? sum / count : double.NaN;
        }

        private double CalculateSpeed(double alt1, double alt2, double x1, double x2)
        {
            if (double.IsNaN(alt1) || double.IsNaN(alt2))
            {
                return double.NaN;
            }

            double deltaX = x2 - x1;
            if (deltaX == 0)
            {
                return double.NaN;
            }

            double deltaSeconds = IsDateTimeXAxis()
                ? TimeSpan.FromDays(deltaX).TotalSeconds
                : deltaX;

            if (deltaSeconds == 0)
            {
                return double.NaN;
            }

            return (alt2 - alt1) / deltaSeconds;
        }

        private string FormatMeasurementValue(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? double.NaN.ToString()
                : value.ToString("F2");
        }

        private List<Messreihe> DekodiereDatenpaket(string datenpaket, string portName)
        {
            if (string.IsNullOrEmpty(datenpaket))
            {
                return null;
            }

            Debug.WriteLine("Decode packet " + portName + ": len=" + datenpaket.Length + " preview=" + BuildPacketPreview(datenpaket));

            //Pr?fe ob ?berhaupt der Beginn einer Aufnahme da ist mittels Anfangskodierung AAAA. Wenn nicht, ist keine g?ltige Aufnahme in den Daten vorhanden
            int splitStartIndex = FindAlignedMarker(datenpaket, "AAAA", 4);
            if (splitStartIndex < 0)
            {
                //Breche die Dekodierung ab und gebe null zur?ck
                return null;
            }

            if (splitStartIndex > 0)
            {
                datenpaket = datenpaket.Substring(splitStartIndex);
            }

            List<Messreihe> messreihen = new List<Messreihe>();

            // Trennung der einzelnen Messreihen an der Anfangskodierung
            string[] einzelneReihen = datenpaket.Split(new[] { "AAAA" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string reihe in einzelneReihen)
            {
                if (string.IsNullOrEmpty(reihe) || reihe.Length < 24)
                {
                    continue; // Leere oder ung?ltige Reihe ?berspringen
                }

                var messreihe = new Messreihe();
                // Anfangsdatenfolge
                string anfangsdaten = reihe.Substring(0, 24); // L?nge der Anfangsdaten
                string startZeitHex = anfangsdaten.Substring(2, 14);
                Debug.WriteLine("ParseDatumUndZeit start " + portName + ": " + startZeitHex);
                messreihe.Startzeit = ParseDatumUndZeit(startZeitHex);
                messreihe.StartTemperatur = (HexZuDouble(anfangsdaten.Substring(16, 4)) - 500) / 10;
                messreihe.StartDruck = HexZuDouble(anfangsdaten.Substring(20, 4)) / 10;

                // Extraktion und Verarbeitung der Messdaten
                int messdatenStartIndex = 24;
                int messdatenEndIndex = -1;
                int searchIndex = messdatenStartIndex;
                while (searchIndex >= 0 && searchIndex < reihe.Length)
                {
                    int candidateIndex = reihe.IndexOf("FFFF", searchIndex, StringComparison.Ordinal);
                    if (candidateIndex < 0)
                    {
                        break;
                    }

                    if (candidateIndex % 4 == 0)
                    {
                        messdatenEndIndex = candidateIndex;
                        break;
                    }

                    searchIndex = candidateIndex + 1;
                }

                if (messdatenEndIndex < 0 || messdatenEndIndex <= messdatenStartIndex)
                {
                    continue;
                }

                string messdaten = reihe.Substring(messdatenStartIndex, messdatenEndIndex - messdatenStartIndex); // Exklusive Abschlussdaten

                //Verarbeitung Messdaten
                double temperatur = messreihe.StartTemperatur; // Starttemperatur aus der Anfangsdatenfolge
                int tempCounter = 1;
                int timeCounter = 0;

                for (int i = 0; i + 15 < messdaten.Length; i += 16)
                {
                    if (tempCounter >= 240)
                    {
                        tempCounter = 0;

                        if (i + 19 >= messdaten.Length)
                        {
                            break;
                        }

                        temperatur = (HexZuDouble(messdaten.Substring(i, 4)) - 500) / 10;
                        i += 4;
                    }

                    if (i + 15 >= messdaten.Length)
                    {
                        break;
                    }

                    var daten = new Messdaten
                    {
                        Zeit = messreihe.Startzeit.AddMilliseconds(250 * timeCounter),
                        Druck = HexZuDouble(messdaten.Substring(i, 4)) / 10,
                        //Hoehe = Math.Round((288.15 / 0.0065) * (1 - ((HexZuDouble(messdaten.Substring(i, 4)) / 10) / 1013.25)) * 0.190294957, 2),
                        Hoehe = Math.Round((288.15 / 0.0065) * (1 - Math.Pow(((HexZuDouble(messdaten.Substring(i, 4)) / 10) / 1013.25), 0.190294957)), 2),
                        BeschleunigungX = CalculateAccelerationFromHex(messdaten.Substring(i + 4, 4)),
                        BeschleunigungY = CalculateAccelerationFromHex(messdaten.Substring(i + 8, 4)),
                        BeschleunigungZ = CalculateAccelerationFromHex(messdaten.Substring(i + 12, 4)),
                        Temperatur = temperatur
                    };

                    messreihe.Messungen.Add(daten);
                    tempCounter++;
                    timeCounter++;
                }

                // Abschlussdatenfolge
                string abschlussdaten = reihe.Substring(messdatenEndIndex + 4);
                if (abschlussdaten.Length >= 32)
                {
                    messreihe.Status = abschlussdaten.Substring(0, 4);
                    messreihe.Spannung = abschlussdaten.Substring(4, 4);
                    string endZeitHex = abschlussdaten.Substring(10, 14);
                    Debug.WriteLine("ParseDatumUndZeit end " + portName + ": " + endZeitHex);
                    messreihe.Endzeit = ParseDatumUndZeit(endZeitHex);
                    messreihe.EndTemperatur = HexZuDouble(abschlussdaten.Substring(24, 4));
                    messreihe.EndDruck = HexZuDouble(abschlussdaten.Substring(28, 4));
                }

                messreihen.Add(messreihe);
            }

            //Messreihen im TreeView dem Datenlogger als Subitem hinzuf?gen
            foreach (Messreihe messreihe in messreihen)
            {
                TreeViewManager.AddSubItem(portName, messreihe.Startzeit.ToString());
            }

            return messreihen;
        }

        private int FindAlignedMarker(string data, string marker, int modulus)
        {
            if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(marker) || modulus <= 0)
            {
                return -1;
            }

            int searchIndex = 0;
            while (true)
            {
                int index = data.IndexOf(marker, searchIndex, StringComparison.Ordinal);
                if (index < 0)
                {
                    return -1;
                }

                if (index % modulus == 0)
                {
                    return index;
                }

                Debug.WriteLine("Marker misaligned: marker=" + marker + " index=" + index + " mod=" + modulus);
                searchIndex = index + 1;
            }
        }

        private DateTime ParseDatumUndZeit(string hexDatumZeit)
        {
            // Stellen Sie sicher, dass die Länge von hexDatumZeit korrekt ist
            if (hexDatumZeit.Length != 14)
            {
                throw new ArgumentException("Ungültige Länge für Datum und Zeit.");
            }

            int sekunden = HexZuInt(hexDatumZeit.Substring(0, 2));
            int minuten = HexZuInt(hexDatumZeit.Substring(2, 2));
            int stunden = HexZuInt(hexDatumZeit.Substring(4, 2));
            int tag = int.Parse(hexDatumZeit.Substring(6, 2));
            int monat = int.Parse(hexDatumZeit.Substring(8, 2));
            int jahr = int.Parse(hexDatumZeit.Substring(10, 4));

            // Validierung der Datumswerte
            if (jahr < 1 || jahr > 9999 || monat < 1 || monat > 12 || tag < 1 || tag > DateTime.DaysInMonth(jahr, monat))
            {
                return new DateTime(1900, 1, 1, stunden, minuten, sekunden);
            }
            return new DateTime(jahr, monat, tag, stunden, minuten, sekunden);
        }

        private int HexZuInt(string hex)
        {
            return int.Parse(hex, NumberStyles.HexNumber);
        }

        private double HexZuDouble(string hex)
        {
            long intValue = Convert.ToInt64(hex, 16);
            return (double)intValue;
        }

        /*private double CalculateAccelerationFromHex(string hexData)
        {
            double acceleration;

            if (hexData.Length > 4)
            {
                throw new ArgumentException("Der Hexadezimal-String darf maximal vier Zeichen lang sein.");
            }

            // Konvertiere Hexadezimal-String in Integer (10-Bit Zweierkomplement)
            int rawValue = Convert.ToInt32(hexData, 16);

            
            if (rawValue <= 32767) // 
            {
                acceleration = rawValue / 2048.0;
                return Math.Round(acceleration, 3);
            }
            else
            {
                acceleration = (rawValue - 65535.0) / 2048.0;
                return Math.Round(acceleration, 3);
            }
        }
        */
        private double CalculateAccelerationFromHex(string hexData)
        {
            if (hexData.Length > 4)
                throw new ArgumentException("Der Hexadezimal-String darf maximal vier Zeichen lang sein.");

            // 1) Hex → signed 16-Bit (LIS3DH liefert sign-extended Werte)
            short raw16 = Convert.ToInt16(hexData, 16);

            // 2) 10-Bit Zweierkomplement extrahieren (links­bündig → 6 Bit nach rechts)
            int raw10 = raw16 >> 6;

            // 3) Skalierung laut Datenblatt: 48 mg / digit = 0.048 g / LSB
            double acceleration_g = raw10 * 0.048;

            return Math.Round(acceleration_g, 3);
        }


        //################################################################################################################################
        //                                                   AUSLÖSEMETHODEN FÜR EVENTS
        //################################################################################################################################


        // Methode zum Auslösen des Events zum Plotten von Datenn aus anderen Klassen heraus
        public void TriggerPlotData(string portName, int index)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                MessageBox.Show("Invalid device selection.");
                return;
            }

            if (!measurementSeriesByPort.TryGetValue(portName, out List<Messreihe> series) || series == null || series.Count == 0)
            {
                MessageBox.Show("No data for selected device.");
                return;
            }

            if (index < 0 || index >= series.Count)
            {
                MessageBox.Show("Invalid data index.");
                return;
            }

            currentPortName = portName;
            currentSeriesIndex = index;
            PlotData(series, index);
            UpdateDeviceInfo(portName);
            TreeViewManager.SelectSubItem(portName, index);
            SyncSeriesStateFromTreeView(portName, index);
        }


        //################################################################################################################################
        //                                                   EVENTHANDLER
        //################################################################################################################################

        // EventHandler für die Verschiebung des Plots mit der Maus
        private void WpfPlot1_MouseMove(object sender, MouseEventArgs e)
        {
            RefreshAxisTextBoxes();
            UpdateCrosshairState();
            UpdateMeasuringState();
            UpdateHoverPoint(e);
        }

        // EventHandler für das Zoomen des Plots mit der Maus
        private void WpfPlot1_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            RefreshAxisTextBoxes();
            UpdateCrosshairState();
            UpdateMeasuringState();
        }

        private void WpfPlot1_MouseLeave(object sender, MouseEventArgs e)
        {
            if (hoverMarker?.IsVisible == true || hoverLabel?.IsVisible == true)
            {
                HideHover();
                WpfPlot1.Refresh();
            }
        }

        // EventHandler f?r das Dragged-Ereignis des Messcursors
        private void measuringSpan_Edge1Dragged(object sender, double e)
        {
            UpdateMeasuringCursor();
        }

        private void measuringSpan_Edge2Dragged(object sender, double e)
        {
            UpdateMeasuringCursor();
        }

        //Eventhandler wenn der X-Achsen Crosshair auf der Zeitachse verschoben wird
        private void crosshairX_Dragged(object sender, EventArgs e)
        {
            UpdateCrosshairState();
        }

        //Eventhandler wenn der Cursor auf der Y-Achse verschoben wird
        private void crosshairAlt_Dragged(object sender, EventArgs e)
        {
            UpdateCrosshairState();
        }

       // Eventhandler wenn der Timer auslöst  - aktuell für Buttons um die Achsen zu verschieben wenn man den Button gedrückt hält
        private void Timer_Tick(object sender, EventArgs e)
        {
            timer.Interval = TimeSpan.FromSeconds(activeTime);
            if (buttonAltUpPressed)
            {
                ShiftAxis(WpfPlot1.Plot.Axes.Left, -1.0 / 50);
                WpfPlot1.Refresh();
                RefreshAxisTextBoxes();
            }
            else if (buttonAltDownPressed)
            {
                ShiftAxis(WpfPlot1.Plot.Axes.Left, 1.0 / 50);
                WpfPlot1.Refresh();
                RefreshAxisTextBoxes();
            }
            else if (buttonTempUpPressed && yAxisTemp != null)
            {
                ShiftAxis(yAxisTemp, -1.0 / 50);
                WpfPlot1.Refresh();
                RefreshAxisTextBoxes();
            }
            else if (buttonTempDownPressed && yAxisTemp != null)
            {
                ShiftAxis(yAxisTemp, 1.0 / 50);
                WpfPlot1.Refresh();
                RefreshAxisTextBoxes();
            }
            else if (buttonAccUpPressed && yAxisAcc != null)
            {
                ShiftAxis(yAxisAcc, -1.0 / 50);
                WpfPlot1.Refresh();
                RefreshAxisTextBoxes();
            }
            else if (buttonAccDownPressed && yAxisAcc != null)
            {
                ShiftAxis(yAxisAcc, 1.0 / 50);
                WpfPlot1.Refresh();
                RefreshAxisTextBoxes();
            }
        }

        //------------------BUTTON EVENTS---------------------------------------

        private void menuItemUnitsMetric_Click(object sender, RoutedEventArgs e)
        {
            SetUnitMode(UnitMode.Metric);
        }

        private void menuItemUnitsImperial_Click(object sender, RoutedEventArgs e)
        {
            SetUnitMode(UnitMode.Imperial);
        }

        private void menuItemChartStyle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string tag &&
                Enum.TryParse(tag, true, out ChartStylePreset preset))
            {
                SetChartStylePreset(preset);
                UpdateChartStyleMenuChecks();
                ApplyChartStyle();
            }
        }

        private void menuItemChartStyleColor_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is MenuItem menuItem) || !(menuItem.Tag is string tag))
            {
                return;
            }

            ScottPlot.Color current = GetColorForTag(tag);
            if (!TryPickColor(current, out ScottPlot.Color selected))
            {
                return;
            }

            ApplyColorByTag(tag, selected);
        }

        private void menuItemChartStyleGrid_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                showGrid = menuItem.IsChecked;
                UpdateChartStyleMenuChecks();
                ApplyChartStyle();
            }
        }

        private void menuItemAxisLabelMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                axisLabelColorsFollowSeries = menuItem.IsChecked;
                if (axisLabelColorsFollowSeries)
                {
                    SyncAxisLabelColorsToSeries();
                }

                UpdateChartStyleMenuChecks();
                ApplySeriesAxisColors();
                WpfPlot1.Refresh();
            }
        }

        private void menuItemChartStyleReset_Click(object sender, RoutedEventArgs e)
        {
            SetChartStylePreset(currentChartStyle);
            UpdateChartStyleMenuChecks();
            ApplyChartStyle();
        }

        private void menuItemSeriesColorsReset_Click(object sender, RoutedEventArgs e)
        {
            ResetSeriesColors();
            ApplySeriesColors();
        }

        private void menuItemSeriesLineWidth_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is MenuItem menuItem) || !(menuItem.Tag is string tag))
            {
                return;
            }

            float current = GetLineWidthForTag(tag);
            if (!TryPickLineWidth(current, out float selected))
            {
                return;
            }

            ApplyLineWidthByTag(tag, selected);
        }

        private void menuItemSeriesLinePattern_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is MenuItem menuItem) || !(menuItem.Tag is string tag))
            {
                return;
            }

            string[] parts = tag.Split('|');
            if (parts.Length != 2)
            {
                return;
            }

            if (!TryGetLinePattern(parts[1], out ScottPlot.LinePattern pattern))
            {
                return;
            }

            ApplyLinePatternByTag(parts[0], pattern);
        }

        private void menuItemSeriesLineStylesReset_Click(object sender, RoutedEventArgs e)
        {
            ResetSeriesLineStyles();
            ApplySeriesLineStyles();
        }


        private void menuItemSeries_Click(object sender, RoutedEventArgs e)
        {
            MenuItem menuItem = sender as MenuItem;
            if (menuItem == null)
            {
                return;
            }

            string tag = menuItem.Tag as string;
            if (string.IsNullOrWhiteSpace(tag))
            {
                return;
            }

            bool isChecked = menuItem.IsChecked;
            switch (tag)
            {
                case "Alt":
                    showAltitude = isChecked;
                    break;
                case "Temp":
                    showTemperature = isChecked;
                    break;
                case "AccAbs":
                    showAccAbs = isChecked;
                    break;
                case "AccX":
                    showAccX = isChecked;
                    break;
                case "AccY":
                    showAccY = isChecked;
                    break;
                case "AccZ":
                    showAccZ = isChecked;
                    break;
            }

            ApplySeriesVisibility();
            TreeViewManager.SetSeriesStates(showAltitude, showTemperature, showAccAbs, showAccX, showAccY, showAccZ);
            UpdateSeriesMenuChecks();
            WpfPlot1.Refresh();
        }

        private void TreeViewManager_SeriesToggleChanged(string seriesKey, bool isChecked)
        {
            switch (seriesKey)
            {
                case "Alt":
                    showAltitude = isChecked;
                    break;
                case "Temp":
                    showTemperature = isChecked;
                    break;
                case "AccAbs":
                    showAccAbs = isChecked;
                    break;
                case "AccX":
                    showAccX = isChecked;
                    break;
                case "AccY":
                    showAccY = isChecked;
                    break;
                case "AccZ":
                    showAccZ = isChecked;
                    break;
            }

            ApplySeriesVisibility();
            TreeViewManager.SetSeriesStates(showAltitude, showTemperature, showAccAbs, showAccX, showAccY, showAccZ);
            UpdateSeriesMenuChecks();
            WpfPlot1.Refresh();
        }

        private void UpdateSeriesMenuChecks()
        {
            if (menuItemSeriesAlt != null) menuItemSeriesAlt.IsChecked = showAltitude;
            if (menuItemSeriesTemp != null) menuItemSeriesTemp.IsChecked = showTemperature;
            if (menuItemSeriesAccAbs != null) menuItemSeriesAccAbs.IsChecked = showAccAbs;
            if (menuItemSeriesAccX != null) menuItemSeriesAccX.IsChecked = showAccX;
            if (menuItemSeriesAccY != null) menuItemSeriesAccY.IsChecked = showAccY;
            if (menuItemSeriesAccZ != null) menuItemSeriesAccZ.IsChecked = showAccZ;
        }

        private void UpdateChartStyleMenuChecks()
        {
            if (menuItemChartStyleLight != null) menuItemChartStyleLight.IsChecked = currentChartStyle == ChartStylePreset.Light;
            if (menuItemChartStyleDark != null) menuItemChartStyleDark.IsChecked = currentChartStyle == ChartStylePreset.Dark;
            if (menuItemChartStyleSlate != null) menuItemChartStyleSlate.IsChecked = currentChartStyle == ChartStylePreset.Slate;
            if (menuItemChartStyleShowGrid != null) menuItemChartStyleShowGrid.IsChecked = showGrid;
            if (menuItemAxisLabelFollowSeries != null) menuItemAxisLabelFollowSeries.IsChecked = axisLabelColorsFollowSeries;

            bool enableAxisLabelColors = !axisLabelColorsFollowSeries;
            if (menuItemAxisLabelAltColor != null) menuItemAxisLabelAltColor.IsEnabled = enableAxisLabelColors;
            if (menuItemAxisLabelTempColor != null) menuItemAxisLabelTempColor.IsEnabled = enableAxisLabelColors;
            if (menuItemAxisLabelAccColor != null) menuItemAxisLabelAccColor.IsEnabled = enableAxisLabelColors;
        }

        private void SyncSeriesStateFromTreeView(string portName, int index)
        {
            if (!TreeViewManager.TryGetSeriesStates(portName, index, out bool alt, out bool temp, out bool accAbs, out bool accX, out bool accY, out bool accZ))
            {
                return;
            }

            showAltitude = alt;
            showTemperature = temp;
            showAccAbs = accAbs;
            showAccX = accX;
            showAccY = accY;
            showAccZ = accZ;

            ApplySeriesVisibility();
            TreeViewManager.SetSeriesStates(showAltitude, showTemperature, showAccAbs, showAccX, showAccY, showAccZ);
            UpdateSeriesMenuChecks();
            WpfPlot1.Refresh();
        }

        private void menuItemOpen_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "Siemert DataViewer Log (*.sdvlog)|*.sdvlog|All files (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            foreach (string filePath in dialog.FileNames)
            {
                try
                {
                    DataViewerLogFile logFile = LoadLogFile(filePath);
                    if (logFile == null)
                    {
                        MessageBox.Show("Invalid file: " + filePath);
                        continue;
                    }

                    string portName = CreateFilePortName(filePath);
                    DataLogger logger = new DataLogger
                    {
                        id = logFile.Logger?.Model,
                        serialNumber = logFile.Logger?.SerialNumber,
                        productionDate = logFile.Logger?.ProductionDate,
                        checkSum = logFile.Logger?.Checksum
                    };

                    DataLoggerManager.AddLogger(logger, portName);

                    List<Messreihe> seriesList = new List<Messreihe>();
                    if (logFile.Recordings != null)
                    {
                        foreach (Recording record in logFile.Recordings)
                        {
                            seriesList.Add(ConvertToMessreihe(record));
                        }
                    }

                    measurementSeriesByPort[portName] = seriesList;
                    filePortNames.Add(portName);

                    string model = logger.id ?? "Unknown";
                    string serialNumber = logger.serialNumber ?? "Unknown";
                    string displayName = $"{model} No. {serialNumber} (File: {System.IO.Path.GetFileName(filePath)})";
                    TreeViewManager.AddTreeViewItem(portName, displayName);
                    TreeViewItem treeItem = TreeViewManager.FindTreeViewItem(portName);
                    if (treeItem != null)
                    {
                        treeItem.ContextMenu = CreateFileItemContextMenu(portName);
                    }

                    foreach (Messreihe entry in seriesList)
                    {
                        TreeViewManager.AddSubItem(portName, entry.Startzeit.ToString());
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("File open failed: " + ex.Message);
                }
            }
        }

        private void menuItemSave_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetSelectedSeries(out string portName, out int seriesIndex))
            {
                MessageBox.Show("Select a recording to save.");
                return;
            }

            if (!measurementSeriesByPort.TryGetValue(portName, out List<Messreihe> seriesList) ||
                seriesList == null || seriesList.Count == 0)
            {
                MessageBox.Show("No data to save.");
                return;
            }

            if (seriesIndex < 0 || seriesIndex >= seriesList.Count)
            {
                MessageBox.Show("Invalid recording selection.");
                return;
            }

            MessageBoxResult choice = MessageBox.Show(
                "Save selected recording only?\nYes = Selected\nNo = All recordings",
                "Save",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);

            if (choice == MessageBoxResult.Cancel)
            {
                return;
            }

            bool saveAll = choice == MessageBoxResult.No;
            IEnumerable<Messreihe> toSave = saveAll
                ? seriesList
                : new List<Messreihe> { seriesList[seriesIndex] };

            DataLogger logger = DataLoggerManager.GetLogger(portName);
            string suffix = saveAll
                ? $"All_{DateTime.Now:yyyyMMdd_HHmmss}"
                : seriesList[seriesIndex].Startzeit.ToString("yyyyMMdd_HHmmss");
            string fileName = BuildLogFileName(logger, suffix);

            SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "Siemert DataViewer Log (*.sdvlog)|*.sdvlog",
                FileName = fileName
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                DataViewerLogFile logFile = BuildLogFile(logger, toSave);
                SaveLogFile(logFile, dialog.FileName);
                MessageBox.Show("Save complete.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save failed: " + ex.Message);
            }
        }

        private void menuItemSaveAll_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetSelectedLogger(out string portName))
            {
                MessageBox.Show("Select a logger to save.");
                return;
            }

            if (!measurementSeriesByPort.TryGetValue(portName, out List<Messreihe> seriesList) ||
                seriesList == null || seriesList.Count == 0)
            {
                MessageBox.Show("No data to save.");
                return;
            }

            DataLogger logger = DataLoggerManager.GetLogger(portName);
            string fileName = BuildLogFileName(logger, $"All_{DateTime.Now:yyyyMMdd_HHmmss}");

            SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "Siemert DataViewer Log (*.sdvlog)|*.sdvlog",
                FileName = fileName
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                DataViewerLogFile logFile = BuildLogFile(logger, seriesList);
                SaveLogFile(logFile, dialog.FileName);
                MessageBox.Show("Save complete.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save failed: " + ex.Message);
            }
        }

        private void menuItemExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetCurrentSeries(out Messreihe series, out string portName, out _))
            {
                MessageBox.Show("No data to export.");
                return;
            }

            if (!TryGetExportUnitMode(out UnitMode exportUnits))
            {
                return;
            }

            DataLogger logger = DataLoggerManager.GetLogger(portName);
            string model = SanitizeFileName(logger?.id ?? "UnknownModel");
            string serialNumber = SanitizeFileName(logger?.serialNumber ?? "UnknownSerial");
            string fileName = $"{model}_{serialNumber}_{series.Startzeit:yyyyMMdd_HHmmss}.csv";
            SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = fileName
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                ExportSeriesToCsv(series, dialog.FileName, exportUnits);
                MessageBox.Show("Export complete.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed: " + ex.Message);
            }
        }

        private void menuItemHelpDocs_Click(object sender, RoutedEventArgs e)
        {
            string docsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Docs", "DataViewer_Help.txt");
            if (!File.Exists(docsPath))
            {
                MessageBox.Show("Help file not found.", "Docs", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = docsPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open help: " + ex.Message, "Docs", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void menuItemHelpAbout_Click(object sender, RoutedEventArgs e)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "SIEMERT DataViewer";
            string company = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "SIEMERT";
            string version = FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "Unknown";

            string message = $"{product}\nVersion: {version}\n{company}\nThird-party notices: Docs\\ThirdParty_Notices.txt";
            MessageBox.Show(message, "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        //Measuring Cursor enable / disable
        private void toggleButtonMeasuringCursor_Checked(object sender, RoutedEventArgs e)
        {
            var limits = WpfPlot1.Plot.Axes.GetLimits();
            double span = limits.Right - limits.Left;
            double x1 = limits.Left + (span / 4);
            double x2 = limits.Right - (span / 4);

            measuringSpan = WpfPlot1.Plot.Add.InteractiveHorizontalSpan(x1, x2);
            measuringSpan.Axes = new ScottPlot.Axes { XAxis = WpfPlot1.Plot.Axes.Bottom, YAxis = WpfPlot1.Plot.Axes.Left };
            measuringSpan.LineStyle.Color = Colors.DimGray;
            measuringSpan.FillStyle.Color = ScottPlot.Color.FromARGB(unchecked((int)0x46C8C8C8));
            lastMeasuringX1 = null;
            lastMeasuringX2 = null;

            UpdateMeasuringCursor();
            WpfPlot1.Refresh();
        }
        private void toggleButtonMeasuringCursor_Unchecked(object sender, RoutedEventArgs e)
        {
            if (measuringSpan != null)
            {
                WpfPlot1.Plot.Remove(measuringSpan);
                measuringSpan = null;
            }

            lastMeasuringX1 = null;
            lastMeasuringX2 = null;
            ClearMeasuringTextBoxes();
            WpfPlot1.Refresh();
        }


        //Crosshair enable
        private void toggleButtonCrosshair_Checked(object sender, RoutedEventArgs e)
        {
            var limits = WpfPlot1.Plot.Axes.GetLimits();
            double xCenter = limits.Left + ((limits.Right - limits.Left) / 2);
            double yCenter = limits.Bottom + ((limits.Top - limits.Bottom) / 2);

            crosshairX = WpfPlot1.Plot.Add.InteractiveVerticalLine(xCenter);
            crosshairX.Axes = new ScottPlot.Axes { XAxis = WpfPlot1.Plot.Axes.Bottom, YAxis = WpfPlot1.Plot.Axes.Left };
            crosshairX.LineStyle.Color = Colors.Black;

            crosshairAlt = WpfPlot1.Plot.Add.InteractiveHorizontalLine(yCenter);
            crosshairAlt.Axes = new ScottPlot.Axes { XAxis = WpfPlot1.Plot.Axes.Bottom, YAxis = WpfPlot1.Plot.Axes.Left };
            crosshairAlt.LineStyle.Color = Colors.Black;

            ScottPlot.Pixel pixel = WpfPlot1.Plot.GetPixel(
                new ScottPlot.Coordinates(xCenter, yCenter),
                WpfPlot1.Plot.Axes.Bottom,
                WpfPlot1.Plot.Axes.Left);

            double tempY = WpfPlot1.Plot.GetCoordinates(pixel, WpfPlot1.Plot.Axes.Bottom, yAxisTemp).Y;
            double accY = WpfPlot1.Plot.GetCoordinates(pixel, WpfPlot1.Plot.Axes.Bottom, yAxisAcc).Y;

            crosshairTemp = WpfPlot1.Plot.Add.HorizontalLine(tempY, color: Colors.Transparent);
            crosshairTemp.Axes = new ScottPlot.Axes { XAxis = WpfPlot1.Plot.Axes.Bottom, YAxis = yAxisTemp };
            crosshairTemp.LabelOppositeAxis = true;
            crosshairTemp.LabelBackgroundColor = Colors.Red;
            crosshairTemp.IsDraggable = false;

            crosshairAcc = WpfPlot1.Plot.Add.HorizontalLine(accY, color: Colors.Transparent);
            crosshairAcc.Axes = new ScottPlot.Axes { XAxis = WpfPlot1.Plot.Axes.Bottom, YAxis = yAxisAcc };
            crosshairAcc.LabelOppositeAxis = true;
            crosshairAcc.LabelBackgroundColor = Colors.Green;
            crosshairAcc.IsDraggable = false;

            crosshairXLabel = WpfPlot1.Plot.Add.VerticalLine(xCenter, color: Colors.Transparent);
            crosshairXLabel.Axes = new ScottPlot.Axes { XAxis = WpfPlot1.Plot.Axes.Bottom, YAxis = WpfPlot1.Plot.Axes.Left };
            crosshairXLabel.LabelBackgroundColor = Colors.Black;
            crosshairXLabel.LabelFontColor = Colors.White;
            crosshairXLabel.IsDraggable = false;

            crosshairAltLabel = WpfPlot1.Plot.Add.HorizontalLine(yCenter, color: Colors.Transparent);
            crosshairAltLabel.Axes = new ScottPlot.Axes { XAxis = WpfPlot1.Plot.Axes.Bottom, YAxis = WpfPlot1.Plot.Axes.Left };
            crosshairAltLabel.LabelBackgroundColor = Colors.Black;
            crosshairAltLabel.LabelFontColor = Colors.White;
            crosshairAltLabel.IsDraggable = false;

            UpdateCrosshairState();
            WpfPlot1.Refresh();
        }


        //Crosshair disable
        private void toggleButtonCrosshair_Unchecked(object sender, RoutedEventArgs e)
        {
            if (crosshairX != null)
            {
                WpfPlot1.Plot.Remove(crosshairX);
                crosshairX = null;
            }

            if (crosshairAlt != null)
            {
                WpfPlot1.Plot.Remove(crosshairAlt);
                crosshairAlt = null;
            }

            if (crosshairTemp != null)
            {
                WpfPlot1.Plot.Remove(crosshairTemp);
                crosshairTemp = null;
            }

            if (crosshairAcc != null)
            {
                WpfPlot1.Plot.Remove(crosshairAcc);
                crosshairAcc = null;
            }

            if (crosshairXLabel != null)
            {
                WpfPlot1.Plot.Remove(crosshairXLabel);
                crosshairXLabel = null;
            }

            if (crosshairAltLabel != null)
            {
                WpfPlot1.Plot.Remove(crosshairAltLabel);
                crosshairAltLabel = null;
            }

            ClearCrosshairTextBoxes();
            WpfPlot1.Refresh();
        }



        //---------------------------- Achsenlimit Textfelder ---------------------------------------


        private void textBoxAltMax_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Versuchen Sie, den Text aus dem TextBox in eine Double-Zahl umzuwandeln.
                if (double.TryParse(textBoxAltMax.Text, out double result))
                {
                    // result enthält jetzt den Double-Wert aus dem TextBox.
                    // Verwenden Sie result für Ihre Berechnungen oder Anzeige.
                    if (double.TryParse(textBoxAltMin.Text, out double testresult))
                    {
                        if(result > testresult)
                        {
                            WpfPlot1.Plot.Axes.SetLimitsY(WpfPlot1.Plot.Axes.GetLimits().Bottom, result);
                            WpfPlot1.Refresh();
                        }
                        else
                        {
                            // Wenn die Umwandlung fehlschlägt, können Sie hier eine Fehlermeldung anzeigen.
                            MessageBox.Show("Invalid input. Upper limit max must be higher than lower limit.");
                        }

                    }
                    else
                    {
                        // Wenn die Umwandlung fehlschlägt, können Sie hier eine Fehlermeldung anzeigen.
                        MessageBox.Show("Invalid input. Please enter a valid number.");
                    }
                }
                else
                {
                    // Wenn die Umwandlung fehlschlägt, können Sie hier eine Fehlermeldung anzeigen.
                    MessageBox.Show("Invalid input. Please enter a valid number.");
                }
                //Textboxen mit Achsenlimits auktualisieren
                RefreshAxisTextBoxes();
            }
        }

        private void textBoxAltMin_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Versuchen Sie, den Text aus dem TextBox in eine Double-Zahl umzuwandeln.
                if (double.TryParse(textBoxAltMin.Text, out double result))
                {
                    // result enthält jetzt den Double-Wert aus dem TextBox.
                    // Verwenden Sie result für Ihre Berechnungen oder Anzeige.
                    if (double.TryParse(textBoxAltMax.Text, out double testresult))
                    {
                        if (result < testresult)
                        {
                            WpfPlot1.Plot.Axes.SetLimitsY(result, WpfPlot1.Plot.Axes.GetLimits().Top);
                            WpfPlot1.Refresh();
                        }
                        else
                        {
                            // Wenn die Umwandlung fehlschlägt, können Sie hier eine Fehlermeldung anzeigen.
                            MessageBox.Show("Invalid input. Upper limit max must be higher than lower limit.");
                        }

                    }
                    else
                    {
                        // Wenn die Umwandlung fehlschlägt, können Sie hier eine Fehlermeldung anzeigen.
                        MessageBox.Show("Invalid input. Please enter a valid number.");
                    }
                }
                else
                {
                    // Wenn die Umwandlung fehlschlägt, können Sie hier eine Fehlermeldung anzeigen.
                    MessageBox.Show("Invalid input. Please enter a valid number.");
                }
                //Textboxen mit Achsenlimits auktualisieren
                RefreshAxisTextBoxes();
            }
        }

        private void textBoxTempMax_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Versuchen Sie, den Text aus dem TextBox in eine Double-Zahl umzuwandeln.
                if (double.TryParse(textBoxTempMax.Text, out double result))
                {
                    // result enthält jetzt den Double-Wert aus dem TextBox.
                    // Verwenden Sie result für Ihre Berechnungen oder Anzeige.
                    if (double.TryParse(textBoxTempMin.Text, out double testresult))
                    {
                        if (result > testresult)
                        {
                            WpfPlot1.Plot.Axes.SetLimitsY(GetAxisLimits(yAxisTemp).Bottom, result, yAxisTemp);
                            WpfPlot1.Refresh();
                        }
                        else
                        {
                            // Wenn die Umwandlung fehlschlägt, können Sie hier eine Fehlermeldung anzeigen.
                            MessageBox.Show("Invalid input. Upper limit max must be higher than lower limit.");
                        }

                    }
                    else
                    {
                        // Wenn die Umwandlung fehlschlägt, können Sie hier eine Fehlermeldung anzeigen.
                        MessageBox.Show("Invalid input. Please enter a valid number.");
                    }
                }
                else
                {
                    // Wenn die Umwandlung fehlschlägt, können Sie hier eine Fehlermeldung anzeigen.
                    MessageBox.Show("Invalid input. Please enter a valid number.");
                }
                //Textboxen mit Achsenlimits auktualisieren
                RefreshAxisTextBoxes();
            }
        }

        private void textBoxTempMin_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Versuchen Sie, den Text aus dem TextBox in eine Double-Zahl umzuwandeln.
                if (double.TryParse(textBoxTempMin.Text, out double result))
                {
                    // result enthält jetzt den Double-Wert aus dem TextBox.
                    // Verwenden Sie result für Ihre Berechnungen oder Anzeige.
                    if (double.TryParse(textBoxTempMax.Text, out double testresult))
                    {
                        if (result < testresult)
                        {
                            WpfPlot1.Plot.Axes.SetLimitsY(result, GetAxisLimits(yAxisTemp).Top, yAxisTemp);
                            WpfPlot1.Refresh();
                        }
                        else
                        {
                            // Wenn die Umwandlung fehlschlägt, können Sie hier eine Fehlermeldung anzeigen.
                            MessageBox.Show("Invalid input. Upper limit max must be higher than lower limit.");
                        }

                    }
                    else
                    {
                        // Wenn die Umwandlung fehlschlägt, können Sie hier eine Fehlermeldung anzeigen.
                        MessageBox.Show("Invalid input. Please enter a valid number.");
                    }
                }
                else
                {
                    // Wenn die Umwandlung fehlschlägt, können Sie hier eine Fehlermeldung anzeigen.
                    MessageBox.Show("Invalid input. Please enter a valid number.");
                }
                //Textboxen mit Achsenlimits auktualisieren
                RefreshAxisTextBoxes();
            }
        }

        private void textBoxAccMax_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Versuchen Sie, den Text aus dem TextBox in eine Double-Zahl umzuwandeln.
                if (double.TryParse(textBoxAccMax.Text, out double result))
                {
                    // result enthält jetzt den Double-Wert aus dem TextBox.
                    // Verwenden Sie result für Ihre Berechnungen oder Anzeige.
                    if (double.TryParse(textBoxAccMin.Text, out double testresult))
                    {
                        if (result > testresult)
                        {
                            WpfPlot1.Plot.Axes.SetLimitsY(GetAxisLimits(yAxisAcc).Bottom, result, yAxisAcc);
                            WpfPlot1.Refresh();
                        }
                        else
                        {
                            // Wenn die Umwandlung fehlschlägt, können Sie hier eine Fehlermeldung anzeigen.
                            MessageBox.Show("Invalid input. Upper limit max must be higher than lower limit.");
                        }

                    }
                    else
                    {
                        // Wenn die Umwandlung fehlschlägt, können Sie hier eine Fehlermeldung anzeigen.
                        MessageBox.Show("Invalid input. Please enter a valid number.");
                    }
                }
                else
                {
                    // Wenn die Umwandlung fehlschlägt, können Sie hier eine Fehlermeldung anzeigen.
                    MessageBox.Show("Invalid input. Please enter a valid number.");
                }
                //Textboxen mit Achsenlimits auktualisieren
                RefreshAxisTextBoxes();
            }
        }

        private void textBoxSmoothing_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            if (!(sender is TextBox textBox) || !(textBox.Tag is string tag))
            {
                return;
            }

            if (!double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double seconds))
            {
                UpdateSmoothingTextBoxes();
                return;
            }

            double original = seconds;
            seconds = ClampSmoothingSeconds(tag, seconds);

            if (string.Equals(tag, "Temp", StringComparison.OrdinalIgnoreCase) && original > 0 && original < 60)
            {
                MessageBox.Show("Temperature smoothing minimum is 60 seconds.", "Smoothing", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            if (original > MaxSmoothingSeconds)
            {
                MessageBox.Show($"Smoothing is limited to {MaxSmoothingSeconds:0} seconds.", "Smoothing", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            SetSmoothingSeconds(tag, seconds);
            UpdateSmoothingTextBoxes();
            ApplySmoothingToSeries(tag);
        }

        private void textBoxAccMin_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Versuchen Sie, den Text aus dem TextBox in eine Double-Zahl umzuwandeln.
                if (double.TryParse(textBoxAccMin.Text, out double result))
                {
                    // result enthält jetzt den Double-Wert aus dem TextBox.
                    // Verwenden Sie result für Ihre Berechnungen oder Anzeige.
                    if (double.TryParse(textBoxAccMax.Text, out double testresult))
                    {
                        if (result < testresult)
                        {
                            WpfPlot1.Plot.Axes.SetLimitsY(result, GetAxisLimits(yAxisAcc).Top, yAxisAcc);
                            WpfPlot1.Refresh();
                        }
                        else
                        {
                            // Wenn die Umwandlung fehlschlägt, können Sie hier eine Fehlermeldung anzeigen.
                            MessageBox.Show("Invalid input. Upper limit max must be higher than lower limit.");
                        }

                    }
                    else
                    {
                        // Wenn die Umwandlung fehlschlägt, können Sie hier eine Fehlermeldung anzeigen.
                        MessageBox.Show("Invalid input. Please enter a valid number.");
                    }
                }
                else
                {
                    // Wenn die Umwandlung fehlschlägt, können Sie hier eine Fehlermeldung anzeigen.
                    MessageBox.Show("Invalid input. Please enter a valid number.");
                }
                //Textboxen mit Achsenlimits auktualisieren
                RefreshAxisTextBoxes();
            }
        }


        //---------------------------- Achsenbuttons ------------------------------------------------

        private void buttonLimitAltUp_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            timer.Start();
            buttonAltUpPressed = true;
            //WpfPlot1.Plot.Axes.SetLimitsY(WpfPlot1.Plot.Axes.GetLimits().Bottom-1, WpfPlot1.Plot.Axes.GetLimits().Top-1);
            WpfPlot1.Plot.Axes.SetLimitsY(WpfPlot1.Plot.Axes.GetLimits().Bottom - (WpfPlot1.Plot.Axes.GetLimits().Top - WpfPlot1.Plot.Axes.GetLimits().Bottom) / 50, WpfPlot1.Plot.Axes.GetLimits().Top - (WpfPlot1.Plot.Axes.GetLimits().Top - WpfPlot1.Plot.Axes.GetLimits().Bottom) / 50);
            WpfPlot1.Refresh();
            RefreshAxisTextBoxes();
        }

        private void buttonLimitAltDown_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            timer.Start();
            buttonAltDownPressed = true;
            //WpfPlot1.Plot.Axes.SetLimitsY(WpfPlot1.Plot.Axes.GetLimits().Bottom + 1, WpfPlot1.Plot.Axes.GetLimits().Top + 1);
            WpfPlot1.Plot.Axes.SetLimitsY(WpfPlot1.Plot.Axes.GetLimits().Bottom + (WpfPlot1.Plot.Axes.GetLimits().Top - WpfPlot1.Plot.Axes.GetLimits().Bottom) / 50, WpfPlot1.Plot.Axes.GetLimits().Top + (WpfPlot1.Plot.Axes.GetLimits().Top - WpfPlot1.Plot.Axes.GetLimits().Bottom) / 50);
            WpfPlot1.Refresh();
            RefreshAxisTextBoxes();
        }

        private void buttonLimitTempUp_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            timer.Start();
            buttonTempUpPressed = true;
            //WpfPlot1.Plot.Axes.SetLimitsY(GetAxisLimits(yAxisTemp).Bottom - 1, GetAxisLimits(yAxisTemp).Top - 1,yAxisTemp);
            WpfPlot1.Plot.Axes.SetLimitsY(GetAxisLimits(yAxisTemp).Bottom - (GetAxisLimits(yAxisTemp).Top - GetAxisLimits(yAxisTemp).Bottom) / 50, GetAxisLimits(yAxisTemp).Top - (GetAxisLimits(yAxisTemp).Top - GetAxisLimits(yAxisTemp).Bottom) / 50, yAxisTemp);
            WpfPlot1.Refresh();
            RefreshAxisTextBoxes();
        }

        private void buttonLimitTempDown_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            timer.Start();
            buttonTempDownPressed = true;
            //WpfPlot1.Plot.Axes.SetLimitsY(WpfPlot1.Plot.GetAxisLimits(0,yAxisTemp).Bottom + 1, GetAxisLimits(yAxisTemp).Top + 1, yAxisTemp);
            WpfPlot1.Plot.Axes.SetLimitsY(GetAxisLimits(yAxisTemp).Bottom + (GetAxisLimits(yAxisTemp).Top - GetAxisLimits(yAxisTemp).Bottom) / 50, GetAxisLimits(yAxisTemp).Top + (GetAxisLimits(yAxisTemp).Top - GetAxisLimits(yAxisTemp).Bottom) / 50, yAxisTemp);
            WpfPlot1.Refresh();
            RefreshAxisTextBoxes();
        }

        private void buttonLimitAccUp_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            timer.Start();
            buttonAccUpPressed = true;
            //WpfPlot1.Plot.Axes.SetLimitsY(GetAxisLimits(yAxisAcc).Bottom - 1, GetAxisLimits(yAxisAcc).Top - 1, yAxisAcc);
            WpfPlot1.Plot.Axes.SetLimitsY(GetAxisLimits(yAxisAcc).Bottom - (GetAxisLimits(yAxisAcc).Top - GetAxisLimits(yAxisAcc).Bottom) / 50, GetAxisLimits(yAxisAcc).Top - (GetAxisLimits(yAxisAcc).Top - GetAxisLimits(yAxisAcc).Bottom) / 50, yAxisAcc);
            WpfPlot1.Refresh();
            RefreshAxisTextBoxes();
        }

        private void buttonLimitAccDown_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            timer.Start();
            buttonAccDownPressed = true;
            //WpfPlot1.Plot.Axes.SetLimitsY(GetAxisLimits(yAxisAcc).Bottom + 1, GetAxisLimits(yAxisAcc).Top + 1, yAxisAcc);
            WpfPlot1.Plot.Axes.SetLimitsY(GetAxisLimits(yAxisAcc).Bottom + (GetAxisLimits(yAxisAcc).Top - GetAxisLimits(yAxisAcc).Bottom) / 50, GetAxisLimits(yAxisAcc).Top + (GetAxisLimits(yAxisAcc).Top - GetAxisLimits(yAxisAcc).Bottom) / 50, yAxisAcc);
            WpfPlot1.Refresh();
            RefreshAxisTextBoxes();
        }

        private void buttonLimitAltUp_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            buttonAltUpPressed = false;
            timer.Stop();
            timer.Interval = TimeSpan.FromSeconds(passiveTime);
        }

        private async Task RefreshDeviceListAsync(bool showNoDevicesMessage)
        {
            if (isRefreshingDevices)
            {
                return;
            }

            isRefreshingDevices = true;
            if (buttonRefreshDeviceList != null)
            {
                buttonRefreshDeviceList.IsEnabled = false;
            }

            try
            {
                //Suche COM-Ports mit SI-TL
                List<string> discoveredPorts = await Task.Run(() => ComPortChecker.FindValidPorts());
                validPorts = discoveredPorts ?? new List<string>();

                if (validPorts.Count == 0 && showNoDevicesMessage)
                {
                    MessageBox.Show("No compatible devices found.");
                }

                HashSet<string> discoveredSet = new HashSet<string>(validPorts, StringComparer.OrdinalIgnoreCase);
                HashSet<string> existingSet = new HashSet<string>(serialPortManagers.Keys, StringComparer.OrdinalIgnoreCase);

                foreach (string removedPort in existingSet.Except(discoveredSet).ToList())
                {
                    if (serialPortManagers.TryGetValue(removedPort, out SerialPortManager manager))
                    {
                        manager.DataReceived -= OnDataReceived;
                        manager.Dispose();
                        serialPortManagers.Remove(removedPort);
                    }

                    TreeViewManager.RemoveTreeViewItem(removedPort);
                    DataLoggerManager.RemoveLogger(removedPort);
                    measurementSeriesByPort.Remove(removedPort);

                    if (string.Equals(currentPortName, removedPort, StringComparison.OrdinalIgnoreCase))
                    {
                        currentPortName = null;
                        currentSeriesIndex = -1;
                    }
                }

                foreach (string addedPort in discoveredSet.Except(existingSet))
                {
                    // Datenlogger in Dictionary aufnehmen
                    DataLoggerManager.AddLogger(new DataLogger(), addedPort);
                    // Erstelle Port f?r den gefundenen Logger
                    SerialPortManager manager = new SerialPortManager(addedPort);
                    manager.DataReceived += OnDataReceived;
                    serialPortManagers[addedPort] = manager;

                    manager.OpenPort();
                    manager.SendCommand("I");
                }
            }
            catch (Exception ex)
            {
                if (showNoDevicesMessage)
                {
                    MessageBox.Show("Device scan failed: " + ex.Message);
                }
                else
                {
                    Debug.WriteLine("Device scan failed: " + ex.Message);
                }
            }
            finally
            {
                if (buttonRefreshDeviceList != null)
                {
                    buttonRefreshDeviceList.IsEnabled = true;
                }

                isRefreshingDevices = false;
            }
        }

        private async void buttonRefreshDeviceList_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDeviceListAsync(true);
        }

        private void buttonLimitAltDown_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            buttonAltDownPressed = false;
            timer.Stop();
            timer.Interval = TimeSpan.FromSeconds(passiveTime);
        }

        private void buttonLimitTempUp_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            buttonTempUpPressed = false;
            timer.Stop();
            timer.Interval = TimeSpan.FromSeconds(passiveTime);
        }

        private void buttonLimitTempDown_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            buttonTempDownPressed = false;
            timer.Stop();
            timer.Interval = TimeSpan.FromSeconds(passiveTime);
        }

        private void buttonLimitAccUp_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            buttonAccUpPressed = false;
            timer.Stop();
            timer.Interval = TimeSpan.FromSeconds(passiveTime);
        }

        private void buttonLimitAccDown_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            buttonAccDownPressed = false;
            timer.Stop();
            timer.Interval = TimeSpan.FromSeconds(passiveTime);
        }
        private void toggleButtonMarker_Checked(object sender, RoutedEventArgs e)
        {
            var limits = WpfPlot1.Plot.Axes.GetLimits();
            marker = WpfPlot1.Plot.Add.InteractiveMarker(new ScottPlot.Coordinates(
                limits.Left + ((limits.Right - limits.Left) / 2),
                limits.Bottom + ((limits.Top - limits.Bottom) / 2)));
            marker.MarkerStyle.Shape = MarkerShape.FilledTriangleDown;
            marker.MarkerStyle.Size = 15;
            marker.MarkerStyle.FillColor = Colors.Magenta;
            marker.MarkerStyle.LineColor = Colors.Magenta;
            WpfPlot1.Refresh();
        }
        private void toggleButtonMarker_Unchecked(object sender, RoutedEventArgs e)
        {
            if (marker != null)
            {
                WpfPlot1.Plot.Remove(marker);
                marker = null;
            }

            WpfPlot1.Refresh();
        }
        private void toggleButtonLegend_Checked(object sender, RoutedEventArgs e)
        {
            WpfPlot1.Plot.Legend.IsVisible = true;
            WpfPlot1.Refresh();
        }
        private void toggleButtonLegend_Unchecked(object sender, RoutedEventArgs e)
        {
            WpfPlot1.Plot.Legend.IsVisible = false;
            WpfPlot1.Refresh();
        }


        /*############################   COM PORT EVENTS        ###################################################*/

        private string BuildPacketPreview(string packet, int maxLength = 200)
        {
            if (string.IsNullOrEmpty(packet))
            {
                return "<empty>";
            }

            if (packet.Length <= maxLength)
            {
                return packet;
            }

            return packet.Substring(0, maxLength) + "...(len=" + packet.Length + ")";
        }

        //Evenhandler für DataReceived
        private void OnDataReceived(string portName, string[] data)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                return;
            }

            Debug.WriteLine("OnDataReceived " + portName + ": packets=" + (data?.Length ?? 0));
            if (data != null && data.Length > 0)
            {
                Debug.WriteLine("Packet[0] len=" + data[0]?.Length + " preview=" + BuildPacketPreview(data[0]));
            }

            if (data != null && data.Length > 3)
            {
                Debug.WriteLine("Packet[3] len=" + data[3]?.Length + " preview=" + BuildPacketPreview(data[3]));
            }

            if (serialPortManagers.TryGetValue(portName, out SerialPortManager manager))
            {
                manager.ClosePort();
            }

            Dispatcher.Invoke(() =>
            {
                if (data == null || data.Length == 0 || string.IsNullOrEmpty(data[0]))
                {
                    MessageBox.Show("Invalid response from " + portName);
                    return;
                }

                char echoCommand = data[0][0];
                switch (echoCommand)
                {
                    case 'I': // Header auslesen
                        if (data[0].Length < 89)
                        {
                            MessageBox.Show("Invalid header data from " + portName);
                            break;
                        }

                        if (!DataLoggerManager.dataLoggers.TryGetValue(portName, out DataLogger logger))
                        {
                            logger = new DataLogger();
                            DataLoggerManager.AddLogger(logger, portName);
                        }

                        logger.comPort = portName;
                        logger.checkSum = data[0].Substring(3, 2) + data[0].Substring(1, 2);
                        string serialHex = data[0].Substring(49, 4);
                        Debug.WriteLine("Header parse " + portName + ": serialHex=" + serialHex + " headerLen=" + data[0].Length);
                        logger.serialNumber = int.Parse(serialHex, NumberStyles.HexNumber).ToString();

                        switch (data[0].Substring(53, 2))
                        {
                            case "20": // Kennung 20 bedeutet Modell SI-TL1
                                logger.id = "SI-TL1";
                                break;
                            default:
                                logger.id = "Unknown";
                                break;
                        }

                        logger.productionDate = data[0].Substring(81, 2) + "." + data[0].Substring(83, 2) + "." + data[0].Substring(85, 4);

                        TreeViewManager.AddTreeViewItem(portName, logger.id + " No. " + logger.serialNumber + " (" + portName + ")");
                        UpdateDeviceInfo(portName);
                        break;
                    case 'S': // Letzte Aufnahme auslesen
                    case 'G': // Gesamten Speicher auslesen
                        if (data.Length <= 3 || string.IsNullOrEmpty(data[3]))
                        {
                            MessageBox.Show("Invalid data response from " + portName);
                            break;
                        }

                        List<Messreihe> series = DekodiereDatenpaket(data[3], portName);

                        //Pr?fe ob Daten enthalten sind (keine Daten => series = null)
                        if (series != null && series.Count > 0)
                        {
                            measurementSeriesByPort[portName] = series;
                            currentPortName = portName;
                            currentSeriesIndex = 0;
                            PlotData(series, 0); //Plotte Daten
                            TreeViewManager.SelectSubItem(portName, 0);
                            SyncSeriesStateFromTreeView(portName, 0);
                        }
                        else
                        {
                            // Wenn im Datenpaket keine verwertbaren Daten vorhanden sind, wird das hier abgefangen
                            MessageBox.Show("No data!");
                        }

                        break;
                    default:
                        // Umgang mit unbekanntem Echo-Befehl
                        break;
                }
            });
        }

        //Evenhandler wenn Fenster geschlossen wird
        private void Window_Closed(object sender, EventArgs e)
        {
            TreeViewManager.RequestCommand -= HandlePortCommand;
            TreeViewManager.TriggerPlotData -= TriggerPlotData;
            TreeViewManager.SeriesToggleChanged -= TreeViewManager_SeriesToggleChanged;
            StopDeviceMonitoring();
            // Beim Schlie?en der Anwendung alle COM-Ports schlie?en
            ClearSerialPortManagers();
        }

    }
}
