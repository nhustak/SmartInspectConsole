using System.Globalization;
using System.Windows.Data;

namespace SmartInspectConsole.Converters;

public class DateTimeFormatMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not DateTime timestamp)
            return string.Empty;

        var format = values.Length > 1 ? values[1] as string : null;
        if (string.IsNullOrWhiteSpace(format))
            return timestamp.ToString(culture);

        return timestamp.ToString(format, culture);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
