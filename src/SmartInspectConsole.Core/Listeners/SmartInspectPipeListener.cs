using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Channels;
using SmartInspectConsole.Core.Enums;
using SmartInspectConsole.Core.Events;
using SmartInspectConsole.Core.Packets;
using SmartInspectConsole.Core.Parsing;

namespace SmartInspectConsole.Core.Listeners;

/// <summary>
/// Named pipe listener for receiving SmartInspect packets.
/// Designed so a stalled console UI or slow consumer cannot pin client write calls forever:
/// the socket/pipe is always drained (or disconnected on timeout), and parse/UI work is decoupled.
/// </summary>
public class SmartInspectPipeListener : IPacketListener
{
    public const string DefaultPipeName = "smartinspect";
    private const string ServerBanner = "SmartInspect Console v1.0\n";
    private const int InOutBufferSize = 64 * 1024;
    private const int MaxPipeServerInstances = 254;
    private const int PendingAcceptors = 64;
    private const int MaxPendingPacketsPerClient = 4096;
    private const int MaxClientBannerLength = 4096;
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);
    /// <summary>
    /// Once a packet header is received, the payload must finish within this window.
    /// Waiting for the *next* packet has no idle timeout — quiet clients must stay connected.
    /// </summary>
    private static readonly TimeSpan PacketPayloadTimeout = TimeSpan.FromSeconds(30);

    private readonly string _pipeName;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, NamedPipeServerStream> _clients = new();
    private readonly ConcurrentDictionary<string, Task> _clientTasks = new();
    private int _clientCounter;
    private readonly List<Task> _acceptLoopTasks = [];
    private int _waitingAcceptors;
    private long _totalConnectionsAccepted;
    private long _totalPacketsDropped;
    private string? _lastError;

    /// <summary>
    /// 0 = not decided, 1 = open ACL instances, 2 = default-security instances only.
    /// Decided once under <see cref="_createPipeLock"/> so 64 acceptors do not race ACL create.
    /// </summary>
    private int _pipeSecurityMode;
    private readonly object _createPipeLock = new();
    private bool _aclFallbackNotified;

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
    public long TotalPacketsDropped => Interlocked.Read(ref _totalPacketsDropped);
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
        _pipeSecurityMode = 0;
        _aclFallbackNotified = false;
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

        // Close all client connections immediately so website/app clients unblock.
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
            var countedAsWaiting = false;

            try
            {
                pipeServer = CreatePipeServer();
                Interlocked.Increment(ref _waitingAcceptors);
                countedAsWaiting = true;
                await pipeServer.WaitForConnectionAsync(cancellationToken);
                Interlocked.Decrement(ref _waitingAcceptors);
                countedAsWaiting = false;

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
                if (countedAsWaiting)
                    Interlocked.Decrement(ref _waitingAcceptors);

                // Only dispose if we didn't transfer ownership
                pipeServer?.Dispose();
            }
        }
    }

    private NamedPipeServerStream CreatePipeServer()
    {
        // Serialize creation: concurrent ACL/default creates race on the same pipe name and
        // produce Access Denied storms (one failure per acceptor loop).
        lock (_createPipeLock)
        {
            if (_pipeSecurityMode == 2)
                return CreateDefaultPipeServer();

            if (_pipeSecurityMode == 1)
                return CreateOpenAclPipeServer();

            // First instance decides security mode for the whole listener.
            try
            {
                var pipe = CreateOpenAclPipeServer();
                _pipeSecurityMode = 1;
                return pipe;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                or PlatformNotSupportedException or System.Security.SecurityException)
            {
                _pipeSecurityMode = 2;

                // One quiet notice only — do not flood the log grid via OnError.
                if (!_aclFallbackNotified)
                {
                    _aclFallbackNotified = true;
                    _lastError =
                        $"{DateTime.UtcNow:O} Pipe open-ACL unavailable ({ex.GetType().Name}: {ex.Message}). " +
                        "Using current-user pipe security. Multi-user clients (IIS app pools) may not connect " +
                        "unless this process can create the open ACL (or is elevated).";
                }

                return CreateDefaultPipeServer();
            }
        }
    }

    private NamedPipeServerStream CreateOpenAclPipeServer()
    {
        // Allow any local user/process to connect (IIS app pools, services, other sessions).
        // CreateNewInstance is required so additional server waiters can be created after the first.
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            MaxPipeServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            InOutBufferSize,
            InOutBufferSize,
            pipeSecurity);
    }

    private NamedPipeServerStream CreateDefaultPipeServer()
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
        // Ignore expected create races / ACL fallback noise; real failures still surface.
        if (ex is UnauthorizedAccessException)
        {
            _lastError = $"{DateTime.UtcNow:O} {ex.GetType().Name}: {ex.Message}";
            return;
        }

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

        // Bounded queue: if UI/parse falls behind, drop packets instead of stopping the read loop.
        // Stopping the read loop is what freezes SmartInspect clients (and websites) on write.
        var packetChannel = Channel.CreateBounded<RawPacket>(new BoundedChannelOptions(MaxPendingPacketsPerClient)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        var packetProcessingTask = ProcessPacketsAsync(packetChannel.Reader, clientId, cancellationToken);

        // Per-client linked CTS so idle/handshake timeouts force a clean disconnect.
        using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            // Handshake must not hang forever (half-open pipe instances starve acceptors).
            using (var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(clientCts.Token))
            {
                handshakeCts.CancelAfter(HandshakeTimeout);

                var bannerBytes = Encoding.ASCII.GetBytes(ServerBanner);
                await pipeServer.WriteAsync(bannerBytes, handshakeCts.Token);
                await pipeServer.FlushAsync(handshakeCts.Token);

                clientInfo = await ReadUntilNewlineAsync(pipeServer, MaxClientBannerLength, handshakeCts.Token);
            }

            OnClientConnected(new ClientEventArgs(clientId, clientInfo));

            // Keep draining the pipe as long as the client is connected.
            // Do not apply an idle timeout while waiting for the next packet — production clients
            // often stay connected and log only occasionally. Stopping the read loop (or leaving
            // a half-open instance) is what freezes websites on SmartInspect Log* calls.
            while (!clientCts.IsCancellationRequested && pipeServer.IsConnected)
            {
                var headerResult = await BinaryPacketReader.ReadPacketHeaderAsync(pipeServer, clientCts.Token);
                if (!headerResult.HasValue)
                    break; // Connection closed

                var (packetType, size) = headerResult.Value;

                using var payloadCts = CancellationTokenSource.CreateLinkedTokenSource(clientCts.Token);
                payloadCts.CancelAfter(PacketPayloadTimeout);

                var payload = new byte[size];
                var bytesRead = await BinaryPacketReader.ReadExactlyAsync(pipeServer, payload, size, payloadCts.Token);
                if (bytesRead < size)
                    break; // Connection closed

                // DropOldest channel: always accept; older queued packets may be discarded under load.
                if (!packetChannel.Writer.TryWrite(new RawPacket(packetType, payload)))
                    Interlocked.Increment(ref _totalPacketsDropped);

                // NOTE: Named pipes don't require acknowledgment. The critical requirement is that
                // we keep reading so the OS pipe buffer never fills and blocks the client process.
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown of the listener.
        }
        catch (OperationCanceledException)
        {
            // Handshake or mid-packet timeout — disconnect so the client unblocks and can reconnect.
            ReportErrorWithRateLimit(new TimeoutException(
                $"Pipe client {clientId} disconnected after handshake or mid-packet timeout."));
        }
        catch (InvalidDataException ex)
        {
            ReportErrorWithRateLimit(ex);
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
                break; // Connection closed

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
