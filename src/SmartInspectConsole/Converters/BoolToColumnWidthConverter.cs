using System.Globalization;
using System.Windows.Data;

namespace SmartInspectConsole.Converters;

/// <summary>
/// Converts boolean to column width (parameter value when true, 0 when false).
/// </summary>
public class BoolToColumnWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue && boolValue)
        {
            // Return the width from parameter, default to 100
            if (parameter is double width)
                return width;

            if (parameter is string widthStr && double.TryParse(widthStr, out var parsedWidth))
                return parsedWidth;

            return 100.0;
        }

        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
