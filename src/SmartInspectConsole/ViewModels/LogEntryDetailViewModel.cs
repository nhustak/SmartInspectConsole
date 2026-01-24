using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using SmartInspectConsole.Core.Packets;

namespace SmartInspectConsole.ViewModels;

/// <summary>
/// View model for displaying log entry details with smart payload formatting.
/// </summary>
public class LogEntryDetailViewModel : ViewModelBase
{
    private readonly LogEntry _logEntry;
    private string _formattedData = string.Empty;
    private string _detectedFormat = "Unknown";
    private bool _isFormatted = true;

    public LogEntryDetailViewModel(LogEntry logEntry)
    {
        _logEntry = logEntry ?? throw new ArgumentNullException(nameof(logEntry));
        DetectAndFormatPayload();
    }

    #region Properties

    public string TabTitle => $"Entry: {TruncateTitle(_logEntry.Title, 30)}";

    public string Title => _logEntry.Title;
    public string AppName => _logEntry.AppName;
    public string SessionName => _logEntry.SessionName;
    public string HostName => _logEntry.HostName;
    public DateTime Timestamp => _logEntry.Timestamp;
    public string LogEntryType => _logEntry.LogEntryType.ToString();
    public string ViewerId => _logEntry.ViewerId.ToString();
    public int ProcessId => _logEntry.ProcessId;
    public int ThreadId => _logEntry.ThreadId;
    public int DataSize => _logEntry.Data?.Length ?? 0;

    public string DetectedFormat
    {
        get => _detectedFormat;
        private set => SetProperty(ref _detectedFormat, value);
    }

    public string FormattedData
    {
        get => _formattedData;
        private set => SetProperty(ref _formattedData, value);
    }

    public bool IsFormatted
    {
        get => _isFormatted;
        set
        {
            if (SetProperty(ref _isFormatted, value))
            {
                DetectAndFormatPayload();
            }
        }
    }

    public bool HasData => _logEntry.Data != null && _logEntry.Data.Length > 0;

    public LogEntry LogEntry => _logEntry;

    #endregion

    #region Payload Detection and Formatting

    private void DetectAndFormatPayload()
    {
        if (_logEntry.Data == null || _logEntry.Data.Length == 0)
        {
            FormattedData = "(No data)";
            DetectedFormat = "Empty";
            return;
        }

        var rawText = Encoding.UTF8.GetString(_logEntry.Data);

        if (!_isFormatted)
        {
            FormattedData = rawText;
            return;
        }

        // Try to detect and format the payload
        if (TryFormatAsJson(rawText, out var jsonFormatted))
        {
            FormattedData = jsonFormatted;
            DetectedFormat = "JSON";
        }
        else if (TryFormatAsXml(rawText, out var xmlFormatted))
        {
            FormattedData = xmlFormatted;
            DetectedFormat = "XML";
        }
        else if (IsLikelyBinary(_logEntry.Data))
        {
            FormattedData = FormatAsBinaryHex(_logEntry.Data);
            DetectedFormat = "Binary";
        }
        else if (IsKeyValuePairs(rawText))
        {
            FormattedData = FormatKeyValuePairs(rawText);
            DetectedFormat = "Key-Value";
        }
        else
        {
            FormattedData = rawText;
            DetectedFormat = "Text";
        }
    }

    private static bool TryFormatAsJson(string text, out string formatted)
    {
        formatted = string.Empty;
        var trimmed = text.Trim();

        // Quick check for JSON-like structure
        if (!((trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
              (trimmed.StartsWith("[") && trimmed.EndsWith("]"))))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var options = new JsonSerializerOptions { WriteIndented = true };
            formatted = JsonSerializer.Serialize(doc.RootElement, options);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFormatAsXml(string text, out string formatted)
    {
        formatted = string.Empty;
        var trimmed = text.Trim();

        // Quick check for XML-like structure
        if (!trimmed.StartsWith("<"))
        {
            return false;
        }

        try
        {
            var doc = XDocument.Parse(trimmed);
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = !trimmed.StartsWith("<?xml")
            };

            using (var writer = XmlWriter.Create(sb, settings))
            {
                doc.WriteTo(writer);
            }

            formatted = sb.ToString();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLikelyBinary(byte[] data)
    {
        if (data.Length == 0) return false;

        // Check for non-printable characters (excluding common whitespace)
        int nonPrintableCount = 0;
        int checkLength = Math.Min(data.Length, 1000);

        for (int i = 0; i < checkLength; i++)
        {
            byte b = data[i];
            // Allow printable ASCII, tab, newline, carriage return
            if (b < 32 && b != 9 && b != 10 && b != 13)
            {
                nonPrintableCount++;
            }
            else if (b > 126)
            {
                // Could be UTF-8 multi-byte, but high ratio suggests binary
                nonPrintableCount++;
            }
        }

        // If more than 10% non-printable, treat as binary
        return (double)nonPrintableCount / checkLength > 0.1;
    }

    private static string FormatAsBinaryHex(byte[] data)
    {
        var sb = new StringBuilder();
        const int bytesPerLine = 16;

        for (int i = 0; i < data.Length; i += bytesPerLine)
        {
            // Offset
            sb.Append($"{i:X8}  ");

            // Hex bytes
            for (int j = 0; j < bytesPerLine; j++)
            {
                if (i + j < data.Length)
                {
                    sb.Append($"{data[i + j]:X2} ");
                }
                else
                {
                    sb.Append("   ");
                }

                if (j == 7) sb.Append(' ');
            }

            sb.Append(' ');

            // ASCII representation
            for (int j = 0; j < bytesPerLine && i + j < data.Length; j++)
            {
                byte b = data[i + j];
                sb.Append(b >= 32 && b < 127 ? (char)b : '.');
            }

            sb.AppendLine();

            // Limit output for very large data
            if (i > 10000)
            {
                sb.AppendLine($"... ({data.Length - i} more bytes)");
                break;
            }
        }

        return sb.ToString();
    }

    private static bool IsKeyValuePairs(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return false;

        int kvCount = 0;
        foreach (var line in lines)
        {
            if (line.Contains('=') || line.Contains(':'))
            {
                kvCount++;
            }
        }

        // At least 50% of lines should be key-value pairs
        return (double)kvCount / lines.Length >= 0.5;
    }

    private static string FormatKeyValuePairs(string text)
    {
        var sb = new StringBuilder();
        var lines = text.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Find separator (= or :)
            int eqIndex = trimmed.IndexOf('=');
            int colonIndex = trimmed.IndexOf(':');

            int sepIndex = -1;
            if (eqIndex >= 0 && colonIndex >= 0)
                sepIndex = Math.Min(eqIndex, colonIndex);
            else if (eqIndex >= 0)
                sepIndex = eqIndex;
            else if (colonIndex >= 0)
                sepIndex = colonIndex;

            if (sepIndex > 0)
            {
                var key = trimmed[..sepIndex].Trim();
                var value = trimmed[(sepIndex + 1)..].Trim();
                sb.AppendLine($"{key,-20} = {value}");
            }
            else
            {
                sb.AppendLine(trimmed);
            }
        }

        return sb.ToString();
    }

    private static string TruncateTitle(string title, int maxLength)
    {
        if (string.IsNullOrEmpty(title)) return "(untitled)";
        if (title.Length <= maxLength) return title;
        return title[..(maxLength - 3)] + "...";
    }

    #endregion
}
