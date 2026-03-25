using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
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
    private readonly CollectionViewSource _viewSource;
    private readonly string[] _emptyFilterValues = [];

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

    // Primary view flag
    private bool _isPrimaryView;

    // Auto-scroll flag
    private bool _autoScroll = true;

    // Cached filtered count to avoid re-enumeration
    private int _filteredCount;
    private int _refreshSuspensionCount;
    private bool _refreshPending;
    private Regex? _titleRegex;
    private bool _titleRegexValid = true;
    private string[] _appNameFilterValues = [];
    private string[] _sessionFilterValues = [];
    private string[] _hostnameFilterValues = [];
    private string[] _processIdFilterValues = [];
    private string[] _threadIdFilterValues = [];

    public LogViewViewModel(ObservableCollection<LogEntry> allLogEntries, object lockObject, string name = "View", bool isPrimaryView = false)
    {
        _allLogEntries = allLogEntries;
        _lockObject = lockObject;
        _name = name;
        _isPrimaryView = isPrimaryView;

        // Create a NEW filtered view for this view (not the shared default view)
        // Each view needs its own CollectionViewSource to have independent filters
        // Keep reference to prevent garbage collection
        _viewSource = new CollectionViewSource { Source = _allLogEntries };
        FilteredLogEntries = _viewSource.View;
        FilteredLogEntries.Filter = FilterLogEntry;
        if (FilteredLogEntries is INotifyCollectionChanged notifyCollectionChanged)
        {
            notifyCollectionChanged.CollectionChanged += OnFilteredLogEntriesCollectionChanged;
        }

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
        RecountFilteredEntries();
    }

    #region Properties

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>
    /// Indicates if this is the primary/default view that cannot be closed.
    /// </summary>
    public bool IsPrimaryView
    {
        get => _isPrimaryView;
        set => SetProperty(ref _isPrimaryView, value);
    }

    /// <summary>
    /// Returns true if this view can be closed (non-primary views).
    /// </summary>
    public bool CanClose => !_isPrimaryView;

    /// <summary>
    /// When enabled, the log list automatically scrolls to show the newest entries.
    /// </summary>
    public bool AutoScroll
    {
        get => _autoScroll;
        set => SetProperty(ref _autoScroll, value);
    }

    public string AppNameFilter
    {
        get => _appNameFilter;
        set
        {
            if (SetProperty(ref _appNameFilter, value))
            {
                _appNameFilterValues = SplitFilterValues(value);
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
                _sessionFilterValues = SplitFilterValues(value);
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
                _hostnameFilterValues = SplitFilterValues(value);
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
                _processIdFilterValues = SplitFilterValues(value);
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
                _threadIdFilterValues = SplitFilterValues(value);
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
                InvalidateTitleRegex();
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
                InvalidateTitleRegex();
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
                InvalidateTitleRegex();
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

    /// <summary>
    /// Collection of selected log entries for multi-select copy operations.
    /// This is set from the view via attached behavior.
    /// </summary>
    public IList? SelectedLogEntries { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public int FilteredCount => _filteredCount;

    #region Copy Commands

    private ICommand? _copySelectedCommand;
    public ICommand CopySelectedCommand => _copySelectedCommand ??= new RelayCommand(CopySelectedToClipboard, () => SelectedLogEntries?.Count > 0);

    private ICommand? _copyAllCommand;
    public ICommand CopyAllCommand => _copyAllCommand ??= new RelayCommand(CopyAllToClipboard, () => _filteredCount > 0);

    #endregion

    #endregion

    #region Methods

    public void RefreshFilter()
    {
        if (_refreshSuspensionCount > 0)
        {
            _refreshPending = true;
            return;
        }

        FilteredLogEntries.Refresh();
        RecountFilteredEntries();
    }

    public IDisposable DeferRefresh()
    {
        _refreshSuspensionCount++;
        return new DeferredRefreshScope(this);
    }

    private bool FilterLogEntry(object obj)
    {
        if (obj is not LogEntry entry)
            return false;

        // Log entry type visibility filter
        if (!PassesLogEntryTypeFilter(entry.LogEntryType))
            return false;

        // App name filter (multi-value comma-separated)
        if (!PassesMultiValueFilter(_appNameFilterValues, entry.AppName))
            return false;

        // Session filter (multi-value comma-separated)
        if (!PassesMultiValueFilter(_sessionFilterValues, entry.SessionName))
            return false;

        // Hostname filter (multi-value comma-separated)
        if (!PassesMultiValueFilter(_hostnameFilterValues, entry.HostName))
            return false;

        // Process ID filter (multi-value comma-separated)
        if (!PassesMultiValueFilter(_processIdFilterValues, entry.ProcessId.ToString()))
            return false;

        // Thread ID filter (multi-value comma-separated)
        if (!PassesMultiValueFilter(_threadIdFilterValues, entry.ThreadId.ToString()))
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

    private bool PassesMultiValueFilter(string[] filterValues, string value)
    {
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
                EnsureTitleRegex();
                return _titleRegexValid && _titleRegex != null && _titleRegex.IsMatch(title);
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
            _titleRegexValid = false;
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
        RecountFilteredEntries();
    }

    /// <summary>
    /// Clears only the log entries that are currently visible in this view's filter.
    /// </summary>
    public void ClearFilteredEntries()
    {
        // Get all entries that pass the current filter
        var entriesToRemove = _allLogEntries.Where(e => FilterLogEntry(e)).ToList();

        lock (_lockObject)
        {
            foreach (var entry in entriesToRemove)
            {
                _allLogEntries.Remove(entry);
            }
        }
    }

    /// <summary>
    /// Copies selected log entries to clipboard.
    /// </summary>
    private void CopySelectedToClipboard()
    {
        if (SelectedLogEntries == null || SelectedLogEntries.Count == 0)
            return;

        var entries = SelectedLogEntries.Cast<LogEntry>().OrderBy(e => e.Timestamp);
        CopyEntriesToClipboard(entries);
    }

    /// <summary>
    /// Copies all filtered log entries to clipboard.
    /// </summary>
    private void CopyAllToClipboard()
    {
        var entries = FilteredLogEntries.Cast<LogEntry>();
        CopyEntriesToClipboard(entries);
    }

    /// <summary>
    /// Formats and copies log entries to clipboard as tab-separated values.
    /// </summary>
    private static void CopyEntriesToClipboard(IEnumerable<LogEntry> entries)
    {
        var sb = new StringBuilder();

        // Header row
        sb.AppendLine("Time\tElapsed\tType\tApp\tSession\tTitle\tThread");

        foreach (var entry in entries)
        {
            sb.Append(entry.Timestamp.ToString("HH:mm:ss.fff"));
            sb.Append('\t');
            sb.Append(entry.ElapsedTimeFormatted);
            sb.Append('\t');
            sb.Append(entry.LogEntryType);
            sb.Append('\t');
            sb.Append(entry.AppName);
            sb.Append('\t');
            sb.Append(entry.SessionName);
            sb.Append('\t');
            sb.Append(entry.Title);
            sb.Append('\t');
            sb.Append(entry.ThreadId);
            sb.AppendLine();
        }

        try
        {
            Clipboard.SetText(sb.ToString());
        }
        catch
        {
            // Clipboard operation failed - ignore
        }
    }

    #endregion

    private static string[] SplitFilterValues(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private void EnsureTitleRegex()
    {
        if (!TitleIsRegex)
            return;

        if (_titleRegex != null || !_titleRegexValid)
            return;

        var options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        if (!TitleCaseSensitive)
            options |= RegexOptions.IgnoreCase;

        _titleRegex = new Regex(TitlePattern, options);
        _titleRegexValid = true;
    }

    private void InvalidateTitleRegex()
    {
        _titleRegex = null;
        _titleRegexValid = true;
    }

    private void RecountFilteredEntries()
    {
        var count = FilteredLogEntries.Cast<object>().Count();
        if (_filteredCount == count)
            return;

        _filteredCount = count;
        OnPropertyChanged(nameof(FilteredCount));
    }

    private void OnFilteredLogEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                AdjustFilteredCount(e.NewItems?.Count ?? 0);
                break;

            case NotifyCollectionChangedAction.Remove:
                AdjustFilteredCount(-(e.OldItems?.Count ?? 0));
                break;

            case NotifyCollectionChangedAction.Replace:
                AdjustFilteredCount((e.NewItems?.Count ?? 0) - (e.OldItems?.Count ?? 0));
                break;

            default:
                RecountFilteredEntries();
                break;
        }
    }

    private void AdjustFilteredCount(int delta)
    {
        if (delta == 0)
            return;

        var count = Math.Max(0, _filteredCount + delta);
        if (count == _filteredCount)
            return;

        _filteredCount = count;
        OnPropertyChanged(nameof(FilteredCount));
    }

    private void ResumeRefresh()
    {
        if (_refreshSuspensionCount == 0)
            return;

        _refreshSuspensionCount--;
        if (_refreshSuspensionCount == 0 && _refreshPending)
        {
            _refreshPending = false;
            RefreshFilter();
        }
    }

    private sealed class DeferredRefreshScope : IDisposable
    {
        private readonly LogViewViewModel _owner;
        private bool _disposed;

        public DeferredRefreshScope(LogViewViewModel owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _owner.ResumeRefresh();
        }
    }
}

/// <summary>
/// Represents a log level option for the filter dropdown.
/// </summary>
public class LogLevelOption
{
    public string DisplayName { get; set; } = string.Empty;
    public LogEntryType? Value { get; set; }
}
