using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;
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
    private const int InOutBufferSize = 4096;
    private const int MaxPipeServerInstances = 254;
    private const int PendingAcceptors = 64;

    private readonly string _pipeName;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, NamedPipeServerStream> _clients = new();
    private readonly ConcurrentDictionary<string, Task> _clientTasks = new();
    private int _clientCounter;
    private readonly List<Task> _acceptLoopTasks = [];
    private int _waitingAcceptors;
    private long _totalConnectionsAccepted;
    private string? _lastError;

    // Global error tracking to prevent spam from multiple instances
    private static string? _lastGlobalErrorMessage;
    private static DateTime _lastGlobalErrorTime = DateTime.MinValue;
    private static readonly object _errorLock = new();

    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    public event EventHandler<ClientEventArgs>? ClientConnected;
    public event EventHandler<ClientEventArgs>? ClientDisconnected;
    public event EventHandler<Exception>? Error;

    public bool IsListening { get; private set; }

    /// <summary>
    /// Gets the number of connected clients.
    /// </summary>
    public int ClientCount => _clients.Count;
    public int WaitingAcceptors => _waitingAcceptors;
    public long TotalConnectionsAccepted => Interlocked.Read(ref _totalConnectionsAccepted);
    public string? LastError => _lastError;

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
        _acceptLoopTasks.Clear();
        for (var i = 0; i < PendingAcceptors; i++)
        {
            _acceptLoopTasks.Add(AcceptConnectionsAsync(_cts.Token));
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

        // Wait for the accept loops to complete
        if (_acceptLoopTasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(_acceptLoopTasks).WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
                // Accept loops didn't complete in time, but we've cancelled them
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation propagates
            }

            _acceptLoopTasks.Clear();
        }

        if (_clientTasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(_clientTasks.Values).WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
                // Client handlers are cancelled/closing
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation propagates
            }

            _clientTasks.Clear();
        }
    }

    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        int retryDelay = 100;
        const int MaxRetryDelay = 30000; // Longer max delay to reduce spam

        while (!cancellationToken.IsCancellationRequested && IsListening)
        {
            NamedPipeServerStream? pipeServer = null;

            try
            {
                pipeServer = CreatePipeServer();
                Interlocked.Increment(ref _waitingAcceptors);
                await pipeServer.WaitForConnectionAsync(cancellationToken);
                Interlocked.Decrement(ref _waitingAcceptors);

                // Reset retry delay on successful connection
                retryDelay = 100;

                var clientId = $"pipe-{Interlocked.Increment(ref _clientCounter)}";
                Interlocked.Increment(ref _totalConnectionsAccepted);
                _clients[clientId] = pipeServer;

                // Handle client - transfer ownership
                var clientPipe = pipeServer;
                pipeServer = null; // Prevent disposal in finally block
                var clientTask = HandleClientAsync(clientPipe, clientId, cancellationToken);
                _clientTasks[clientId] = clientTask;
                _ = clientTask.ContinueWith(_ =>
                    ((ICollection<KeyValuePair<string, Task>>)_clientTasks).Remove(new KeyValuePair<string, Task>(clientId, clientTask)),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Global rate limiting - only report if different error or enough time passed
                ReportErrorWithRateLimit(ex);

                // Exponential backoff for retries
                try
                {
                    await Task.Delay(retryDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                retryDelay = Math.Min(retryDelay * 2, MaxRetryDelay);
            }
            finally
            {
                if (pipeServer != null)
                    Interlocked.Exchange(ref _waitingAcceptors, Math.Max(0, _waitingAcceptors - 1));

                // Only dispose if we didn't transfer ownership
                pipeServer?.Dispose();
            }
        }
    }

    private NamedPipeServerStream CreatePipeServer()
    {
        return new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            MaxPipeServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            InOutBufferSize,
            InOutBufferSize);
    }

    private void ReportErrorWithRateLimit(Exception ex)
    {
        // Don't report UnauthorizedAccessException - it's expected when not running as admin
        // and the fallback in CreatePipeServer handles it gracefully
        if (ex is UnauthorizedAccessException)
            return;

        lock (_errorLock)
        {
            var now = DateTime.UtcNow;
            var timeSinceLastError = now - _lastGlobalErrorTime;
            _lastError = $"{now:O} {ex.GetType().Name}: {ex.Message}";

            // Only report if different message or at least 30 seconds have passed
            if (ex.Message != _lastGlobalErrorMessage || timeSinceLastError.TotalSeconds >= 30)
            {
                _lastGlobalErrorMessage = ex.Message;
                _lastGlobalErrorTime = now;
                OnError(ex);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipeServer, string clientId, CancellationToken cancellationToken)
    {
        var clientInfo = string.Empty;
        var packetChannel = Channel.CreateUnbounded<RawPacket>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
        var packetProcessingTask = ProcessPacketsAsync(packetChannel.Reader, clientId, cancellationToken);

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

                packetChannel.Writer.TryWrite(new RawPacket(packetType, payload));

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
            packetChannel.Writer.TryComplete();
            try { await packetProcessingTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            _clients.TryRemove(clientId, out _);
            try { pipeServer.Close(); } catch { }
            pipeServer.Dispose();
            OnClientDisconnected(new ClientEventArgs(clientId, clientInfo));
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

    private sealed record RawPacket(PacketType Type, byte[] Payload);
}
