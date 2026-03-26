using System.Globalization;

namespace SmartInspectConsole.Services;

public static class TimeFormatSettings
{
    public const string TwentyFourHourShortFormat = "HH:mm:ss.fff";
    public const string TwelveHourShortFormat = "hh:mm:ss.fff tt";
    public const string TwentyFourHourFullFormat = "yyyy-MM-dd HH:mm:ss.fff";
    public const string TwelveHourFullFormat = "yyyy-MM-dd hh:mm:ss.fff tt";

    public static bool Use24HourTime { get; set; } = true;

    public static string ShortTimeFormat => Use24HourTime ? TwentyFourHourShortFormat : TwelveHourShortFormat;

    public static string FullTimestampFormat => Use24HourTime ? TwentyFourHourFullFormat : TwelveHourFullFormat;

    public static string FormatShort(DateTime value) => value.ToString(ShortTimeFormat, CultureInfo.CurrentCulture);

    public static string FormatFull(DateTime value) => value.ToString(FullTimestampFormat, CultureInfo.CurrentCulture);
}
