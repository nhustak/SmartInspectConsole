using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using SmartInspectConsole.Core.Enums;
using SmartInspectConsole.Core.FileIO;
using SmartInspectConsole.Core.Packets;

namespace SmartInspectConsole.PipeTestClient;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(Options.HelpText);
                return 0;
            }

            using var cts = new CancellationTokenSource(options.OverallTimeout);
            var stats = new SendStats();
            var wall = Stopwatch.StartNew();

            await using var pipe = new NamedPipeClientStream(".", options.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync((int)options.ConnectTimeout.TotalMilliseconds, cts.Token);

            await ReadServerBannerAsync(pipe, cts.Token);

            var clientBanner = Encoding.ASCII.GetBytes($"SmartInspect PipeTestClient/{options.ClientId}\n");
            await WriteWithTimeoutAsync(pipe, clientBanner, options.SendTimeout, cts.Token, stats);

            var writer = new BinaryPacketWriter();
            var hostName = Environment.MachineName;
            var appName = string.IsNullOrWhiteSpace(options.AppNamePrefix)
                ? $"client-{options.ClientId}"
                : $"{options.AppNamePrefix}-client-{options.ClientId}";

            var payload = options.PayloadBytes > 0
                ? Encoding.ASCII.GetBytes(new string('x', options.PayloadBytes))
                : null;

            await SendPacketWithTimeoutAsync(writer, pipe, new LogHeader
            {
                Content = $"appname={appName}\r\nhostname={hostName}\r\n"
            }, options.SendTimeout, cts.Token, stats);

            for (var i = 0; i < options.Count; i++)
            {
                var entry = new LogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    LogEntryType = LogEntryType.Message,
                    ViewerId = ViewerId.Data,
                    AppName = appName,
                    SessionName = appName,
                    Title = $"{appName} seq-{i:D6}",
                    HostName = hostName,
                    Data = payload ?? Encoding.ASCII.GetBytes($"{appName}/seq-{i}"),
                    ProcessId = Environment.ProcessId,
                    ThreadId = Environment.CurrentManagedThreadId
                };
                await SendPacketWithTimeoutAsync(writer, pipe, entry, options.SendTimeout, cts.Token, stats);
            }

            using (var flushCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
            {
                flushCts.CancelAfter(options.SendTimeout);
                await pipe.FlushAsync(flushCts.Token);
            }

            wall.Stop();
            Console.WriteLine(
                $"client={options.ClientId} ok count={options.Count} payloadBytes={options.PayloadBytes} " +
                $"wallMs={wall.Elapsed.TotalMilliseconds:F0} maxSendMs={stats.MaxSendMs:F0}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"PipeTestClient failed: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static async Task SendPacketWithTimeoutAsync(
        BinaryPacketWriter writer,
        Stream stream,
        Packet packet,
        TimeSpan sendTimeout,
        CancellationToken overallToken,
        SendStats stats)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(overallToken);
        cts.CancelAfter(sendTimeout);
        await writer.WritePacketAsync(stream, packet, cts.Token);
        await stream.FlushAsync(cts.Token);
        sw.Stop();
        stats.Note(sw.Elapsed.TotalMilliseconds);
    }

    private static async Task WriteWithTimeoutAsync(
        Stream stream,
        byte[] data,
        TimeSpan sendTimeout,
        CancellationToken overallToken,
        SendStats stats)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(overallToken);
        cts.CancelAfter(sendTimeout);
        await stream.WriteAsync(data, cts.Token);
        await stream.FlushAsync(cts.Token);
        sw.Stop();
        stats.Note(sw.Elapsed.TotalMilliseconds);
    }

    private sealed class SendStats
    {
        public double MaxSendMs { get; private set; }

        public void Note(double elapsedMs)
        {
            if (elapsedMs > MaxSendMs)
                MaxSendMs = elapsedMs;
        }
    }

    private static async Task ReadServerBannerAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                throw new IOException("Pipe closed before server banner.");
            if (buffer[0] == (byte)'\n')
                return;
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private sealed class Options
    {
        public string PipeName { get; init; } = "smartinspect";
        public int ClientId { get; init; }
        public int Count { get; init; } = 100;
        public string? AppNamePrefix { get; init; }
        public int PayloadBytes { get; init; }
        public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
        public TimeSpan OverallTimeout { get; init; } = TimeSpan.FromSeconds(60);
        public TimeSpan SendTimeout { get; init; } = TimeSpan.FromSeconds(5);
        public bool ShowHelp { get; init; }

        public static string HelpText =>
            """
            SmartInspectConsole.PipeTestClient

            Connects once to the SmartInspect Console named pipe and sends a fixed
            number of deterministic LogEntry packets, then exits.

            Options:
              --pipe-name <name>           Named pipe name. Default: smartinspect
              --client-id <int>            Unique numeric identifier (required)
              --count <int>                Number of LogEntry packets to send. Default: 100
              --app-name-prefix <text>     Optional prefix for AppName/SessionName.
              --payload-bytes <int>        Optional fixed payload size (lock-up / flood tests).
              --connect-timeout-ms <int>   Pipe connect timeout (ms). Default: 10000
              --overall-timeout-ms <int>   Hard upper bound on the run (ms). Default: 60000
              --send-timeout-ms <int>      Per write/flush timeout (ms). Default: 5000
              --help                       Show this help text
            """;

        public static Options Parse(string[] args)
        {
            string pipeName = "smartinspect";
            int? clientId = null;
            int count = 100;
            string? appNamePrefix = null;
            int payloadBytes = 0;
            int connectTimeoutMs = 10_000;
            int overallTimeoutMs = 60_000;
            int sendTimeoutMs = 5_000;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase))
                    return new Options { ShowHelp = true };

                if (i + 1 >= args.Length)
                    throw new ArgumentException($"Missing value for {arg}");

                var value = args[++i];
                switch (arg)
                {
                    case "--pipe-name":
                        pipeName = value;
                        break;
                    case "--client-id":
                        clientId = int.Parse(value);
                        break;
                    case "--count":
                        count = int.Parse(value);
                        break;
                    case "--app-name-prefix":
                        appNamePrefix = value;
                        break;
                    case "--payload-bytes":
                        payloadBytes = int.Parse(value);
                        break;
                    case "--connect-timeout-ms":
                        connectTimeoutMs = int.Parse(value);
                        break;
                    case "--overall-timeout-ms":
                        overallTimeoutMs = int.Parse(value);
                        break;
                    case "--send-timeout-ms":
                        sendTimeoutMs = int.Parse(value);
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument: {arg}");
                }
            }

            if (clientId is null)
                throw new ArgumentException("--client-id is required");

            return new Options
            {
                PipeName = pipeName,
                ClientId = clientId.Value,
                Count = count,
                AppNamePrefix = appNamePrefix,
                PayloadBytes = payloadBytes,
                ConnectTimeout = TimeSpan.FromMilliseconds(connectTimeoutMs),
                OverallTimeout = TimeSpan.FromMilliseconds(overallTimeoutMs),
                SendTimeout = TimeSpan.FromMilliseconds(sendTimeoutMs)
            };
        }
    }
}
