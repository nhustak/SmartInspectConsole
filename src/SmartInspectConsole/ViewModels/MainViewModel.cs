using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using SmartInspectConsole.Core.Enums;
using SmartInspectConsole.Core.Events;
using SmartInspectConsole.Core.Listeners;
using SmartInspectConsole.Core.Packets;
using SmartInspectConsole.Views;

namespace SmartInspectConsole.ViewModels;

/// <summary>
/// Main view model for the SmartInspect Console.
/// </summary>
public class MainViewModel : ViewModelBase, IDisposable
{
    private readonly SmartInspectTcpListener _tcpListener;
    private readonly SmartInspectPipeListener _pipeListener;
    private readonly object _logEntriesLock = new();

    private DateTime? _lastLogEntryTimestamp;
    private string _statusText = "Ready";
    private bool _isListening;
    private LogViewViewModel? _selectedView;
    private int _viewCounter = 1;

    // Panel visibility
    private bool _showWatchesPanel = true;
    private bool _showProcessFlowPanel = true;
    private bool _showDetailsPanel = true;

    public MainViewModel()
    {
        _tcpListener = new SmartInspectTcpListener();
        _pipeListener = new SmartInspectPipeListener();

        // Wire up events
        _tcpListener.PacketReceived += OnPacketReceived;
        _tcpListener.ClientConnected += OnClientConnected;
        _tcpListener.ClientDisconnected += OnClientDisconnected;
        _tcpListener.Error += OnError;

        _pipeListener.PacketReceived += OnPacketReceived;
        _pipeListener.ClientConnected += OnClientConnected;
        _pipeListener.ClientDisconnected += OnClientDisconnected;
        _pipeListener.Error += OnError;

        // Initialize collections
        LogEntries = new ObservableCollection<LogEntry>();
        Watches = new ObservableCollection<Watch>();
        ProcessFlows = new ObservableCollection<ProcessFlow>();
        Views = new ObservableCollection<LogViewViewModel>();

        // Enable collection synchronization for cross-thread access
        BindingOperations.EnableCollectionSynchronization(LogEntries, _logEntriesLock);
        BindingOperations.EnableCollectionSynchronization(Watches, _logEntriesLock);
        BindingOperations.EnableCollectionSynchronization(ProcessFlows, _logEntriesLock);
        BindingOperations.EnableCollectionSynchronization(Views, _logEntriesLock);

        // Create default "All" view
        var allView = new LogViewViewModel(LogEntries, _logEntriesLock, "All");
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
    }

    #region Properties

    public ObservableCollection<LogEntry> LogEntries { get; }
    public ObservableCollection<Watch> Watches { get; }
    public ObservableCollection<ProcessFlow> ProcessFlows { get; }
    public ObservableCollection<LogViewViewModel> Views { get; }

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

    public string TcpStatus => IsListening
        ? $"TCP: Port {SmartInspectTcpListener.DefaultPort} ({_tcpListener.ClientCount} clients)"
        : "TCP: Stopped";

    public string PipeStatus => IsListening
        ? $"Pipe: {_pipeListener.PipeName} ({_pipeListener.ClientCount} clients)"
        : "Pipe: Stopped";

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

    #endregion

    #region Public Methods

    public async Task StartAsync()
    {
        if (IsListening) return;

        try
        {
            StatusText = "Starting listeners...";

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

            await Task.WhenAll(
                _tcpListener.StopAsync(),
                _pipeListener.StopAsync());

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
        // Can't remove if it's the last view
        return view != null && Views.Count > 1;
    }

    private void RemoveView(LogViewViewModel? view)
    {
        if (view == null || Views.Count <= 1) return;

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

        var dialog = new EditViewDialog(editViewModel)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            editViewModel.SaveTo(view);
        }
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

        // Notify all views to update their filtered counts
        foreach (var view in Views)
        {
            view.RefreshFilter();
        }

        OnPropertyChanged(nameof(EntryCount));
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
        }

        foreach (var view in Views)
        {
            view.Clear();
        }

        OnPropertyChanged(nameof(EntryCount));
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

    #region IDisposable

    public void Dispose()
    {
        _tcpListener.Dispose();
        _pipeListener.Dispose();
    }

    #endregion
}
