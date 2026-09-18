namespace Siemert.DataViewer.Core.Units;

public enum UnitSystem
{
    /// <summary>Meter, Grad Celsius.</summary>
    Metric,

    /// <summary>Fuss, Grad Fahrenheit.</summary>
    Imperial
}

/// <summary>
/// Einheit fuer die Vertikalgeschwindigkeit. Fallschirmspringer rechnen je nach Land und
/// Disziplin unterschiedlich, deshalb ist das getrennt vom Einheitensystem einstellbar.
/// </summary>
public enum SpeedUnit
{
    MetersPerSecond,
    KilometersPerHour,
    MilesPerHour,
    FeetPerMinute
}

public static class UnitConverter
{
    /// <summary>Exakter internationaler Fuss.</summary>
    public const double FeetPerMeter = 1.0 / 0.3048;

    public const double MetersPerFoot = 0.3048;

    public static double MetersToFeet(double m) => m * FeetPerMeter;

    public static double FeetToMeters(double ft) => ft * MetersPerFoot;

    public static double CelsiusToFahrenheit(double c) => (c * 9.0 / 5.0) + 32.0;

    public static double FahrenheitToCelsius(double f) => (f - 32.0) * 5.0 / 9.0;

    /// <summary>Temperaturdifferenz - ohne den Offset von 32 Grad.</summary>
    public static double CelsiusDeltaToFahrenheit(double d) => d * 9.0 / 5.0;

    public static double Altitude(double meters, UnitSystem target) =>
        target == UnitSystem.Metric ? meters : MetersToFeet(meters);

    public static double Temperature(double celsius, UnitSystem target) =>
        target == UnitSystem.Metric ? celsius : CelsiusToFahrenheit(celsius);

    /// <summary>Wandelt eine Vertikalgeschwindigkeit in m/s in die gewuenschte Anzeigeeinheit.</summary>
    public static double Speed(double metersPerSecond, SpeedUnit target) => target switch
    {
        SpeedUnit.MetersPerSecond => metersPerSecond,
        SpeedUnit.KilometersPerHour => metersPerSecond * 3.6,
        SpeedUnit.MilesPerHour => metersPerSecond * 2.2369362920544,
        SpeedUnit.FeetPerMinute => metersPerSecond * FeetPerMeter * 60.0,
        _ => metersPerSecond
    };

    public static string AltitudeUnitLabel(UnitSystem system) => system == UnitSystem.Metric ? "m" : "ft";

    public static string TemperatureUnitLabel(UnitSystem system) => system == UnitSystem.Metric ? "°C" : "°F";

    public static string SpeedUnitLabel(SpeedUnit unit) => unit switch
    {
        SpeedUnit.MetersPerSecond => "m/s",
        SpeedUnit.KilometersPerHour => "km/h",
        SpeedUnit.MilesPerHour => "mph",
        SpeedUnit.FeetPerMinute => "ft/min",
        _ => "m/s"
    };
}
