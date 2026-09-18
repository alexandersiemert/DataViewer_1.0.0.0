using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Siemert.DataViewer.App.ViewModels;
using Siemert.DataViewer.Core.Analysis;
using Siemert.DataViewer.Core.Model;
using Siemert.DataViewer.Core.Units;

namespace Siemert.DataViewer.App.Views;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is bool v && v;
        if (Invert)
        {
            b = !b;
        }

        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool present = value is not null && (value is not string s || s.Length > 0);
        if (Invert)
        {
            present = !present;
        }

        return present ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class CountToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool any = value is int i && i > 0;
        if (Invert)
        {
            any = !any;
        }

        return any ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NoticeLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is NoticeLevel level
            ? level switch
            {
                NoticeLevel.Success => new SolidColorBrush(Color.FromRgb(0x1B, 0x7F, 0x4B)),
                NoticeLevel.Warning => new SolidColorBrush(Color.FromRgb(0x9A, 0x62, 0x06)),
                NoticeLevel.Error => new SolidColorBrush(Color.FromRgb(0xB3, 0x26, 0x1E)),
                _ => new SolidColorBrush(Color.FromRgb(0x4A, 0x58, 0x66))
            }
            : Brushes.Gray;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class EventKindToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DeviceEventKind k && k == DeviceEventKind.ConnectedToPc ? "Angeschlossen" : "Getrennt";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Formatiert eine Kennzahl für die Sprungübersicht. Fehlt der Wert, erscheint ein Gedankenstrich
/// statt einer erfundenen Zahl.
/// </summary>
public sealed class MetricConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double d || double.IsNaN(d))
        {
            return "-";
        }

        string kind = parameter as string ?? "raw";
        var units = values[1] as UnitSystem? ?? UnitSystem.Metric;
        SpeedUnit speedUnit = values.Length > 2 && values[2] is SpeedUnit su ? su : SpeedUnit.MetersPerSecond;

        return kind switch
        {
            "altitude" => UnitConverter.Altitude(d, units).ToString("N0", culture),
            "speed" => UnitConverter.Speed(d, speedUnit).ToString("N0", culture),
            "seconds" => d >= 60
                ? $"{(int)(d / 60)}:{(int)(d % 60):00}"
                : d.ToString("N0", culture),
            "g" => d.ToString("N1", culture),
            "temperature" => UnitConverter.Temperature(d, units).ToString("N1", culture),
            _ => d.ToString("N1", culture)
        };
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class UnitLabelConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var units = values.Length > 0 && values[0] is UnitSystem u ? u : UnitSystem.Metric;
        SpeedUnit speedUnit = values.Length > 1 && values[1] is SpeedUnit su ? su : SpeedUnit.MetersPerSecond;

        return (parameter as string) switch
        {
            "altitude" => UnitConverter.AltitudeUnitLabel(units),
            "speed" => UnitConverter.SpeedUnitLabel(speedUnit),
            "temperature" => UnitConverter.TemperatureUnitLabel(units),
            "g" => "g",
            "seconds" => "s",
            _ => string.Empty
        };
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class AltitudeModeToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is AltitudeMode m
            ? m switch
            {
                AltitudeMode.PressureAltitude => "Druckhöhe (Normatmosphäre)",
                AltitudeMode.MeanSeaLevel => "Höhe über NN (QNH)",
                AltitudeMode.AboveGroundLevel => "Höhe über Grund (QNH + Platzhöhe)",
                AltitudeMode.GroundZero => "Höhe über Landepunkt (empfohlen)",
                _ => m.ToString()
            }
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
