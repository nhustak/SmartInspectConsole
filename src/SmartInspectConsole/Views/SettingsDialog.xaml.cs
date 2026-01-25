using System.Windows;
using SmartInspectConsole.Core.Listeners;

namespace SmartInspectConsole.Views;

/// <summary>
/// Interaction logic for SettingsDialog.xaml
/// </summary>
public partial class SettingsDialog : Window
{
    public int TcpPort { get; private set; }
    public string PipeName { get; private set; }

    public SettingsDialog(int currentPort, string currentPipeName)
    {
        InitializeComponent();
        TcpPort = currentPort;
        PipeName = currentPipeName;
        TcpPortTextBox.Text = currentPort.ToString();
        PipeNameTextBox.Text = currentPipeName;
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        // Validate TCP port
        if (!int.TryParse(TcpPortTextBox.Text, out var port) || port <= 0 || port > 65535)
        {
            MessageBox.Show("Please enter a valid port number (1-65535).", "Invalid Port",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TcpPortTextBox.Focus();
            return;
        }

        // Validate pipe name
        var pipeName = PipeNameTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            MessageBox.Show("Please enter a valid pipe name.", "Invalid Pipe Name",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            PipeNameTextBox.Focus();
            return;
        }

        TcpPort = port;
        PipeName = pipeName;
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
}
