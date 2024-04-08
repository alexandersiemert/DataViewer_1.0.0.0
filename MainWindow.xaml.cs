using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ScottPlot;
using ScottPlot.Drawing.Colormaps;
using ScottPlot.Plottable;
using ScottPlot.Renderable;
using ScottPlot.Styles;
using static ScottPlot.Plottable.PopulationPlot;
using Color = System.Drawing.Color;

namespace DataViewer_1._0._0._0
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        ScatterPlot pltAlt, pltTemp, pltAcc;

        Axis yAxisTemp, yAxisAcc;

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

        HSpan measuringSpan;

        VLine crosshairX;
        HLine crosshairAlt, crosshairTemp, crosshairAcc;

        DraggableMarkerPlot marker;

        bool buttonAltUpPressed = false;
        bool buttonAltDownPressed = false;
        bool buttonTempUpPressed = false;
        bool buttonTempDownPressed = false;
        bool buttonAccUpPressed = false;
        bool buttonAccDownPressed = false;

        bool buttonRefreshPressed = false;

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
        public static SerialPortManager serialPortManager;

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
        List<Messreihe> measurementSeries = new List<Messreihe>();

        public MainWindow()
        {
            InitializeComponent();

            //Timer initialisieren
            InitTimer();

            //TreeView initialisieren für DeviceList
            TreeViewManager.Initialize(deviceListTreeView);

            //Testdaten für Entwicklung erzeugen
            (xh, yh) = DataGen.RandomWalk2D(new Random(4), 10000); //Testdaten Höhe
            (xt, yt) = DataGen.RandomWalk2D(new Random(5), 10000); //Testdaten Temperatur
            (xa, ya) = DataGen.RandomWalk2D(new Random(6), 10000); //Testdaten Beschleunigung

            //Plot Titel festlegen
            WpfPlot1.Plot.Title("SI-TL1");

            //Daten für Höhe zum Plot WpfPlot1 (in XAML definiert) hinzufügen
            pltAlt = WpfPlot1.Plot.AddScatter(xh, yh, label: "Altitude");
            pltAlt.YAxisIndex = WpfPlot1.Plot.LeftAxis.AxisIndex;
            WpfPlot1.Plot.YAxis.Label("Altitude [m]");
            pltAlt.MarkerSize = 1;
            pltAlt.Color = Color.Black;
            WpfPlot1.Plot.YAxis.Color(pltAlt.Color);

            //Daten für Temperatur zum Plot WpfPlot1 (in XAML definiert) hinzufügen
            pltTemp = WpfPlot1.Plot.AddScatter(xh, yt, label: "Temperature");
            yAxisTemp = WpfPlot1.Plot.AddAxis(Edge.Right);
            pltTemp.YAxisIndex = yAxisTemp.AxisIndex;
            yAxisTemp.Label("Temperature [°C]");
            pltTemp.MarkerSize = 1;
            pltTemp.Color = Color.Red;
            yAxisTemp.Color(pltTemp.Color);

            //Daten für Beschleunigung zum Plot WpfPlot1 (in XAML definiert) hinzufügen
            pltAcc = WpfPlot1.Plot.AddScatter(xh, ya, label: "3-Axis Acceleration");
            yAxisAcc = WpfPlot1.Plot.AddAxis(Edge.Right);
            pltAcc.YAxisIndex = yAxisAcc.AxisIndex;
            yAxisAcc.Label("Acceleration [g]");
            pltAcc.MarkerSize = 1;
            pltAcc.Color = Color.Green;
            yAxisAcc.Color(pltAcc.Color);


            /****************EventHandler registrieren******************/

            //Plot mit Maus verschieben
            WpfPlot1.MouseMove += WpfPlot1_MouseMove;
            //Plot zoomen
            WpfPlot1.MouseWheel += WpfPlot1_MouseWheel;

            //Textboxen für Messungen leeren, weil da stehen sonst Fragezeichen drin und war zu faul das zu ändern :-D
            ClearMeasuringTextBoxes();

            //Plot neu zeichnen
            WpfPlot1.Refresh();
            
        }

        //################################################################################################################################
        //                                                   FUNKTIONEN
        //################################################################################################################################

        // Daten plotten
        private void PlotData(List<Messreihe> _messreihe, int index)
        {
            WpfPlot1.Plot.Clear();

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

            for(int i=0;i<dateTimeArray.Length;i++)
            {
                timeArray[i] = dateTimeArray[i].ToOADate();
            }

            double[] pressureArray = pressureList.ToArray();
            double[] heightArray = heightList.ToArray();
            double[] accXArray = accXList.ToArray();
            double[] accYArray = accYList.ToArray();
            double[] accZArray = accZList.ToArray();
            double[] tempArray = tempList.ToArray();

            // Schreiben der Werte in den public main Bereich um die Arrays außerhalb dieser Methode verfügbar zu machen (z.B. für Cursor Berechnungen)
            yh = heightArray;
            xh = timeArray;

            ya = accXArray;
            xa = timeArray;

            yt = tempArray;
            xt = timeArray;

            //Aktiviere DateTime Format für die X-Achse
            WpfPlot1.Plot.XAxis.DateTimeFormat(true);

            //Daten für Höhe zum Plot WpfPlot1 (in XAML definiert) hinzufügen
            pltAlt = WpfPlot1.Plot.AddScatter(timeArray, heightArray, label: "Altitude");
            pltAlt.YAxisIndex = WpfPlot1.Plot.LeftAxis.AxisIndex;
            WpfPlot1.Plot.YAxis.Label("Altitude [m]");
            pltAlt.MarkerSize = 1;
            pltAlt.Color = Color.Black;
            WpfPlot1.Plot.YAxis.Color(pltAlt.Color);

            //Daten für Temperatur zum Plot WpfPlot1 (in XAML definiert) hinzufügen
            pltTemp = WpfPlot1.Plot.AddScatter(timeArray, tempArray, label: "Temperature");
            //yAxisTemp = WpfPlot1.Plot.AddAxis(Edge.Right);
            pltTemp.YAxisIndex = yAxisTemp.AxisIndex;
            yAxisTemp.Label("Temperature [°C]");
            pltTemp.MarkerSize = 1;
            pltTemp.Color = Color.Red;
            yAxisTemp.Color(pltTemp.Color);

            //Daten für Beschleunigung zum Plot WpfPlot1 (in XAML definiert) hinzufügen
            pltAcc = WpfPlot1.Plot.AddScatter(timeArray, accXArray, label: "3-Axis Acceleration");
            //yAxisAcc = WpfPlot1.Plot.AddAxis(Edge.Right);
            pltAcc.YAxisIndex = yAxisAcc.AxisIndex;
            yAxisAcc.Label("Acceleration [g]");
            pltAcc.MarkerSize = 1;
            pltAcc.Color = Color.Green;
            yAxisAcc.Color(pltAcc.Color);

            WpfPlot1.Refresh();
        }
        
        // Timer initialisieren
        private void InitTimer()
        {
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(passiveTime);
            timer.Tick += new EventHandler(Timer_Tick);
        }


        //Textboxen für Achsenlimits aktualisieren
        private void RefreshAxisTextBoxes()
        {
            //Textboxen für Achsenlimits aktualisieren
            textBoxAltMax.Text = WpfPlot1.Plot.GetAxisLimits(0, 0).YMax.ToString("F2");
            textBoxAltMin.Text = WpfPlot1.Plot.GetAxisLimits(0, 0).YMin.ToString("F2");
            textBoxTempMax.Text = WpfPlot1.Plot.GetAxisLimits(0, 2).YMax.ToString("F2");
            textBoxTempMin.Text = WpfPlot1.Plot.GetAxisLimits(0, 2).YMin.ToString("F2");
            textBoxAccMax.Text = WpfPlot1.Plot.GetAxisLimits(0, 3).YMax.ToString("F2");
            textBoxAccMin.Text = WpfPlot1.Plot.GetAxisLimits(0, 3).YMin.ToString("F2");   
        }

        //Textboxen für Crosshair aktualisieren
        private void RefreshCrosshairTextBoxes()
        {
            //Textboxen für Crosshair aktualisieren
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
            //Textboxen für Crosshair leeren
            textBoxCrossAlt.Text = double.NaN.ToString();
            textBoxCrossTemp.Text = double.NaN.ToString();
            textBoxCrossAcc.Text = double.NaN.ToString();
            /* FREISCHALTEN WENN ES SOWEIT IST ###########################################################################################################################################
            textBoxCrossAccX.Text = double.NaN.ToString();
            textBoxCrossAccY.Text = double.NaN.ToString();
            textBoxCrossAccZ.Text = double.NaN.ToString();
            */
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

            // Methode zur linearen Interpolation für den Measuring Cursor um Y-Koordinate des Plots aus X-Koordinate des Messcursors zu bekommen
            private double InterpolateY(double[] xData, double[] yData, double xValue)
        {
            for (int i = 1; i < xData.Length; i++)
            {
                if (xValue < 0 || xData.Length != yData.Length || xData.Length == 0)
                {
                    return double.NaN; // Frühzeitige Rückkehr bei negativem xValue oder ungültigen Eingabedaten
                }

                if (xValue < xData[i])
                {
                    double slope = (yData[i] - yData[i - 1]) / (xData[i] - xData[i - 1]);
                    double yInterpolated = yData[i - 1] + slope * (xValue - xData[i - 1]);

                    return yInterpolated;
                }
            }
            return double.NaN; // X-Wert liegt außerhalb des Bereichs
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

        private List<Messreihe> DekodiereDatenpaket(string datenpaket)
        {
            List<Messreihe> messreihen = new List<Messreihe>();

            int splitStartIndex = datenpaket.IndexOf("AAAA");

            // Trennung der einzelnen Messreihen an der Anfangskodierung
            string[] einzelneReihen = datenpaket.Split(new[] { "AAAA" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var reihe in einzelneReihen)
            {
                if (string.IsNullOrEmpty(reihe) || splitStartIndex > 0 )
                {
                    continue; // Leere oder ungültige Reihe überspringen
                }

                
                var messreihe = new Messreihe();
                // Anfangsdatenfolge
                string anfangsdaten = reihe.Substring(0, 24); // Länge der Anfangsdaten
                messreihe.Startzeit = ParseDatumUndZeit(anfangsdaten.Substring(2, 14));
                messreihe.StartTemperatur = (HexZuDouble(anfangsdaten.Substring(16, 4))-500)/10;
                messreihe.StartDruck = HexZuDouble(anfangsdaten.Substring(20, 4))/10;

                // Extraktion und Verarbeitung der Messdaten
                int messdatenStartIndex = 24;
                int messdatenEndIndex = reihe.IndexOf("FFFF");
                string messdaten = reihe.Substring(messdatenStartIndex, messdatenEndIndex - messdatenStartIndex);// Exklusive Abschlussdaten

                //Verarbeitung Messdaten
                double temperatur = messreihe.StartTemperatur; // Starttemperatur aus der Anfangsdatenfolge
                int tempCounter = 1;
                int timeCounter = 0;
               

                for (int i = 0; i < messdaten.Length; i += 16)
                {
                    Debug.WriteLine(tempCounter.ToString() + "  " +  i.ToString() + "  " + messdaten.Substring(i, 16));

                    if (tempCounter >=240)
                    {
                        tempCounter = 0;
                        
                        Debug.WriteLine("TEMPERATUR");
                        temperatur = (HexZuDouble(messdaten.Substring(i, 4)) - 500)/ 10;
                        i += 4;
                    }


                    var daten = new Messdaten
                    {
                        Zeit = messreihe.Startzeit.AddMilliseconds(250*timeCounter),
                        Druck = HexZuDouble(messdaten.Substring(i, 4))/10,
                        Hoehe = Math.Round((288.15/0.0065)*(1-((HexZuDouble(messdaten.Substring(i, 4)) / 10) /1013.25))*0.190294957,2),
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
                messreihe.Status = abschlussdaten.Substring(0, 4);
                messreihe.Spannung = abschlussdaten.Substring(4, 4);
                messreihe.Endzeit = ParseDatumUndZeit(abschlussdaten.Substring(10, 14));
                messreihe.EndTemperatur = HexZuDouble(abschlussdaten.Substring(24, 4));
                messreihe.EndDruck = HexZuDouble(abschlussdaten.Substring(28, 4));

                messreihen.Add(messreihe);
             
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
        //                                                   EVENTHANDLER
        //################################################################################################################################

        // EventHandler für die Verschiebung des Plots mit der Maus
        private void WpfPlot1_MouseMove(object sender, MouseEventArgs e)
        {
            //Textboxen für Achsenlimits aktualisieren
            textBoxAltMax.Text = WpfPlot1.Plot.GetAxisLimits(0, 0).YMax.ToString("F2");
            textBoxAltMin.Text = WpfPlot1.Plot.GetAxisLimits(0, 0).YMin.ToString("F2");
            textBoxTempMax.Text = WpfPlot1.Plot.GetAxisLimits(0, 2).YMax.ToString("F2");
            textBoxTempMin.Text = WpfPlot1.Plot.GetAxisLimits(0, 2).YMin.ToString("F2");
            textBoxAccMax.Text = WpfPlot1.Plot.GetAxisLimits(0, 3).YMax.ToString("F2");
            textBoxAccMin.Text = WpfPlot1.Plot.GetAxisLimits(0, 3).YMin.ToString("F2");
        }

        // EventHandler für das Zoomen des Plots mit der Maus
        private void WpfPlot1_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            //Textboxen für Achsenlimits aktualisieren
            textBoxAltMax.Text = WpfPlot1.Plot.GetAxisLimits(0, 0).YMax.ToString("F2");
            textBoxAltMin.Text = WpfPlot1.Plot.GetAxisLimits(0, 0).YMin.ToString("F2");
            textBoxTempMax.Text = WpfPlot1.Plot.GetAxisLimits(0, 2).YMax.ToString("F2");
            textBoxTempMin.Text = WpfPlot1.Plot.GetAxisLimits(0, 2).YMin.ToString("F2");
            textBoxAccMax.Text = WpfPlot1.Plot.GetAxisLimits(0, 3).YMax.ToString("F2");
            textBoxAccMin.Text = WpfPlot1.Plot.GetAxisLimits(0, 3).YMin.ToString("F2");
        }


        // EventHandler für das Dragged-Ereignis des Messcursors

        private void measuringSpan_Edge1Dragged(object sender, double e)
        {
            if (measuringSpan.X1 < measuringSpan.X2)
            {
                measuringSpan.X1 = e;
            }
            else if (measuringSpan.X2 < measuringSpan.X1)
            {
                measuringSpan.X2 = e;
            }
            //Finde den X-Wert des Cursors
            indexCursor1 = FindIndex(xh, measuringSpan.X1);

            //Finde Min/Max Werte für Höhe
            minMaxAlt = FindMinMax(minMaxAlt, yh, indexCursor1, indexCursor2);
            textBoxMeasAltMin.Text = minMaxAlt.min.ToString("F2");
            textBoxMeasAltMax.Text = minMaxAlt.max.ToString("F2");

            //Finde Min/Max Werte für Temperatur
            minMaxTemp = FindMinMax(minMaxTemp, yt, indexCursor1, indexCursor2);
            textBoxMeasTempMin.Text = minMaxTemp.min.ToString("F2");
            textBoxMeasTempMax.Text = minMaxTemp.max.ToString("F2");

            //Finde Min/Max Werte für Beschleunigung
            minMaxAcc = FindMinMax(minMaxAcc, ya, indexCursor1, indexCursor2);
            textBoxMeasAccMin.Text = minMaxAcc.min.ToString("F2");
            textBoxMeasAccMax.Text = minMaxAcc.max.ToString("F2");

            /*
             * 
             * HIER NOCH XYZ BESCHLEUNIGUNGEN HINZUFÜGEN!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!###################################################
             * 
             */


            RefreshMeasuringTextBoxes();
        }

        private void measuringSpan_Edge2Dragged(object sender, double e)
        {
            if (measuringSpan.X1 < measuringSpan.X2)
            {
                measuringSpan.X2 = e;
                //x2SpanPosTextBlock.Text = vLine1.X2.ToString();
            }
            else if (measuringSpan.X2 < measuringSpan.X1)
            {
                measuringSpan.X1 = e;
                //x1SpanPosTextBlock.Text = vLine1.X1.ToString();
            }
            //Finde den X-Wert des Cursors
            indexCursor2 = FindIndex(xh, measuringSpan.X2);

            //Finde Min/Max Werte für Höhe
            minMaxAlt = FindMinMax(minMaxAlt, yh, indexCursor1, indexCursor2);
            textBoxMeasAltMin.Text = minMaxAlt.min.ToString("F2");
            textBoxMeasAltMax.Text = minMaxAlt.max.ToString("F2");

            //Finde Min/Max Werte für Temperatur
            minMaxTemp = FindMinMax(minMaxTemp, yt, indexCursor1, indexCursor2);
            textBoxMeasTempMin.Text = minMaxTemp.min.ToString("F2");
            textBoxMeasTempMax.Text = minMaxTemp.max.ToString("F2");

            //Finde Min/Max Werte für Beschleunigung
            minMaxAcc = FindMinMax(minMaxAcc, ya, indexCursor1, indexCursor2);
            textBoxMeasAccMin.Text = minMaxAcc.min.ToString("F2");
            textBoxMeasAccMax.Text = minMaxAcc.max.ToString("F2");

            /*
             * 
             * HIER NOCH MIN/MAX XYZ BESCHLEUNIGUNGEN HINZUFÜGEN!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!###############################################
             * 
             */

            RefreshMeasuringTextBoxes();
        }

        //Eventhandler wenn der X-Achsen Crosshair auf der Zeitachse verschoben wird
        private void crosshairX_Dragged(object sender, EventArgs e)
        {
            //Während der Verschiebeung des X-Achsen Crosshairs laufend die Textboxen mit den aktuellen Y-Werten an der X-Position füllen
            RefreshCrosshairTextBoxes();
        }

        //Eventhandler wenn der Cursor auf der Y-Achse verschoben wird
        private void crosshairAlt_Dragged(object sender, EventArgs e)
        {
            var draggedPixel = WpfPlot1.Plot.GetPixelY(crosshairAlt.Y);
            crosshairTemp.Y = WpfPlot1.Plot.GetCoordinateY(draggedPixel,yAxisTemp.AxisIndex);
            crosshairAcc.Y = WpfPlot1.Plot.GetCoordinateY(draggedPixel, yAxisAcc.AxisIndex);
        }

       // Eventhandler wenn der Timer auslöst  - aktuell für Buttons um die Achsen zu verschieben wenn man den Button gedrückt hält
        private void Timer_Tick(object sender, EventArgs e)
        {
            timer.Interval = TimeSpan.FromSeconds(activeTime);
            if (buttonAltUpPressed)
            {
                WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits().YMin - (WpfPlot1.Plot.GetAxisLimits().YMax- WpfPlot1.Plot.GetAxisLimits().YMin)/50, WpfPlot1.Plot.GetAxisLimits().YMax - (WpfPlot1.Plot.GetAxisLimits().YMax - WpfPlot1.Plot.GetAxisLimits().YMin) / 50);
                WpfPlot1.Refresh();
                RefreshAxisTextBoxes();
            }
            else if(buttonAltDownPressed)
            {
                WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits().YMin + (WpfPlot1.Plot.GetAxisLimits().YMax - WpfPlot1.Plot.GetAxisLimits().YMin) / 50, WpfPlot1.Plot.GetAxisLimits().YMax + (WpfPlot1.Plot.GetAxisLimits().YMax - WpfPlot1.Plot.GetAxisLimits().YMin) / 50);
                WpfPlot1.Refresh();
                RefreshAxisTextBoxes();
            }
            else if (buttonTempUpPressed)
            {
                WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin - (WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin) / 50, WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax - (WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin) / 50, yAxisTemp.AxisIndex);
                WpfPlot1.Refresh();
                RefreshAxisTextBoxes();
            }
            else if (buttonTempDownPressed)
            {
                WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin + (WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin) / 50, WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax + (WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin) / 50, yAxisTemp.AxisIndex);
                WpfPlot1.Refresh();
                RefreshAxisTextBoxes();
            }
            else if (buttonAccUpPressed)
            {
                WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin - (WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin) / 50, WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax - (WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin) / 50, yAxisAcc.AxisIndex);
                WpfPlot1.Refresh();
                RefreshAxisTextBoxes();
            }
            else if (buttonAccDownPressed)
            {
                WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin + (WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin) / 50, WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax + (WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin) / 50, yAxisAcc.AxisIndex);
                WpfPlot1.Refresh();
                RefreshAxisTextBoxes();
            }
        }

        //------------------BUTTON EVENTS---------------------------------------

        //Measuring Cursor enable / disable
        private void toggleButtonMeasuringCursor_Checked(object sender, RoutedEventArgs e)
        {
            measuringSpan = WpfPlot1.Plot.AddHorizontalSpan((WpfPlot1.Plot.GetAxisLimits().XMin+((WpfPlot1.Plot.GetAxisLimits().XMax - WpfPlot1.Plot.GetAxisLimits().XMin) / 4)), (WpfPlot1.Plot.GetAxisLimits().XMax - ((WpfPlot1.Plot.GetAxisLimits().XMax - WpfPlot1.Plot.GetAxisLimits().XMin) / 4)), label: "Measuring Cursor");
            measuringSpan.DragEnabled = true;

            // Registriere einen EventHandler für das Dragged-Ereignis
            measuringSpan.Edge1Dragged += measuringSpan_Edge1Dragged;
            measuringSpan.Edge2Dragged += measuringSpan_Edge2Dragged;

            //Finde den X-Wert des Cursors 1 und 2
            indexCursor1 = FindIndex(xh, measuringSpan.X1);
            indexCursor2 = FindIndex(xh, measuringSpan.X2);

            //Finde Min/Max Werte für Höhe
            minMaxAlt = FindMinMax(minMaxAlt,yh,indexCursor1,indexCursor2);
            textBoxMeasAltMin.Text = minMaxAlt.min.ToString("F2");
            textBoxMeasAltMax.Text = minMaxAlt.max.ToString("F2");

            //Finde Min/Max Werte für Temperatur
            minMaxTemp = FindMinMax(minMaxTemp, yt, indexCursor1, indexCursor2);
            textBoxMeasTempMin.Text = minMaxTemp.min.ToString("F2");
            textBoxMeasTempMax.Text = minMaxTemp.max.ToString("F2");

            //Finde Min/Max Werte für Beschleunigung
            minMaxAcc = FindMinMax(minMaxAcc, ya, indexCursor1, indexCursor2);
            textBoxMeasAccMin.Text = minMaxAcc.min.ToString("F2");
            textBoxMeasAccMax.Text = minMaxAcc.max.ToString("F2");

            /*
             * 
             * HIER NOCH XYZ BESCHLEUNIGUNGEN HINZUFÜGEN!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!##########################################################
             * 
             */

            RefreshMeasuringTextBoxes(); //Textboxen der Messungen initial aktualisieren wenn der Messcursor aktiviert wird
            WpfPlot1.Refresh();
        }

        private void toggleButtonMeasuringCursor_Unchecked(object sender, RoutedEventArgs e)
        {
            WpfPlot1.Plot.Remove(measuringSpan);

            ClearMeasuringTextBoxes(); //Textboxen der Messungen leeren wenn der Messcursor deaktiviert wird
            WpfPlot1.Refresh();
        }

        //Crosshair enable
        private void toggleButtonCrosshair_Checked(object sender, RoutedEventArgs e)
        {
            crosshairX = WpfPlot1.Plot.AddVerticalLine(WpfPlot1.Plot.GetAxisLimits().XMin + (WpfPlot1.Plot.GetAxisLimits().XMax - WpfPlot1.Plot.GetAxisLimits().XMin) / 2, label: "Crosshair X-Axis");
            crosshairX.PositionLabel = true;
            crosshairX.PositionLabelBackground = crosshairX.Color;
            crosshairX.DragEnabled = true;

            crosshairAlt = WpfPlot1.Plot.AddHorizontalLine(WpfPlot1.Plot.GetAxisLimits().YMin + (WpfPlot1.Plot.GetAxisLimits().YMax - WpfPlot1.Plot.GetAxisLimits().YMin) / 2, color: Color.Black, label: "Crosshair Y-Axis");
            crosshairAlt.YAxisIndex = pltAlt.YAxisIndex;
            crosshairAlt.PositionLabel = true;
            crosshairAlt.PositionLabelBackground = crosshairAlt.Color;
            crosshairAlt.DragEnabled = true;

            crosshairTemp = WpfPlot1.Plot.AddHorizontalLine(WpfPlot1.Plot.GetCoordinateY(WpfPlot1.Plot.GetPixelY(crosshairAlt.Y), yAxisTemp.AxisIndex), color: Color.Transparent);
            crosshairTemp.YAxisIndex = pltTemp.YAxisIndex;
            crosshairTemp.PositionLabel = true;
            crosshairTemp.PositionLabelOppositeAxis = true;
            crosshairTemp.PositionLabelBackground = Color.Red;
            crosshairTemp.PositionLabelAxis = yAxisTemp;
            crosshairTemp.DragEnabled = true;

            crosshairAcc = WpfPlot1.Plot.AddHorizontalLine(WpfPlot1.Plot.GetCoordinateY(WpfPlot1.Plot.GetPixelY(crosshairAlt.Y), yAxisAcc.AxisIndex), color: Color.Transparent);
            crosshairAcc.YAxisIndex = pltAcc.YAxisIndex;
            crosshairAcc.PositionLabel = true;
            crosshairAcc.PositionLabelOppositeAxis = true;
            crosshairAcc.PositionLabelBackground = Color.Green;
            crosshairAcc.PositionLabelAxis = yAxisAcc;
            crosshairAcc.DragEnabled = true;

            // Registriere einen EventHandler für das Dragged-Ereignis
            crosshairX.Dragged += crosshairX_Dragged;
            crosshairAlt.Dragged += crosshairAlt_Dragged;

            //Textboxen für Cursorwerte initial refreshen
            RefreshCrosshairTextBoxes();

            //Plot refreshen
            WpfPlot1.Refresh();
        }

        //Crosshair disable
        private void toggleButtonCrosshair_Unchecked(object sender, RoutedEventArgs e)
        {
            //Crosshairs entfernen
            WpfPlot1.Plot.Remove(crosshairX);
            WpfPlot1.Plot.Remove(crosshairAlt);
            WpfPlot1.Plot.Remove(crosshairTemp);
            WpfPlot1.Plot.Remove(crosshairAcc);
            //Textboxen für Cursorwerte leeren (NaN)
            ClearCrosshairTextBoxes();
            //Plot refreshen
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
                            WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits().YMin, result);
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
                            WpfPlot1.Plot.SetAxisLimitsY(result, WpfPlot1.Plot.GetAxisLimits().YMax);
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
                            WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin, result, yAxisTemp.AxisIndex);
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
                            WpfPlot1.Plot.SetAxisLimitsY(result, WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax, yAxisTemp.AxisIndex);
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
                            WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin, result, yAxisAcc.AxisIndex);
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
                            WpfPlot1.Plot.SetAxisLimitsY(result, WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax, yAxisAcc.AxisIndex);
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
            //WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits().YMin-1, WpfPlot1.Plot.GetAxisLimits().YMax-1);
            WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits().YMin - (WpfPlot1.Plot.GetAxisLimits().YMax - WpfPlot1.Plot.GetAxisLimits().YMin) / 50, WpfPlot1.Plot.GetAxisLimits().YMax - (WpfPlot1.Plot.GetAxisLimits().YMax - WpfPlot1.Plot.GetAxisLimits().YMin) / 50);
            WpfPlot1.Refresh();
            RefreshAxisTextBoxes();
        }

        private void buttonLimitAltDown_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            timer.Start();
            buttonAltDownPressed = true;
            //WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits().YMin + 1, WpfPlot1.Plot.GetAxisLimits().YMax + 1);
            WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits().YMin + (WpfPlot1.Plot.GetAxisLimits().YMax - WpfPlot1.Plot.GetAxisLimits().YMin) / 50, WpfPlot1.Plot.GetAxisLimits().YMax + (WpfPlot1.Plot.GetAxisLimits().YMax - WpfPlot1.Plot.GetAxisLimits().YMin) / 50);
            WpfPlot1.Refresh();
            RefreshAxisTextBoxes();
        }

        private void buttonLimitTempUp_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            timer.Start();
            buttonTempUpPressed = true;
            //WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin - 1, WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax - 1,yAxisTemp.AxisIndex);
            WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin - (WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin) / 50, WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax - (WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin) / 50, yAxisTemp.AxisIndex);
            WpfPlot1.Refresh();
            RefreshAxisTextBoxes();
        }

        private void buttonLimitTempDown_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            timer.Start();
            buttonTempDownPressed = true;
            //WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0,yAxisTemp.AxisIndex).YMin + 1, WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax + 1, yAxisTemp.AxisIndex);
            WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin + (WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin) / 50, WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax + (WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin) / 50, yAxisTemp.AxisIndex);
            WpfPlot1.Refresh();
            RefreshAxisTextBoxes();
        }

        private void buttonLimitAccUp_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            timer.Start();
            buttonAccUpPressed = true;
            //WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin - 1, WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax - 1, yAxisAcc.AxisIndex);
            WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin - (WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin) / 50, WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax - (WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin) / 50, yAxisAcc.AxisIndex);
            WpfPlot1.Refresh();
            RefreshAxisTextBoxes();
        }

        private void buttonLimitAccDown_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            timer.Start();
            buttonAccDownPressed = true;
            //WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin + 1, WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax + 1, yAxisAcc.AxisIndex);
            WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin + (WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin) / 50, WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax + (WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin) / 50, yAxisAcc.AxisIndex);
            WpfPlot1.Refresh();
            RefreshAxisTextBoxes();
        }

        private void buttonLimitAltUp_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            buttonAltUpPressed = false;
            timer.Stop();
            timer.Interval = TimeSpan.FromSeconds(passiveTime);
        }

        private void buttonRefreshDeviceList_Click(object sender, RoutedEventArgs e)
        {
            //Status is refreshing
            buttonRefreshPressed = true;
            // DeviceListe leeren
            TreeViewManager.ClearTreeView();
            // DataLogger Dictionary Einträge löschen
            DataLoggerManager.ClearLoggers();
            //Suche COM-Ports mit SI-TL
            validPorts = ComPortChecker.FindValidPorts();
            if (validPorts != null)
            {
                foreach(string validPort in validPorts)
                {
                    // Datenlogger in Dictionary aufnehmen
                    DataLoggerManager.AddLogger(new DataLogger(), validPort);
                    // Erstelle Port für den gefundenen Logger
                    serialPortManager = new SerialPortManager(validPort);
                    serialPortManager.DataReceived += OnDataReceived;
                    serialPortManager.OpenPort();
                    serialPortManager.SendCommand("I");
                }
                
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
            buttonAccUpPressed = false;
            timer.Stop();
            timer.Interval = TimeSpan.FromSeconds(passiveTime);
        }

        
        private void toggleButtonMarker_Checked(object sender, RoutedEventArgs e)
        {
            // place the marker at the first data point
            marker = WpfPlot1.Plot.AddMarkerDraggable((WpfPlot1.Plot.GetAxisLimits().XMin + (WpfPlot1.Plot.GetAxisLimits().XMax - WpfPlot1.Plot.GetAxisLimits().XMin) / 2), (WpfPlot1.Plot.GetAxisLimits().YMin + (WpfPlot1.Plot.GetAxisLimits().YMax - WpfPlot1.Plot.GetAxisLimits().YMin) / 2), MarkerShape.filledTriangleDown, 15, Color.Magenta, label: "Marker");
            WpfPlot1.Refresh();
        }

        private void toggleButtonMarker_Unchecked(object sender, RoutedEventArgs e)
        {
            WpfPlot1.Plot.Remove(marker);
            WpfPlot1.Refresh();
        }

        private void toggleButtonLegend_Checked(object sender, RoutedEventArgs e)
        {
            WpfPlot1.Plot.Legend(true);
            WpfPlot1.Refresh();
        }

        private void toggleButtonLegend_Unchecked(object sender, RoutedEventArgs e)
        {
            WpfPlot1.Plot.Legend(false);
            WpfPlot1.Refresh();
        }

        /*############################   COM PORT EVENTS        ###################################################*/

        //Evenhandler für DataReceived
        private void OnDataReceived(string[] data)
        {
            serialPortManager.ClosePort();

            Dispatcher.Invoke(() =>
            {
                char echoCommand = data[0][0];
                switch (echoCommand)
                {
                    case 'I': // Header auslesen
                        if (buttonRefreshPressed) //Ausführen wenn DeviceList refreshed werden soll
                        {
                            buttonRefreshPressed = false;


                            DataLoggerManager.dataLoggers[serialPortManager.GetPortName()].comPort = serialPortManager.GetPortName();
                            DataLoggerManager.dataLoggers[serialPortManager.GetPortName()].checkSum = data[0].Substring(3, 2) + data[0].Substring(1, 2);
                            DataLoggerManager.dataLoggers[serialPortManager.GetPortName()].serialNumber = int.Parse(data[0].Substring(49, 4), NumberStyles.HexNumber).ToString();

                            switch (data[0].Substring(53, 2))
                            {
                                case "20": // Kennung 20 bedeutet Modell SI-TL1
                                    DataLoggerManager.dataLoggers[serialPortManager.GetPortName()].id = "SI-TL1";
                                    break;
                                default:
                                    // Umgang mit unbekannter ID
                                    break;
                            }

                            DataLoggerManager.dataLoggers[serialPortManager.GetPortName()].productionDate = data[0].Substring(81, 2) + "." + data[0].Substring(83, 2) + "." + data[0].Substring(85, 4);

                            TreeViewManager.AddTreeViewItem(serialPortManager.GetPortName(), DataLoggerManager.dataLoggers[serialPortManager.GetPortName()].id + " No. " + DataLoggerManager.dataLoggers[serialPortManager.GetPortName()].serialNumber + " (" + serialPortManager.GetPortName() + ")");

                            //###################################TESTCODE################################################################################################
                            //###################################TESTCODE################################################################################################
                            //###################################TESTCODE################################################################################################
                            //###################################TESTCODE################################################################################################
                            //###################################TESTCODE################################################################################################

                            textBoxModel.Text = DataLoggerManager.dataLoggers[serialPortManager.GetPortName()].id;
                            textBoxSerialNumber.Text = DataLoggerManager.dataLoggers[serialPortManager.GetPortName()].serialNumber;
                            textBoxProductionDate.Text = DataLoggerManager.dataLoggers[serialPortManager.GetPortName()].productionDate;
                            textBoxChecksum.Text = DataLoggerManager.dataLoggers[serialPortManager.GetPortName()].checkSum;

                            //###################################TESTCODE################################################################################################
                            //###################################TESTCODE################################################################################################
                            //###################################TESTCODE################################################################################################
                            //###################################TESTCODE################################################################################################
                            //###################################TESTCODE################################################################################################
                        }

                        break;
                    case 'S': // Letzte Aufnahme auslesen
                    case 'G': // Gesamten Speicher auslesen
                        measurementSeries = DekodiereDatenpaket(data[3]);
                        PlotData(measurementSeries, 1);
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
            // Beim Schließen der Anwendung den COM-Port schließen
            if (serialPortManager.IsOpen())
            {
                serialPortManager.ClosePort();
            }
        }

    }
}
