using SmartInspect.Relay.AspNetCore;
using SmartInspect.Relay.AspNetCore.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Load configuration
var relayConfig = builder.Configuration.GetSection("Relay");
var consoleHost = relayConfig["ConsoleHost"] ?? "localhost";
var consolePort = int.TryParse(relayConfig["ConsolePort"], out var port) ? port : 4229;

// Add SmartInspect relay services
builder.Services.AddSmartInspectRelay(options =>
{
    options.Mode = RelayMode.WebSocket;
    options.ConsoleHost = consoleHost;
    options.ConsolePort = consolePort;

    // Additional configuration from appsettings
    if (int.TryParse(relayConfig["BufferSize"], out var bufferSize))
        options.BufferSize = bufferSize;
    if (int.TryParse(relayConfig["ReconnectDelayMs"], out var reconnectDelay))
        options.ReconnectDelayMs = reconnectDelay;
    if (int.TryParse(relayConfig["MaxReconnectAttempts"], out var maxAttempts))
        options.MaxReconnectAttempts = maxAttempts;
});

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

// Map SmartInspect relay endpoints
app.MapSmartInspectRelay("/api/v1");

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("SmartInspect Relay starting");
logger.LogInformation("Will connect to console at ws://{Host}:{Port}", consoleHost, consolePort);

app.Run();
