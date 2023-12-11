using System;
using System.Collections.Generic;
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

        DispatcherTimer timer;
        const double passiveTime = 0.5;
        const double activeTime = 0.05;

        public MainWindow()
        {
            InitializeComponent();

            //Timer initialisieren
            InitTimer();

            //Testdaten für Entwicklung erzeugen
            (double[] xh, double[] yh) = DataGen.RandomWalk2D(new Random(0), 10000); //Testdaten Höhe
            (double[] xt, double[] yt) = DataGen.RandomWalk2D(new Random(1), 10000); //Testdaten Temperatur
            (double[] xa, double[] ya) = DataGen.RandomWalk2D(new Random(2), 10000); //Testdaten Beschleunigung

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
        private void RefreshTextBoxes()
        {
            //Textboxen für Achsenlimits aktualisieren
            textBoxAltMax.Text = WpfPlot1.Plot.GetAxisLimits(0, 0).YMax.ToString("F2");
            textBoxAltMin.Text = WpfPlot1.Plot.GetAxisLimits(0, 0).YMin.ToString("F2");
            textBoxTempMax.Text = WpfPlot1.Plot.GetAxisLimits(0, 2).YMax.ToString("F2");
            textBoxTempMin.Text = WpfPlot1.Plot.GetAxisLimits(0, 2).YMin.ToString("F2");
            textBoxAccMax.Text = WpfPlot1.Plot.GetAxisLimits(0, 3).YMax.ToString("F2");
            textBoxAccMin.Text = WpfPlot1.Plot.GetAxisLimits(0, 3).YMin.ToString("F2");
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
                //x1SpanPosTextBlock.Text = vLine1.X1.ToString();
            }
            else if (measuringSpan.X2 < measuringSpan.X1)
            {
                measuringSpan.X2 = e;
                //x2SpanPosTextBlock.Text = vLine1.X2.ToString();
            }
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
        }

        //Eventhandler wenn der Cursor auf der Zeitachse verschoben wird
        private void crosshairX_Dragged(object sender, EventArgs e)
        {
            //angelegt falls nötig
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
            RefreshTextBoxes();
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

            WpfPlot1.Refresh();
        }

        private void toggleButtonMeasuringCursor_Unchecked(object sender, RoutedEventArgs e)
        {
            WpfPlot1.Plot.Remove(measuringSpan);
            WpfPlot1.Refresh();
        }

        //Crosshair enable / disable
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
                RefreshTextBoxes();
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
                            RefreshTextBoxes();
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
                RefreshTextBoxes();
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
                RefreshTextBoxes();
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
                RefreshTextBoxes();
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
                RefreshTextBoxes();
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
                    if (double.TryParse(textBoxAltMax.Text, out double testresult))
                    {
                        if (result < testresult)
                        {
                            WpfPlot1.Plot.SetAxisLimitsY(result, WpfPlot1.Plot.GetAxisLimits(0, yAxisAcc.AxisIndex).YMin, yAxisAcc.AxisIndex);
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
                RefreshTextBoxes();
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

        private void toggleButtonCrosshair_Unchecked(object sender, RoutedEventArgs e)
        {
            WpfPlot1.Plot.Remove(crosshairX);
            WpfPlot1.Plot.Remove(crosshairAlt);
            WpfPlot1.Plot.Remove(crosshairTemp);
            WpfPlot1.Plot.Remove(crosshairAcc);
            WpfPlot1.Refresh();
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
