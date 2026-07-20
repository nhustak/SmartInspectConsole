using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SmartInspectConsole.Helpers;
using SmartInspectConsole.Models;

namespace SmartInspectConsole.Views;

public partial class RemoteServersDialog : Window
{
    private readonly ObservableCollection<RemoteServerProfile> _servers;
    private bool _suppressSelection;

    public IReadOnlyList<RemoteServerProfile> ResultServers { get; private set; } = [];

    public RemoteServersDialog(IEnumerable<RemoteServerProfile> existing)
    {
        InitializeComponent();
        _servers = new ObservableCollection<RemoteServerProfile>(
            existing.Select(Clone));
        ServersList.ItemsSource = _servers;

        if (_servers.Count == 0)
        {
            _servers.Add(CreateDefault("SureCourt", "surecourt.example"));
            _servers.Add(CreateDefault("CC3 Prod", "cc3prod.example"));
        }

        ServersList.SelectedIndex = 0;
    }

    private static RemoteServerProfile CreateDefault(string name, string host) => new()
    {
        Name = name,
        SshHost = host,
        SshPort = 22,
        SshUser = Environment.UserName,
        AuthMethod = "privateKey",
        PrivateKeyPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ssh",
            "id_rsa"),
        RemoteApiPort = 42331,
        CatchUpLimit = 2000,
        PollIntervalMs = 750,
        Enabled = true
    };

    private static RemoteServerProfile Clone(RemoteServerProfile s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        SshHost = s.SshHost,
        SshPort = s.SshPort,
        SshUser = s.SshUser,
        AuthMethod = s.AuthMethod,
        PrivateKeyPath = s.PrivateKeyPath,
        PrivateKeyPassphrase = s.PrivateKeyPassphrase,
        Password = s.Password,
        RemoteApiPort = s.RemoteApiPort,
        LocalTunnelPort = s.LocalTunnelPort,
        CatchUpLimit = s.CatchUpLimit,
        PollIntervalMs = s.PollIntervalMs,
        Enabled = s.Enabled
    };

    private void ServersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection)
            return;

        if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is RemoteServerProfile previous)
            SaveEditorTo(previous);

        if (ServersList.SelectedItem is RemoteServerProfile selected)
            LoadEditorFrom(selected);
        else
            EditorPanel.IsEnabled = false;
    }

    private void LoadEditorFrom(RemoteServerProfile s)
    {
        EditorPanel.IsEnabled = true;
        NameBox.Text = s.Name;
        HostBox.Text = s.SshHost;
        SshPortBox.Text = s.SshPort.ToString();
        UserBox.Text = s.SshUser;
        AuthBox.SelectedIndex = string.Equals(s.AuthMethod, "password", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        KeyPathBox.Text = s.PrivateKeyPath ?? string.Empty;
        KeyPassBox.Password = s.PrivateKeyPassphrase ?? string.Empty;
        PasswordBox.Password = s.Password ?? string.Empty;
        RemoteApiPortBox.Text = s.RemoteApiPort.ToString();
        LocalTunnelPortBox.Text = s.LocalTunnelPort.ToString();
        CatchUpBox.Text = s.CatchUpLimit.ToString();
        PollBox.Text = s.PollIntervalMs.ToString();
        EnabledBox.IsChecked = s.Enabled;
        UpdateAuthFieldsEnabled();
    }

    private void SaveEditorTo(RemoteServerProfile s)
    {
        s.Name = NameBox.Text.Trim();
        s.SshHost = HostBox.Text.Trim();
        s.SshPort = int.TryParse(SshPortBox.Text, out var sp) ? sp : 22;
        s.SshUser = UserBox.Text.Trim();
        s.AuthMethod = AuthBox.SelectedIndex == 1 ? "password" : "privateKey";
        s.PrivateKeyPath = KeyPathBox.Text.Trim();
        s.PrivateKeyPassphrase = string.IsNullOrEmpty(KeyPassBox.Password) ? null : KeyPassBox.Password;
        s.Password = string.IsNullOrEmpty(PasswordBox.Password) ? null : PasswordBox.Password;
        s.RemoteApiPort = int.TryParse(RemoteApiPortBox.Text, out var rap) ? rap : 42331;
        s.LocalTunnelPort = int.TryParse(LocalTunnelPortBox.Text, out var ltp) ? ltp : 0;
        s.CatchUpLimit = int.TryParse(CatchUpBox.Text, out var cu) ? cu : 2000;
        s.PollIntervalMs = int.TryParse(PollBox.Text, out var pi) ? pi : 750;
        s.Enabled = EnabledBox.IsChecked == true;

        // Refresh list label
        var idx = _servers.IndexOf(s);
        if (idx >= 0)
        {
            _suppressSelection = true;
            _servers[idx] = s;
            ServersList.SelectedItem = s;
            _suppressSelection = false;
        }
    }

    private void AuthBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateAuthFieldsEnabled();
    }

    private void UpdateAuthFieldsEnabled()
    {
        var password = AuthBox.SelectedIndex == 1;
        KeyPathBox.IsEnabled = !password;
        KeyPassBox.IsEnabled = !password;
        PasswordBox.IsEnabled = password;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (ServersList.SelectedItem is RemoteServerProfile current)
            SaveEditorTo(current);

        var profile = CreateDefault("New server", "");
        _servers.Add(profile);
        ServersList.SelectedItem = profile;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (ServersList.SelectedItem is not RemoteServerProfile selected)
            return;

        var idx = _servers.IndexOf(selected);
        _servers.Remove(selected);
        if (_servers.Count == 0)
        {
            EditorPanel.IsEnabled = false;
            return;
        }

        ServersList.SelectedIndex = Math.Clamp(idx, 0, _servers.Count - 1);
    }

    private void BrowseKey_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select SSH private key",
            Filter = "All files (*.*)|*.*|PEM (*.pem)|*.pem|Key (*.key)|*.key"
        };
        if (dlg.ShowDialog(this) == true)
            KeyPathBox.Text = dlg.FileName;
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (ServersList.SelectedItem is RemoteServerProfile selected)
            SaveEditorTo(selected);

        foreach (var s in _servers)
        {
            if (string.IsNullOrWhiteSpace(s.Name) || string.IsNullOrWhiteSpace(s.SshHost) || string.IsNullOrWhiteSpace(s.SshUser))
            {
                MessageBoxHelper.Show(
                    "Each server needs Name, SSH Host, and SSH User.",
                    "Remote Servers",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        ResultServers = _servers.Select(Clone).ToList();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
