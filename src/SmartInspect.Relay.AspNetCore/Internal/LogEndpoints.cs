using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SmartInspect.Relay.AspNetCore.Services;

namespace SmartInspect.Relay.AspNetCore.Internal;

/// <summary>
/// Logger category for relay endpoints.
/// </summary>
internal sealed class RelayEndpointLogger { }

/// <summary>
/// Internal endpoint handlers for the relay API.
/// </summary>
internal static class LogEndpoints
{
    private static readonly DateTime StartTime = DateTime.UtcNow;

    /// <summary>
    /// POST /logs - Accept single message or batch.
    /// </summary>
    public static async Task<IResult> PostLogs(
        HttpContext context,
        ILogForwarder forwarder,
        ILogger<RelayEndpointLogger> logger)
    {
        var requestId = context.Request.Headers["X-Request-Id"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N")[..12];

        try
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(body))
            {
                return Results.BadRequest(new LogResponse
                {
                    Success = false,
                    Error = "Empty request body",
                    Code = "EMPTY_BODY",
                    RequestId = requestId
                });
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var messages = new List<string>();

            // Check for batch format: { messages: [...] }
            if (root.TryGetProperty("messages", out var messagesElement) &&
                messagesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messagesElement.EnumerateArray())
                {
                    messages.Add(message.GetRawText());
                }
            }
            // Check for single message format: { type: "logEntry", ... }
            else if (root.TryGetProperty("type", out _))
            {
                messages.Add(body);
            }
            else
            {
                return Results.BadRequest(new LogResponse
                {
                    Success = false,
                    Error = "Invalid message format. Expected { type: ... } or { messages: [...] }",
                    Code = "INVALID_FORMAT",
                    RequestId = requestId
                });
            }

            if (messages.Count == 0)
            {
                return Results.BadRequest(new LogResponse
                {
                    Success = false,
                    Error = "No messages provided",
                    Code = "NO_MESSAGES",
                    RequestId = requestId
                });
            }

            // Forward messages
            var accepted = await forwarder.ForwardBatchAsync(messages, context.RequestAborted);

            return Results.Ok(new LogResponse
            {
                Success = true,
                Accepted = accepted,
                Rejected = messages.Count - accepted,
                RequestId = requestId
            });
        }
        catch (JsonException ex)
        {
            return Results.BadRequest(new LogResponse
            {
                Success = false,
                Error = $"Invalid JSON: {ex.Message}",
                Code = "INVALID_JSON",
                RequestId = requestId
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing log request");
            return Results.Json(new LogResponse
            {
                Success = false,
                Error = "Internal server error",
                Code = "INTERNAL_ERROR",
                RequestId = requestId
            }, statusCode: 500);
        }
    }

    /// <summary>
    /// GET /health - Health check.
    /// </summary>
    public static IResult GetHealth(ILogForwarder forwarder)
    {
        return Results.Ok(new
        {
            status = "healthy",
            version = "1.0.0",
            uptime = (long)(DateTime.UtcNow - StartTime).TotalSeconds,
            consoleConnected = forwarder.IsConnected
        });
    }

    /// <summary>
    /// GET /status - Detailed status.
    /// </summary>
    public static IResult GetStatus(ILogForwarder forwarder)
    {
        return Results.Ok(new
        {
            connected = forwarder.IsConnected,
            messagesForwarded = forwarder.MessagesForwarded,
            messagesBuffered = forwarder.MessagesBuffered,
            lastForwardedAt = forwarder.LastForwardedAt
        });
    }
}

/// <summary>
/// Response model for log endpoint.
/// </summary>
public record LogResponse
{
    public bool Success { get; init; }
    public int Accepted { get; init; }
    public int Rejected { get; init; }
    public string? Error { get; init; }
    public string? Code { get; init; }
    public string RequestId { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
