namespace SmartInspectConsole.ViewModels;

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

        view.RefreshFilter();
    }

    #endregion
}
