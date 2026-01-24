using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using SmartInspectConsole.Core.Enums;
using SmartInspectConsole.Core.Events;
using SmartInspectConsole.Core.Packets;
using SmartInspectConsole.Core.Parsing;

namespace SmartInspectConsole.Core.Listeners;

/// <summary>
/// Named pipe listener for receiving SmartInspect packets.
/// </summary>
public class SmartInspectPipeListener : IPacketListener
{
    public const string DefaultPipeName = "smartinspect";
    private const string ServerBanner = "SmartInspect Console v1.0\n";
    private const int MaxInstances = 10;

    private readonly string _pipeName;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, NamedPipeServerStream> _clients = new();
    private readonly List<Task> _listenerTasks = new();
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

    /// <summary>
    /// Gets the pipe name.
    /// </summary>
    public string PipeName => _pipeName;

    public SmartInspectPipeListener(string pipeName = DefaultPipeName)
    {
        _pipeName = pipeName;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsListening)
            return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsListening = true;
        _listenerTasks.Clear();

        // Start multiple pipe instances to handle concurrent connections
        for (int i = 0; i < MaxInstances; i++)
        {
            _listenerTasks.Add(ListenForConnectionAsync(_cts.Token));
        }

        await Task.CompletedTask;
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

        // Wait for all listener tasks to complete (with timeout)
        if (_listenerTasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(_listenerTasks).WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
                // Tasks didn't complete in time, but we've cancelled them
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation propagates
            }
            _listenerTasks.Clear();
        }
    }

    private async Task ListenForConnectionAsync(CancellationToken cancellationToken)
    {
        int retryDelay = 100;
        const int MaxRetryDelay = 5000;
        string? lastErrorMessage = null;

        while (!cancellationToken.IsCancellationRequested && IsListening)
        {
            NamedPipeServerStream? pipeServer = null;

            try
            {
                // Create pipe security that allows all users to connect
                var pipeSecurity = new PipeSecurity();
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    PipeAccessRights.ReadWrite,
                    AccessControlType.Allow));

                pipeServer = NamedPipeServerStreamAcl.Create(
                    _pipeName,
                    PipeDirection.InOut,
                    MaxInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    0,
                    0,
                    pipeSecurity);

                await pipeServer.WaitForConnectionAsync(cancellationToken);

                // Reset retry delay on successful connection
                retryDelay = 100;
                lastErrorMessage = null;

                var clientId = $"pipe-{Interlocked.Increment(ref _clientCounter)}";
                _clients[clientId] = pipeServer;

                // Handle client - transfer ownership
                var clientPipe = pipeServer;
                pipeServer = null; // Prevent disposal in finally block
                _ = HandleClientAsync(clientPipe, clientId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Only report error if it's different from the last one (avoid spam)
                if (ex.Message != lastErrorMessage)
                {
                    OnError(ex);
                    lastErrorMessage = ex.Message;
                }

                // Exponential backoff for retries
                await Task.Delay(retryDelay, cancellationToken);
                retryDelay = Math.Min(retryDelay * 2, MaxRetryDelay);
            }
            finally
            {
                // Only dispose if we didn't transfer ownership
                pipeServer?.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipeServer, string clientId, CancellationToken cancellationToken)
    {
        var clientInfo = string.Empty;

        try
        {
            // Send server banner
            var bannerBytes = Encoding.ASCII.GetBytes(ServerBanner);
            await pipeServer.WriteAsync(bannerBytes, cancellationToken);
            await pipeServer.FlushAsync(cancellationToken);

            // Read client banner (until newline) - same protocol as TCP
            clientInfo = await ReadUntilNewlineAsync(pipeServer, cancellationToken);

            // Notify client connected
            OnClientConnected(new ClientEventArgs(clientId, clientInfo));

            // Packet processing loop
            while (!cancellationToken.IsCancellationRequested && pipeServer.IsConnected)
            {
                // Read packet header
                var headerResult = await BinaryPacketReader.ReadPacketHeaderAsync(pipeServer);
                if (!headerResult.HasValue)
                    break; // Connection closed

                var (packetType, size) = headerResult.Value;

                // Read payload
                var payload = new byte[size];
                var bytesRead = await BinaryPacketReader.ReadExactlyAsync(pipeServer, payload, size);
                if (bytesRead < size)
                    break; // Connection closed

                // Parse packet
                var packet = _packetReader.ParsePacket(packetType, payload);
                if (packet != null)
                {
                    OnPacketReceived(new PacketReceivedEventArgs(packet, clientId));
                }

                // NOTE: Named pipes don't require acknowledgment
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
            try { pipeServer.Close(); } catch { }
            pipeServer.Dispose();
            OnClientDisconnected(new ClientEventArgs(clientId, clientInfo));

            // Start a new listener to replace this one
            if (IsListening && !cancellationToken.IsCancellationRequested)
            {
                _ = ListenForConnectionAsync(cancellationToken);
            }
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
                break; // Connection closed

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
