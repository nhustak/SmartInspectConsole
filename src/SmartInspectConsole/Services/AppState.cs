using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace SmartInspectConsole.Services;

/// <summary>
/// Represents the complete application state that can be saved and restored.
/// </summary>
public class AppState
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmartInspectConsole");

    private static readonly string StateFile = Path.Combine(SettingsFolder, "appstate.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Window settings
    public double WindowLeft { get; set; }
    public double WindowTop { get; set; }
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool IsMaximized { get; set; }

    // Edit View dialog settings
    public double EditViewDialogLeft { get; set; }
    public double EditViewDialogTop { get; set; }
    public double EditViewDialogWidth { get; set; }
    public double EditViewDialogHeight { get; set; }

    // Theme
    public bool IsDarkTheme { get; set; } = true;
    public bool Use24HourTime { get; set; } = true;

    // Panel visibility
    public bool ShowWatchesPanel { get; set; } = true;
    public bool ShowProcessFlowPanel { get; set; } = true;
    public bool ShowDetailsPanel { get; set; } = true;

    // Network settings
    public int TcpPort { get; set; } = 4228;
    public string PipeName { get; set; } = "smartinspect";
    public int WebSocketPort { get; set; } = 4229;

    // Developer settings
    public bool DebugMode { get; set; } = false;
    public bool EnableMcpTrace { get; set; } = true;

    // Behavior
    public bool ConfirmBeforeClear { get; set; } = true;

    // Connection Manager
    public bool ShowConnectionsPanel { get; set; } = true;
    public List<string> MutedApplications { get; set; } = new();
    public int MaxLogEntries { get; set; } = 20_000;

    // Views configuration
    public List<ViewState> Views { get; set; } = new();

    // Selected view index
    public int SelectedViewIndex { get; set; }
    public string? SelectedViewName { get; set; }

    /// <summary>
    /// Loads state from the default location, or returns defaults if not found.
    /// </summary>
    public static AppState Load()
    {
        return LoadFrom(StateFile);
    }

    /// <summary>
    /// Loads state from a specific file path.
    /// </summary>
    public static AppState LoadFrom(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var state = JsonSerializer.Deserialize<AppState>(json, JsonOptions);
                if (state != null)
                    return state;
            }
        }
        catch
        {
            // Ignore errors, return defaults
        }

        return new AppState();
    }

    /// <summary>
    /// Saves state to the default location.
    /// </summary>
    public void Save()
    {
        SaveTo(StateFile);
    }

    /// <summary>
    /// Saves state to a specific file path (for export).
    /// </summary>
    public void SaveTo(string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Ignore save errors for default location
            // Rethrow for explicit export
            if (filePath != StateFile)
                throw;
        }
    }

    /// <summary>
    /// Applies window settings to a window.
    /// </summary>
    public void ApplyWindowSettings(Window window)
    {
        ApplyWindowSettings(window, WindowLeft, WindowTop, WindowWidth, WindowHeight, IsMaximized);
    }

    /// <summary>
    /// Captures window settings from a window.
    /// </summary>
    public void CaptureWindowSettings(Window window)
    {
        CaptureWindowSettings(window, out var left, out var top, out var width, out var height, out var isMaximized);
        WindowLeft = left;
        WindowTop = top;
        WindowWidth = width;
        WindowHeight = height;
        IsMaximized = isMaximized;
    }

    public void ApplyEditViewDialogSettings(Window window)
    {
        ApplyWindowSettings(window, EditViewDialogLeft, EditViewDialogTop, EditViewDialogWidth, EditViewDialogHeight, isMaximized: false);
    }

    public void CaptureEditViewDialogSettings(Window window)
    {
        CaptureWindowSettings(window, out var left, out var top, out var width, out var height, out _);
        EditViewDialogLeft = left;
        EditViewDialogTop = top;
        EditViewDialogWidth = width;
        EditViewDialogHeight = height;
    }

    private static void ApplyWindowSettings(Window window, double savedLeft, double savedTop, double savedWidth, double savedHeight, bool isMaximized)
    {
        if (savedWidth <= 0 || savedHeight <= 0)
            return;

        var left = savedLeft;
        var top = savedTop;

        var screenLeft = SystemParameters.VirtualScreenLeft;
        var screenTop = SystemParameters.VirtualScreenTop;
        var screenWidth = SystemParameters.VirtualScreenWidth;
        var screenHeight = SystemParameters.VirtualScreenHeight;

        if (left < screenLeft - savedWidth + 100)
            left = screenLeft;
        if (top < screenTop - savedHeight + 100)
            top = screenTop;
        if (left > screenLeft + screenWidth - 100)
            left = screenLeft + screenWidth - savedWidth;
        if (top > screenTop + screenHeight - 100)
            top = screenTop + screenHeight - savedHeight;

        window.Left = left;
        window.Top = top;
        window.Width = savedWidth;
        window.Height = savedHeight;
        window.WindowStartupLocation = WindowStartupLocation.Manual;

        if (isMaximized)
            window.WindowState = WindowState.Maximized;
    }

    private static void CaptureWindowSettings(Window window, out double left, out double top, out double width, out double height, out bool isMaximized)
    {
        isMaximized = window.WindowState == WindowState.Maximized;

        if (window.WindowState == WindowState.Maximized)
        {
            left = window.RestoreBounds.Left;
            top = window.RestoreBounds.Top;
            width = window.RestoreBounds.Width;
            height = window.RestoreBounds.Height;
            return;
        }

        left = window.Left;
        top = window.Top;
        width = window.Width;
        height = window.Height;
    }
}

/// <summary>
/// Represents the saved state of a single view/tab.
/// </summary>
public class ViewState
{
    public string Name { get; set; } = "View";

    // Basic filters
    public string? AppNameFilter { get; set; }
    public string? SessionFilter { get; set; }
    public string? HostnameFilter { get; set; }
    public string? ProcessIdFilter { get; set; }
    public string? ThreadIdFilter { get; set; }
    public string? TextFilter { get; set; }
    public string? MinLogLevel { get; set; }

    // Title matching
    public bool EnableTitleMatching { get; set; }
    public string? TitlePattern { get; set; }
    public bool TitleCaseSensitive { get; set; }
    public bool TitleIsRegex { get; set; }
    public bool TitleInvert { get; set; }

    // Log entry type visibility
    public bool ShowDebug { get; set; } = true;
    public bool ShowVerbose { get; set; } = true;
    public bool ShowMessage { get; set; } = true;
    public bool ShowWarning { get; set; } = true;
    public bool ShowError { get; set; } = true;
    public bool ShowFatal { get; set; } = true;
    public bool ShowMethodFlow { get; set; } = true;
    public bool ShowSeparator { get; set; } = true;
    public bool ShowOther { get; set; } = true;

    // Column visibility
    public bool ShowTimeColumn { get; set; } = true;
    public bool ShowElapsedColumn { get; set; } = true;
    public bool ShowAppColumn { get; set; } = true;
    public bool ShowSessionColumn { get; set; } = true;
    public bool ShowTitleColumn { get; set; } = true;
    public bool ShowThreadColumn { get; set; } = true;

    // Auto-scroll
    public bool AutoScroll { get; set; } = true;
}
