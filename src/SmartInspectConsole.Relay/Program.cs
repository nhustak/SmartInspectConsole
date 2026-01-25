using System.Diagnostics;
using System.Text.Json;
using SmartInspectConsole.Relay.Services;

var builder = WebApplication.CreateBuilder(args);

// Load configuration
var relayOptions = builder.Configuration.GetSection("Relay").Get<RelayOptions>() ?? new RelayOptions();

// Services
builder.Services.AddSingleton(relayOptions);
builder.Services.AddSingleton<ConsoleForwarder>();

// CORS - allow all origins for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Request decompression for gzipped requests
builder.Services.AddRequestDecompression();

var app = builder.Build();

// Middleware
app.UseRequestDecompression();
app.UseCors();

// Get the forwarder service
var forwarder = app.Services.GetRequiredService<ConsoleForwarder>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

// ==================== API Endpoints ====================

/// <summary>
/// POST /api/v1/logs - Accept single message or batch
/// </summary>
app.MapPost("/api/v1/logs", async (HttpContext context) =>
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
        if (root.TryGetProperty("messages", out var messagesElement) && messagesElement.ValueKind == JsonValueKind.Array)
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
})
.WithName("PostLogs");

/// <summary>
/// GET /api/v1/health - Health check
/// </summary>
app.MapGet("/api/v1/health", () =>
{
    return Results.Ok(new
    {
        status = "healthy",
        version = "1.0.0",
        uptime = (long)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds,
        consoleConnected = forwarder.IsConnected
    });
})
.WithName("HealthCheck");

/// <summary>
/// GET /api/v1/status - Detailed status
/// </summary>
app.MapGet("/api/v1/status", () =>
{
    return Results.Ok(new
    {
        consoleConnected = forwarder.IsConnected,
        consoleAddress = $"{relayOptions.ConsoleHost}:{relayOptions.ConsolePort}",
        messagesForwarded = forwarder.MessagesForwarded,
        messagesBuffered = forwarder.MessagesBuffered,
        lastForwardedAt = forwarder.LastForwardedAt
    });
})
.WithName("Status");

// ==================== Startup ====================

// Start the console forwarder
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

lifetime.ApplicationStarted.Register(async () =>
{
    try
    {
        await forwarder.StartAsync(lifetime.ApplicationStopping);
        logger.LogInformation("Console forwarder started");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to start console forwarder");
    }
});

lifetime.ApplicationStopping.Register(async () =>
{
    try
    {
        await forwarder.StopAsync();
        logger.LogInformation("Console forwarder stopped");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error stopping console forwarder");
    }
});

logger.LogInformation("SmartInspect Relay starting");
logger.LogInformation("Will connect to console at ws://{Host}:{Port}", relayOptions.ConsoleHost, relayOptions.ConsolePort);

app.Run();

// ==================== Response Models ====================

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
