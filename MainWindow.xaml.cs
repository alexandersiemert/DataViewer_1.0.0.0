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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
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
        HSpan vLine1;

        private DispatcherTimer timer;
        private int currentIndex;
        private Rectangle[] rectangles;

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
            var pltAlt = WpfPlot1.Plot.AddScatter(xh, yh);
            pltAlt.YAxisIndex = WpfPlot1.Plot.LeftAxis.AxisIndex;
            WpfPlot1.Plot.YAxis.Label("Altitude [m]");
            pltAlt.MarkerSize = 1;
            pltAlt.Color = Color.Black;
            WpfPlot1.Plot.YAxis.Color(pltAlt.Color);

            //Daten für Temperatur zum Plot WpfPlot1 (in XAML definiert) hinzufügen
            var pltTemp = WpfPlot1.Plot.AddScatter(xh, yt);
            var yAxisTemp = WpfPlot1.Plot.AddAxis(Edge.Right);
            pltTemp.YAxisIndex = yAxisTemp.AxisIndex;
            yAxisTemp.Label("Temperature [°C]");
            pltTemp.MarkerSize = 1;
            pltTemp.Color = Color.Red;
            yAxisTemp.Color(pltTemp.Color);

            //Daten für Beschleunigung zum Plot WpfPlot1 (in XAML definiert) hinzufügen
            var pltAcc = WpfPlot1.Plot.AddScatter(xh, ya);
            var yAxisAcc = WpfPlot1.Plot.AddAxis(Edge.Right);
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
            // Array von Rectangles erstellen, die als Lichter dienen
            rectangles = new Rectangle[] { rectangle1, rectangle2, rectangle3, rectangle4, rectangle5 };

            // Timer für die Animation erstellen
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(500); // Intervall in Millisekunden (0,5 Sekunden)
            timer.Tick += Timer_Tick;
            timer.Start();


            WpfPlot1.Refresh();
            
        }

        private void Timer_Tick(object sender, System.EventArgs e)
        {
            // Aktuellen Zustand zurücksetzen
            rectangles[currentIndex].Fill = Brushes.Black;

            // Nächsten Zustand aktualisieren
            currentIndex = (currentIndex + 1) % rectangles.Length;
            rectangles[currentIndex].Fill = Brushes.Green;
        }
    }
}
