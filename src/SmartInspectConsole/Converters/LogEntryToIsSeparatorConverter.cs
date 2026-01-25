using System.Globalization;
using System.Windows;
using System.Windows.Data;
using SmartInspectConsole.Core.Enums;
using SmartInspectConsole.Core.Packets;

namespace SmartInspectConsole.Converters;

/// <summary>
/// Converts a LogEntry or LogEntryType to Visibility based on whether it's a Separator.
/// Use parameter "invert" to show non-separators instead.
/// </summary>
public class LogEntryToIsSeparatorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isSeparator = false;

        if (value is LogEntry logEntry)
        {
            isSeparator = logEntry.LogEntryType == LogEntryType.Separator;
        }
        else if (value is LogEntryType logEntryType)
        {
            isSeparator = logEntryType == LogEntryType.Separator;
        }

        // Check for invert parameter
        bool invert = parameter is string paramStr &&
                      paramStr.Equals("invert", StringComparison.OrdinalIgnoreCase);

        if (invert)
        {
            isSeparator = !isSeparator;
        }

        // Return appropriate type based on target
        if (targetType == typeof(Visibility))
        {
            return isSeparator ? Visibility.Visible : Visibility.Collapsed;
        }

        return isSeparator;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
