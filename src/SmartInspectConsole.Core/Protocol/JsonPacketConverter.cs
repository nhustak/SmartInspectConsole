using System.Drawing;
using System.Text;
using System.Text.Json;
using SmartInspectConsole.Core.Enums;
using SmartInspectConsole.Core.Packets;

namespace SmartInspectConsole.Core.Protocol;

/// <summary>
/// Converts JSON messages from browser clients to SmartInspect packet objects.
/// </summary>
public static class JsonPacketConverter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Parses a JSON message and returns the appropriate packet object.
    /// </summary>
    public static Packet? ParsePacket(string json, string clientId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeElement))
            return null;

        var type = typeElement.GetString()?.ToLowerInvariant();

        return type switch
        {
            "logentry" or "log" => ParseLogEntry(root, clientId),
            "watch" => ParseWatch(root),
            "processflow" or "flow" => ParseProcessFlow(root, clientId),
            "control" or "command" => ParseControlCommand(root),
            _ => null
        };
    }

    /// <summary>
    /// Parses a JSON log entry message.
    /// </summary>
    private static LogEntry ParseLogEntry(JsonElement json, string clientId)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            ProcessId = Environment.ProcessId,
            ThreadId = Environment.CurrentManagedThreadId,
            HostName = Environment.MachineName
        };

        // Log entry type
        if (json.TryGetProperty("logEntryType", out var logTypeEl))
        {
            entry.LogEntryType = ParseLogEntryType(logTypeEl.GetString());
        }
        else
        {
            entry.LogEntryType = LogEntryType.Message;
        }

        // Viewer ID
        if (json.TryGetProperty("viewerId", out var viewerEl))
        {
            entry.ViewerId = ParseViewerId(viewerEl.GetString());
        }
        else
        {
            // Default viewer based on whether data is present
            entry.ViewerId = ViewerId.Title;
        }

        // Session name
        if (json.TryGetProperty("session", out var sessionEl))
        {
            entry.SessionName = sessionEl.GetString() ?? "Default";
        }
        else
        {
            entry.SessionName = "Default";
        }

        // App name (use client ID if not specified)
        if (json.TryGetProperty("appName", out var appEl))
        {
            entry.AppName = appEl.GetString() ?? clientId;
        }
        else
        {
            entry.AppName = clientId;
        }

        // Title/message
        if (json.TryGetProperty("title", out var titleEl))
        {
            entry.Title = titleEl.GetString() ?? string.Empty;
        }
        else if (json.TryGetProperty("message", out var msgEl))
        {
            entry.Title = msgEl.GetString() ?? string.Empty;
        }

        // Data payload
        if (json.TryGetProperty("data", out var dataEl))
        {
            var dataStr = dataEl.ValueKind == JsonValueKind.String
                ? dataEl.GetString()
                : dataEl.GetRawText();

            if (!string.IsNullOrEmpty(dataStr))
            {
                entry.Data = Encoding.UTF8.GetBytes(dataStr);
                // If we have data, default to Data viewer
                if (entry.ViewerId == ViewerId.Title)
                {
                    entry.ViewerId = ViewerId.Data;
                }
            }
        }

        // Color
        if (json.TryGetProperty("color", out var colorEl))
        {
            entry.Color = ParseColor(colorEl.GetString());
        }
        else
        {
            entry.Color = Color.Transparent;
        }

        // Optional: timestamp override
        if (json.TryGetProperty("timestamp", out var tsEl))
        {
            if (tsEl.TryGetDateTime(out var dt))
            {
                entry.Timestamp = dt;
            }
        }

        // Optional: thread ID
        if (json.TryGetProperty("threadId", out var threadEl) && threadEl.TryGetInt32(out var tid))
        {
            entry.ThreadId = tid;
        }

        return entry;
    }

    /// <summary>
    /// Parses a JSON watch message.
    /// </summary>
    private static Watch ParseWatch(JsonElement json)
    {
        var watch = new Watch
        {
            Timestamp = DateTime.Now
        };

        if (json.TryGetProperty("name", out var nameEl))
        {
            watch.Name = nameEl.GetString() ?? string.Empty;
        }

        if (json.TryGetProperty("value", out var valueEl))
        {
            watch.Value = valueEl.ValueKind == JsonValueKind.String
                ? valueEl.GetString() ?? string.Empty
                : valueEl.GetRawText();
        }

        if (json.TryGetProperty("watchType", out var typeEl))
        {
            watch.WatchType = ParseWatchType(typeEl.GetString());
        }
        else
        {
            // Auto-detect type from value
            watch.WatchType = DetectWatchType(json);
        }

        return watch;
    }

    /// <summary>
    /// Parses a JSON process flow message.
    /// </summary>
    private static ProcessFlow ParseProcessFlow(JsonElement json, string clientId)
    {
        var flow = new ProcessFlow
        {
            Timestamp = DateTime.Now,
            ProcessId = Environment.ProcessId,
            ThreadId = Environment.CurrentManagedThreadId,
            HostName = Environment.MachineName
        };

        if (json.TryGetProperty("flowType", out var typeEl))
        {
            flow.ProcessFlowType = ParseProcessFlowType(typeEl.GetString());
        }
        else
        {
            flow.ProcessFlowType = ProcessFlowType.EnterMethod;
        }

        if (json.TryGetProperty("title", out var titleEl))
        {
            flow.Title = titleEl.GetString() ?? string.Empty;
        }
        else if (json.TryGetProperty("method", out var methodEl))
        {
            flow.Title = methodEl.GetString() ?? string.Empty;
        }

        return flow;
    }

    /// <summary>
    /// Parses a JSON control command message.
    /// </summary>
    private static ControlCommand ParseControlCommand(JsonElement json)
    {
        var cmd = new ControlCommand();

        if (json.TryGetProperty("command", out var cmdEl))
        {
            cmd.ControlCommandType = ParseControlCommandType(cmdEl.GetString());
        }
        else
        {
            cmd.ControlCommandType = ControlCommandType.ClearLog;
        }

        return cmd;
    }

    #region Type Parsers

    private static LogEntryType ParseLogEntryType(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            "message" or "msg" => LogEntryType.Message,
            "warning" or "warn" => LogEntryType.Warning,
            "error" or "err" => LogEntryType.Error,
            "debug" or "dbg" => LogEntryType.Debug,
            "verbose" => LogEntryType.Verbose,
            "fatal" => LogEntryType.Fatal,
            "separator" or "sep" => LogEntryType.Separator,
            "entermethod" or "enter" => LogEntryType.EnterMethod,
            "leavemethod" or "leave" => LogEntryType.LeaveMethod,
            "comment" => LogEntryType.Comment,
            "checkpoint" => LogEntryType.Checkpoint,
            "assert" => LogEntryType.Assert,
            "text" => LogEntryType.Text,
            "object" or "obj" => LogEntryType.Object,
            "binary" => LogEntryType.Binary,
            _ => LogEntryType.Message
        };
    }

    private static ViewerId ParseViewerId(string? viewerId)
    {
        return viewerId?.ToLowerInvariant() switch
        {
            "title" => ViewerId.Title,
            "data" => ViewerId.Data,
            "list" => ViewerId.List,
            "valuelist" => ViewerId.ValueList,
            "inspector" => ViewerId.Inspector,
            "table" => ViewerId.Table,
            "web" => ViewerId.Web,
            "binary" => ViewerId.Binary,
            "json" or "javascript" => ViewerId.JavaScriptSource,
            "xml" => ViewerId.XmlSource,
            "html" => ViewerId.HtmlSource,
            "sql" => ViewerId.SqlSource,
            "python" => ViewerId.PythonSource,
            _ => ViewerId.Data
        };
    }

    private static WatchType ParseWatchType(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            "string" or "str" => WatchType.String,
            "integer" or "int" or "number" => WatchType.Integer,
            "float" or "double" or "decimal" => WatchType.Float,
            "boolean" or "bool" => WatchType.Boolean,
            "char" => WatchType.Char,
            "address" or "pointer" => WatchType.Address,
            "timestamp" or "date" or "datetime" => WatchType.Timestamp,
            "object" or "obj" => WatchType.Object,
            _ => WatchType.String
        };
    }

    private static WatchType DetectWatchType(JsonElement json)
    {
        if (!json.TryGetProperty("value", out var valueEl))
            return WatchType.String;

        return valueEl.ValueKind switch
        {
            JsonValueKind.Number => valueEl.TryGetInt64(out _) ? WatchType.Integer : WatchType.Float,
            JsonValueKind.True or JsonValueKind.False => WatchType.Boolean,
            JsonValueKind.Object or JsonValueKind.Array => WatchType.Object,
            _ => WatchType.String
        };
    }

    private static ProcessFlowType ParseProcessFlowType(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            "entermethod" or "enter" => ProcessFlowType.EnterMethod,
            "leavemethod" or "leave" => ProcessFlowType.LeaveMethod,
            "enterthread" => ProcessFlowType.EnterThread,
            "leavethread" => ProcessFlowType.LeaveThread,
            "enterprocess" => ProcessFlowType.EnterProcess,
            "leaveprocess" => ProcessFlowType.LeaveProcess,
            _ => ProcessFlowType.EnterMethod
        };
    }

    private static ControlCommandType ParseControlCommandType(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            "clearlog" or "clear" => ControlCommandType.ClearLog,
            "clearwatches" => ControlCommandType.ClearWatches,
            "clearall" => ControlCommandType.ClearAll,
            "clearprocessflow" or "clearflow" => ControlCommandType.ClearProcessFlow,
            _ => ControlCommandType.ClearLog
        };
    }

    private static Color ParseColor(string? colorStr)
    {
        if (string.IsNullOrEmpty(colorStr))
            return Color.Transparent;

        try
        {
            // Handle hex colors like #FF0000 or #F00
            if (colorStr.StartsWith('#'))
            {
                var hex = colorStr[1..];
                if (hex.Length == 3)
                {
                    // Expand shorthand #RGB to #RRGGBB
                    hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
                }

                if (hex.Length == 6)
                {
                    var r = Convert.ToByte(hex[..2], 16);
                    var g = Convert.ToByte(hex[2..4], 16);
                    var b = Convert.ToByte(hex[4..6], 16);
                    return Color.FromArgb(255, r, g, b);
                }

                if (hex.Length == 8)
                {
                    var a = Convert.ToByte(hex[..2], 16);
                    var r = Convert.ToByte(hex[2..4], 16);
                    var g = Convert.ToByte(hex[4..6], 16);
                    var b = Convert.ToByte(hex[6..8], 16);
                    return Color.FromArgb(a, r, g, b);
                }
            }

            // Try named color
            return Color.FromName(colorStr);
        }
        catch
        {
            return Color.Transparent;
        }
    }

    #endregion
}
