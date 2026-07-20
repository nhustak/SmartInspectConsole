using System.Windows;
using SmartInspectConsole.Helpers;
using SmartInspectConsole.Services;

namespace SmartInspectConsole.Views;

public partial class ExportAttachInviteDialog : Window
{
    public ExportAttachInviteDialog()
    {
        InitializeComponent();
        NameBox.Text = RemoteAttachInviteService.SuggestDisplayName();
        HostBox.Text = RemoteAttachInviteService.SuggestHostName();
        SshPortBox.Text = "22";
        UserBox.Text = RemoteAttachInviteService.SuggestUser();
        ApiPortBox.Text = "42331";
        CatchUpBox.Text = "2000";
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Working…";

            var name = NameBox.Text.Trim();
            var host = HostBox.Text.Trim();
            var user = UserBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user))
            {
                MessageBoxHelper.Show("Name, SSH Host, and SSH User are required.", "Export Invite",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var sshPort = int.TryParse(SshPortBox.Text, out var sp) ? sp : 22;
            var apiPort = int.TryParse(ApiPortBox.Text, out var ap) ? ap : 42331;
            var catchUp = int.TryParse(CatchUpBox.Text, out var cu) ? cu : 2000;
            var passphrase = string.IsNullOrEmpty(PassphraseBox.Password) ? null : PassphraseBox.Password;

            var invite = GenerateKeysBox.IsChecked == true
                ? RemoteAttachInviteService.CreateInviteWithNewKeys(
                    name, host, sshPort, user, apiPort, catchUp, 750, passphrase)
                : RemoteAttachInviteService.CreateInviteProfileOnly(
                    name, host, sshPort, user, apiPort, catchUp, 750);

            var json = RemoteAttachInviteService.ToClipboardJson(invite);
            Clipboard.SetText(json);

            var keyNote = GenerateKeysBox.IsChecked == true
                ? (invite.PublicKeyInstalledOnRemote
                    ? "Public key installed on this machine's authorized_keys."
                    : "WARNING: could not update authorized_keys automatically — install the public key manually.")
                : "Profile-only invite (no private key).";

            StatusText.Text = "Invite copied to clipboard.";
            MessageBoxHelper.Show(
                "Attach invite copied to the clipboard.\n\n" +
                keyNote + "\n\n" +
                "On your laptop: Tools → Import SSH Attach Invite.\n\n" +
                "The invite includes a private key when generated — treat the clipboard as secret and clear it after import.\n\n" +
                "Remote OpenSSH Server must be running (sshd) for attach to work.",
                "Export Invite",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Failed.";
            MessageBoxHelper.Show(
                $"Failed to create invite:\n\n{ex.Message}",
                "Export Invite",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
