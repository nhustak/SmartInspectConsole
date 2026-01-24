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

    // Theme
    public bool IsDarkTheme { get; set; } = true;

    // Panel visibility
    public bool ShowWatchesPanel { get; set; } = true;
    public bool ShowProcessFlowPanel { get; set; } = true;
    public bool ShowDetailsPanel { get; set; } = true;

    // Views configuration
    public List<ViewState> Views { get; set; } = new();

    // Selected view index
    public int SelectedViewIndex { get; set; }

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
        if (WindowWidth > 0 && WindowHeight > 0)
        {
            // Validate position is on screen
            var left = WindowLeft;
            var top = WindowTop;

            var screenLeft = SystemParameters.VirtualScreenLeft;
            var screenTop = SystemParameters.VirtualScreenTop;
            var screenWidth = SystemParameters.VirtualScreenWidth;
            var screenHeight = SystemParameters.VirtualScreenHeight;

            // Ensure window is at least partially visible
            if (left < screenLeft - WindowWidth + 100)
                left = screenLeft;
            if (top < screenTop - WindowHeight + 100)
                top = screenTop;
            if (left > screenLeft + screenWidth - 100)
                left = screenLeft + screenWidth - WindowWidth;
            if (top > screenTop + screenHeight - 100)
                top = screenTop + screenHeight - WindowHeight;

            window.Left = left;
            window.Top = top;
            window.Width = WindowWidth;
            window.Height = WindowHeight;
            window.WindowStartupLocation = WindowStartupLocation.Manual;

            if (IsMaximized)
                window.WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// Captures window settings from a window.
    /// </summary>
    public void CaptureWindowSettings(Window window)
    {
        IsMaximized = window.WindowState == WindowState.Maximized;

        if (window.WindowState == WindowState.Maximized)
        {
            WindowLeft = window.RestoreBounds.Left;
            WindowTop = window.RestoreBounds.Top;
            WindowWidth = window.RestoreBounds.Width;
            WindowHeight = window.RestoreBounds.Height;
        }
        else
        {
            WindowLeft = window.Left;
            WindowTop = window.Top;
            WindowWidth = window.Width;
            WindowHeight = window.Height;
        }
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
}
