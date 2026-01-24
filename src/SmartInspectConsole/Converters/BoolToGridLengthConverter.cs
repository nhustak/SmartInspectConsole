using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SmartInspectConsole.Converters;

/// <summary>
/// Converts boolean to GridLength. True = Star (1*), False = 0.
/// </summary>
public class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isVisible && isVisible)
        {
            // Parse parameter for star value, default to 1*
            if (parameter is string starValue && double.TryParse(starValue, out var stars))
            {
                return new GridLength(stars, GridUnitType.Star);
            }
            return new GridLength(1, GridUnitType.Star);
        }

        return new GridLength(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is GridLength gridLength)
        {
            return gridLength.Value > 0;
        }
        return false;
    }
}
