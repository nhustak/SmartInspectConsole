namespace SmartInspectConsole.Models;

/// <summary>
/// A configurable remote SmartInspect Console reachable over SSH port-forward.
/// Local attach tunnels to the remote loopback local API (default 42331).
/// </summary>
public sealed class RemoteServerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Display name (e.g. SureCourt, CC3 Prod).</summary>
    public string Name { get; set; } = string.Empty;

    public string SshHost { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public string SshUser { get; set; } = string.Empty;

    /// <summary>password | privateKey</summary>
    public string AuthMethod { get; set; } = "privateKey";

    public string? PrivateKeyPath { get; set; }
    public string? PrivateKeyPassphrase { get; set; }
    public string? Password { get; set; }

    /// <summary>Remote SmartInspect local API port (loopback on the server).</summary>
    public int RemoteApiPort { get; set; } = 42331;

    /// <summary>0 = choose an ephemeral local port for the tunnel.</summary>
    public int LocalTunnelPort { get; set; }

    /// <summary>How many newest log entries to pull on attach (catch-up).</summary>
    public int CatchUpLimit { get; set; } = 2000;

    /// <summary>Live poll interval while attached (ms).</summary>
    public int PollIntervalMs { get; set; } = 750;

    public bool Enabled { get; set; } = true;

    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(Name)
            ? $"{SshUser}@{SshHost}:{SshPort}"
            : $"{Name} ({SshUser}@{SshHost})";
}
