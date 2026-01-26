using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartInspect.Relay.AspNetCore.Configuration;

namespace SmartInspect.Relay.AspNetCore.Services;

/// <summary>
/// Forwards log messages to SmartInspect Console via WebSocket.
/// </summary>
public class WebSocketForwarder : ILogForwarder
{
    private readonly ILogger<WebSocketForwarder> _logger;
    private readonly SmartInspectRelayOptions _options;
    private readonly ConcurrentQueue<string> _buffer = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectTask;
    private long _messagesForwarded;
    private DateTime? _lastForwardedAt;

    public bool IsConnected => _webSocket?.State == WebSocketState.Open;
    public long MessagesForwarded => Interlocked.Read(ref _messagesForwarded);
    public int MessagesBuffered => _buffer.Count;
    public DateTime? LastForwardedAt => _lastForwardedAt;

    public WebSocketForwarder(
        ILogger<WebSocketForwarder> logger,
        IOptions<SmartInspectRelayOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await ConnectAsync(_reconnectCts.Token);
        _reconnectTask = MonitorConnectionAsync(_reconnectCts.Token);
    }

    public async Task StopAsync()
    {
        _reconnectCts?.Cancel();

        if (_reconnectTask != null)
        {
            try
            {
                await _reconnectTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        await DisconnectAsync();
    }

    public async Task<bool> ForwardAsync(string json, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            BufferMessage(json);
            return false;
        }

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_webSocket?.State != WebSocketState.Open)
            {
                BufferMessage(json);
                return false;
            }

            var buffer = Encoding.UTF8.GetBytes(json);
            await _webSocket.SendAsync(
                new ArraySegment<byte>(buffer),
                WebSocketMessageType.Text,
                true,
                cancellationToken);

            Interlocked.Increment(ref _messagesForwarded);
            _lastForwardedAt = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to forward message, buffering");
            BufferMessage(json);
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<int> ForwardBatchAsync(IEnumerable<string> messages, CancellationToken cancellationToken = default)
    {
        var forwarded = 0;
        foreach (var message in messages)
        {
            if (await ForwardAsync(message, cancellationToken))
            {
                forwarded++;
            }
        }
        return forwarded;
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            _webSocket?.Dispose();
            _webSocket = new ClientWebSocket();

            var uri = new Uri($"ws://{_options.ConsoleHost}:{_options.ConsolePort}");
            _logger.LogInformation("Connecting to SmartInspect Console at {Uri}", uri);

            await _webSocket.ConnectAsync(uri, cancellationToken);
            _logger.LogInformation("Connected to SmartInspect Console");

            // Flush buffered messages
            await FlushBufferAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to SmartInspect Console");
            throw;
        }
    }

    private async Task DisconnectAsync()
    {
        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Relay shutting down",
                        CancellationToken.None);
                }
            }
            catch
            {
                // Ignore errors during close
            }

            _webSocket.Dispose();
            _webSocket = null;
        }
    }

    private async Task MonitorConnectionAsync(CancellationToken cancellationToken)
    {
        var retryDelay = _options.ReconnectDelayMs;
        var attempts = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, cancellationToken);

                if (_webSocket?.State != WebSocketState.Open)
                {
                    _logger.LogInformation("Connection lost, attempting reconnect...");

                    if (_options.MaxReconnectAttempts > 0 && attempts >= _options.MaxReconnectAttempts)
                    {
                        _logger.LogError("Max reconnect attempts reached, giving up");
                        break;
                    }

                    attempts++;
                    await Task.Delay(retryDelay, cancellationToken);

                    try
                    {
                        await ConnectAsync(cancellationToken);
                        attempts = 0;
                        retryDelay = _options.ReconnectDelayMs;
                    }
                    catch
                    {
                        retryDelay = Math.Min(retryDelay * 2, 30000);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task FlushBufferAsync(CancellationToken cancellationToken)
    {
        while (_buffer.TryDequeue(out var message))
        {
            try
            {
                await ForwardAsync(message, cancellationToken);
            }
            catch
            {
                // Re-queue if failed
                BufferMessage(message);
                break;
            }
        }
    }

    private void BufferMessage(string json)
    {
        _buffer.Enqueue(json);

        // Trim buffer if too large
        while (_buffer.Count > _options.BufferSize)
        {
            _buffer.TryDequeue(out _);
        }
    }

    public void Dispose()
    {
        _reconnectCts?.Cancel();
        _webSocket?.Dispose();
        _sendLock.Dispose();
    }
}
