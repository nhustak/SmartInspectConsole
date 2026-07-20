using System.IO;
using Renci.SshNet;

namespace SmartInspectConsole.Services;

/// <summary>
/// SSH session with a local port forward to a remote loopback API port.
/// </summary>
public sealed class SshTunnelSession : IDisposable
{
    private readonly SshClient _client;
    private readonly ForwardedPortLocal _forwardedPort;
    private bool _disposed;

    public SshTunnelSession(SshClient client, ForwardedPortLocal forwardedPort)
    {
        _client = client;
        _forwardedPort = forwardedPort;
    }

    public uint LocalPort => _forwardedPort.BoundPort;

    public string LocalBaseUrl => $"http://127.0.0.1:{LocalPort}";

    public bool IsConnected => _client.IsConnected && _forwardedPort.IsStarted;

    public static SshTunnelSession Connect(
        string host,
        int sshPort,
        string user,
        string authMethod,
        string? password,
        string? privateKeyPath,
        string? privateKeyPassphrase,
        int remoteApiPort,
        uint preferredLocalPort = 0)
    {
        ConnectionInfo connectionInfo;
        if (string.Equals(authMethod, "password", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException("SSH password is required for password authentication.");

            connectionInfo = new ConnectionInfo(host, sshPort, user, new PasswordAuthenticationMethod(user, password));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(privateKeyPath) || !File.Exists(privateKeyPath))
                throw new FileNotFoundException("SSH private key file was not found.", privateKeyPath);

            PrivateKeyFile keyFile = string.IsNullOrEmpty(privateKeyPassphrase)
                ? new PrivateKeyFile(privateKeyPath)
                : new PrivateKeyFile(privateKeyPath, privateKeyPassphrase);

            connectionInfo = new ConnectionInfo(host, sshPort, user, new PrivateKeyAuthenticationMethod(user, keyFile));
        }

        var client = new SshClient(connectionInfo)
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30)
        };

        try
        {
            client.Connect();
            if (!client.IsConnected)
                throw new InvalidOperationException("SSH connection failed.");

            // Forward local 127.0.0.1:localPort -> remote 127.0.0.1:remoteApiPort
            var forward = preferredLocalPort == 0
                ? new ForwardedPortLocal("127.0.0.1", "127.0.0.1", (uint)remoteApiPort)
                : new ForwardedPortLocal("127.0.0.1", preferredLocalPort, "127.0.0.1", (uint)remoteApiPort);

            client.AddForwardedPort(forward);
            forward.Start();

            return new SshTunnelSession(client, forward);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            if (_forwardedPort.IsStarted)
                _forwardedPort.Stop();
        }
        catch
        {
            // ignore
        }

        try
        {
            _client.Disconnect();
        }
        catch
        {
            // ignore
        }

        _forwardedPort.Dispose();
        _client.Dispose();
    }
}
