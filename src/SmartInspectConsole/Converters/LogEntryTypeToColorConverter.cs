using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SmartInspectConsole.Core.Enums;

namespace SmartInspectConsole.Converters;

/// <summary>
/// Converts LogEntryType to a color brush for visual distinction.
/// </summary>
public class LogEntryTypeToColorConverter : IValueConverter
{
    // Color palette for different log types
    private static readonly SolidColorBrush MessageBrush = new(Color.FromRgb(0x21, 0x96, 0xF3));     // Blue
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(0xFF, 0x98, 0x00));     // Orange
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xF4, 0x43, 0x36));       // Red
    private static readonly SolidColorBrush FatalBrush = new(Color.FromRgb(0x9C, 0x27, 0xB0));       // Purple
    private static readonly SolidColorBrush DebugBrush = new(Color.FromRgb(0x60, 0x7D, 0x8B));       // Blue-gray
    private static readonly SolidColorBrush VerboseBrush = new(Color.FromRgb(0x9E, 0x9E, 0x9E));     // Gray
    private static readonly SolidColorBrush MethodEnterBrush = new(Color.FromRgb(0x4C, 0xAF, 0x50)); // Green
    private static readonly SolidColorBrush MethodLeaveBrush = new(Color.FromRgb(0x8B, 0xC3, 0x4A)); // Light green
    private static readonly SolidColorBrush CheckpointBrush = new(Color.FromRgb(0x00, 0xBC, 0xD4));  // Cyan
    private static readonly SolidColorBrush AssertBrush = new(Color.FromRgb(0xFF, 0x57, 0x22));      // Deep orange
    private static readonly SolidColorBrush SeparatorBrush = new(Color.FromRgb(0x78, 0x78, 0x78));   // Dark gray
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(0xBD, 0xBD, 0xBD));     // Light gray

    static LogEntryTypeToColorConverter()
    {
        // Freeze brushes for performance
        MessageBrush.Freeze();
        WarningBrush.Freeze();
        ErrorBrush.Freeze();
        FatalBrush.Freeze();
        DebugBrush.Freeze();
        VerboseBrush.Freeze();
        MethodEnterBrush.Freeze();
        MethodLeaveBrush.Freeze();
        CheckpointBrush.Freeze();
        AssertBrush.Freeze();
        SeparatorBrush.Freeze();
        DefaultBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not LogEntryType logEntryType)
            return DefaultBrush;

        return logEntryType switch
        {
            LogEntryType.Message => MessageBrush,
            LogEntryType.Warning => WarningBrush,
            LogEntryType.Error => ErrorBrush,
            LogEntryType.Fatal => FatalBrush,
            LogEntryType.InternalError => ErrorBrush,
            LogEntryType.Debug => DebugBrush,
            LogEntryType.Verbose => VerboseBrush,
            LogEntryType.EnterMethod => MethodEnterBrush,
            LogEntryType.LeaveMethod => MethodLeaveBrush,
            LogEntryType.Checkpoint => CheckpointBrush,
            LogEntryType.Assert => AssertBrush,
            LogEntryType.Separator => SeparatorBrush,
            LogEntryType.Comment => MessageBrush,
            LogEntryType.Conditional => WarningBrush,
            _ => DefaultBrush
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
