using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.Plottables.Interactive;
using Microsoft.Win32;

namespace DataViewer_1._0._0._0
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Scatter pltAlt, pltTemp, pltAcc;

        ScottPlot.AxisPanels.RightAxis yAxisTemp, yAxisAcc;

        //XY-Datenarray für Höhe
        double[] xh, yh;

        //XY-Datenarray für Temperatur
        double[] xt, yt;

        //XY-Datenarray für Beschleunigung
        double[] xa, ya; //Betrag 3-Achsen
        /* FREISCHALTEN WENN ES SOWEIT IST ##########################################################################################################################
        double[] xax, yax; //Beschleunigung X-Achse
        double[] xay, yay; //Beschleunigung Y-Achse
        double[] xaz, yaz; //Beschleunigung Z-Achse
        */

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

        bool buttonAltUpPressed = false;
        bool buttonAltDownPressed = false;
        bool buttonTempUpPressed = false;
        bool buttonTempDownPressed = false;
        bool buttonAccUpPressed = false;
        bool buttonAccDownPressed = false;
        private readonly Dictionary<string, SerialPortManager> serialPortManagers = new Dictionary<string, SerialPortManager>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Messreihe>> measurementSeriesByPort = new Dictionary<string, List<Messreihe>>(StringComparer.OrdinalIgnoreCase);
        private string currentPortName;
        private int currentSeriesIndex = -1;

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
        /* FREISCHALTEN WENN ES SOWEIT IST ###############################################################################################################################
        minMax minMaxAccX = new minMax();
        minMax minMaxAccY = new minMax();
        minMax minMaxAccZ = new minMax();
        */

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

            //TreeView initialisieren für DeviceList
            TreeViewManager.Initialize(deviceListTreeView);
            TreeViewManager.RequestCommand += HandlePortCommand;

            //Testdaten für Entwicklung erzeugen
            (xh, yh) = GenerateRandomWalk(10000, 4); //Testdaten Höhe
            (xt, yt) = GenerateRandomWalk(10000, 5); //Testdaten Temperatur
            (xa, ya) = GenerateRandomWalk(10000, 6); //Testdaten Beschleunigung

            //Plot Titel festlegen
            WpfPlot1.Plot.Title("SI-TL1");

            //Daten für Höhe zum Plot WpfPlot1 (in XAML definiert) hinzufügen
            pltAlt = WpfPlot1.Plot.Add.Scatter(xh, yh);
            pltAlt.LegendText = "Altitude";
            pltAlt.Axes = new ScottPlot.Axes { XAxis = WpfPlot1.Plot.Axes.Bottom, YAxis = WpfPlot1.Plot.Axes.Left };
            WpfPlot1.Plot.YLabel("Altitude [m]");
            pltAlt.MarkerSize = 0;
            pltAlt.Color = Colors.Black;
            if (WpfPlot1.Plot.Axes.Left is ScottPlot.AxisPanels.LeftAxis leftAxis)
            {
                leftAxis.LabelFontColor = pltAlt.Color;
            }

            //Daten für Temperatur zum Plot WpfPlot1 (in XAML definiert) hinzufügen
            yAxisTemp = WpfPlot1.Plot.Axes.AddRightAxis();
            pltTemp = WpfPlot1.Plot.Add.Scatter(xh, yt);
            pltTemp.LegendText = "Temperature";
            pltTemp.Axes = new ScottPlot.Axes { XAxis = WpfPlot1.Plot.Axes.Bottom, YAxis = yAxisTemp };
            yAxisTemp.LabelText = "Temperature [\u00b0C]";
            pltTemp.MarkerSize = 0;
            pltTemp.Color = Colors.Red;
            yAxisTemp.LabelFontColor = pltTemp.Color;

            //Daten für Beschleunigung zum Plot WpfPlot1 (in XAML definiert) hinzufügen
            yAxisAcc = WpfPlot1.Plot.Axes.AddRightAxis();
            pltAcc = WpfPlot1.Plot.Add.Scatter(xh, ya);
            pltAcc.LegendText = "3-Axis Acceleration";
            pltAcc.Axes = new ScottPlot.Axes { XAxis = WpfPlot1.Plot.Axes.Bottom, YAxis = yAxisAcc };
            yAxisAcc.LabelText = "Acceleration [g]";
            pltAcc.MarkerSize = 0;
            pltAcc.Color = Colors.Green;
            yAxisAcc.LabelFontColor = pltAcc.Color;

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

        private void ExportSeriesToCsv(Messreihe series, string filePath)
        {
            if (series == null)
            {
                throw new ArgumentNullException(nameof(series));
            }

            StringBuilder csv = new StringBuilder();
            csv.AppendLine("Timestamp;Pressure_hPa;Altitude_m;Temperature_C;AccX_g;AccY_g;AccZ_g;AccAbs_g");

            foreach (Messdaten data in series.Messungen)
            {
                double accAbs = Math.Sqrt(
                    (data.BeschleunigungX * data.BeschleunigungX) +
                    (data.BeschleunigungY * data.BeschleunigungY) +
                    (data.BeschleunigungZ * data.BeschleunigungZ));

                csv.Append(data.Zeit.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                csv.Append(';');
                csv.Append(data.Druck.ToString("F2", CultureInfo.InvariantCulture));
                csv.Append(';');
                csv.Append(data.Hoehe.ToString("F2", CultureInfo.InvariantCulture));
                csv.Append(';');
                csv.Append(data.Temperatur.ToString("F2", CultureInfo.InvariantCulture));
                csv.Append(';');
                csv.Append(data.BeschleunigungX.ToString("F3", CultureInfo.InvariantCulture));
                csv.Append(';');
                csv.Append(data.BeschleunigungY.ToString("F3", CultureInfo.InvariantCulture));
                csv.Append(';');
                csv.Append(data.BeschleunigungZ.ToString("F3", CultureInfo.InvariantCulture));
                csv.Append(';');
                csv.AppendLine(accAbs.ToString("F3", CultureInfo.InvariantCulture));
            }

            File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
        }

        //################################################################################################################################
        //                                                   FUNKTIONEN
        //################################################################################################################################

        // Daten plotten
        private void PlotData(List<Messreihe> _messreihe, int index)
        {
            if (_messreihe == null || _messreihe.Count == 0 || index < 0 || index >= _messreihe.Count)
            {
                MessageBox.Show("No data to plot.");
                return;
            }

            WpfPlot1.Plot.Clear();
            yAxisTemp = WpfPlot1.Plot.Axes.AddRightAxis();
            yAxisAcc = WpfPlot1.Plot.Axes.AddRightAxis();

            //Plot Titel festlegen
            WpfPlot1.Plot.Title(_messreihe[index].Startzeit.ToString());

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
            yh = heightArray;
            xh = timeArray;

            ya = accAbsArray;
            xa = timeArray;

            yt = tempArray;
            xt = timeArray;

            //Aktiviere DateTime Format f?r die X-Achse
            WpfPlot1.Plot.Axes.DateTimeTicksBottom();
            isDateTimeXAxis = true;

            //Daten f?r H?he zum Plot WpfPlot1 (in XAML definiert) hinzuf?gen
            pltAlt = WpfPlot1.Plot.Add.Scatter(timeArray, heightArray);
            pltAlt.LegendText = "Altitude";
            pltAlt.Axes = new ScottPlot.Axes { XAxis = WpfPlot1.Plot.Axes.Bottom, YAxis = WpfPlot1.Plot.Axes.Left };
            WpfPlot1.Plot.YLabel("Altitude [m]");
            pltAlt.MarkerSize = 0;
            pltAlt.Color = Colors.Black;
            if (WpfPlot1.Plot.Axes.Left is ScottPlot.AxisPanels.LeftAxis leftAxis)
            {
                leftAxis.LabelFontColor = pltAlt.Color;
            }

            //Daten f?r Temperatur zum Plot WpfPlot1 (in XAML definiert) hinzuf?gen
            pltTemp = WpfPlot1.Plot.Add.Scatter(timeArray, tempArray);
            pltTemp.LegendText = "Temperature";
            pltTemp.Axes = new ScottPlot.Axes { XAxis = WpfPlot1.Plot.Axes.Bottom, YAxis = yAxisTemp };
            yAxisTemp.LabelText = "Temperature [\u00b0C]";
            pltTemp.MarkerSize = 0;
            pltTemp.Color = Colors.Red;
            yAxisTemp.LabelFontColor = pltTemp.Color;

            //Daten f?r Beschleunigung zum Plot WpfPlot1 (in XAML definiert) hinzuf?gen
            pltAcc = WpfPlot1.Plot.Add.Scatter(timeArray, accAbsArray);
            pltAcc.LegendText = "3-Axis Acceleration";
            pltAcc.Axes = new ScottPlot.Axes { XAxis = WpfPlot1.Plot.Axes.Bottom, YAxis = yAxisAcc };
            yAxisAcc.LabelText = "Acceleration [g]";
            pltAcc.MarkerSize = 0;
            pltAcc.Color = Colors.Green;
            yAxisAcc.LabelFontColor = pltAcc.Color;

            WpfPlot1.Plot.Legend.IsVisible = false;

            WpfPlot1.Refresh();
        }

        // Timer initialisieren
        private void InitTimer()
        {
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(passiveTime);
            timer.Tick += new EventHandler(Timer_Tick);
        }

        private static (double[] xs, double[] ys) GenerateRandomWalk(int count, int seed)
        {
            double[] xs = new double[count];
            double[] ys = new double[count];
            Random random = new Random(seed);

            for (int i = 1; i < count; i++)
            {
                xs[i] = i;
                ys[i] = ys[i - 1] + (random.NextDouble() - 0.5);
            }

            return (xs, ys);
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
            /* FREISCHALTEN WENN ES SOWEIT IST #########################################################################################################################################
            textBoxCrossAccX.Text = InterpolateY(xax, yax, crosshairX.X).ToString("F2");
            textBoxCrossAccY.Text = InterpolateY(xay, yay, crosshairX.X).ToString("F2");
            textBoxCrossAccZ.Text = InterpolateY(xaz, yaz, crosshairX.X).ToString("F2");
            */
        }

        //Textboxen für Crosshair leeren
        private void ClearCrosshairTextBoxes()
        {
            textBoxCrossAlt.Text = double.NaN.ToString();
            textBoxCrossTemp.Text = double.NaN.ToString();
            textBoxCrossAcc.Text = double.NaN.ToString();
            /* FREISCHALTEN WENN ES SOWEIT IST ###########################################################################################################################################
            textBoxCrossAccX.Text = double.NaN.ToString();
            textBoxCrossAccY.Text = double.NaN.ToString();
            textBoxCrossAccZ.Text = double.NaN.ToString();
            */
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

        private static string GetSeriesUnit(string seriesName)
        {
            switch (seriesName)
            {
                case "Altitude":
                    return "m";
                case "Temperature":
                    return "\u00b0C";
                case "3-Axis Acceleration":
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
            //Textboxen für Messungen aktualisieren
            textBoxMeasAltCursor1.Text = InterpolateY(xh, yh, measuringSpan.X1).ToString("F2");
            textBoxMeasAltCursor2.Text = InterpolateY(xh, yh, measuringSpan.X2).ToString("F2");

            //Textboxen für Messungen aktualisieren
            textBoxMeasTempCursor1.Text = InterpolateY(xh, yt, measuringSpan.X1).ToString("F2");
            textBoxMeasTempCursor2.Text = InterpolateY(xh, yt, measuringSpan.X2).ToString("F2");

            //Textboxen für Messungen aktualisieren
            textBoxMeasAccCursor1.Text = InterpolateY(xh, ya, measuringSpan.X1).ToString("F2");
            textBoxMeasAccCursor2.Text = InterpolateY(xh, ya, measuringSpan.X2).ToString("F2");

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

            RefreshMeasuringTextBoxes();
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
                hoverLabel.LabelFontSize = 12;
                hoverLabel.OffsetX = 8;
                hoverLabel.OffsetY = -8;
                hoverLabel.IsVisible = false;
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
            TryUpdateBestCandidate(pltAcc, "3-Axis Acceleration", mousePixel, ref bestPoint, ref bestAxes, ref bestColor, ref bestSeries, ref bestDistance);

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
            if (xValue < 0 || xData == null || yData == null || xData.Length == 0 || xData.Length != yData.Length)
            {
                return double.NaN; // Fr?hzeitige R?ckkehr bei negativem xValue oder ung?ltigen Eingabedaten
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
            if (start < 0 || end > data.Length || start >= end)
            {
                Debug.WriteLine("Invalid indices");
                minMax.min = double.NaN;
                minMax.max = double.NaN;
                return minMax;
            }

            minMax.min = data[start];
            minMax.max = data[start];

            for (int i = start; i < end; i++)
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

        private List<Messreihe> DekodiereDatenpaket(string datenpaket, string portName)
        {
            if (string.IsNullOrEmpty(datenpaket))
            {
                return null;
            }

            //Pr?fe ob ?berhaupt der Beginn einer Aufnahme da ist mittels Anfangskodierung AAAA. Wenn nicht, ist keine g?ltige Aufnahme in den Daten vorhanden
            int splitStartIndex = datenpaket.IndexOf("AAAA", StringComparison.Ordinal);
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
                messreihe.Startzeit = ParseDatumUndZeit(anfangsdaten.Substring(2, 14));
                messreihe.StartTemperatur = (HexZuDouble(anfangsdaten.Substring(16, 4)) - 500) / 10;
                messreihe.StartDruck = HexZuDouble(anfangsdaten.Substring(20, 4)) / 10;

                // Extraktion und Verarbeitung der Messdaten
                int messdatenStartIndex = 24;
                int messdatenEndIndex = reihe.IndexOf("FFFF", StringComparison.Ordinal);
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
                        Hoehe = Math.Round((288.15 / 0.0065) * (1 - ((HexZuDouble(messdaten.Substring(i, 4)) / 10) / 1013.25)) * 0.190294957, 2),
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
                    messreihe.Endzeit = ParseDatumUndZeit(abschlussdaten.Substring(10, 14));
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

        private double CalculateAccelerationFromHex(string hexData)
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

        private void menuItemExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetCurrentSeries(out Messreihe series, out string portName, out _))
            {
                MessageBox.Show("No data to export.");
                return;
            }

            SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"{portName}_{series.Startzeit:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                ExportSeriesToCsv(series, dialog.FileName);
                MessageBox.Show("Export complete.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed: " + ex.Message);
            }
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

        private async void buttonRefreshDeviceList_Click(object sender, RoutedEventArgs e)
        {
            buttonRefreshDeviceList.IsEnabled = false;

            try
            {
                ClearSerialPortManagers();
                measurementSeriesByPort.Clear();
                currentPortName = null;
                currentSeriesIndex = -1;
                TreeViewManager.ClearTreeView();
                DataLoggerManager.ClearLoggers();

                //Suche COM-Ports mit SI-TL
                validPorts = await Task.Run(() => ComPortChecker.FindValidPorts());
                if (validPorts == null || validPorts.Count == 0)
                {
                    MessageBox.Show("No compatible devices found.");
                    return;
                }

                foreach (string validPort in validPorts)
                {
                    // Datenlogger in Dictionary aufnehmen
                    DataLoggerManager.AddLogger(new DataLogger(), validPort);
                    // Erstelle Port f?r den gefundenen Logger
                    SerialPortManager manager = new SerialPortManager(validPort);
                    manager.DataReceived += OnDataReceived;
                    serialPortManagers[validPort] = manager;

                    manager.OpenPort();
                    manager.SendCommand("I");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Device scan failed: " + ex.Message);
            }
            finally
            {
                buttonRefreshDeviceList.IsEnabled = true;
            }
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

        //Evenhandler für DataReceived
        private void OnDataReceived(string portName, string[] data)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                return;
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
                        logger.serialNumber = int.Parse(data[0].Substring(49, 4), NumberStyles.HexNumber).ToString();

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
            // Beim Schlie?en der Anwendung alle COM-Ports schlie?en
            ClearSerialPortManagers();
        }

    }
}
