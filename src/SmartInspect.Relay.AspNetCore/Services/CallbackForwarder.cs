using System.Text.Json;
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
    private DateTime? _lastForwardedAt;
    private bool _started;

    public bool IsConnected => _started && HasCallbacks;
    public long MessagesForwarded => Interlocked.Read(ref _messagesForwarded);
    public int MessagesBuffered => 0; // No buffering in callback mode
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

        _started = true;
        _logger.LogInformation("Callback forwarder started");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _started = false;
        _logger.LogInformation("Callback forwarder stopped");
        return Task.CompletedTask;
    }

    public Task<bool> ForwardAsync(string json, CancellationToken cancellationToken = default)
    {
        if (!_started)
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
            var handled = false;

            switch (messageType)
            {
                case "logEntry":
                    handled = ForwardLogEntry(root);
                    break;
                case "watch":
                    handled = ForwardWatch(root);
                    break;
                case "processFlow":
                    handled = ForwardProcessFlow(root);
                    break;
                case "control":
                    handled = ForwardControl(root);
                    break;
                default:
                    _logger.LogWarning("Unknown message type: {Type}", messageType);
                    return Task.FromResult(false);
            }

            if (handled)
            {
                Interlocked.Increment(ref _messagesForwarded);
                _lastForwardedAt = DateTime.UtcNow;
            }

            return Task.FromResult(handled);
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

    private bool ForwardLogEntry(JsonElement root)
    {
        if (_options.OnLogEntry == null) return false;

        var level = GetString(root, "logEntryType") ?? "message";
        var title = GetString(root, "title") ?? GetString(root, "message") ?? "";
        var data = GetString(root, "data");
        var viewerId = GetString(root, "viewerId");

        _options.OnLogEntry(level, title, data, viewerId);
        return true;
    }

    private bool ForwardWatch(JsonElement root)
    {
        if (_options.OnWatch == null) return false;

        var name = GetString(root, "name") ?? "unknown";
        var value = GetString(root, "value") ?? "";
        var watchType = GetString(root, "watchType") ?? "string";

        _options.OnWatch(name, value, watchType);
        return true;
    }

    private bool ForwardProcessFlow(JsonElement root)
    {
        if (_options.OnProcessFlow == null) return false;

        var flowType = GetString(root, "flowType") ?? "";
        var title = GetString(root, "title") ?? "";

        _options.OnProcessFlow(flowType, title);
        return true;
    }

    private bool ForwardControl(JsonElement root)
    {
        if (_options.OnControl == null) return false;

        var command = GetString(root, "command") ?? "";

        _options.OnControl(command);
        return true;
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
    }
}
