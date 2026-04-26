using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using SmartInspectConsole.Core.Enums;
using SmartInspectConsole.Core.FileIO;
using SmartInspectConsole.Core.Packets;

namespace SmartInspectConsole.LoadTester;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = LoadTestOptions.Parse(args);
        if (options.ShowHelp)
        {
            Console.WriteLine(LoadTestOptions.HelpText);
            return 0;
        }

        using var cts = new CancellationTokenSource(options.Duration);
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var stats = new LoadTestStats();
        var workers = Enumerable.Range(0, options.Clients)
            .Select(index => RunClientAsync(options, index + 1, stats, cts.Token))
            .ToArray();

        var reporter = ReportProgressAsync(stats, options, cts.Token);

        try
        {
            await Task.WhenAll(workers.Append(reporter));
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            Console.WriteLine("Load test cancelled.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"Load test failed: {ex}");
            return 1;
        }
    }

    private static async Task RunClientAsync(LoadTestOptions options, int clientNumber, LoadTestStats stats, CancellationToken cancellationToken)
    {
        LoadTestConnection connection = options.Transport switch
        {
            TransportKind.Tcp => await ExecuteWithTimeoutAsync(
                token => TcpLoadTestConnection.ConnectAsync(options, clientNumber, token),
                options.ConnectTimeout,
                $"tcp connect client {clientNumber}",
                cancellationToken),
            TransportKind.Pipe => await ExecuteWithTimeoutAsync(
                token => PipeLoadTestConnection.ConnectAsync(options, clientNumber, token),
                options.ConnectTimeout,
                $"pipe connect client {clientNumber}",
                cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported transport: {options.Transport}")
        };

        await using (connection)
        {
            var writer = new BinaryPacketWriter();
            var hostname = Environment.MachineName;
            var sessionName = $"{options.SessionPrefix}-{clientNumber:00}";
            var appName = $"{options.AppNamePrefix}-{clientNumber:00}";
            var payload = CreatePayload(options.PayloadBytes);
            var random = new Random(HashCode.Combine(clientNumber, Environment.TickCount));
            var stopwatch = Stopwatch.StartNew();
            var nextWatchAt = options.WatchesEvery <= 0 ? long.MaxValue : options.WatchesEvery;
            var nextProcessFlowAt = options.ProcessFlowsEvery <= 0 ? long.MaxValue : options.ProcessFlowsEvery;
            long sentLogs = 0;
            double tokens = 0;
            var lastRefill = stopwatch.Elapsed;

            await ExecuteWithTimeoutAsync(
                token => connection.SendPacketAsync(writer, new LogHeader
                {
                    Content = $"appname={appName}\r\nhostname={hostname}\r\n"
                }, token),
                options.OperationTimeout,
                $"send log header client {clientNumber}",
                cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                RefillTokens(options, ref tokens, ref lastRefill, stopwatch.Elapsed);
                if (options.MessagesPerSecond > 0 && tokens < 1)
                {
                    await Task.Delay(1, cancellationToken);
                    continue;
                }

                if (options.MessagesPerSecond > 0)
                    tokens -= 1;

                sentLogs++;

                var entryType = PickLogEntryType(sentLogs);
                var packet = new LogEntry
                {
                    Timestamp = DateTime.Now,
                    LogEntryType = entryType,
                    ViewerId = ViewerId.Data,
                    AppName = appName,
                    SessionName = sessionName,
                    Title = $"[{clientNumber:00}] Event {sentLogs:N0} ({entryType})",
                    HostName = hostname,
                    Data = payload,
                    ProcessId = Environment.ProcessId,
                    ThreadId = Environment.CurrentManagedThreadId,
                    Color = PickColor(entryType)
                };

                await ExecuteWithTimeoutAsync(
                    token => connection.SendPacketAsync(writer, packet, token),
                    options.OperationTimeout,
                    $"send log packet client {clientNumber}",
                    cancellationToken);
                stats.RecordLog(payload.Length);

                if (sentLogs >= nextWatchAt)
                {
                    await ExecuteWithTimeoutAsync(
                        token => connection.SendPacketAsync(writer, new Watch
                        {
                            Name = $"{sessionName}.Rate",
                            Value = $"{sentLogs:N0}",
                            WatchType = WatchType.String,
                            Timestamp = DateTime.Now
                        }, token),
                        options.OperationTimeout,
                        $"send watch packet client {clientNumber}",
                        cancellationToken);
                    stats.RecordWatch();
                    nextWatchAt += options.WatchesEvery;
                }

                if (sentLogs >= nextProcessFlowAt)
                {
                    await ExecuteWithTimeoutAsync(
                        token => connection.SendPacketAsync(writer, new ProcessFlow
                        {
                            ProcessFlowType = sentLogs % 2 == 0 ? ProcessFlowType.EnterMethod : ProcessFlowType.LeaveMethod,
                            Title = $"Worker{clientNumber}.Tick{random.Next(1, 500)}",
                            HostName = hostname,
                            ProcessId = Environment.ProcessId,
                            ThreadId = Environment.CurrentManagedThreadId,
                            Timestamp = DateTime.Now
                        }, token),
                        options.OperationTimeout,
                        $"send process flow packet client {clientNumber}",
                        cancellationToken);
                    stats.RecordProcessFlow();
                    nextProcessFlowAt += options.ProcessFlowsEvery;
                }
            }
        }
    }

    private static void RefillTokens(LoadTestOptions options, ref double tokens, ref TimeSpan lastRefill, TimeSpan now)
    {
        if (options.MessagesPerSecond <= 0)
            return;

        var elapsed = now - lastRefill;
        if (elapsed <= TimeSpan.Zero)
            return;

        tokens = Math.Min(options.MessagesPerSecond, tokens + elapsed.TotalSeconds * options.MessagesPerSecond);
        lastRefill = now;
    }

    private static async Task<T> ExecuteWithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout,
        string operationName,
        CancellationToken cancellationToken)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
            return await operation(cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            return await operation(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"{operationName} timed out after {timeout.TotalSeconds:F0}s.");
        }
    }

    private static async Task ExecuteWithTimeoutAsync(
        Func<CancellationToken, Task> operation,
        TimeSpan timeout,
        string operationName,
        CancellationToken cancellationToken)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            await operation(cancellationToken);
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await operation(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"{operationName} timed out after {timeout.TotalSeconds:F0}s.");
        }
    }

    private static async Task ReportProgressAsync(LoadTestStats stats, LoadTestOptions options, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            Console.Write(
                $"\r{DateTime.Now:HH:mm:ss} | {options.Transport,-4} | clients {options.Clients,2} | " +
                $"logs {stats.LogEntriesSent,12:N0} | watches {stats.WatchesSent,10:N0} | flows {stats.ProcessFlowsSent,10:N0} | " +
                $"payload {stats.BytesSent / 1024.0 / 1024.0,8:F1} MiB");
        }
    }

    private static byte[] CreatePayload(int payloadBytes)
    {
        if (payloadBytes <= 0)
            return [];

        var builder = new StringBuilder(payloadBytes);
        builder.Append("{\"message\":\"");
        while (builder.Length < payloadBytes - 16)
        {
            builder.Append("load-test-data-");
        }
        builder.Append("\"}");

        var text = builder.ToString();
        if (text.Length > payloadBytes)
            text = text[..payloadBytes];

        return Encoding.UTF8.GetBytes(text);
    }

    private static LogEntryType PickLogEntryType(long sequence)
        => (sequence % 50) switch
        {
            0 => LogEntryType.Error,
            1 => LogEntryType.Warning,
            2 => LogEntryType.Debug,
            3 => LogEntryType.Verbose,
            _ => LogEntryType.Message
        };

    private static System.Drawing.Color PickColor(LogEntryType entryType)
        => entryType switch
        {
            LogEntryType.Error => System.Drawing.Color.FromArgb(255, 190, 60, 60),
            LogEntryType.Warning => System.Drawing.Color.FromArgb(255, 210, 170, 40),
            LogEntryType.Debug => System.Drawing.Color.FromArgb(255, 80, 140, 220),
            _ => System.Drawing.Color.FromArgb(255, 90, 180, 90)
        };
}

internal enum TransportKind
{
    Tcp,
    Pipe
}

internal sealed class LoadTestOptions
{
    public static string HelpText =>
        """
        SmartInspectConsole.LoadTester

        Options:
          --transport tcp|pipe        Transport to use. Default: tcp
          --host <name>               TCP host. Default: localhost
          --port <number>             TCP port. Default: 4228
          --pipe <name>               Named pipe name. Default: smartinspect
          --clients <number>          Concurrent clients. Default: 4
          --messages-per-second <n>   Per-client log rate. 0 = unthrottled. Default: 1000
          --payload-bytes <n>         Approximate bytes in each log payload. Default: 512
          --duration-seconds <n>      Test duration in seconds. Default: 300
          --connect-timeout-seconds <n>
                                      Maximum time to allow each client connection. 0 = infinite. Default: 15
          --operation-timeout-seconds <n>
                                      Maximum time to allow each send/ack operation. 0 = infinite. Default: 15
          --watches-every <n>         Send a watch packet every N log entries. Default: 100
          --flows-every <n>           Send a process flow packet every N log entries. Default: 200
          --app-prefix <text>         App name prefix. Default: LoadTestApp
          --session-prefix <text>     Session prefix. Default: Worker
          --help                      Show this help text

        Examples:
          dotnet run --project src/SmartInspectConsole.LoadTester -- --transport tcp --clients 8 --messages-per-second 2000
          dotnet run --project src/SmartInspectConsole.LoadTester -- --transport pipe --pipe smartinspect --clients 4 --duration-seconds 120
        """;

    public TransportKind Transport { get; init; } = TransportKind.Tcp;
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 4228;
    public string PipeName { get; init; } = "smartinspect";
    public int Clients { get; init; } = 4;
    public int MessagesPerSecond { get; init; } = 1000;
    public int PayloadBytes { get; init; } = 512;
    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public int WatchesEvery { get; init; } = 100;
    public int ProcessFlowsEvery { get; init; } = 200;
    public string AppNamePrefix { get; init; } = "LoadTestApp";
    public string SessionPrefix { get; init; } = "Worker";
    public bool ShowHelp { get; init; }

    public static LoadTestOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                continue;

            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase))
            {
                values["help"] = "true";
                continue;
            }

            var key = arg[2..];
            var value = i + 1 < args.Length ? args[++i] : string.Empty;
            values[key] = value;
        }

        if (values.ContainsKey("help"))
            return new LoadTestOptions { ShowHelp = true };

        return new LoadTestOptions
        {
            Transport = ParseTransport(GetValue(values, "transport", "tcp")),
            Host = GetValue(values, "host", "localhost"),
            Port = ParseInt(GetValue(values, "port", "4228"), "port", 1),
            PipeName = GetValue(values, "pipe", "smartinspect"),
            Clients = ParseInt(GetValue(values, "clients", "4"), "clients", 1),
            MessagesPerSecond = ParseInt(GetValue(values, "messages-per-second", "1000"), "messages-per-second", 0),
            PayloadBytes = ParseInt(GetValue(values, "payload-bytes", "512"), "payload-bytes", 0),
            Duration = TimeSpan.FromSeconds(ParseInt(GetValue(values, "duration-seconds", "300"), "duration-seconds", 1)),
            ConnectTimeout = ParseTimeoutSeconds(GetValue(values, "connect-timeout-seconds", "15"), "connect-timeout-seconds"),
            OperationTimeout = ParseTimeoutSeconds(GetValue(values, "operation-timeout-seconds", "15"), "operation-timeout-seconds"),
            WatchesEvery = ParseInt(GetValue(values, "watches-every", "100"), "watches-every", 0),
            ProcessFlowsEvery = ParseInt(GetValue(values, "flows-every", "200"), "flows-every", 0),
            AppNamePrefix = GetValue(values, "app-prefix", "LoadTestApp"),
            SessionPrefix = GetValue(values, "session-prefix", "Worker")
        };
    }

    private static string GetValue(Dictionary<string, string> values, string key, string defaultValue)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : defaultValue;

    private static int ParseInt(string rawValue, string argumentName, int minValue)
    {
        if (!int.TryParse(rawValue, out var value) || value < minValue)
            throw new ArgumentOutOfRangeException(argumentName, $"Expected {argumentName} to be an integer >= {minValue}.");

        return value;
    }

    private static TimeSpan ParseTimeoutSeconds(string rawValue, string argumentName)
    {
        var seconds = ParseInt(rawValue, argumentName, 0);
        return seconds == 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(seconds);
    }

    private static TransportKind ParseTransport(string value)
        => value.ToLowerInvariant() switch
        {
            "tcp" => TransportKind.Tcp,
            "pipe" => TransportKind.Pipe,
            _ => throw new ArgumentException($"Unsupported transport '{value}'. Expected tcp or pipe.")
        };
}

internal sealed class LoadTestStats
{
    private long _logEntriesSent;
    private long _watchesSent;
    private long _processFlowsSent;
    private long _bytesSent;

    public long LogEntriesSent => Interlocked.Read(ref _logEntriesSent);
    public long WatchesSent => Interlocked.Read(ref _watchesSent);
    public long ProcessFlowsSent => Interlocked.Read(ref _processFlowsSent);
    public long BytesSent => Interlocked.Read(ref _bytesSent);

    public void RecordLog(int bytes)
    {
        Interlocked.Increment(ref _logEntriesSent);
        Interlocked.Add(ref _bytesSent, bytes);
    }

    public void RecordWatch() => Interlocked.Increment(ref _watchesSent);

    public void RecordProcessFlow() => Interlocked.Increment(ref _processFlowsSent);
}

internal abstract class LoadTestConnection : IAsyncDisposable
{
    protected LoadTestConnection(Stream stream, string clientBanner, bool requiresAcknowledgment)
    {
        Stream = stream;
        ClientBanner = clientBanner;
        RequiresAcknowledgment = requiresAcknowledgment;
    }

    protected Stream Stream { get; }
    protected string ClientBanner { get; }
    protected bool RequiresAcknowledgment { get; }

    public async Task SendPacketAsync(BinaryPacketWriter writer, Packet packet, CancellationToken cancellationToken)
    {
        writer.WritePacket(Stream, packet);
        await Stream.FlushAsync(cancellationToken);

        if (RequiresAcknowledgment)
        {
            var acknowledgment = new byte[2];
            await ReadExactlyAsync(Stream, acknowledgment, cancellationToken);
        }
    }

    protected static async Task<string> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new byte[1];
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;

            var value = (char)buffer[0];
            if (value == '\n')
                break;

            builder.Append(value);
        }

        return builder.ToString().TrimEnd('\r');
    }

    protected async Task SendBannerAsync(CancellationToken cancellationToken)
    {
        _ = await ReadLineAsync(Stream, cancellationToken);
        var bannerBytes = Encoding.ASCII.GetBytes(ClientBanner + "\n");
        await Stream.WriteAsync(bannerBytes, cancellationToken);
        await Stream.FlushAsync(cancellationToken);
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
                throw new IOException("Connection closed while waiting for acknowledgment.");
            offset += read;
        }
    }

    public virtual ValueTask DisposeAsync()
    {
        Stream.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class TcpLoadTestConnection : LoadTestConnection
{
    private readonly TcpClient _client;

    private TcpLoadTestConnection(TcpClient client, Stream stream, string clientBanner)
        : base(stream, clientBanner, requiresAcknowledgment: true)
    {
        _client = client;
    }

    public static async Task<TcpLoadTestConnection> ConnectAsync(LoadTestOptions options, int clientNumber, CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        await client.ConnectAsync(options.Host, options.Port, cancellationToken);
        var connection = new TcpLoadTestConnection(client, new BufferedStream(client.GetStream(), 64 * 1024), $"SmartInspect Load Tester/{clientNumber}");
        await connection.SendBannerAsync(cancellationToken);
        return connection;
    }

    public override ValueTask DisposeAsync()
    {
        try
        {
            _client.Close();
        }
        catch
        {
            // Ignore shutdown failures during test teardown.
        }

        return base.DisposeAsync();
    }
}

internal sealed class PipeLoadTestConnection : LoadTestConnection
{
    private readonly NamedPipeClientStream _client;

    private PipeLoadTestConnection(NamedPipeClientStream client, string clientBanner)
        : base(client, clientBanner, requiresAcknowledgment: false)
    {
        _client = client;
    }

    public static async Task<PipeLoadTestConnection> ConnectAsync(LoadTestOptions options, int clientNumber, CancellationToken cancellationToken)
    {
        var client = new NamedPipeClientStream(".", options.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(cancellationToken);
        var connection = new PipeLoadTestConnection(client, $"SmartInspect Load Tester/{clientNumber}");
        await connection.SendBannerAsync(cancellationToken);
        return connection;
    }

    public override ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
