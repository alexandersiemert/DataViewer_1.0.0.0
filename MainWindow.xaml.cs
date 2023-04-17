using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
using ScottPlot;
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

        public MainWindow()
        {
            InitializeComponent();

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




            /*  DAS WAR ALLES NUR TESTCODE
             *  
            double[] dataX = new double[] { 1, 2, 3, 4, 5 };
            double[] dataY = new double[] { 1, 4, 9, 16, 25 };

            (double[] xs, double[] ys) = DataGen.RandomWalk2D(new Random(0), 200);

            var sp = WpfPlot1.Plot.AddScatter(xs, ys);
            sp.YAxisIndex = WpfPlot1.Plot.LeftAxis.AxisIndex;
            WpfPlot1.Plot.YAxis.Label("Altitude [m]", sp.Color, size: 12);
            WpfPlot1.Plot.YAxis.Color(sp.Color);
            sp.MarkerSize = 1;
            //WpfPlot1.Plot.Style(ScottPlot.Style.Default);

            (double[] xs2, double[] ys2) = DataGen.RandomWalk2D(new Random(0), 300);

            var sp2 = WpfPlot1.Plot.AddScatter(xs2, ys2);
            var yAxis3 = WpfPlot1.Plot.AddAxis(Edge.Right);
            sp2.YAxisIndex = yAxis3.AxisIndex;
            yAxis3.Label("Temperature [°C]", sp2.Color, size: 12);
            yAxis3.Color(sp2.Color);
            sp2.MarkerSize = 1;

            (double[] xs3, double[] ys3) = DataGen.RandomWalk2D(new Random(0), 1000);

            var sp3 = WpfPlot1.Plot.AddScatter(xs3, ys3);
            var yAxis4 = WpfPlot1.Plot.AddAxis(Edge.Right);
            sp3.YAxisIndex = yAxis4.AxisIndex;
            yAxis4.Label("Acceleration [g]", sp3.Color, size: 12);
            yAxis4.Color(sp3.Color);
            sp3.MarkerSize = 1;


            vLine1 = WpfPlot1.Plot.AddHorizontalSpan(20, 80);
            vLine1.DragEnabled = true;

            // place the marker at the first data point
            var marker = WpfPlot1.Plot.AddMarkerDraggable(xs[0], ys[0], MarkerShape.filledDiamond, 15, Color.Magenta);
            // constrain snapping to the array of data points
            marker.DragSnap = new ScottPlot.SnapLogic.Nearest2D(xs, ys);

            var snapDisabled = new ScottPlot.SnapLogic.NoSnap1D();
            var snapPos = new ScottPlot.SnapLogic.Nearest1D(xs);
            vLine1.DragSnap = new ScottPlot.SnapLogic.Independent2D(x: snapPos, y: snapDisabled);

            vLine1.Edge1Dragged += (s, e) =>
            {
                if (vLine1.X1 < vLine1.X2)
                {
                    vLine1.X1 = e;
                    //x1SpanPosTextBlock.Text = vLine1.X1.ToString();
                }
                else if (vLine1.X2 < vLine1.X1)
                {
                    vLine1.X2 = e;
                    //x2SpanPosTextBlock.Text = vLine1.X2.ToString();
                }

            };
            vLine1.Edge2Dragged += (s, e) =>
            {
                if (vLine1.X1 < vLine1.X2)
                {
                    vLine1.X2 = e;
                    //x2SpanPosTextBlock.Text = vLine1.X2.ToString();
                }
                else if (vLine1.X2 < vLine1.X1)
                {
                    vLine1.X1 = e;
                    //x1SpanPosTextBlock.Text = vLine1.X1.ToString();
                }
            };
           
            
*/

            WpfPlot1.Refresh();
            
        }


        //-----------------EVENTHANDLER-------------------------------------------

        // EventHandler für das Dragged-Ereignis

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

        private void crosshairX_Dragged(object sender, EventArgs e)
        {
            
        }
        private void crosshairAlt_Dragged(object sender, EventArgs e)
        {
            var draggedPixel = WpfPlot1.Plot.GetPixelY(crosshairAlt.Y);
            crosshairTemp.Y = WpfPlot1.Plot.GetCoordinateY(draggedPixel,yAxisTemp.AxisIndex);
            crosshairAcc.Y = WpfPlot1.Plot.GetCoordinateY(draggedPixel, yAxisAcc.AxisIndex);
        }

        //------------------BUTTON FUNKTIONEN---------------------------------------

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
