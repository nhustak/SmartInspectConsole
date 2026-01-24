using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SmartInspectConsole.Core.Enums;
using SmartInspectConsole.Core.Events;
using SmartInspectConsole.Core.Packets;
using SmartInspectConsole.Core.Parsing;

namespace SmartInspectConsole.Core.Listeners;

/// <summary>
/// TCP listener for receiving SmartInspect packets.
/// </summary>
public class SmartInspectTcpListener : IPacketListener
{
    public const int DefaultPort = 4228;
    private const string ServerBanner = "SmartInspect Console v1.0\n";
    private const int AnswerSize = 2;
    private static readonly byte[] Acknowledgment = [0, 0];

    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, TcpClient> _clients = new();
    private readonly BinaryPacketReader _packetReader = new();
    private int _clientCounter;

    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    public event EventHandler<ClientEventArgs>? ClientConnected;
    public event EventHandler<ClientEventArgs>? ClientDisconnected;
    public event EventHandler<Exception>? Error;

    public bool IsListening { get; private set; }

    /// <summary>
    /// Gets the number of connected clients.
    /// </summary>
    public int ClientCount => _clients.Count;

    public SmartInspectTcpListener(int port = DefaultPort)
    {
        _port = port;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsListening)
            return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        IsListening = true;

        // Start accepting clients in background
        _ = AcceptClientsAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (!IsListening)
            return;

        IsListening = false;
        _cts?.Cancel();

        // Close all client connections
        foreach (var client in _clients.Values)
        {
            try { client.Close(); } catch { }
        }
        _clients.Clear();

        _listener?.Stop();
        _listener = null;

        await Task.CompletedTask;
    }

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                var clientId = $"tcp-{Interlocked.Increment(ref _clientCounter)}";
                _clients[clientId] = client;

                // Handle client in background
                _ = HandleClientAsync(client, clientId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, string clientId, CancellationToken cancellationToken)
    {
        var clientBanner = string.Empty;

        try
        {
            using var stream = new BufferedStream(client.GetStream(), 8192);

            // Send server banner
            var bannerBytes = Encoding.ASCII.GetBytes(ServerBanner);
            await stream.WriteAsync(bannerBytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            // Read client banner (until newline)
            clientBanner = await ReadUntilNewlineAsync(stream, cancellationToken);

            // Notify client connected
            OnClientConnected(new ClientEventArgs(clientId, clientBanner));

            // Packet processing loop
            while (!cancellationToken.IsCancellationRequested && client.Connected)
            {
                // Read packet header
                var headerResult = await BinaryPacketReader.ReadPacketHeaderAsync(stream);
                if (!headerResult.HasValue)
                    break; // Connection closed

                var (packetType, size) = headerResult.Value;

                // Read payload
                var payload = new byte[size];
                var bytesRead = await BinaryPacketReader.ReadExactlyAsync(stream, payload, size);
                if (bytesRead < size)
                    break; // Connection closed

                // Parse packet
                var packet = _packetReader.ParsePacket(packetType, payload);
                if (packet != null)
                {
                    OnPacketReceived(new PacketReceivedEventArgs(packet, clientId));
                }

                // Send acknowledgment
                await stream.WriteAsync(Acknowledgment, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (IOException)
        {
            // Connection closed
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
        finally
        {
            _clients.TryRemove(clientId, out _);
            try { client.Close(); } catch { }
            OnClientDisconnected(new ClientEventArgs(clientId, clientBanner));
        }
    }

    private static async Task<string> ReadUntilNewlineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var buffer = new byte[1];

        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;

            var c = (char)buffer[0];
            if (c == '\n')
                break;

            sb.Append(c);
        }

        return sb.ToString().TrimEnd('\r');
    }

    protected virtual void OnPacketReceived(PacketReceivedEventArgs e)
        => PacketReceived?.Invoke(this, e);

    protected virtual void OnClientConnected(ClientEventArgs e)
        => ClientConnected?.Invoke(this, e);

    protected virtual void OnClientDisconnected(ClientEventArgs e)
        => ClientDisconnected?.Invoke(this, e);

    protected virtual void OnError(Exception e)
        => Error?.Invoke(this, e);

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _cts?.Dispose();
    }
}
