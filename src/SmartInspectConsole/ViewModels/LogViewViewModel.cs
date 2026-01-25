using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Data;
using SmartInspectConsole.Core.Enums;
using SmartInspectConsole.Core.Packets;

namespace SmartInspectConsole.ViewModels;

/// <summary>
/// Represents a single log view tab with its own filter settings.
/// </summary>
public class LogViewViewModel : ViewModelBase
{
    private readonly ObservableCollection<LogEntry> _allLogEntries;
    private readonly object _lockObject;

    private string _name = "View";
    private string _appNameFilter = string.Empty;
    private string _sessionFilter = string.Empty;
    private string _hostnameFilter = string.Empty;
    private string _processIdFilter = string.Empty;
    private string _threadIdFilter = string.Empty;
    private string _textFilter = string.Empty;
    private LogEntryType? _minLogLevel;
    private LogEntry? _selectedLogEntry;
    private bool _isSelected;

    // Title matching
    private bool _enableTitleMatching;
    private string _titlePattern = string.Empty;
    private bool _titleCaseSensitive;
    private bool _titleIsRegex;
    private bool _titleInvert;

    // Log entry type visibility
    private bool _showDebug = true;
    private bool _showVerbose = true;
    private bool _showMessage = true;
    private bool _showWarning = true;
    private bool _showError = true;
    private bool _showFatal = true;
    private bool _showMethodFlow = true;
    private bool _showSeparator = true;
    private bool _showOther = true;

    // Column visibility
    private bool _showTimeColumn = true;
    private bool _showElapsedColumn = true;
    private bool _showAppColumn = true;
    private bool _showSessionColumn = true;
    private bool _showTitleColumn = true;
    private bool _showThreadColumn = true;

    public LogViewViewModel(ObservableCollection<LogEntry> allLogEntries, object lockObject, string name = "View")
    {
        _allLogEntries = allLogEntries;
        _lockObject = lockObject;
        _name = name;

        // Create a filtered view of the shared log entries
        FilteredLogEntries = CollectionViewSource.GetDefaultView(_allLogEntries);
        FilteredLogEntries.Filter = FilterLogEntry;

        // Available log levels for filtering
        LogLevels = new ObservableCollection<LogLevelOption>
        {
            new() { DisplayName = "(All Levels)", Value = null },
            new() { DisplayName = "Debug+", Value = LogEntryType.Debug },
            new() { DisplayName = "Verbose+", Value = LogEntryType.Verbose },
            new() { DisplayName = "Message+", Value = LogEntryType.Message },
            new() { DisplayName = "Warning+", Value = LogEntryType.Warning },
            new() { DisplayName = "Error+", Value = LogEntryType.Error },
            new() { DisplayName = "Fatal Only", Value = LogEntryType.Fatal }
        };

        SelectedLogLevel = LogLevels[0];
    }

    #region Properties

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string AppNameFilter
    {
        get => _appNameFilter;
        set
        {
            if (SetProperty(ref _appNameFilter, value))
            {
                RefreshFilter();
            }
        }
    }

    public string SessionFilter
    {
        get => _sessionFilter;
        set
        {
            if (SetProperty(ref _sessionFilter, value))
            {
                RefreshFilter();
            }
        }
    }

    public string HostnameFilter
    {
        get => _hostnameFilter;
        set
        {
            if (SetProperty(ref _hostnameFilter, value))
            {
                RefreshFilter();
            }
        }
    }

    public string ProcessIdFilter
    {
        get => _processIdFilter;
        set
        {
            if (SetProperty(ref _processIdFilter, value))
            {
                RefreshFilter();
            }
        }
    }

    public string ThreadIdFilter
    {
        get => _threadIdFilter;
        set
        {
            if (SetProperty(ref _threadIdFilter, value))
            {
                RefreshFilter();
            }
        }
    }

    public string TextFilter
    {
        get => _textFilter;
        set
        {
            if (SetProperty(ref _textFilter, value))
            {
                RefreshFilter();
            }
        }
    }

    public LogEntryType? MinLogLevel
    {
        get => _minLogLevel;
        set
        {
            if (SetProperty(ref _minLogLevel, value))
            {
                RefreshFilter();
            }
        }
    }

    private LogLevelOption? _selectedLogLevel;
    public LogLevelOption? SelectedLogLevel
    {
        get => _selectedLogLevel;
        set
        {
            if (SetProperty(ref _selectedLogLevel, value))
            {
                MinLogLevel = value?.Value;
            }
        }
    }

    public ObservableCollection<LogLevelOption> LogLevels { get; }

    #region Title Matching

    public bool EnableTitleMatching
    {
        get => _enableTitleMatching;
        set
        {
            if (SetProperty(ref _enableTitleMatching, value))
            {
                RefreshFilter();
            }
        }
    }

    public string TitlePattern
    {
        get => _titlePattern;
        set
        {
            if (SetProperty(ref _titlePattern, value))
            {
                RefreshFilter();
            }
        }
    }

    public bool TitleCaseSensitive
    {
        get => _titleCaseSensitive;
        set
        {
            if (SetProperty(ref _titleCaseSensitive, value))
            {
                RefreshFilter();
            }
        }
    }

    public bool TitleIsRegex
    {
        get => _titleIsRegex;
        set
        {
            if (SetProperty(ref _titleIsRegex, value))
            {
                RefreshFilter();
            }
        }
    }

    public bool TitleInvert
    {
        get => _titleInvert;
        set
        {
            if (SetProperty(ref _titleInvert, value))
            {
                RefreshFilter();
            }
        }
    }

    #endregion

    #region Log Entry Type Visibility

    public bool ShowDebug
    {
        get => _showDebug;
        set
        {
            if (SetProperty(ref _showDebug, value))
            {
                RefreshFilter();
            }
        }
    }

    public bool ShowVerbose
    {
        get => _showVerbose;
        set
        {
            if (SetProperty(ref _showVerbose, value))
            {
                RefreshFilter();
            }
        }
    }

    public bool ShowMessage
    {
        get => _showMessage;
        set
        {
            if (SetProperty(ref _showMessage, value))
            {
                RefreshFilter();
            }
        }
    }

    public bool ShowWarning
    {
        get => _showWarning;
        set
        {
            if (SetProperty(ref _showWarning, value))
            {
                RefreshFilter();
            }
        }
    }

    public bool ShowError
    {
        get => _showError;
        set
        {
            if (SetProperty(ref _showError, value))
            {
                RefreshFilter();
            }
        }
    }

    public bool ShowFatal
    {
        get => _showFatal;
        set
        {
            if (SetProperty(ref _showFatal, value))
            {
                RefreshFilter();
            }
        }
    }

    public bool ShowMethodFlow
    {
        get => _showMethodFlow;
        set
        {
            if (SetProperty(ref _showMethodFlow, value))
            {
                RefreshFilter();
            }
        }
    }

    public bool ShowSeparator
    {
        get => _showSeparator;
        set
        {
            if (SetProperty(ref _showSeparator, value))
            {
                RefreshFilter();
            }
        }
    }

    public bool ShowOther
    {
        get => _showOther;
        set
        {
            if (SetProperty(ref _showOther, value))
            {
                RefreshFilter();
            }
        }
    }

    #endregion

    #region Column Visibility

    public bool ShowTimeColumn
    {
        get => _showTimeColumn;
        set => SetProperty(ref _showTimeColumn, value);
    }

    public bool ShowElapsedColumn
    {
        get => _showElapsedColumn;
        set => SetProperty(ref _showElapsedColumn, value);
    }

    public bool ShowAppColumn
    {
        get => _showAppColumn;
        set => SetProperty(ref _showAppColumn, value);
    }

    public bool ShowSessionColumn
    {
        get => _showSessionColumn;
        set => SetProperty(ref _showSessionColumn, value);
    }

    public bool ShowTitleColumn
    {
        get => _showTitleColumn;
        set => SetProperty(ref _showTitleColumn, value);
    }

    public bool ShowThreadColumn
    {
        get => _showThreadColumn;
        set => SetProperty(ref _showThreadColumn, value);
    }

    #endregion

    public ICollectionView FilteredLogEntries { get; }

    public LogEntry? SelectedLogEntry
    {
        get => _selectedLogEntry;
        set => SetProperty(ref _selectedLogEntry, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public int FilteredCount => FilteredLogEntries.Cast<object>().Count();

    #endregion

    #region Methods

    public void RefreshFilter()
    {
        FilteredLogEntries.Refresh();
        OnPropertyChanged(nameof(FilteredCount));
    }

    private bool FilterLogEntry(object obj)
    {
        if (obj is not LogEntry entry)
            return false;

        // Log entry type visibility filter
        if (!PassesLogEntryTypeFilter(entry.LogEntryType))
            return false;

        // App name filter (multi-value comma-separated)
        if (!PassesMultiValueFilter(AppNameFilter, entry.AppName))
            return false;

        // Session filter (multi-value comma-separated)
        if (!PassesMultiValueFilter(SessionFilter, entry.SessionName))
            return false;

        // Hostname filter (multi-value comma-separated)
        if (!PassesMultiValueFilter(HostnameFilter, entry.HostName))
            return false;

        // Process ID filter (multi-value comma-separated)
        if (!PassesMultiValueFilter(ProcessIdFilter, entry.ProcessId.ToString()))
            return false;

        // Thread ID filter (multi-value comma-separated)
        if (!PassesMultiValueFilter(ThreadIdFilter, entry.ThreadId.ToString()))
            return false;

        // Title matching filter
        if (EnableTitleMatching && !string.IsNullOrWhiteSpace(TitlePattern))
        {
            bool matches = MatchesTitle(entry.Title);
            if (TitleInvert)
                matches = !matches;
            if (!matches)
                return false;
        }

        // Text filter (searches title and data)
        if (!string.IsNullOrWhiteSpace(TextFilter))
        {
            var matchesTitle = entry.Title.Contains(TextFilter, StringComparison.OrdinalIgnoreCase);
            var matchesData = entry.DataAsString?.Contains(TextFilter, StringComparison.OrdinalIgnoreCase) ?? false;
            if (!matchesTitle && !matchesData)
                return false;
        }

        // Log level filter (legacy, still used for quick filtering)
        if (MinLogLevel.HasValue)
        {
            if (!PassesLogLevelFilter(entry.LogEntryType, MinLogLevel.Value))
                return false;
        }

        return true;
    }

    private bool PassesLogEntryTypeFilter(LogEntryType type)
    {
        return type switch
        {
            LogEntryType.Debug => ShowDebug,
            LogEntryType.Verbose => ShowVerbose,
            LogEntryType.Message => ShowMessage,
            LogEntryType.Warning => ShowWarning,
            LogEntryType.Error => ShowError,
            LogEntryType.Fatal => ShowFatal,
            LogEntryType.EnterMethod or LogEntryType.LeaveMethod => ShowMethodFlow,
            LogEntryType.Separator => ShowSeparator,
            _ => ShowOther
        };
    }

    private static bool PassesMultiValueFilter(string filter, string value)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        // Split by comma and check if any value matches
        var filterValues = filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (filterValues.Length == 0)
            return true;

        foreach (var filterValue in filterValues)
        {
            if (value.Contains(filterValue, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private bool MatchesTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(TitlePattern))
            return true;

        try
        {
            if (TitleIsRegex)
            {
                var options = TitleCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                return Regex.IsMatch(title, TitlePattern, options);
            }
            else
            {
                var comparison = TitleCaseSensitive
                    ? StringComparison.Ordinal
                    : StringComparison.OrdinalIgnoreCase;
                return title.Contains(TitlePattern, comparison);
            }
        }
        catch (ArgumentException)
        {
            // Invalid regex pattern - treat as no match
            return false;
        }
    }

    private static bool PassesLogLevelFilter(LogEntryType entryType, LogEntryType minLevel)
    {
        // Define severity order (higher = more severe)
        int GetSeverity(LogEntryType type) => type switch
        {
            LogEntryType.Debug => 1,
            LogEntryType.Verbose => 2,
            LogEntryType.Message => 3,
            LogEntryType.Warning => 4,
            LogEntryType.Error => 5,
            LogEntryType.Fatal => 6,
            // Method entry/exit, separators etc. - show at Message level
            LogEntryType.EnterMethod or LogEntryType.LeaveMethod => 3,
            LogEntryType.Separator => 3,
            _ => 3 // Default to Message level
        };

        return GetSeverity(entryType) >= GetSeverity(minLevel);
    }

    public void Clear()
    {
        OnPropertyChanged(nameof(FilteredCount));
    }

    #endregion
}

/// <summary>
/// Represents a log level option for the filter dropdown.
/// </summary>
public class LogLevelOption
{
    public string DisplayName { get; set; } = string.Empty;
    public LogEntryType? Value { get; set; }
}
