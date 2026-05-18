using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartInspect.Relay.AspNetCore.Configuration;

namespace SmartInspect.Relay.AspNetCore.Services;

/// <summary>
/// Forwards log messages through user-provided callback functions.
/// This allows integration with any logging system including SmartInspect.
/// </summary>
public class CallbackForwarder : ILogForwarder
{
    private readonly ILogger<CallbackForwarder> _logger;
    private readonly SmartInspectRelayOptions _options;
    private long _messagesForwarded;
    private int _queuedMessages;
    private DateTime? _lastForwardedAt;
    private CancellationTokenSource? _processingCts;
    private Channel<CallbackMessage>? _channel;
    private Task? _processingTask;
    private bool _started;

    public bool IsConnected => _started && HasCallbacks;
    public long MessagesForwarded => Interlocked.Read(ref _messagesForwarded);
    public int MessagesBuffered => Math.Max(0, Volatile.Read(ref _queuedMessages));
    public DateTime? LastForwardedAt => _lastForwardedAt;

    private bool HasCallbacks =>
        _options.OnLogEntry != null ||
        _options.OnWatch != null ||
        _options.OnProcessFlow != null ||
        _options.OnControl != null;

    public CallbackForwarder(
        ILogger<CallbackForwarder> logger,
        IOptions<SmartInspectRelayOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!HasCallbacks)
        {
            _logger.LogWarning(
                "Callback forwarder started but no callbacks configured. " +
                "Configure OnLogEntry, OnWatch, OnProcessFlow, or OnControl in options.");
        }

        var capacity = Math.Max(1, _options.BufferSize);
        _channel = Channel.CreateBounded<CallbackMessage>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _processingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processingTask = ProcessQueueAsync(_processingCts.Token);
        _started = true;
        _logger.LogInformation("Callback forwarder started");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _started = false;
        _channel?.Writer.TryComplete();
        _processingCts?.Cancel();

        if (_processingTask != null)
        {
            try
            {
                var completedTask = await Task.WhenAny(_processingTask, Task.Delay(TimeSpan.FromSeconds(5)));
                if (completedTask == _processingTask)
                {
                    await _processingTask;
                }
                else
                {
                    _logger.LogWarning("Callback forwarder queue did not stop within 5 seconds");
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }

        _logger.LogInformation("Callback forwarder stopped");
    }

    public Task<bool> ForwardAsync(string json, CancellationToken cancellationToken = default)
    {
        if (!_started || _channel == null)
        {
            return Task.FromResult(false);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElement))
            {
                _logger.LogWarning("Message missing 'type' property");
                return Task.FromResult(false);
            }

            var messageType = typeElement.GetString();
            CallbackMessage? message = messageType switch
            {
                "logEntry" => CreateLogEntryMessage(root),
                "watch" => CreateWatchMessage(root),
                "processFlow" => CreateProcessFlowMessage(root),
                "control" => CreateControlMessage(root),
                _ => null
            };

            if (message == null)
            {
                _logger.LogWarning("Unknown or unhandled message type: {Type}", messageType);
                return Task.FromResult(false);
            }

            if (_channel.Writer.TryWrite(message))
            {
                Interlocked.Increment(ref _queuedMessages);
                return Task.FromResult(true);
            }

            _logger.LogWarning("Callback forwarder queue is full; dropping message");
            return Task.FromResult(false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON message");
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forwarding message");
            return Task.FromResult(false);
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

    private CallbackMessage? CreateLogEntryMessage(JsonElement root)
    {
        if (_options.OnLogEntry == null) return null;

        var level = GetString(root, "logEntryType") ?? "message";
        var title = GetString(root, "title") ?? GetString(root, "message") ?? "";
        var data = GetString(root, "data");
        var viewerId = GetString(root, "viewerId");

        return new CallbackMessage(message => _options.OnLogEntry(message.Value1, message.Value2, message.Value3, message.Value4),
            level,
            title,
            data,
            viewerId);
    }

    private CallbackMessage? CreateWatchMessage(JsonElement root)
    {
        if (_options.OnWatch == null) return null;

        var name = GetString(root, "name") ?? "unknown";
        var value = GetString(root, "value") ?? "";
        var watchType = GetString(root, "watchType") ?? "string";

        return new CallbackMessage(message => _options.OnWatch(message.Value1, message.Value2, message.Value3 ?? "string"),
            name,
            value,
            watchType,
            null);
    }

    private CallbackMessage? CreateProcessFlowMessage(JsonElement root)
    {
        if (_options.OnProcessFlow == null) return null;

        var flowType = GetString(root, "flowType") ?? "";
        var title = GetString(root, "title") ?? "";

        return new CallbackMessage(message => _options.OnProcessFlow(message.Value1, message.Value2),
            flowType,
            title,
            null,
            null);
    }

    private CallbackMessage? CreateControlMessage(JsonElement root)
    {
        if (_options.OnControl == null) return null;

        var command = GetString(root, "command") ?? "";

        return new CallbackMessage(message => _options.OnControl(message.Value1),
            command,
            string.Empty,
            null,
            null);
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        var reader = _channel?.Reader;
        if (reader == null)
        {
            return;
        }

        await foreach (var message in reader.ReadAllAsync(cancellationToken))
        {
            Interlocked.Decrement(ref _queuedMessages);

            try
            {
                message.Forward(message);
                Interlocked.Increment(ref _messagesForwarded);
                _lastForwardedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error forwarding queued callback message");
            }
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    public void Dispose()
    {
        _started = false;
        _channel?.Writer.TryComplete();
        _processingCts?.Cancel();
        _processingCts?.Dispose();
    }

    private sealed record CallbackMessage(
        Action<CallbackMessage> Forward,
        string Value1,
        string Value2,
        string? Value3,
        string? Value4);
}
