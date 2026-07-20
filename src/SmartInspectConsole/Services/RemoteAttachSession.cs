using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SmartInspectConsole.Contracts;
using SmartInspectConsole.Core.Enums;
using SmartInspectConsole.Core.Packets;
using SmartInspectConsole.Models;

namespace SmartInspectConsole.Services;

/// <summary>
/// Attaches to a remote SmartInspect Console through an SSH tunnel:
/// catch-up snapshot of existing logs, then live poll via SinceSequence.
/// </summary>
public sealed class RemoteAttachSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RemoteServerProfile _profile;
    private readonly Action<IReadOnlyList<LogEntry>, string> _onLogs;
    private readonly Action<IReadOnlyList<ApplicationSummaryDto>, string> _onApps;
    private readonly Action<string> _onStatus;
    private readonly Action<Exception> _onError;

    private SshTunnelSession? _tunnel;
    private HttpClient? _http;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private long _lastSequence;
    private string _runId = string.Empty;

    public RemoteAttachSession(
        RemoteServerProfile profile,
        Action<IReadOnlyList<LogEntry>, string> onLogs,
        Action<IReadOnlyList<ApplicationSummaryDto>, string> onApps,
        Action<string> onStatus,
        Action<Exception> onError)
    {
        _profile = profile;
        _onLogs = onLogs;
        _onApps = onApps;
        _onStatus = onStatus;
        _onError = onError;
    }

    public string ProfileName => _profile.Name;
    public string ProfileId => _profile.Id;
    public bool IsAttached => _tunnel?.IsConnected == true && _cts is { IsCancellationRequested: false };
    public string? LocalBaseUrl => _tunnel?.LocalBaseUrl;
    public long LastSequence => Interlocked.Read(ref _lastSequence);
    public string RunId => _runId;

    public async Task AttachAsync(CancellationToken cancellationToken = default)
    {
        if (IsAttached)
            throw new InvalidOperationException("Already attached.");

        _onStatus($"SSH connecting to {_profile.SshUser}@{_profile.SshHost}:{_profile.SshPort}…");

        await Task.Run(() =>
        {
            _tunnel = SshTunnelSession.Connect(
                _profile.SshHost,
                _profile.SshPort > 0 ? _profile.SshPort : 22,
                _profile.SshUser,
                _profile.AuthMethod,
                _profile.Password,
                _profile.PrivateKeyPath,
                _profile.PrivateKeyPassphrase,
                _profile.RemoteApiPort > 0 ? _profile.RemoteApiPort : 42331,
                preferredLocalPort: _profile.LocalTunnelPort > 0 ? (uint)_profile.LocalTunnelPort : 0u);
        }, cancellationToken);

        _http = new HttpClient
        {
            BaseAddress = new Uri(_tunnel!.LocalBaseUrl + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        _onStatus($"Tunnel up {_tunnel.LocalBaseUrl} → remote :{_profile.RemoteApiPort}. Loading history…");

        // Health
        using (var healthCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            healthCts.CancelAfter(TimeSpan.FromSeconds(15));
            var health = await _http.GetFromJsonAsync<JsonElement>("api/local/v1/health", JsonOptions, healthCts.Token);
            if (health.TryGetProperty("runId", out var runIdEl))
                _runId = runIdEl.GetString() ?? string.Empty;
        }

        // Applications snapshot
        var apps = await _http.GetFromJsonAsync<List<ApplicationSummaryDto>>(
            "api/local/v1/applications?connectedOnly=false",
            JsonOptions,
            cancellationToken) ?? [];
        _onApps(apps, _profile.Name);

        // Catch-up: newest page(s), then reverse to chronological for UI.
        var catchUpLimit = Math.Clamp(_profile.CatchUpLimit <= 0 ? 2000 : _profile.CatchUpLimit, 100, 20_000);
        var catchUp = await QueryAsync(new LogQueryRequest
        {
            Limit = catchUpLimit,
            IncludeData = true
        }, cancellationToken);

        if (!string.IsNullOrWhiteSpace(catchUp.RunId))
            _runId = catchUp.RunId;

        var chronological = catchUp.Items.Reverse().ToList();
        if (chronological.Count > 0)
        {
            var entries = chronological.Select(ToLogEntry).ToList();
            _onLogs(entries, ClientPrefix());
            _lastSequence = chronological.Max(i => i.Sequence);
        }
        else
        {
            // Empty store: start from 0 so first live poll gets everything new.
            _lastSequence = 0;
        }

        _onStatus(
            $"Attached to {_profile.Name}: catch-up {chronological.Count} entries " +
            $"(seq={_lastSequence}), live polling…");

        _cts = new CancellationTokenSource();
        _pollTask = PollLoopAsync(_cts.Token);
    }

    public async Task DetachAsync()
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();
            if (_pollTask != null)
            {
                try { await _pollTask.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch { /* ignore */ }
            }
            _cts.Dispose();
            _cts = null;
            _pollTask = null;
        }

        _http?.Dispose();
        _http = null;
        _tunnel?.Dispose();
        _tunnel = null;
        _onStatus("Remote attach detached.");
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        var interval = Math.Clamp(_profile.PollIntervalMs <= 0 ? 750 : _profile.PollIntervalMs, 200, 10_000);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken);

                if (_http == null || _tunnel is not { IsConnected: true })
                {
                    _onError(new InvalidOperationException("SSH tunnel disconnected."));
                    break;
                }

                // Drain live pages until empty or cap this tick.
                for (var page = 0; page < 20; page++)
                {
                    var since = Interlocked.Read(ref _lastSequence);
                    var pageResult = await QueryAsync(new LogQueryRequest
                    {
                        SinceSequence = since,
                        Limit = 500,
                        IncludeData = true
                    }, cancellationToken);

                    if (pageResult.Items.Count == 0)
                        break;

                    var entries = pageResult.Items.Select(ToLogEntry).ToList();
                    _onLogs(entries, ClientPrefix());
                    Interlocked.Exchange(ref _lastSequence, pageResult.Items.Max(i => i.Sequence));

                    if (!pageResult.HasMore)
                        break;
                }

                // Refresh application list occasionally
                var apps = await _http.GetFromJsonAsync<List<ApplicationSummaryDto>>(
                    "api/local/v1/applications?connectedOnly=false",
                    JsonOptions,
                    cancellationToken) ?? [];
                _onApps(apps, _profile.Name);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _onError(ex);
                try { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task<LogQueryResponse> QueryAsync(LogQueryRequest request, CancellationToken cancellationToken)
    {
        if (_http == null)
            throw new InvalidOperationException("HTTP client is not connected.");

        using var response = await _http.PostAsJsonAsync("api/local/v1/logs/query", request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LogQueryResponse>(JsonOptions, cancellationToken);
        return body ?? throw new InvalidOperationException("Empty log query response from remote console.");
    }

    private string ClientPrefix() => $"ssh:{_profile.Id}";

    private static LogEntry ToLogEntry(LogEntryDto dto)
    {
        var type = Enum.TryParse<LogEntryType>(dto.Type, ignoreCase: true, out var parsedType)
            ? parsedType
            : LogEntryType.Message;
        var viewer = Enum.TryParse<ViewerId>(dto.ViewerId, ignoreCase: true, out var parsedViewer)
            ? parsedViewer
            : ViewerId.Title;

        return new LogEntry
        {
            Timestamp = dto.TimestampUtc.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dto.TimestampUtc, DateTimeKind.Utc).ToLocalTime()
                : dto.TimestampUtc.ToLocalTime(),
            LogEntryType = type,
            ViewerId = viewer,
            AppName = dto.AppName ?? string.Empty,
            SessionName = dto.SessionName ?? string.Empty,
            HostName = dto.HostName ?? string.Empty,
            Title = dto.Title ?? string.Empty,
            ProcessId = dto.ProcessId,
            ThreadId = dto.ThreadId,
            Data = string.IsNullOrEmpty(dto.DataText) ? null : Encoding.UTF8.GetBytes(dto.DataText)
        };
    }

    public async ValueTask DisposeAsync()
    {
        await DetachAsync();
    }
}
