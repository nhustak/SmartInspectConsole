using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SmartInspectConsole.Core.Events;
using SmartInspectConsole.Core.Protocol;

namespace SmartInspectConsole.Core.Listeners;

/// <summary>
/// WebSocket listener for receiving SmartInspect packets from browser clients.
/// Accepts JSON-formatted log messages over WebSocket connections.
/// </summary>
public class SmartInspectWebSocketListener : IPacketListener
{
    public const int DefaultPort = 4229;

    /// <summary>
    /// When enabled, logs internal debug messages to the console.
    /// </summary>
    public static bool DebugMode { get; set; } = false;

    private readonly int _port;
    private HttpListener? _httpListener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, WebSocket> _clients = new();
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

    public SmartInspectWebSocketListener(int port = DefaultPort)
    {
        _port = port;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsListening)
            return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add($"http://+:{_port}/");

        try
        {
            _httpListener.Start();
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5) // Access denied
        {
            // Try localhost only if we can't bind to all interfaces
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://localhost:{_port}/");
            _httpListener.Start();
        }

        IsListening = true;

        // Start accepting connections in background
        _ = AcceptConnectionsAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (!IsListening)
            return;

        IsListening = false;
        _cts?.Cancel();

        // Close all WebSocket connections
        foreach (var kvp in _clients)
        {
            try
            {
                if (kvp.Value.State == WebSocketState.Open)
                {
                    await kvp.Value.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Server shutting down",
                        CancellationToken.None);
                }
            }
            catch { }
        }
        _clients.Clear();

        try
        {
            _httpListener?.Stop();
            _httpListener?.Close();
        }
        catch { }

        _httpListener = null;
    }

    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _httpListener != null)
        {
            try
            {
                var context = await _httpListener.GetContextAsync();

                if (context.Request.IsWebSocketRequest)
                {
                    _ = HandleWebSocketRequestAsync(context, cancellationToken);
                }
                else
                {
                    // Return CORS headers for preflight requests
                    if (context.Request.HttpMethod == "OPTIONS")
                    {
                        AddCorsHeaders(context.Response);
                        context.Response.StatusCode = 204;
                        context.Response.Close();
                    }
                    else
                    {
                        // Return a simple status page for non-WebSocket requests
                        AddCorsHeaders(context.Response);
                        context.Response.ContentType = "application/json";
                        context.Response.StatusCode = 200;
                        var response = JsonSerializer.Serialize(new
                        {
                            status = "SmartInspect WebSocket Server",
                            port = _port,
                            clients = _clients.Count,
                            listening = IsListening
                        });
                        var buffer = Encoding.UTF8.GetBytes(response);
                        await context.Response.OutputStream.WriteAsync(buffer, cancellationToken);
                        context.Response.Close();
                    }
                }
            }
            catch (HttpListenerException) when (!IsListening)
            {
                // Normal shutdown
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
        }
    }

    private static void AddCorsHeaders(HttpListenerResponse response)
    {
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "*");
    }

    private async Task HandleWebSocketRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var clientId = $"ws-{Interlocked.Increment(ref _clientCounter)}";
        WebSocket? webSocket = null;

        LogDebug($"WebSocket request from {context.Request.RemoteEndPoint}");

        try
        {
            var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
            webSocket = wsContext.WebSocket;
            _clients[clientId] = webSocket;

            // Extract client info from query string or headers
            var userAgent = context.Request.Headers["User-Agent"] ?? "Unknown Browser";
            var clientBanner = $"WebSocket Client ({userAgent})";

            LogDebug($"WebSocket accepted: {clientId}, State: {webSocket.State}");
            OnClientConnected(new ClientEventArgs(clientId, clientBanner));

            // Handle messages
            LogDebug($"Starting message receive loop for {clientId}");
            await ReceiveMessagesAsync(webSocket, clientId, cancellationToken);
        }
        catch (WebSocketException)
        {
            // Connection closed
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
        finally
        {
            _clients.TryRemove(clientId, out _);

            if (webSocket != null)
            {
                try
                {
                    if (webSocket.State == WebSocketState.Open)
                    {
                        await webSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Closing",
                            CancellationToken.None);
                    }
                    webSocket.Dispose();
                }
                catch { }
            }

            OnClientDisconnected(new ClientEventArgs(clientId));
        }
    }

    private async Task ReceiveMessagesAsync(WebSocket webSocket, string clientId, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var messageBuffer = new List<byte>();

        LogDebug($"Receive loop started for {clientId}, WebSocket state: {webSocket.State}");

        while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            LogDebug($"Waiting for message from {clientId}...");
            var result = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                cancellationToken);

            LogDebug($"Received from {clientId}: Type={result.MessageType}, Count={result.Count}, EndOfMessage={result.EndOfMessage}");

            if (result.MessageType == WebSocketMessageType.Close)
            {
                LogDebug($"Close message from {clientId}");
                break;
            }

            if (result.MessageType == WebSocketMessageType.Text)
            {
                messageBuffer.AddRange(buffer.Take(result.Count));

                if (result.EndOfMessage)
                {
                    var json = Encoding.UTF8.GetString(messageBuffer.ToArray());
                    messageBuffer.Clear();

                    try
                    {
                        LogDebug($"Received from {clientId}: {json}");
                        var packet = JsonPacketConverter.ParsePacket(json, clientId);
                        LogDebug($"Parsed packet: {packet?.GetType().Name ?? "NULL"}");
                        if (packet != null)
                        {
                            OnPacketReceived(new PacketReceivedEventArgs(packet, clientId));
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Error parsing from {clientId}: {ex.Message}");
                        OnError(new InvalidOperationException($"Error from {clientId}: {ex.Message}"));
                    }
                }
            }
        }
    }

    protected virtual void OnPacketReceived(PacketReceivedEventArgs e)
        => PacketReceived?.Invoke(this, e);

    protected virtual void OnClientConnected(ClientEventArgs e)
        => ClientConnected?.Invoke(this, e);

    protected virtual void OnClientDisconnected(ClientEventArgs e)
        => ClientDisconnected?.Invoke(this, e);

    protected virtual void OnError(Exception e)
        => Error?.Invoke(this, e);

    private void LogDebug(string message)
    {
        if (!DebugMode) return;

        var debugEntry = new Packets.LogEntry
        {
            Timestamp = DateTime.Now,
            LogEntryType = Enums.LogEntryType.Debug,
            SessionName = "[WebSocket]",
            AppName = "Console",
            Title = message,
            ViewerId = Enums.ViewerId.Title,
            Color = System.Drawing.Color.FromArgb(128, 128, 128)
        };
        OnPacketReceived(new PacketReceivedEventArgs(debugEntry, "internal"));
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _cts?.Dispose();
    }
}
