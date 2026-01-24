using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private string _textFilter = string.Empty;
    private LogEntryType? _minLogLevel;
    private LogEntry? _selectedLogEntry;
    private bool _isSelected;

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

        // App name filter
        if (!string.IsNullOrWhiteSpace(AppNameFilter))
        {
            if (!entry.AppName.Contains(AppNameFilter, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Session filter
        if (!string.IsNullOrWhiteSpace(SessionFilter))
        {
            if (!entry.SessionName.Contains(SessionFilter, StringComparison.OrdinalIgnoreCase))
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

        // Log level filter
        if (MinLogLevel.HasValue)
        {
            if (!PassesLogLevelFilter(entry.LogEntryType, MinLogLevel.Value))
                return false;
        }

        return true;
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
