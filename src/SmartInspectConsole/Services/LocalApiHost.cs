using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartInspectConsole.Backend;
using SmartInspectConsole.Contracts;

namespace SmartInspectConsole.Services;

public sealed class LocalApiHost : IAsyncDisposable
{
    private readonly ISmartInspectLogBackend _backend;
    private WebApplication? _app;

    public LocalApiHost(ISmartInspectLogBackend backend, int port = 42331)
    {
        _backend = backend;
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
        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.WriteIndented = true;
        });
        builder.Services.AddSingleton(_backend);

        var app = builder.Build();

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

        _app = app;
        await _app.StartAsync(cancellationToken);
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
