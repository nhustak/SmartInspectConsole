using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using SmartInspectConsole.Core.Enums;
using SmartInspectConsole.Core.Events;
using SmartInspectConsole.Core.Listeners;
using SmartInspectConsole.Core.Packets;
using SmartInspectConsole.Services;
using SmartInspectConsole.Views;

namespace SmartInspectConsole.ViewModels;

/// <summary>
/// Main view model for the SmartInspect Console.
/// </summary>
public class MainViewModel : ViewModelBase, IDisposable
{
    private SmartInspectTcpListener? _tcpListener;
    private SmartInspectPipeListener? _pipeListener;
    private readonly object _logEntriesLock = new();

    private DateTime? _lastLogEntryTimestamp;
    private string _statusText = "Ready";
    private bool _isListening;
    private LogViewViewModel? _selectedView;
    private int _viewCounter = 1;
    private LogEntryDetailViewModel? _selectedDetailTab;

    // Panel visibility
    private bool _showWatchesPanel = true;
    private bool _showProcessFlowPanel = true;
    private bool _showDetailsPanel = true;

    // Network settings
    private int _tcpPort = SmartInspectTcpListener.DefaultPort;
    private string _pipeName = SmartInspectPipeListener.DefaultPipeName;

    public MainViewModel()
    {
        // Initialize collections
        LogEntries = new ObservableCollection<LogEntry>();
        Watches = new ObservableCollection<Watch>();
        ProcessFlows = new ObservableCollection<ProcessFlow>();
        Views = new ObservableCollection<LogViewViewModel>();
        DetailTabs = new ObservableCollection<LogEntryDetailViewModel>();

        // Enable collection synchronization for cross-thread access
        BindingOperations.EnableCollectionSynchronization(LogEntries, _logEntriesLock);
        BindingOperations.EnableCollectionSynchronization(Watches, _logEntriesLock);
        BindingOperations.EnableCollectionSynchronization(ProcessFlows, _logEntriesLock);
        BindingOperations.EnableCollectionSynchronization(Views, _logEntriesLock);
        BindingOperations.EnableCollectionSynchronization(DetailTabs, _logEntriesLock);

        // Create default "All" view (primary view that cannot be closed)
        var allView = new LogViewViewModel(LogEntries, _logEntriesLock, "All", isPrimaryView: true);
        Views.Add(allView);
        SelectedView = allView;

        // Initialize commands
        StartCommand = new AsyncRelayCommand(StartAsync, () => !IsListening);
        StopCommand = new AsyncRelayCommand(StopAsync, () => IsListening);
        ClearAllCommand = new RelayCommand(ClearAll);
        ClearLogCommand = new RelayCommand(ClearLog);
        ClearWatchesCommand = new RelayCommand(ClearWatches);
        ClearProcessFlowCommand = new RelayCommand(ClearProcessFlow);

        // Tab management commands
        AddViewCommand = new RelayCommand(AddView);
        RemoveViewCommand = new RelayCommand<LogViewViewModel>(RemoveView, CanRemoveView);
        RenameViewCommand = new RelayCommand<LogViewViewModel>(RenameView);
        DuplicateViewCommand = new RelayCommand<LogViewViewModel>(DuplicateView);
        EditViewCommand = new RelayCommand<LogViewViewModel>(EditView);

        // Panel visibility commands
        HideWatchesPanelCommand = new RelayCommand(() => ShowWatchesPanel = false);
        HideProcessFlowPanelCommand = new RelayCommand(() => ShowProcessFlowPanel = false);
        HideDetailsPanelCommand = new RelayCommand(() => ShowDetailsPanel = false);

        // Detail tab commands
        OpenLogEntryDetailCommand = new RelayCommand<LogEntry>(OpenLogEntryDetail);
        CloseDetailTabCommand = new RelayCommand<LogEntryDetailViewModel>(CloseDetailTab);

        // View-specific commands
        ClearViewLogCommand = new RelayCommand<LogViewViewModel>(ClearViewLog);
    }

    #region Properties

    public ObservableCollection<LogEntry> LogEntries { get; }
    public ObservableCollection<Watch> Watches { get; }
    public ObservableCollection<ProcessFlow> ProcessFlows { get; }
    public ObservableCollection<LogViewViewModel> Views { get; }
    public ObservableCollection<LogEntryDetailViewModel> DetailTabs { get; }

    // Available filter values (populated from received log entries)
    public ObservableCollection<string> AvailableAppNames { get; } = new();
    public ObservableCollection<string> AvailableSessions { get; } = new();
    public ObservableCollection<string> AvailableHostnames { get; } = new();

    public LogEntryDetailViewModel? SelectedDetailTab
    {
        get => _selectedDetailTab;
        set => SetProperty(ref _selectedDetailTab, value);
    }

    public bool HasDetailTabs => DetailTabs.Count > 0;

    public LogViewViewModel? SelectedView
    {
        get => _selectedView;
        set
        {
            if (_selectedView != null)
                _selectedView.IsSelected = false;

            if (SetProperty(ref _selectedView, value))
            {
                if (_selectedView != null)
                    _selectedView.IsSelected = true;

                OnPropertyChanged(nameof(SelectedLogEntry));
            }
        }
    }

    public LogEntry? SelectedLogEntry => SelectedView?.SelectedLogEntry;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsListening
    {
        get => _isListening;
        private set
        {
            if (SetProperty(ref _isListening, value))
            {
                OnPropertyChanged(nameof(TcpStatus));
                OnPropertyChanged(nameof(PipeStatus));
            }
        }
    }

    public string TcpStatus => IsListening && _tcpListener != null
        ? $"TCP: Port {_tcpPort} ({_tcpListener.ClientCount} clients)"
        : $"TCP: Port {_tcpPort} (Stopped)";

    public string PipeStatus => IsListening && _pipeListener != null
        ? $"Pipe: {_pipeName} ({_pipeListener.ClientCount} clients)"
        : $"Pipe: {_pipeName} (Stopped)";

    public int EntryCount => LogEntries.Count;

    // Panel visibility
    public bool ShowWatchesPanel
    {
        get => _showWatchesPanel;
        set => SetProperty(ref _showWatchesPanel, value);
    }

    public bool ShowProcessFlowPanel
    {
        get => _showProcessFlowPanel;
        set => SetProperty(ref _showProcessFlowPanel, value);
    }

    public bool ShowDetailsPanel
    {
        get => _showDetailsPanel;
        set => SetProperty(ref _showDetailsPanel, value);
    }

    public int TcpPort
    {
        get => _tcpPort;
        set
        {
            if (SetProperty(ref _tcpPort, value))
            {
                OnPropertyChanged(nameof(TcpStatus));
            }
        }
    }

    public string PipeName
    {
        get => _pipeName;
        set
        {
            if (SetProperty(ref _pipeName, value))
            {
                OnPropertyChanged(nameof(PipeStatus));
            }
        }
    }

    #endregion

    #region Commands

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand ClearWatchesCommand { get; }
    public ICommand ClearProcessFlowCommand { get; }

    // Tab management
    public ICommand AddViewCommand { get; }
    public ICommand RemoveViewCommand { get; }
    public ICommand RenameViewCommand { get; }
    public ICommand DuplicateViewCommand { get; }
    public ICommand EditViewCommand { get; }

    // Panel visibility
    public ICommand HideWatchesPanelCommand { get; }
    public ICommand HideProcessFlowPanelCommand { get; }
    public ICommand HideDetailsPanelCommand { get; }

    // Detail tabs
    public ICommand OpenLogEntryDetailCommand { get; }
    public ICommand CloseDetailTabCommand { get; }

    // View-specific
    public ICommand ClearViewLogCommand { get; }

    #endregion

    #region Public Methods

    public async Task StartAsync()
    {
        if (IsListening) return;

        try
        {
            StatusText = "Starting listeners...";

            // Clean up old listeners asynchronously (avoid blocking Dispose on UI thread)
            var cleanupTasks = new List<Task>();
            if (_tcpListener != null)
            {
                _tcpListener.PacketReceived -= OnPacketReceived;
                _tcpListener.ClientConnected -= OnClientConnected;
                _tcpListener.ClientDisconnected -= OnClientDisconnected;
                _tcpListener.Error -= OnError;
                cleanupTasks.Add(_tcpListener.StopAsync());
            }
            if (_pipeListener != null)
            {
                _pipeListener.PacketReceived -= OnPacketReceived;
                _pipeListener.ClientConnected -= OnClientConnected;
                _pipeListener.ClientDisconnected -= OnClientDisconnected;
                _pipeListener.Error -= OnError;
                cleanupTasks.Add(_pipeListener.StopAsync());
            }
            if (cleanupTasks.Count > 0)
            {
                await Task.WhenAll(cleanupTasks);
                // Brief delay to allow Windows to fully release resources (ports/pipes)
                await Task.Delay(100);
            }

            // Create TCP listener with configured port
            _tcpListener = new SmartInspectTcpListener(_tcpPort);
            _tcpListener.PacketReceived += OnPacketReceived;
            _tcpListener.ClientConnected += OnClientConnected;
            _tcpListener.ClientDisconnected += OnClientDisconnected;
            _tcpListener.Error += OnError;

            // Create pipe listener with configured name
            _pipeListener = new SmartInspectPipeListener(_pipeName);
            _pipeListener.PacketReceived += OnPacketReceived;
            _pipeListener.ClientConnected += OnClientConnected;
            _pipeListener.ClientDisconnected += OnClientDisconnected;
            _pipeListener.Error += OnError;

            await Task.WhenAll(
                _tcpListener.StartAsync(),
                _pipeListener.StartAsync());

            IsListening = true;
            StatusText = "Listening for connections...";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            MessageBox.Show($"Failed to start listeners:\n\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public async Task StopAsync()
    {
        if (!IsListening) return;

        try
        {
            StatusText = "Stopping listeners...";

            var tasks = new List<Task>();
            if (_tcpListener != null)
                tasks.Add(_tcpListener.StopAsync());
            if (_pipeListener != null)
                tasks.Add(_pipeListener.StopAsync());

            await Task.WhenAll(tasks);

            IsListening = false;
            StatusText = "Stopped";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    #endregion

    #region View Management

    private void AddView()
    {
        _viewCounter++;
        var newView = new LogViewViewModel(LogEntries, _logEntriesLock, $"View {_viewCounter}");
        Views.Add(newView);
        SelectedView = newView;
    }

    private bool CanRemoveView(LogViewViewModel? view)
    {
        // Can't remove if it's the primary view or if it's the last view
        return view != null && !view.IsPrimaryView && Views.Count > 1;
    }

    private void RemoveView(LogViewViewModel? view)
    {
        if (view == null || view.IsPrimaryView || Views.Count <= 1) return;

        // Show confirmation dialog
        var result = MessageBox.Show(
            $"Are you sure you want to close the view \"{view.Name}\"?",
            "Close View",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        var index = Views.IndexOf(view);
        Views.Remove(view);

        // Select adjacent view
        if (index >= Views.Count)
            index = Views.Count - 1;
        SelectedView = Views[index];
    }

    private void RenameView(LogViewViewModel? view)
    {
        if (view == null) return;

        // This would typically show a dialog - for now we'll handle it in the UI
        // The view name is already bindable and editable
    }

    private void DuplicateView(LogViewViewModel? view)
    {
        if (view == null) return;

        _viewCounter++;
        var newView = new LogViewViewModel(LogEntries, _logEntriesLock, $"{view.Name} (Copy)")
        {
            AppNameFilter = view.AppNameFilter,
            SessionFilter = view.SessionFilter,
            TextFilter = view.TextFilter,
            SelectedLogLevel = view.SelectedLogLevel
        };
        Views.Add(newView);
        SelectedView = newView;
    }

    private void EditView(LogViewViewModel? view)
    {
        if (view == null) return;

        var editViewModel = new EditViewViewModel();
        editViewModel.LoadFrom(view);
        editViewModel.SetAvailableValues(AvailableAppNames, AvailableSessions, AvailableHostnames);
        editViewModel.SyncSelectionStates();

        var dialog = new EditViewDialog(editViewModel)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            editViewModel.SaveTo(view);
        }
    }

    private void ClearViewLog(LogViewViewModel? view)
    {
        if (view == null) return;

        view.ClearFilteredEntries();

        // Update all views since we modified the shared collection
        foreach (var v in Views)
        {
            v.RefreshFilter();
        }

        OnPropertyChanged(nameof(EntryCount));
    }

    #endregion

    #region Detail Tab Management

    private void OpenLogEntryDetail(LogEntry? logEntry)
    {
        if (logEntry == null) return;

        // Check if already open
        var existing = DetailTabs.FirstOrDefault(d => d.LogEntry == logEntry);
        if (existing != null)
        {
            SelectedDetailTab = existing;
            return;
        }

        var detailVm = new LogEntryDetailViewModel(logEntry);
        DetailTabs.Add(detailVm);
        SelectedDetailTab = detailVm;
        OnPropertyChanged(nameof(HasDetailTabs));
    }

    private void CloseDetailTab(LogEntryDetailViewModel? detailVm)
    {
        if (detailVm == null) return;

        var index = DetailTabs.IndexOf(detailVm);
        DetailTabs.Remove(detailVm);

        // Select adjacent tab if available
        if (DetailTabs.Count > 0)
        {
            if (index >= DetailTabs.Count)
                index = DetailTabs.Count - 1;
            SelectedDetailTab = DetailTabs[index];
        }
        else
        {
            SelectedDetailTab = null;
        }

        OnPropertyChanged(nameof(HasDetailTabs));
    }

    #endregion

    #region Private Methods

    private void OnPacketReceived(object? sender, PacketReceivedEventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            switch (e.Packet)
            {
                case LogEntry logEntry:
                    HandleLogEntry(logEntry);
                    break;

                case Watch watch:
                    HandleWatch(watch);
                    break;

                case ProcessFlow processFlow:
                    HandleProcessFlow(processFlow);
                    break;

                case ControlCommand controlCommand:
                    HandleControlCommand(controlCommand);
                    break;

                case LogHeader logHeader:
                    HandleLogHeader(logHeader, e.ClientId);
                    break;
            }
        });
    }

    private void HandleLogEntry(LogEntry logEntry)
    {
        // Calculate elapsed time since previous entry
        if (_lastLogEntryTimestamp.HasValue)
        {
            logEntry.ElapsedTime = logEntry.Timestamp - _lastLogEntryTimestamp.Value;
        }
        _lastLogEntryTimestamp = logEntry.Timestamp;

        lock (_logEntriesLock)
        {
            LogEntries.Add(logEntry);
        }

        // Track available filter values
        TrackAvailableFilterValues(logEntry);

        // Notify all views to update their filtered counts
        foreach (var view in Views)
        {
            view.RefreshFilter();
        }

        OnPropertyChanged(nameof(EntryCount));
    }

    private void TrackAvailableFilterValues(LogEntry logEntry)
    {
        // Track app names
        if (!string.IsNullOrWhiteSpace(logEntry.AppName) &&
            !AvailableAppNames.Contains(logEntry.AppName))
        {
            AvailableAppNames.Add(logEntry.AppName);
        }

        // Track session names
        if (!string.IsNullOrWhiteSpace(logEntry.SessionName) &&
            !AvailableSessions.Contains(logEntry.SessionName))
        {
            AvailableSessions.Add(logEntry.SessionName);
        }

        // Track hostnames
        if (!string.IsNullOrWhiteSpace(logEntry.HostName) &&
            !AvailableHostnames.Contains(logEntry.HostName))
        {
            AvailableHostnames.Add(logEntry.HostName);
        }
    }

    private void HandleWatch(Watch watch)
    {
        lock (_logEntriesLock)
        {
            // Update existing watch or add new one
            var existing = Watches.FirstOrDefault(w =>
                w.Name.Equals(watch.Name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                var index = Watches.IndexOf(existing);
                Watches[index] = watch;
            }
            else
            {
                Watches.Add(watch);
            }
        }
    }

    private void HandleProcessFlow(ProcessFlow processFlow)
    {
        lock (_logEntriesLock)
        {
            ProcessFlows.Add(processFlow);
        }
    }

    private void HandleControlCommand(ControlCommand command)
    {
        switch (command.ControlCommandType)
        {
            case ControlCommandType.ClearAll:
                ClearAll();
                break;

            case ControlCommandType.ClearLog:
                ClearLog();
                break;

            case ControlCommandType.ClearWatches:
                ClearWatches();
                break;

            case ControlCommandType.ClearProcessFlow:
                ClearProcessFlow();
                break;
        }
    }

    private void HandleLogHeader(LogHeader header, string clientId)
    {
        header.ParseContent();
        StatusText = $"Client connected: {header.AppName ?? "Unknown"} @ {header.HostName ?? "Unknown"}";
    }

    private void OnClientConnected(object? sender, ClientEventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            StatusText = $"Client connected: {e.ClientId}";
            OnPropertyChanged(nameof(TcpStatus));
            OnPropertyChanged(nameof(PipeStatus));
        });
    }

    private void OnClientDisconnected(object? sender, ClientEventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            StatusText = $"Client disconnected: {e.ClientId}";
            OnPropertyChanged(nameof(TcpStatus));
            OnPropertyChanged(nameof(PipeStatus));
        });
    }

    private void OnError(object? sender, Exception e)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            StatusText = $"Error: {e.Message}";
        });
    }

    private void ClearAll()
    {
        ClearLog();
        ClearWatches();
        ClearProcessFlow();
    }

    private void ClearLog()
    {
        _lastLogEntryTimestamp = null;

        lock (_logEntriesLock)
        {
            LogEntries.Clear();
            DetailTabs.Clear();
        }

        // Clear available filter values
        AvailableAppNames.Clear();
        AvailableSessions.Clear();
        AvailableHostnames.Clear();

        foreach (var view in Views)
        {
            view.Clear();
        }

        SelectedDetailTab = null;
        OnPropertyChanged(nameof(EntryCount));
        OnPropertyChanged(nameof(HasDetailTabs));
    }

    private void ClearWatches()
    {
        lock (_logEntriesLock)
        {
            Watches.Clear();
        }
    }

    private void ClearProcessFlow()
    {
        lock (_logEntriesLock)
        {
            ProcessFlows.Clear();
        }
    }

    #endregion

    #region State Management

    /// <summary>
    /// Captures the current view model state for saving.
    /// </summary>
    public void SaveStateTo(AppState state)
    {
        // Panel visibility
        state.ShowWatchesPanel = ShowWatchesPanel;
        state.ShowProcessFlowPanel = ShowProcessFlowPanel;
        state.ShowDetailsPanel = ShowDetailsPanel;

        // Network settings
        state.TcpPort = TcpPort;
        state.PipeName = PipeName;

        // Views
        state.Views.Clear();
        foreach (var view in Views)
        {
            state.Views.Add(CaptureViewState(view));
        }

        // Selected view index
        state.SelectedViewIndex = SelectedView != null ? Views.IndexOf(SelectedView) : 0;
    }

    /// <summary>
    /// Restores view model state from saved state.
    /// </summary>
    public void RestoreStateFrom(AppState state)
    {
        // Panel visibility
        ShowWatchesPanel = state.ShowWatchesPanel;
        ShowProcessFlowPanel = state.ShowProcessFlowPanel;
        ShowDetailsPanel = state.ShowDetailsPanel;

        // Network settings
        TcpPort = state.TcpPort > 0 ? state.TcpPort : SmartInspectTcpListener.DefaultPort;
        PipeName = !string.IsNullOrWhiteSpace(state.PipeName) ? state.PipeName : SmartInspectPipeListener.DefaultPipeName;

        // Restore views if any saved
        if (state.Views.Count > 0)
        {
            Views.Clear();
            var isFirst = true;
            foreach (var viewState in state.Views)
            {
                // First view is always the primary view that cannot be closed
                var view = new LogViewViewModel(LogEntries, _logEntriesLock, viewState.Name, isPrimaryView: isFirst);
                ApplyViewState(view, viewState);
                Views.Add(view);
                isFirst = false;
            }

            // Restore selected view
            var index = Math.Max(0, Math.Min(state.SelectedViewIndex, Views.Count - 1));
            SelectedView = Views[index];

            // Update view counter
            _viewCounter = Views.Count;
        }
    }

    private static ViewState CaptureViewState(LogViewViewModel view)
    {
        return new ViewState
        {
            Name = view.Name,
            AppNameFilter = view.AppNameFilter,
            SessionFilter = view.SessionFilter,
            HostnameFilter = view.HostnameFilter,
            ProcessIdFilter = view.ProcessIdFilter,
            ThreadIdFilter = view.ThreadIdFilter,
            TextFilter = view.TextFilter,
            MinLogLevel = view.MinLogLevel?.ToString(),
            EnableTitleMatching = view.EnableTitleMatching,
            TitlePattern = view.TitlePattern,
            TitleCaseSensitive = view.TitleCaseSensitive,
            TitleIsRegex = view.TitleIsRegex,
            TitleInvert = view.TitleInvert,
            ShowDebug = view.ShowDebug,
            ShowVerbose = view.ShowVerbose,
            ShowMessage = view.ShowMessage,
            ShowWarning = view.ShowWarning,
            ShowError = view.ShowError,
            ShowFatal = view.ShowFatal,
            ShowMethodFlow = view.ShowMethodFlow,
            ShowSeparator = view.ShowSeparator,
            ShowOther = view.ShowOther,
            ShowTimeColumn = view.ShowTimeColumn,
            ShowElapsedColumn = view.ShowElapsedColumn,
            ShowAppColumn = view.ShowAppColumn,
            ShowSessionColumn = view.ShowSessionColumn,
            ShowTitleColumn = view.ShowTitleColumn,
            ShowThreadColumn = view.ShowThreadColumn,
            AutoScroll = view.AutoScroll
        };
    }

    private static void ApplyViewState(LogViewViewModel view, ViewState state)
    {
        view.AppNameFilter = state.AppNameFilter ?? string.Empty;
        view.SessionFilter = state.SessionFilter ?? string.Empty;
        view.HostnameFilter = state.HostnameFilter ?? string.Empty;
        view.ProcessIdFilter = state.ProcessIdFilter ?? string.Empty;
        view.ThreadIdFilter = state.ThreadIdFilter ?? string.Empty;
        view.TextFilter = state.TextFilter ?? string.Empty;

        if (!string.IsNullOrEmpty(state.MinLogLevel) &&
            Enum.TryParse<LogEntryType>(state.MinLogLevel, out var logLevel))
        {
            view.MinLogLevel = logLevel;
            // Find and set the matching log level option
            var option = view.LogLevels.FirstOrDefault(l => l.Value == logLevel);
            if (option != null)
                view.SelectedLogLevel = option;
        }

        view.EnableTitleMatching = state.EnableTitleMatching;
        view.TitlePattern = state.TitlePattern ?? string.Empty;
        view.TitleCaseSensitive = state.TitleCaseSensitive;
        view.TitleIsRegex = state.TitleIsRegex;
        view.TitleInvert = state.TitleInvert;

        view.ShowDebug = state.ShowDebug;
        view.ShowVerbose = state.ShowVerbose;
        view.ShowMessage = state.ShowMessage;
        view.ShowWarning = state.ShowWarning;
        view.ShowError = state.ShowError;
        view.ShowFatal = state.ShowFatal;
        view.ShowMethodFlow = state.ShowMethodFlow;
        view.ShowSeparator = state.ShowSeparator;
        view.ShowOther = state.ShowOther;

        view.ShowTimeColumn = state.ShowTimeColumn;
        view.ShowElapsedColumn = state.ShowElapsedColumn;
        view.ShowAppColumn = state.ShowAppColumn;
        view.ShowSessionColumn = state.ShowSessionColumn;
        view.ShowTitleColumn = state.ShowTitleColumn;
        view.ShowThreadColumn = state.ShowThreadColumn;
        view.AutoScroll = state.AutoScroll;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _tcpListener?.Dispose();
        _pipeListener?.Dispose();
    }

    #endregion
}
