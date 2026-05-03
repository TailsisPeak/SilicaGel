using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace SilicaGel;

/// <summary>
/// Converts a bool to a GridLength. true → 200px (problems panel open), false → 0.
/// </summary>
public sealed class BoolToHeightConverter : IValueConverter
{
    public static readonly BoolToHeightConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is bool v && v;
        return b ? new GridLength(220) : new GridLength(0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
