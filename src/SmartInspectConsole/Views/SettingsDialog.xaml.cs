using System.Windows;
using SmartInspectConsole.Core.Listeners;
using SmartInspectConsole.Helpers;

namespace SmartInspectConsole.Views;

/// <summary>
/// Interaction logic for SettingsDialog.xaml
/// </summary>
public partial class SettingsDialog : Window
{
    public int TcpPort { get; private set; }
    public string PipeName { get; private set; }
    public int WebSocketPort { get; private set; }
    public bool DebugMode { get; private set; }
    public int MaxLogEntries { get; private set; }
    public bool ConfirmBeforeClear { get; private set; }

    public SettingsDialog(int currentPort, string currentPipeName, int currentWebSocketPort, bool debugMode, int maxLogEntries, bool confirmBeforeClear)
    {
        InitializeComponent();
        TcpPort = currentPort;
        PipeName = currentPipeName;
        WebSocketPort = currentWebSocketPort;
        DebugMode = debugMode;
        MaxLogEntries = maxLogEntries;
        ConfirmBeforeClear = confirmBeforeClear;
        TcpPortTextBox.Text = currentPort.ToString();
        PipeNameTextBox.Text = currentPipeName;
        WebSocketPortTextBox.Text = currentWebSocketPort.ToString();
        DebugModeCheckBox.IsChecked = debugMode;
        MaxLogEntriesTextBox.Text = maxLogEntries.ToString("N0");
        ConfirmBeforeClearCheckBox.IsChecked = confirmBeforeClear;
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        // Validate TCP port
        if (!int.TryParse(TcpPortTextBox.Text, out var port) || port <= 0 || port > 65535)
        {
            MessageBoxHelper.Show("Please enter a valid port number (1-65535).", "Invalid Port",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TcpPortTextBox.Focus();
            return;
        }

        // Validate pipe name
        var pipeName = PipeNameTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            MessageBoxHelper.Show("Please enter a valid pipe name.", "Invalid Pipe Name",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            PipeNameTextBox.Focus();
            return;
        }

        // Validate WebSocket port
        if (!int.TryParse(WebSocketPortTextBox.Text, out var wsPort) || wsPort <= 0 || wsPort > 65535)
        {
            MessageBoxHelper.Show("Please enter a valid WebSocket port number (1-65535).", "Invalid Port",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            WebSocketPortTextBox.Focus();
            return;
        }

        // Check for port conflicts
        if (port == wsPort)
        {
            MessageBoxHelper.Show("TCP and WebSocket ports must be different.", "Port Conflict",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            WebSocketPortTextBox.Focus();
            return;
        }

        // Validate max log entries
        var maxLogText = MaxLogEntriesTextBox.Text.Replace(",", "").Replace(" ", "");
        if (!int.TryParse(maxLogText, out var maxLog) || maxLog < 1000)
        {
            MessageBoxHelper.Show("Max log entries must be at least 1,000.", "Invalid Value",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            MaxLogEntriesTextBox.Focus();
            return;
        }

        TcpPort = port;
        PipeName = pipeName;
        WebSocketPort = wsPort;
        DebugMode = DebugModeCheckBox.IsChecked == true;
        ConfirmBeforeClear = ConfirmBeforeClearCheckBox.IsChecked == true;
        MaxLogEntries = maxLog;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void DefaultPort_Click(object sender, RoutedEventArgs e)
    {
        TcpPortTextBox.Text = SmartInspectTcpListener.DefaultPort.ToString();
    }

    private void DefaultPipe_Click(object sender, RoutedEventArgs e)
    {
        PipeNameTextBox.Text = SmartInspectPipeListener.DefaultPipeName;
    }

    private void DefaultWebSocket_Click(object sender, RoutedEventArgs e)
    {
        WebSocketPortTextBox.Text = SmartInspectWebSocketListener.DefaultPort.ToString();
    }

    private void DefaultMaxLogEntries_Click(object sender, RoutedEventArgs e)
    {
        MaxLogEntriesTextBox.Text = "100,000";
    }
}
