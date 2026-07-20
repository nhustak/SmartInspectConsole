using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using SmartInspectConsole.Core.Enums;
using SmartInspectConsole.Core.Events;
using SmartInspectConsole.Core.Packets;
using SmartInspectConsole.Core.Parsing;

namespace SmartInspectConsole.Core.Listeners;

/// <summary>
/// TCP listener for receiving SmartInspect packets.
/// ACK is sent before parse/UI work so client logging is not blocked by console rendering.
/// Handshake and idle timeouts force disconnect so half-open clients cannot hang forever.
/// </summary>
public class SmartInspectTcpListener : IPacketListener
{
    public const int DefaultPort = 4228;
    private const string ServerBanner = "SmartInspect Console v1.0\n";
    private static readonly byte[] Acknowledgment = [0, 0];
    private const int MaxPendingPacketsPerClient = 4096;
    private const int MaxClientBannerLength = 4096;
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);
    /// <summary>
    /// Once a packet header is received, the payload must finish within this window.
    /// Waiting for the *next* packet has no idle timeout — quiet clients must stay connected.
    /// </summary>
    private static readonly TimeSpan PacketPayloadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AckWriteTimeout = TimeSpan.FromSeconds(5);

    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, TcpClient> _clients = new();
    private int _clientCounter;
    private long _totalPacketsDropped;
    private string? _lastError;

    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    public event EventHandler<ClientEventArgs>? ClientConnected;
    public event EventHandler<ClientEventArgs>? ClientDisconnected;
    public event EventHandler<Exception>? Error;

    public bool IsListening { get; private set; }

    /// <summary>
    /// Gets the number of connected clients.
    /// </summary>
    public int ClientCount => _clients.Count;
    public long TotalPacketsDropped => Interlocked.Read(ref _totalPacketsDropped);
    public string? LastError => _lastError;

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

        // Close all client connections immediately so client apps unblock on write/ACK.
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
                client.NoDelay = true;
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
        var packetChannel = Channel.CreateBounded<RawPacket>(new BoundedChannelOptions(MaxPendingPacketsPerClient)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        var packetProcessingTask = ProcessPacketsAsync(packetChannel.Reader, clientId, cancellationToken);
        using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            using var stream = new BufferedStream(client.GetStream(), 8192);

            using (var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(clientCts.Token))
            {
                handshakeCts.CancelAfter(HandshakeTimeout);

                var bannerBytes = Encoding.ASCII.GetBytes(ServerBanner);
                await stream.WriteAsync(bannerBytes, handshakeCts.Token);
                await stream.FlushAsync(handshakeCts.Token);

                clientBanner = await ReadUntilNewlineAsync(stream, MaxClientBannerLength, handshakeCts.Token);
            }

            OnClientConnected(new ClientEventArgs(clientId, clientBanner));

            while (!clientCts.IsCancellationRequested && client.Connected)
            {
                // Wait indefinitely for the next packet header (quiet clients stay connected).
                var headerResult = await BinaryPacketReader.ReadPacketHeaderAsync(stream, clientCts.Token);
                if (!headerResult.HasValue)
                    break; // Connection closed

                var (packetType, size) = headerResult.Value;

                using var payloadCts = CancellationTokenSource.CreateLinkedTokenSource(clientCts.Token);
                payloadCts.CancelAfter(PacketPayloadTimeout);

                var payload = new byte[size];
                var bytesRead = await BinaryPacketReader.ReadExactlyAsync(stream, payload, size, payloadCts.Token);
                if (bytesRead < size)
                    break; // Connection closed

                // Acknowledge before parse/UI so client Log* calls do not wait on console work.
                using (var ackCts = CancellationTokenSource.CreateLinkedTokenSource(clientCts.Token))
                {
                    ackCts.CancelAfter(AckWriteTimeout);
                    await stream.WriteAsync(Acknowledgment, ackCts.Token);
                    await stream.FlushAsync(ackCts.Token);
                }

                if (!packetChannel.Writer.TryWrite(new RawPacket(packetType, payload)))
                    Interlocked.Increment(ref _totalPacketsDropped);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (OperationCanceledException)
        {
            _lastError = $"{DateTime.UtcNow:O} Timeout: TCP client {clientId} handshake/mid-packet/ack timeout.";
        }
        catch (InvalidDataException ex)
        {
            _lastError = $"{DateTime.UtcNow:O} {ex.GetType().Name}: {ex.Message}";
            OnError(ex);
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
            packetChannel.Writer.TryComplete();
            try { await packetProcessingTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            _clients.TryRemove(clientId, out _);
            try { client.Close(); } catch { }
            OnClientDisconnected(new ClientEventArgs(clientId, clientBanner));
        }
    }

    private async Task ProcessPacketsAsync(ChannelReader<RawPacket> reader, string clientId, CancellationToken cancellationToken)
    {
        var packetReader = new BinaryPacketReader();

        try
        {
            await foreach (var rawPacket in reader.ReadAllAsync(cancellationToken))
            {
                var packet = packetReader.ParsePacket(rawPacket.Type, rawPacket.Payload);
                if (packet != null)
                {
                    OnPacketReceived(new PacketReceivedEventArgs(packet, clientId));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
    }

    private static async Task<string> ReadUntilNewlineAsync(
        Stream stream,
        int maxLength,
        CancellationToken cancellationToken)
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

            if (sb.Length >= maxLength)
                throw new InvalidDataException($"Client banner exceeded {maxLength} bytes without a newline.");

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

    private sealed record RawPacket(PacketType Type, byte[] Payload);
}
