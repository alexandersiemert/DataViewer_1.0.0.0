using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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


        public MainWindow()
        {
            InitializeComponent();

            //Timer initialisieren
            InitTimer();

            //Testdaten für Entwicklung erzeugen
            (xh, yh) = DataGen.RandomWalk2D(new Random(0), 10000); //Testdaten Höhe
            (xt, yt) = DataGen.RandomWalk2D(new Random(1), 10000); //Testdaten Temperatur
            (xa, ya) = DataGen.RandomWalk2D(new Random(2), 10000); //Testdaten Beschleunigung

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
            textBoxMeasTempCursor1.Text = InterpolateY(xt, yt, measuringSpan.X1).ToString("F2");
            textBoxMeasTempCursor2.Text = InterpolateY(xt, yt, measuringSpan.X2).ToString("F2");

            //Textboxen für Messungen aktualisieren
            textBoxMeasAccCursor1.Text = InterpolateY(xa, ya, measuringSpan.X1).ToString("F2");
            textBoxMeasAccCursor2.Text = InterpolateY(xa, ya, measuringSpan.X2).ToString("F2");

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
            }
            else if(buttonAltDownPressed)
            {
                WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits().YMin + (WpfPlot1.Plot.GetAxisLimits().YMax - WpfPlot1.Plot.GetAxisLimits().YMin) / 50, WpfPlot1.Plot.GetAxisLimits().YMax + (WpfPlot1.Plot.GetAxisLimits().YMax - WpfPlot1.Plot.GetAxisLimits().YMin) / 50);
                WpfPlot1.Refresh();
            }
            else if (buttonTempUpPressed)
            {
                WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin - (WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin) / 50, WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax - (WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin) / 50, yAxisTemp.AxisIndex);
                WpfPlot1.Refresh();
            }
            else if (buttonTempDownPressed)
            {
                WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin + (WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin) / 50, WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax + (WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin) / 50, yAxisTemp.AxisIndex);
                WpfPlot1.Refresh();
            }
            else if (buttonAccUpPressed)
            {
                WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin - (WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin) / 50, WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax - (WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin) / 50, yAxisAcc.AxisIndex);
                WpfPlot1.Refresh();
            }
            else if (buttonAccDownPressed)
            {
                WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin + (WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin) / 50, WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax + (WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax - WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin) / 50, yAxisAcc.AxisIndex);
                WpfPlot1.Refresh();
            }
            //Textboxen mit Achsenlimits auktualisieren
            RefreshAxisTextBoxes();
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
                    if (double.TryParse(textBoxAltMax.Text, out double testresult))
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
                    if (double.TryParse(textBoxAltMax.Text, out double testresult))
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
                    if (double.TryParse(textBoxAltMax.Text, out double testresult))
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
            WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits().YMin-1, WpfPlot1.Plot.GetAxisLimits().YMax-1);
            WpfPlot1.Refresh();
        }

        private void buttonLimitAltDown_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            timer.Start();
            buttonAltDownPressed = true;
            WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits().YMin + 1, WpfPlot1.Plot.GetAxisLimits().YMax + 1);
            WpfPlot1.Refresh();
        }

        private void buttonLimitTempUp_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            timer.Start();
            buttonTempUpPressed = true;
            WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMin - 1, WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax - 1,yAxisTemp.AxisIndex);
            WpfPlot1.Refresh();
        }

        private void buttonLimitTempDown_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            timer.Start();
            buttonTempDownPressed = true;
            WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0,yAxisTemp.AxisIndex).YMin + 1, WpfPlot1.Plot.GetAxisLimits(0, yAxisTemp.AxisIndex).YMax + 1, yAxisTemp.AxisIndex);
            WpfPlot1.Refresh();
        }

        private void buttonLimitAccUp_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            timer.Start();
            buttonAccUpPressed = true;
            WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin - 1, WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax - 1, yAxisAcc.AxisIndex);
            WpfPlot1.Refresh();
        }

        private void buttonLimitAccDown_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            timer.Start();
            buttonAccDownPressed = true;
            WpfPlot1.Plot.SetAxisLimitsY(WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin + 1, WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMax + 1, yAxisAcc.AxisIndex);
            WpfPlot1.Refresh();
        }

        private void buttonLimitAltUp_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            buttonAltUpPressed = false;
            timer.Stop();
            timer.Interval = TimeSpan.FromSeconds(passiveTime);
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

    }
}
