using SmartInspectConsole.ViewModels;

namespace SmartInspectConsole.Models;

/// <summary>
/// Represents a connected application in the Connection Manager panel.
/// </summary>
public class ConnectedApplication : ViewModelBase
{
    private string _clientId = string.Empty;
    private string _appName = string.Empty;
    private string _hostName = string.Empty;
    private long _messageCount;
    private bool _isMuted;
    private bool _isConnected = true;

    public string ClientId
    {
        get => _clientId;
        set => SetProperty(ref _clientId, value);
    }

    public string AppName
    {
        get => _appName;
        set
        {
            if (SetProperty(ref _appName, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string HostName
    {
        get => _hostName;
        set
        {
            if (SetProperty(ref _hostName, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string DisplayName => string.IsNullOrEmpty(AppName)
        ? ClientId
        : string.IsNullOrEmpty(HostName) ? AppName : $"{AppName} @ {HostName}";

    /// <summary>
    /// Key used for mute lookup: "AppName@HostName"
    /// </summary>
    public string MuteKey => $"{AppName}@{HostName}";

    public long MessageCount
    {
        get => _messageCount;
        set => SetProperty(ref _messageCount, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        set => SetProperty(ref _isMuted, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    public DateTime DisconnectedAt { get; set; }
}
