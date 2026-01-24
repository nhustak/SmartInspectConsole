using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using SmartInspectConsole.Core.Enums;
using SmartInspectConsole.Core.Events;
using SmartInspectConsole.Core.Listeners;
using SmartInspectConsole.Core.Packets;

namespace SmartInspectConsole.ViewModels;

/// <summary>
/// Main view model for the SmartInspect Console.
/// </summary>
public class MainViewModel : ViewModelBase, IDisposable
{
    private readonly SmartInspectTcpListener _tcpListener;
    private readonly SmartInspectPipeListener _pipeListener;
    private readonly object _logEntriesLock = new();
    private readonly Dictionary<string, SessionViewModel> _sessionMap = new(StringComparer.OrdinalIgnoreCase);

    private LogEntry? _selectedLogEntry;
    private string _filterText = string.Empty;
    private SessionViewModel? _selectedSession;
    private string _statusText = "Ready";
    private bool _isListening;

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
        Sessions = new ObservableCollection<SessionViewModel> { SessionViewModel.All };

        // Enable collection synchronization for cross-thread access
        BindingOperations.EnableCollectionSynchronization(LogEntries, _logEntriesLock);
        BindingOperations.EnableCollectionSynchronization(Watches, _logEntriesLock);
        BindingOperations.EnableCollectionSynchronization(ProcessFlows, _logEntriesLock);
        BindingOperations.EnableCollectionSynchronization(Sessions, _logEntriesLock);

        // Setup filtered view
        FilteredLogEntries = CollectionViewSource.GetDefaultView(LogEntries);
        FilteredLogEntries.Filter = FilterLogEntry;

        // Initialize selected session
        SelectedSession = SessionViewModel.All;

        // Initialize commands
        StartCommand = new AsyncRelayCommand(StartAsync, () => !IsListening);
        StopCommand = new AsyncRelayCommand(StopAsync, () => IsListening);
        ClearAllCommand = new RelayCommand(ClearAll);
        ClearLogCommand = new RelayCommand(ClearLog);
        ClearWatchesCommand = new RelayCommand(ClearWatches);
        ClearProcessFlowCommand = new RelayCommand(ClearProcessFlow);
    }

    #region Properties

    public ObservableCollection<LogEntry> LogEntries { get; }
    public ObservableCollection<Watch> Watches { get; }
    public ObservableCollection<ProcessFlow> ProcessFlows { get; }
    public ObservableCollection<SessionViewModel> Sessions { get; }
    public ICollectionView FilteredLogEntries { get; }

    public LogEntry? SelectedLogEntry
    {
        get => _selectedLogEntry;
        set => SetProperty(ref _selectedLogEntry, value);
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                FilteredLogEntries.Refresh();
            }
        }
    }

    public SessionViewModel? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                FilteredLogEntries.Refresh();
            }
        }
    }

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

    #endregion

    #region Commands

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand ClearWatchesCommand { get; }
    public ICommand ClearProcessFlowCommand { get; }

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
        lock (_logEntriesLock)
        {
            LogEntries.Add(logEntry);

            // Update session list
            if (!string.IsNullOrEmpty(logEntry.SessionName) &&
                !_sessionMap.ContainsKey(logEntry.SessionName))
            {
                var session = new SessionViewModel { Name = logEntry.SessionName };
                _sessionMap[logEntry.SessionName] = session;
                Sessions.Add(session);
            }

            if (_sessionMap.TryGetValue(logEntry.SessionName, out var existingSession))
            {
                existingSession.EntryCount++;
            }
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

    private bool FilterLogEntry(object obj)
    {
        if (obj is not LogEntry entry)
            return false;

        // Session filter
        if (SelectedSession != null && SelectedSession != SessionViewModel.All)
        {
            if (!entry.SessionName.Equals(SelectedSession.Name, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Text filter
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            return entry.Title.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                   entry.SessionName.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                   (entry.DataAsString?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        return true;
    }

    private void ClearAll()
    {
        ClearLog();
        ClearWatches();
        ClearProcessFlow();
    }

    private void ClearLog()
    {
        lock (_logEntriesLock)
        {
            LogEntries.Clear();
            _sessionMap.Clear();
            Sessions.Clear();
            Sessions.Add(SessionViewModel.All);
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
