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
using ScottPlot;
using ScottPlot.Plottable;
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

        public MainWindow()
        {
            InitializeComponent();

            double[] dataX = new double[] { 1, 2, 3, 4, 5 };
            double[] dataY = new double[] { 1, 4, 9, 16, 25 };

            (double[] xs, double[] ys) = DataGen.RandomWalk2D(new Random(0), 200);

            var sp = WpfPlot1.Plot.AddScatter(xs, ys);
            sp.MarkerSize = 1;
            WpfPlot1.Plot.Style(ScottPlot.Style.Default);

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


            WpfPlot1.Refresh();
        }
    }
}
