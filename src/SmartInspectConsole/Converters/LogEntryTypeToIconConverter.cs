using System.Globalization;
using System.Windows.Data;
using SmartInspectConsole.Core.Enums;

namespace SmartInspectConsole.Converters;

/// <summary>
/// Converts LogEntryType to an icon character.
/// </summary>
public class LogEntryTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not LogEntryType logEntryType)
            return "?";

        return logEntryType switch
        {
            LogEntryType.Message => "\u2709",      // Envelope
            LogEntryType.Warning => "\u26A0",      // Warning sign
            LogEntryType.Error => "\u2716",        // X mark
            LogEntryType.Fatal => "\u2620",        // Skull
            LogEntryType.Debug => "\u2699",        // Gear
            LogEntryType.Verbose => "\u270E",      // Pencil
            LogEntryType.InternalError => "\u26A0", // Warning
            LogEntryType.Comment => "\u270D",      // Writing hand
            LogEntryType.Checkpoint => "\u2713",   // Check mark
            LogEntryType.Conditional => "\u2753",  // Question mark
            LogEntryType.Assert => "\u2757",       // Exclamation
            LogEntryType.EnterMethod => "\u2192",  // Right arrow
            LogEntryType.LeaveMethod => "\u2190",  // Left arrow
            LogEntryType.Separator => "\u2500",    // Horizontal line
            LogEntryType.Text => "\u2261",         // Triple bar
            LogEntryType.Binary => "\u2630",       // Trigram
            LogEntryType.Graphic => "\u2318",      // Place of interest
            LogEntryType.Source => "\u2261",       // Triple bar
            LogEntryType.Object => "\u2B1B",       // Black square
            LogEntryType.WebContent => "\u2318",   // Place of interest
            LogEntryType.System => "\u2699",       // Gear
            LogEntryType.MemoryStatistic => "\u2623", // Biohazard
            LogEntryType.DatabaseResult => "\u2637",  // Trigram
            LogEntryType.DatabaseStructure => "\u2637", // Trigram
            LogEntryType.VariableValue => "\u2261", // Triple bar
            LogEntryType.ResetCallstack => "\u21BA", // Anticlockwise arrow
            _ => "\u2022"                          // Bullet
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
