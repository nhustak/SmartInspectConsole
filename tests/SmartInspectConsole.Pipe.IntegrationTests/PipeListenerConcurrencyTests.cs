using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SmartInspectConsole.Contracts;
using Xunit;
using Xunit.Abstractions;

namespace SmartInspectConsole.Pipe.IntegrationTests;

public class PipeListenerConcurrencyTests
{
    private const string McpEndpoint = "http://127.0.0.1:42331/mcp";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITestOutputHelper _output;

    public PipeListenerConcurrencyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(5, 100)]
    public async Task LiveConsoleConcurrentClients_AllPacketsArriveUncorrupted(int clientCount, int packetsPerClient)
    {
        await using var mcp = await LiveConsoleMcpClient.ConnectAsync();
        var beforeContext = await mcp.GetLiveContextAsync();
        AssertLivePipeReady(beforeContext);

        var runId = Guid.NewGuid().ToString("N");
        var appNamePrefix = $"pipe-live-{runId}";
        var expectedAppNames = Enumerable.Range(1, clientCount)
            .Select(i => $"{appNamePrefix}-client-{i}")
            .ToHashSet(StringComparer.Ordinal);

        _output.WriteLine($"MCP endpoint: {McpEndpoint}");
        _output.WriteLine($"live console run id: {beforeContext.RunId}");
        _output.WriteLine($"test app prefix: {appNamePrefix}");
        _output.WriteLine($"clientCount={clientCount} packetsPerClient={packetsPerClient}");

        var subprocesses = await RunSubprocessClientsAsync(clientCount, packetsPerClient, appNamePrefix);
        var afterContext = await mcp.GetLiveContextAsync();
        var packetsByAppName = await WaitForExpectedLogsAsync(mcp, expectedAppNames, packetsPerClient);

        var failures = new List<string>();
        foreach (var proc in subprocesses)
        {
            if (proc.ExitCode != 0)
                failures.Add($"client-{proc.ClientId}: exit code {proc.ExitCode}; stderr={Truncate(proc.StdErr, 700)}; stdout={Truncate(proc.StdOut, 700)}");
        }

        var actualAppNames = packetsByAppName.Keys.ToHashSet(StringComparer.Ordinal);
        var missingApps = expectedAppNames.Except(actualAppNames).ToList();
        var unexpectedApps = actualAppNames.Except(expectedAppNames).ToList();
        if (missingApps.Count > 0)
            failures.Add($"missing AppNames from MCP query: {string.Join(", ", missingApps)}");
        if (unexpectedApps.Count > 0)
            failures.Add($"unexpected AppNames from MCP query: {string.Join(", ", unexpectedApps)}");

        ValidatePackets(expectedAppNames, packetsByAppName, packetsPerClient, failures);

        _output.WriteLine($"subprocesses launched: {subprocesses.Count}; exit-0: {subprocesses.Count(p => p.ExitCode == 0)}");
        _output.WriteLine($"distinct AppNames received via MCP: {packetsByAppName.Count}");
        _output.WriteLine($"total LogEntry packets received via MCP: {packetsByAppName.Values.Sum(q => q.Count)}");
        _output.WriteLine($"before live context: {JsonSerializer.Serialize(beforeContext, JsonOptions)}");
        _output.WriteLine($"after live context: {JsonSerializer.Serialize(afterContext, JsonOptions)}");

        if (failures.Count > 0)
        {
            var report = new StringBuilder();
            report.AppendLine($"Live console pipe test failed for clientCount={clientCount}, packetsPerClient={packetsPerClient}:");
            foreach (var f in failures)
                report.AppendLine($"  - {f}");
            report.AppendLine($"  - MCP live context after run: {JsonSerializer.Serialize(afterContext, JsonOptions)}");
            _output.WriteLine(report.ToString());
            Assert.Fail(report.ToString());
        }
    }

    private static void AssertLivePipeReady(LiveContextDto context)
    {
        var pipe = context.ListenerStatus.FirstOrDefault(s =>
            string.Equals(s.Transport, "pipe", StringComparison.OrdinalIgnoreCase));

        if (pipe == null)
            Assert.Fail($"The real SmartInspect Console is reachable at {McpEndpoint}, but it did not report a pipe listener.");

        if (!pipe.Enabled || !string.Equals(pipe.Endpoint, "pipe://smartinspect", StringComparison.OrdinalIgnoreCase))
            Assert.Fail($"The real SmartInspect Console pipe listener must be enabled at pipe://smartinspect. Actual: enabled={pipe.Enabled}, endpoint={pipe.Endpoint}");
    }

    private async Task<IReadOnlyList<SubprocessResult>> RunSubprocessClientsAsync(
        int clientCount,
        int packetsPerClient,
        string appNamePrefix)
    {
        var clientExePath = ResolveClientExe();
        _output.WriteLine($"PipeTestClient exe: {clientExePath}");
        _output.WriteLine("pipe: smartinspect");

        var processes = new List<Process>();
        for (var clientId = 1; clientId <= clientCount; clientId++)
        {
            var psi = new ProcessStartInfo
            {
                FileName = clientExePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--pipe-name");
            psi.ArgumentList.Add("smartinspect");
            psi.ArgumentList.Add("--client-id");
            psi.ArgumentList.Add(clientId.ToString());
            psi.ArgumentList.Add("--count");
            psi.ArgumentList.Add(packetsPerClient.ToString());
            psi.ArgumentList.Add("--app-name-prefix");
            psi.ArgumentList.Add(appNamePrefix);
            psi.ArgumentList.Add("--connect-timeout-ms");
            psi.ArgumentList.Add("15000");
            psi.ArgumentList.Add("--overall-timeout-ms");
            psi.ArgumentList.Add("60000");

            var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start client-{clientId}");
            processes.Add(proc);
        }

        var ioTasks = processes.Select(p => Task.Run(async () =>
        {
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            return (StdOut: await stdoutTask, StdErr: await stderrTask);
        })).ToArray();

        var allDone = Task.WhenAll(ioTasks);
        var completed = await Task.WhenAny(allDone, Task.Delay(TimeSpan.FromSeconds(70)));
        if (completed != allDone)
        {
            foreach (var p in processes)
            {
                if (!p.HasExited)
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                }
            }
            await Task.WhenAll(ioTasks);
        }

        var subprocesses = new List<SubprocessResult>();
        for (var i = 0; i < processes.Count; i++)
        {
            var p = processes[i];
            var (stdout, stderr) = await ioTasks[i];
            subprocesses.Add(new SubprocessResult(i + 1, p.ExitCode, stdout, stderr));
        }

        return subprocesses;
    }

    private static async Task<ConcurrentDictionary<string, ConcurrentQueue<LogEntryDto>>> WaitForExpectedLogsAsync(
        LiveConsoleMcpClient mcp,
        IReadOnlySet<string> expectedAppNames,
        int packetsPerClient)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        ConcurrentDictionary<string, ConcurrentQueue<LogEntryDto>> latest = new();

        do
        {
            var response = await mcp.QueryLogsAsync(expectedAppNames, packetsPerClient * expectedAppNames.Count);
            latest = new ConcurrentDictionary<string, ConcurrentQueue<LogEntryDto>>(StringComparer.Ordinal);
            foreach (var entry in response.Items)
            {
                if (!expectedAppNames.Contains(entry.AppName))
                    continue;

                var queue = latest.GetOrAdd(entry.AppName, _ => new ConcurrentQueue<LogEntryDto>());
                queue.Enqueue(entry);
            }

            if (expectedAppNames.All(appName => latest.TryGetValue(appName, out var entries) && entries.Count >= packetsPerClient))
                return latest;

            await Task.Delay(250);
        }
        while (DateTime.UtcNow < deadline);

        return latest;
    }

    private static void ValidatePackets(
        IReadOnlySet<string> expectedAppNames,
        ConcurrentDictionary<string, ConcurrentQueue<LogEntryDto>> packetsByAppName,
        int packetsPerClient,
        List<string> failures)
    {
        var titlePattern = new Regex(@"^(?<app>.+-client-\d+) seq-(?<seq>\d{6})$", RegexOptions.Compiled);

        foreach (var appName in expectedAppNames.Intersect(packetsByAppName.Keys))
        {
            var packets = packetsByAppName[appName].ToList();

            if (packets.Count != packetsPerClient)
                failures.Add($"{appName}: received {packets.Count} LogEntry packets, expected {packetsPerClient}");

            var seenSequences = new HashSet<int>();
            var corruptedDataExamples = new List<string>();
            var corruptedTitleExamples = new List<string>();
            var crossClientTitleExamples = new List<string>();

            foreach (var p in packets)
            {
                var match = titlePattern.Match(p.Title ?? string.Empty);
                if (!match.Success)
                {
                    if (corruptedTitleExamples.Count < 3)
                        corruptedTitleExamples.Add(EscapeForReport(p.Title ?? "<null>"));
                    continue;
                }

                var titleAppName = match.Groups["app"].Value;
                var seq = int.Parse(match.Groups["seq"].Value);

                if (!string.Equals(titleAppName, appName, StringComparison.Ordinal))
                {
                    if (crossClientTitleExamples.Count < 3)
                        crossClientTitleExamples.Add($"AppName={appName} but Title={p.Title}");
                    continue;
                }

                if (seq < 0 || seq >= packetsPerClient)
                {
                    if (corruptedTitleExamples.Count < 3)
                        corruptedTitleExamples.Add($"out-of-range seq: Title={p.Title}");
                    continue;
                }

                seenSequences.Add(seq);

                var expectedData = $"{appName}/seq-{seq}";
                if (p.DataText != expectedData)
                {
                    if (corruptedDataExamples.Count < 3)
                        corruptedDataExamples.Add($"Title={p.Title} expected Data='{expectedData}' got Data='{EscapeForReport(p.DataText ?? "<null>")}'");
                }
            }

            if (corruptedTitleExamples.Count > 0)
                failures.Add($"{appName}: malformed/unparseable Title field (sample): {string.Join(" | ", corruptedTitleExamples)}");
            if (crossClientTitleExamples.Count > 0)
                failures.Add($"{appName}: cross-client Title corruption (sample): {string.Join(" | ", crossClientTitleExamples)}");
            if (corruptedDataExamples.Count > 0)
                failures.Add($"{appName}: Data payload mismatch (sample): {string.Join(" | ", corruptedDataExamples)}");

            if (seenSequences.Count != packetsPerClient)
            {
                var missing = Enumerable.Range(0, packetsPerClient).Except(seenSequences).Take(10).ToList();
                failures.Add($"{appName}: sequence coverage {seenSequences.Count}/{packetsPerClient}; missing seqs (first 10): {string.Join(", ", missing)}");
            }
        }
    }

    private static string ResolveClientExe()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parts = baseDir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var binIdx = Array.LastIndexOf(parts, "bin");
        if (binIdx < 0 || binIdx + 2 >= parts.Length)
            throw new InvalidOperationException($"Cannot parse Configuration/TargetFramework from base dir: {baseDir}");

        var configuration = parts[binIdx + 1];
        var targetFramework = parts[binIdx + 2];
        var testProjectDir = string.Join(Path.DirectorySeparatorChar, parts.Take(binIdx));
        var testsRoot = Path.GetDirectoryName(testProjectDir)
            ?? throw new InvalidOperationException("Cannot locate tests root directory.");

        var clientExe = Path.Combine(
            testsRoot,
            "SmartInspectConsole.PipeTestClient",
            "bin",
            configuration,
            targetFramework,
            "SmartInspectConsole.PipeTestClient.exe");

        if (!File.Exists(clientExe))
            throw new FileNotFoundException("PipeTestClient.exe not found at expected location. Build the solution first.", clientExe);

        return clientExe;
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Length <= max ? s : s[..max] + "...[truncated]";
    }

    private static string EscapeForReport(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            if (c < 0x20 || c == 0x7F)
                sb.Append($"\\x{(int)c:x2}");
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    private sealed record SubprocessResult(int ClientId, int ExitCode, string StdOut, string StdErr);

    private sealed class LiveConsoleMcpClient : IAsyncDisposable
    {
        private readonly McpClient _client;

        private LiveConsoleMcpClient(McpClient client)
        {
            _client = client;
        }

        public static async Task<LiveConsoleMcpClient> ConnectAsync()
        {
            try
            {
                var transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(McpEndpoint),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    Name = "SmartInspect live console pipe tests",
                    ConnectionTimeout = TimeSpan.FromSeconds(10)
                });

                var client = await McpClient.CreateAsync(transport);
                return new LiveConsoleMcpClient(client);
            }
            catch (Exception ex)
            {
                Assert.Fail($"Could not connect to the real SmartInspect Console MCP endpoint at {McpEndpoint}. Start SmartInspectConsole and ensure its in-process MCP server is running. {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        public async Task<LiveContextDto> GetLiveContextAsync()
        {
            var result = await _client.CallToolAsync("get_live_context", new Dictionary<string, object?>());
            return DeserializeToolResult<LiveContextDto>(result);
        }

        public async Task<LogQueryResponse> QueryLogsAsync(IReadOnlySet<string> appNames, int limit)
        {
            var result = await _client.CallToolAsync("query_logs", new Dictionary<string, object?>
            {
                ["appNames"] = appNames.ToArray(),
                ["limit"] = limit,
                ["includeData"] = true
            });

            return DeserializeToolResult<LogQueryResponse>(result);
        }

        private static T DeserializeToolResult<T>(CallToolResult result)
        {
            if (result.StructuredContent is { } structuredContent)
            {
                return structuredContent.Deserialize<T>(JsonOptions)
                    ?? throw new InvalidOperationException($"Could not deserialize MCP structured content as {typeof(T).Name}.");
            }

            var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException($"MCP tool result did not include JSON content for {typeof(T).Name}.");

            return JsonSerializer.Deserialize<T>(text, JsonOptions)
                ?? throw new InvalidOperationException($"Could not deserialize MCP text content as {typeof(T).Name}.");
        }

        public ValueTask DisposeAsync()
        {
            return _client.DisposeAsync();
        }
    }
}
