using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using SmartInspectConsole.Backend;
using SmartInspectConsole.Contracts;

namespace SmartInspectConsole.Services;

public sealed class LocalApiHost : IAsyncDisposable
{
    private readonly ISmartInspectLogBackend _backend;
    private readonly Action<McpTraceEvent>? _traceSink;
    private WebApplication? _app;

    public LocalApiHost(ISmartInspectLogBackend backend, Action<McpTraceEvent>? traceSink = null, int port = 42331)
    {
        _backend = backend;
        _traceSink = traceSink;
        Port = port;
    }

    public int Port { get; }

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_app != null)
        {
            return;
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(LocalApiHost).Assembly.FullName
        });

        builder.WebHost.UseUrls(BaseUrl);
        builder.Services.AddHttpContextAccessor();
        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.WriteIndented = true;
        });
        builder.Services.AddSingleton(_backend);
        builder.Services.AddSingleton<SmartInspectMcpTools>();
        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<SmartInspectMcpTools>()
            .WithListResourcesHandler((_, _) => ValueTask.FromResult(new ListResourcesResult
            {
                Resources = []
            }))
            .WithListResourceTemplatesHandler((_, _) => ValueTask.FromResult(new ListResourceTemplatesResult
            {
                ResourceTemplates = []
            }));

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var startedAtUtc = DateTime.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            var requestSummary = await ReadRequestSummaryAsync(context.Request, context.RequestAborted);
            Exception? error = null;

            try
            {
                await next();
            }
            catch (Exception ex)
            {
                error = ex;
                throw;
            }
            finally
            {
                stopwatch.Stop();
                WriteTrace(context, requestSummary, startedAtUtc, stopwatch.Elapsed, error);
            }
        });

        app.MapGet("/api/local/v1/health", () => Results.Ok(new
        {
            status = "healthy",
            runId = _backend.RunId
        }));

        app.MapGet("/api/local/v1/applications", (bool? connectedOnly, bool? mutedOnly) =>
            Results.Ok(_backend.ListApplications(connectedOnly ?? false, mutedOnly ?? false)));

        app.MapPost("/api/local/v1/logs/query", (LogQueryRequest request) =>
            Results.Ok(_backend.QueryLogs(request)));

        app.MapGet("/api/local/v1/logs/{entryId}", (string entryId, bool includeData) =>
        {
            var entry = _backend.GetLogEntry(entryId, includeData);
            return entry == null ? Results.NotFound() : Results.Ok(entry);
        });

        app.MapGet("/api/local/v1/context/live", () => Results.Ok(_backend.GetLiveContext()));

        app.MapGet("/api/local/v1/flags", (string? category, int? limit) =>
            Results.Ok(_backend.ListFlaggedEntries(category, limit ?? 100)));

        app.MapPost("/api/local/v1/flags", (FlagEntryRequest request) =>
        {
            try
            {
                return Results.Ok(_backend.FlagEntry(request));
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapDelete("/api/local/v1/flags/{entryId}", (string entryId) =>
            _backend.UnflagEntry(entryId) ? Results.NoContent() : Results.NotFound());

        app.MapMcp("/mcp");

        _app = app;
        await _app.StartAsync(cancellationToken);
    }

    private void WriteTrace(HttpContext context, McpRequestSummary summary, DateTime startedAtUtc, TimeSpan elapsed, Exception? error)
    {
        if (_traceSink == null)
            return;

        var builder = new StringBuilder();
        builder.AppendLine($"ProtocolVersion: {context.Request.Headers["MCP-Protocol-Version"]}");
        builder.AppendLine($"SessionId: {context.Response.Headers["Mcp-Session-Id"]}");
        builder.AppendLine($"HTTP: {context.Request.Method} {context.Request.Path}");
        builder.AppendLine($"StatusCode: {context.Response.StatusCode}");
        builder.AppendLine($"ElapsedMs: {elapsed.TotalMilliseconds:F1}");
        if (!string.IsNullOrWhiteSpace(summary.JsonRpcId))
            builder.AppendLine($"JsonRpcId: {summary.JsonRpcId}");
        if (!string.IsNullOrWhiteSpace(summary.Method))
            builder.AppendLine($"Method: {summary.Method}");
        if (!string.IsNullOrWhiteSpace(summary.ToolName))
            builder.AppendLine($"Tool: {summary.ToolName}");
        if (!string.IsNullOrWhiteSpace(summary.ArgumentsPreview))
            builder.AppendLine($"Arguments: {summary.ArgumentsPreview}");
        if (error != null)
        {
            builder.AppendLine();
            builder.AppendLine(error.ToString());
        }

        _traceSink(new McpTraceEvent
        {
            TimestampUtc = startedAtUtc,
            Title = BuildTraceTitle(summary),
            Data = builder.ToString().TrimEnd(),
            IsError = error != null || context.Response.StatusCode >= StatusCodes.Status400BadRequest
        });
    }

    private static string BuildTraceTitle(McpRequestSummary summary)
    {
        if (string.Equals(summary.Method, "tools/call", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(summary.ToolName))
        {
            return $"tools/call: {summary.ToolName}";
        }

        return string.IsNullOrWhiteSpace(summary.Method) ? "mcp/request" : summary.Method;
    }

    private static async Task<McpRequestSummary> ReadRequestSummaryAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!HttpMethods.IsPost(request.Method))
            return McpRequestSummary.Empty;

        request.EnableBuffering();
        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);
        request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(body))
            return McpRequestSummary.Empty;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var method = root.TryGetProperty("method", out var methodEl) ? methodEl.GetString() : null;
            var jsonRpcId = root.TryGetProperty("id", out var idEl) ? idEl.ToString() : null;
            string? toolName = null;
            string? argumentsPreview = null;

            if (root.TryGetProperty("params", out var paramsEl))
            {
                if (paramsEl.TryGetProperty("name", out var nameEl))
                    toolName = nameEl.GetString();

                if (paramsEl.TryGetProperty("arguments", out var argumentsEl))
                    argumentsPreview = Truncate(argumentsEl.GetRawText(), 500);
            }

            return new McpRequestSummary(method, jsonRpcId, toolName, argumentsPreview);
        }
        catch (JsonException)
        {
            return new McpRequestSummary(null, null, null, Truncate(body, 500));
        }
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";

    private sealed record McpRequestSummary(string? Method, string? JsonRpcId, string? ToolName, string? ArgumentsPreview)
    {
        public static readonly McpRequestSummary Empty = new(null, null, null, null);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app == null)
        {
            return;
        }

        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
    }
}
