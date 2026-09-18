using System.Globalization;
using System.Windows;
using Siemert.DataViewer.App.Services;
using Siemert.DataViewer.Core.Analysis;

namespace Siemert.DataViewer.App.Views;

public partial class AltitudeReferenceWindow : Window
{
    private readonly AppSettings _settings;
    private readonly double? _groundPressure;
    private readonly double? _startPressure;
    private readonly bool _ready;

    public AltitudeReferenceWindow(AppSettings settings, double? groundPressureHpa, double? startPressureHpa = null)
    {
        InitializeComponent();
        _settings = settings;
        _groundPressure = groundPressureHpa;
        _startPressure = startPressureHpa;

        QnhBox.Text = settings.QnhHpa.ToString("F0", CultureInfo.CurrentCulture);
        ElevationBox.Text = settings.StationElevationM.ToString("F0", CultureInfo.CurrentCulture);
        TempCorrection.IsChecked = settings.TemperatureCorrection;

        switch (settings.AltitudeMode)
        {
            case AltitudeMode.MeanSeaLevel:
                OptMsl.IsChecked = true;
                break;
            case AltitudeMode.AboveGroundLevel:
                OptAgl.IsChecked = true;
                break;
            case AltitudeMode.PressureAltitude:
                OptPressure.IsChecked = true;
                break;
            case AltitudeMode.StartZero:
                OptStartZero.IsChecked = true;
                break;
            default:
                OptGroundZero.IsChecked = true;
                break;
        }

        _ready = true;
        UpdatePreview();
    }

    private AltitudeMode SelectedMode =>
        OptMsl.IsChecked == true ? AltitudeMode.MeanSeaLevel :
        OptAgl.IsChecked == true ? AltitudeMode.AboveGroundLevel :
        OptPressure.IsChecked == true ? AltitudeMode.PressureAltitude :
        OptStartZero.IsChecked == true ? AltitudeMode.StartZero :
        AltitudeMode.GroundZero;

    private void OnModeChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private void OnValueChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (!_ready)
        {
            return;
        }

        bool needsQnh = SelectedMode is AltitudeMode.MeanSeaLevel or AltitudeMode.AboveGroundLevel;
        QnhBox.IsEnabled = needsQnh;
        ElevationBox.IsEnabled = SelectedMode == AltitudeMode.AboveGroundLevel;

        AltitudeReference reference = Build();
        string text = reference.Describe();

        if (SelectedMode == AltitudeMode.StartZero && _startPressure is null)
        {
            text += "  Achtung: Für die gewählte Aufnahme liegt kein Anfangsdruck vor. " +
                    "Es wird ersatzweise gegen die Normatmosphäre gerechnet.";
        }

        if (SelectedMode == AltitudeMode.GroundZero && _groundPressure is null)
        {
            text += "  Achtung: Für die gewählte Aufnahme liegt kein Enddruck vor. " +
                    "Es wird ersatzweise gegen die Normatmosphäre gerechnet.";
        }

        if (SelectedMode == AltitudeMode.PressureAltitude)
        {
            text += "  Für die Beurteilung von Öffnungshöhen ist diese Einstellung ungeeignet.";
        }

        PreviewText.Text = text;
    }

    private AltitudeReference Build() => new()
    {
        Mode = SelectedMode,
        QnhHpa = ParseOr(QnhBox.Text, Atmosphere.StandardPressureHpa),
        StationElevationM = ParseOr(ElevationBox.Text, 0),
        GroundPressureHpa = _groundPressure,
        StartPressureHpa = _startPressure,
        ApplyTemperatureCorrection = TempCorrection.IsChecked == true
    };

    private static double ParseOr(string text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double v) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
            ? v
            : fallback;

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        double qnh = ParseOr(QnhBox.Text, Atmosphere.StandardPressureHpa);
        if (qnh < 850 || qnh > 1100)
        {
            MessageBox.Show(this,
                "Der eingetragene Luftdruck liegt außerhalb des plausiblen Bereichs von 850 bis 1100 hPa.",
                "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            QnhBox.Focus();
            QnhBox.SelectAll();
            return;
        }

        double elevation = ParseOr(ElevationBox.Text, 0);
        if (elevation < -500 || elevation > 5000)
        {
            MessageBox.Show(this,
                "Die Platzhöhe liegt außerhalb des plausiblen Bereichs von -500 bis 5000 m.",
                "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            ElevationBox.Focus();
            ElevationBox.SelectAll();
            return;
        }

        _settings.AltitudeMode = SelectedMode;
        _settings.QnhHpa = qnh;
        _settings.StationElevationM = elevation;
        _settings.TemperatureCorrection = TempCorrection.IsChecked == true;

        DialogResult = true;
    }
}
