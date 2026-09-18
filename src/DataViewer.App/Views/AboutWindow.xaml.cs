using System.Windows;
using Siemert.DataViewer.App.Services;

namespace Siemert.DataViewer.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {AppInfo.Version}";
        PathsText.Text =
            "Rohdaten werden gesichert unter:\n" + AppInfo.RawArchiveDirectory +
            "\n\nProtokoll für den Kundendienst:\n" + AppInfo.LogDirectory;
    }
}
