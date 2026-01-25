using System.Collections.ObjectModel;

namespace SmartInspectConsole.ViewModels;

/// <summary>
/// Represents a selectable filter option.
/// </summary>
public class FilterOption : ViewModelBase
{
    private bool _isSelected;

    public string Value { get; set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>
/// View model for the Edit View dialog.
/// </summary>
public class EditViewViewModel : ViewModelBase
{
    private string _viewName = string.Empty;
    private string _appFilter = string.Empty;
    private string _sessionFilter = string.Empty;
    private string _hostnameFilter = string.Empty;
    private string _processIdFilter = string.Empty;
    private string _threadIdFilter = string.Empty;

    // Available filter options
    public ObservableCollection<FilterOption> AvailableAppNames { get; } = new();
    public ObservableCollection<FilterOption> AvailableSessions { get; } = new();
    public ObservableCollection<FilterOption> AvailableHostnames { get; } = new();

    private bool _enableTitleMatching;
    private string _titlePattern = string.Empty;
    private bool _titleCaseSensitive;
    private bool _titleIsRegex;
    private bool _titleInvert;

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

    #region View Name

    public string ViewName
    {
        get => _viewName;
        set => SetProperty(ref _viewName, value);
    }

    #endregion

    #region Data Filters

    public string AppFilter
    {
        get => _appFilter;
        set => SetProperty(ref _appFilter, value);
    }

    public string SessionFilter
    {
        get => _sessionFilter;
        set => SetProperty(ref _sessionFilter, value);
    }

    public string HostnameFilter
    {
        get => _hostnameFilter;
        set => SetProperty(ref _hostnameFilter, value);
    }

    public string ProcessIdFilter
    {
        get => _processIdFilter;
        set => SetProperty(ref _processIdFilter, value);
    }

    public string ThreadIdFilter
    {
        get => _threadIdFilter;
        set => SetProperty(ref _threadIdFilter, value);
    }

    #endregion

    #region Title Matching

    public bool EnableTitleMatching
    {
        get => _enableTitleMatching;
        set => SetProperty(ref _enableTitleMatching, value);
    }

    public string TitlePattern
    {
        get => _titlePattern;
        set => SetProperty(ref _titlePattern, value);
    }

    public bool TitleCaseSensitive
    {
        get => _titleCaseSensitive;
        set => SetProperty(ref _titleCaseSensitive, value);
    }

    public bool TitleIsRegex
    {
        get => _titleIsRegex;
        set => SetProperty(ref _titleIsRegex, value);
    }

    public bool TitleInvert
    {
        get => _titleInvert;
        set => SetProperty(ref _titleInvert, value);
    }

    #endregion

    #region Log Entry Types

    public bool ShowDebug
    {
        get => _showDebug;
        set => SetProperty(ref _showDebug, value);
    }

    public bool ShowVerbose
    {
        get => _showVerbose;
        set => SetProperty(ref _showVerbose, value);
    }

    public bool ShowMessage
    {
        get => _showMessage;
        set => SetProperty(ref _showMessage, value);
    }

    public bool ShowWarning
    {
        get => _showWarning;
        set => SetProperty(ref _showWarning, value);
    }

    public bool ShowError
    {
        get => _showError;
        set => SetProperty(ref _showError, value);
    }

    public bool ShowFatal
    {
        get => _showFatal;
        set => SetProperty(ref _showFatal, value);
    }

    public bool ShowMethodFlow
    {
        get => _showMethodFlow;
        set => SetProperty(ref _showMethodFlow, value);
    }

    public bool ShowSeparator
    {
        get => _showSeparator;
        set => SetProperty(ref _showSeparator, value);
    }

    public bool ShowOther
    {
        get => _showOther;
        set => SetProperty(ref _showOther, value);
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

    #region Log Entry Type Helpers

    public void SelectAllLogTypes()
    {
        ShowDebug = true;
        ShowVerbose = true;
        ShowMessage = true;
        ShowWarning = true;
        ShowError = true;
        ShowFatal = true;
        ShowMethodFlow = true;
        ShowSeparator = true;
        ShowOther = true;
    }

    public void SelectNoLogTypes()
    {
        ShowDebug = false;
        ShowVerbose = false;
        ShowMessage = false;
        ShowWarning = false;
        ShowError = false;
        ShowFatal = false;
        ShowMethodFlow = false;
        ShowSeparator = false;
        ShowOther = false;
    }

    #endregion

    #region Available Values

    /// <summary>
    /// Sets the available filter values from the main view model.
    /// </summary>
    public void SetAvailableValues(
        IEnumerable<string> appNames,
        IEnumerable<string> sessions,
        IEnumerable<string> hostnames)
    {
        AvailableAppNames.Clear();
        foreach (var name in appNames.OrderBy(n => n))
        {
            AvailableAppNames.Add(new FilterOption { Value = name });
        }

        AvailableSessions.Clear();
        foreach (var name in sessions.OrderBy(n => n))
        {
            AvailableSessions.Add(new FilterOption { Value = name });
        }

        AvailableHostnames.Clear();
        foreach (var name in hostnames.OrderBy(n => n))
        {
            AvailableHostnames.Add(new FilterOption { Value = name });
        }
    }

    /// <summary>
    /// Updates available options selection state based on filter string.
    /// </summary>
    private void UpdateSelectionFromFilter(ObservableCollection<FilterOption> options, string filter)
    {
        var filterValues = string.IsNullOrWhiteSpace(filter)
            ? Array.Empty<string>()
            : filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var option in options)
        {
            option.IsSelected = filterValues.Any(f =>
                f.Equals(option.Value, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Builds a filter string from selected options.
    /// </summary>
    private static string BuildFilterFromSelection(ObservableCollection<FilterOption> options)
    {
        var selected = options.Where(o => o.IsSelected).Select(o => o.Value);
        return string.Join(", ", selected);
    }

    /// <summary>
    /// Toggles selection of an app filter option.
    /// </summary>
    public void ToggleAppSelection(FilterOption option)
    {
        option.IsSelected = !option.IsSelected;
        AppFilter = BuildFilterFromSelection(AvailableAppNames);
    }

    /// <summary>
    /// Toggles selection of a session filter option.
    /// </summary>
    public void ToggleSessionSelection(FilterOption option)
    {
        option.IsSelected = !option.IsSelected;
        SessionFilter = BuildFilterFromSelection(AvailableSessions);
    }

    /// <summary>
    /// Toggles selection of a hostname filter option.
    /// </summary>
    public void ToggleHostnameSelection(FilterOption option)
    {
        option.IsSelected = !option.IsSelected;
        HostnameFilter = BuildFilterFromSelection(AvailableHostnames);
    }

    #endregion

    #region Load/Save from LogViewViewModel

    public void LoadFrom(LogViewViewModel view)
    {
        ViewName = view.Name;
        AppFilter = view.AppNameFilter;
        SessionFilter = view.SessionFilter;
        HostnameFilter = view.HostnameFilter;
        ProcessIdFilter = view.ProcessIdFilter;
        ThreadIdFilter = view.ThreadIdFilter;

        EnableTitleMatching = view.EnableTitleMatching;
        TitlePattern = view.TitlePattern;
        TitleCaseSensitive = view.TitleCaseSensitive;
        TitleIsRegex = view.TitleIsRegex;
        TitleInvert = view.TitleInvert;

        ShowDebug = view.ShowDebug;
        ShowVerbose = view.ShowVerbose;
        ShowMessage = view.ShowMessage;
        ShowWarning = view.ShowWarning;
        ShowError = view.ShowError;
        ShowFatal = view.ShowFatal;
        ShowMethodFlow = view.ShowMethodFlow;
        ShowSeparator = view.ShowSeparator;
        ShowOther = view.ShowOther;

        ShowTimeColumn = view.ShowTimeColumn;
        ShowElapsedColumn = view.ShowElapsedColumn;
        ShowAppColumn = view.ShowAppColumn;
        ShowSessionColumn = view.ShowSessionColumn;
        ShowTitleColumn = view.ShowTitleColumn;
        ShowThreadColumn = view.ShowThreadColumn;
    }

    /// <summary>
    /// Updates selection states after available values are set.
    /// Call this after both LoadFrom and SetAvailableValues have been called.
    /// </summary>
    public void SyncSelectionStates()
    {
        UpdateSelectionFromFilter(AvailableAppNames, AppFilter);
        UpdateSelectionFromFilter(AvailableSessions, SessionFilter);
        UpdateSelectionFromFilter(AvailableHostnames, HostnameFilter);
    }

    public void SaveTo(LogViewViewModel view)
    {
        view.Name = ViewName;
        view.AppNameFilter = AppFilter;
        view.SessionFilter = SessionFilter;
        view.HostnameFilter = HostnameFilter;
        view.ProcessIdFilter = ProcessIdFilter;
        view.ThreadIdFilter = ThreadIdFilter;

        view.EnableTitleMatching = EnableTitleMatching;
        view.TitlePattern = TitlePattern;
        view.TitleCaseSensitive = TitleCaseSensitive;
        view.TitleIsRegex = TitleIsRegex;
        view.TitleInvert = TitleInvert;

        view.ShowDebug = ShowDebug;
        view.ShowVerbose = ShowVerbose;
        view.ShowMessage = ShowMessage;
        view.ShowWarning = ShowWarning;
        view.ShowError = ShowError;
        view.ShowFatal = ShowFatal;
        view.ShowMethodFlow = ShowMethodFlow;
        view.ShowSeparator = ShowSeparator;
        view.ShowOther = ShowOther;

        view.ShowTimeColumn = ShowTimeColumn;
        view.ShowElapsedColumn = ShowElapsedColumn;
        view.ShowAppColumn = ShowAppColumn;
        view.ShowSessionColumn = ShowSessionColumn;
        view.ShowTitleColumn = ShowTitleColumn;
        view.ShowThreadColumn = ShowThreadColumn;

        view.RefreshFilter();
    }

    #endregion
}
