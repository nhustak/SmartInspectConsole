namespace SmartInspectConsole.Models;

/// <summary>
/// Clipboard-portable invite created on a remote SmartInspect Console.
/// Local consoles import this to create an SSH attach profile (+ private key file).
/// </summary>
public sealed class RemoteAttachInvite
{
    public const string FormatId = "SmartInspectConsole.RemoteAttachInvite/v1";

    public string Format { get; set; } = FormatId;
    public string Name { get; set; } = string.Empty;
    public string SshHost { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public string SshUser { get; set; } = string.Empty;
    public string AuthMethod { get; set; } = "privateKey";
    public int RemoteApiPort { get; set; } = 42331;
    public int CatchUpLimit { get; set; } = 2000;
    public int PollIntervalMs { get; set; } = 750;

    /// <summary>OpenSSH private key body (PEM / OpenSSH format).</summary>
    public string? PrivateKeyOpenSsh { get; set; }

    /// <summary>Matching public key line for documentation / re-install.</summary>
    public string? PublicKeyOpenSsh { get; set; }

    public bool PublicKeyInstalledOnRemote { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public string? GeneratedOnMachine { get; set; }
    public string? Notes { get; set; }
}
