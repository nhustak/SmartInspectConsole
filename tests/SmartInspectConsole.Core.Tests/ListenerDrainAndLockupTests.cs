using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using SmartInspectConsole.Core.Enums;
using SmartInspectConsole.Core.Events;
using SmartInspectConsole.Core.FileIO;
using SmartInspectConsole.Core.Listeners;
using SmartInspectConsole.Core.Packets;
using Xunit;

namespace SmartInspectConsole.Core.Tests;

/// <summary>
/// In-process listener tests for the production lock-up class of bugs:
/// if the console stops draining pipe/TCP, client writers block (and IIS request
/// threads freeze). These tests fail when sends hang past tight timeouts.
/// </summary>
public class ListenerDrainAndLockupTests
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TestOverall = TimeSpan.FromSeconds(45);

    [Theory]
    [InlineData(8, 50, 256)]
    [InlineData(16, 100, 1024)]
    public async Task Pipe_ConcurrentClients_WritesCompleteDespiteSlowConsumer(
        int clientCount,
        int packetsPerClient,
        int payloadBytes)
    {
        var pipeName = $"si-lockup-pipe-{Guid.NewGuid():N}";
        using var listener = new SmartInspectPipeListener(pipeName);

        // Slow handler simulates UI/backend backlog. Bounded queues must keep the
        // read loop draining so client WriteAsync does not hang.
        listener.PacketReceived += SlowPacketHandler;

        await listener.StartAsync();
        try
        {
            var received = 0;
            listener.PacketReceived += (_, e) =>
            {
                if (e.Packet is LogEntry)
                    Interlocked.Increment(ref received);
            };

            using var overallCts = new CancellationTokenSource(TestOverall);
            var clients = Enumerable.Range(1, clientCount)
                .Select(id => RunPipeClientAsync(
                    pipeName,
                    id,
                    packetsPerClient,
                    payloadBytes,
                    overallCts.Token))
                .ToArray();

            var results = await Task.WhenAll(clients);

            Assert.All(results, r => Assert.True(r.Success, r.Detail));
            Assert.All(results, r => Assert.True(r.MaxSendMs < SendTimeout.TotalMilliseconds,
                $"Client {r.ClientId} max send {r.MaxSendMs:F0}ms exceeded {SendTimeout.TotalMilliseconds:F0}ms — console likely stopped draining."));

            // ProcessPacketsAsync may still be draining after writers finish (slow handler).
            var target = clientCount * packetsPerClient;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            var last = -1;
            while (DateTime.UtcNow < deadline)
            {
                var current = Volatile.Read(ref received);
                if (current >= target * 0.5 && current == last)
                    break;
                last = current;
                await Task.Delay(50);
            }

            // Allow bounded-queue drops under intentional slow handler; still expect most traffic.
            var minExpected = (int)(target * 0.5);
            Assert.True(Volatile.Read(ref received) >= minExpected,
                $"Only {Volatile.Read(ref received)} LogEntry packets received; expected at least {minExpected} of {target}.");
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Theory]
    [InlineData(6, 40, 512)]
    public async Task Tcp_ConcurrentClients_WritesAndAcksCompleteDespiteSlowConsumer(
        int clientCount,
        int packetsPerClient,
        int payloadBytes)
    {
        // Ephemeral port: bind 0 then re-create listener is awkward; pick free port.
        var port = GetFreeTcpPort();
        using var listener = new SmartInspectTcpListener(port);
        listener.PacketReceived += SlowPacketHandler;

        await listener.StartAsync();
        try
        {
            using var overallCts = new CancellationTokenSource(TestOverall);
            var clients = Enumerable.Range(1, clientCount)
                .Select(id => RunTcpClientAsync(
                    port,
                    id,
                    packetsPerClient,
                    payloadBytes,
                    overallCts.Token))
                .ToArray();

            var results = await Task.WhenAll(clients);
            Assert.All(results, r => Assert.True(r.Success, r.Detail));
            Assert.All(results, r => Assert.True(r.MaxSendMs < SendTimeout.TotalMilliseconds,
                $"TCP client {r.ClientId} max send+ack {r.MaxSendMs:F0}ms — console may have stalled ACKs/drain."));
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Fact]
    public async Task Pipe_HalfOpenClient_DoesNotBlockOtherClients()
    {
        var pipeName = $"si-halfopen-{Guid.NewGuid():N}";
        using var listener = new SmartInspectPipeListener(pipeName);
        await listener.StartAsync();

        try
        {
            // Half-open: connect and read banner, but never send client banner.
            await using var halfOpen = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var connectCts = new CancellationTokenSource(ConnectTimeout);
            await halfOpen.ConnectAsync(connectCts.Token);
            await ReadUntilNewlineAsync(halfOpen, connectCts.Token);

            // Healthy clients must still connect and complete while half-open sits.
            using var overallCts = new CancellationTokenSource(TestOverall);
            var healthy = await Task.WhenAll(
                RunPipeClientAsync(pipeName, 1, 20, 128, overallCts.Token),
                RunPipeClientAsync(pipeName, 2, 20, 128, overallCts.Token),
                RunPipeClientAsync(pipeName, 3, 20, 128, overallCts.Token));

            Assert.All(healthy, r => Assert.True(r.Success, r.Detail));
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Fact]
    public async Task Pipe_BurstFlood_CompletesWithinBound()
    {
        // Large payloads + many clients: if the server stopped reading, writers hang
        // once OS pipe buffers fill (~64KB). Tight overall budget catches that.
        var pipeName = $"si-burst-{Guid.NewGuid():N}";
        using var listener = new SmartInspectPipeListener(pipeName);
        await listener.StartAsync();

        try
        {
            using var overallCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var sw = Stopwatch.StartNew();
            var results = await Task.WhenAll(
                Enumerable.Range(1, 12)
                    .Select(id => RunPipeClientAsync(pipeName, id, packetsPerClient: 80, payloadBytes: 4096, overallCts.Token)));

            sw.Stop();
            Assert.All(results, r => Assert.True(r.Success, r.Detail));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(25),
                $"Burst flood took {sw.Elapsed.TotalSeconds:F1}s — possible drain stall.");
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Fact]
    public async Task Pipe_SameAppNameConcurrentClients_AllDeliverDistinctClientTraffic()
    {
        // Production often reuses AppName; drain/accept must still handle N connections.
        var pipeName = $"si-sameapp-{Guid.NewGuid():N}";
        using var listener = new SmartInspectPipeListener(pipeName);
        var clientIdsSeen = new ConcurrentDictionary<string, byte>();
        var logCount = 0;

        listener.PacketReceived += (_, e) =>
        {
            clientIdsSeen[e.ClientId] = 0;
            if (e.Packet is LogEntry)
                Interlocked.Increment(ref logCount);
        };

        await listener.StartAsync();
        try
        {
            using var overallCts = new CancellationTokenSource(TestOverall);
            const int n = 8;
            const int packets = 30;
            var results = await Task.WhenAll(
                Enumerable.Range(1, n)
                    .Select(id => RunPipeClientAsync(pipeName, id, packets, 64, overallCts.Token, forceAppName: "SameApp")));

            Assert.All(results, r => Assert.True(r.Success, r.Detail));
            Assert.True(clientIdsSeen.Count >= n,
                $"Expected at least {n} distinct transport clientIds, saw {clientIdsSeen.Count}: {string.Join(", ", clientIdsSeen.Keys)}");
            Assert.True(logCount >= n * packets * 0.9,
                $"Expected ~{n * packets} log packets, saw {logCount}");
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    private static void SlowPacketHandler(object? sender, PacketReceivedEventArgs e)
    {
        // ~5ms stall per packet — enough to fill unbounded queues historically,
        // but must not block the network read loop with current design.
        Thread.Sleep(5);
    }

    private static async Task<ClientRunResult> RunPipeClientAsync(
        string pipeName,
        int clientId,
        int packetsPerClient,
        int payloadBytes,
        CancellationToken overallToken,
        string? forceAppName = null)
    {
        var sw = Stopwatch.StartNew();
        var stats = new SendStats();
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(overallToken))
            {
                connectCts.CancelAfter(ConnectTimeout);
                await pipe.ConnectAsync(connectCts.Token);
            }

            await ReadUntilNewlineAsync(pipe, overallToken);

            var banner = Encoding.ASCII.GetBytes($"CoreTestClient/{clientId}\n");
            await WriteWithTimeoutAsync(pipe, banner, overallToken, stats);

            var writer = new BinaryPacketWriter();
            var appName = forceAppName ?? $"core-client-{clientId}";
            var host = Environment.MachineName;
            var payload = new byte[payloadBytes];
            Random.Shared.NextBytes(payload);

            await SendPacketWithTimeoutAsync(writer, pipe, new LogHeader
            {
                Content = $"appname={appName}\r\nhostname={host}\r\n"
            }, overallToken, stats);

            for (var i = 0; i < packetsPerClient; i++)
            {
                overallToken.ThrowIfCancellationRequested();
                await SendPacketWithTimeoutAsync(writer, pipe, new LogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    LogEntryType = LogEntryType.Message,
                    ViewerId = ViewerId.Data,
                    AppName = appName,
                    SessionName = appName,
                    Title = $"{appName} seq-{i:D6}",
                    HostName = host,
                    Data = payload,
                    ProcessId = Environment.ProcessId,
                    ThreadId = Environment.CurrentManagedThreadId
                }, overallToken, stats);
            }

            await FlushWithTimeoutAsync(pipe, overallToken);
            return new ClientRunResult(clientId, true, stats.MaxSendMs, $"ok in {sw.Elapsed.TotalMilliseconds:F0}ms");
        }
        catch (Exception ex)
        {
            return new ClientRunResult(clientId, false, stats.MaxSendMs,
                $"{ex.GetType().Name}: {ex.Message} (maxSendMs={stats.MaxSendMs:F0}, elapsed={sw.Elapsed.TotalMilliseconds:F0}ms)");
        }
    }

    private static async Task<ClientRunResult> RunTcpClientAsync(
        int port,
        int clientId,
        int packetsPerClient,
        int payloadBytes,
        CancellationToken overallToken)
    {
        var sw = Stopwatch.StartNew();
        var stats = new SendStats();
        try
        {
            using var client = new TcpClient();
            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(overallToken))
            {
                connectCts.CancelAfter(ConnectTimeout);
                await client.ConnectAsync("127.0.0.1", port, connectCts.Token);
            }

            await using var stream = new BufferedStream(client.GetStream(), 8192);
            await ReadUntilNewlineAsync(stream, overallToken);

            var banner = Encoding.ASCII.GetBytes($"CoreTestTcp/{clientId}\n");
            await WriteWithTimeoutAsync(stream, banner, overallToken, stats);

            var writer = new BinaryPacketWriter();
            var appName = $"tcp-client-{clientId}";
            var host = Environment.MachineName;
            var payload = new byte[payloadBytes];
            Random.Shared.NextBytes(payload);
            var ack = new byte[2];

            await SendPacketWithTimeoutAsync(writer, stream, new LogHeader
            {
                Content = $"appname={appName}\r\nhostname={host}\r\n"
            }, overallToken, stats);
            await ReadExactlyWithTimeoutAsync(stream, ack, overallToken, stats);

            for (var i = 0; i < packetsPerClient; i++)
            {
                overallToken.ThrowIfCancellationRequested();
                await SendPacketWithTimeoutAsync(writer, stream, new LogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    LogEntryType = LogEntryType.Message,
                    ViewerId = ViewerId.Data,
                    AppName = appName,
                    SessionName = appName,
                    Title = $"{appName} seq-{i:D6}",
                    HostName = host,
                    Data = payload,
                    ProcessId = Environment.ProcessId,
                    ThreadId = Environment.CurrentManagedThreadId
                }, overallToken, stats);
                await ReadExactlyWithTimeoutAsync(stream, ack, overallToken, stats);
            }

            return new ClientRunResult(clientId, true, stats.MaxSendMs, $"ok in {sw.Elapsed.TotalMilliseconds:F0}ms");
        }
        catch (Exception ex)
        {
            return new ClientRunResult(clientId, false, stats.MaxSendMs,
                $"{ex.GetType().Name}: {ex.Message} (maxSendMs={stats.MaxSendMs:F0}, elapsed={sw.Elapsed.TotalMilliseconds:F0}ms)");
        }
    }

    private static async Task SendPacketWithTimeoutAsync(
        BinaryPacketWriter writer,
        Stream stream,
        Packet packet,
        CancellationToken overallToken,
        SendStats stats)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(overallToken);
        cts.CancelAfter(SendTimeout);
        await writer.WritePacketAsync(stream, packet, cts.Token);
        await stream.FlushAsync(cts.Token);
        sw.Stop();
        stats.Note(sw.Elapsed.TotalMilliseconds);
    }

    private static async Task WriteWithTimeoutAsync(Stream stream, byte[] data, CancellationToken overallToken, SendStats stats)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(overallToken);
        cts.CancelAfter(SendTimeout);
        await stream.WriteAsync(data, cts.Token);
        await stream.FlushAsync(cts.Token);
        sw.Stop();
        stats.Note(sw.Elapsed.TotalMilliseconds);
    }

    private static async Task FlushWithTimeoutAsync(Stream stream, CancellationToken overallToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(overallToken);
        cts.CancelAfter(SendTimeout);
        await stream.FlushAsync(cts.Token);
    }

    private static async Task ReadExactlyWithTimeoutAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken overallToken,
        SendStats stats)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(overallToken);
        cts.CancelAfter(SendTimeout);
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cts.Token);
            if (read == 0)
                throw new IOException("Connection closed waiting for ACK.");
            offset += read;
        }
        sw.Stop();
        stats.Note(sw.Elapsed.TotalMilliseconds);
    }

    private static async Task ReadUntilNewlineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                throw new IOException("Connection closed before newline.");
            if (buffer[0] == (byte)'\n')
                return;
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed record ClientRunResult(int ClientId, bool Success, double MaxSendMs, string Detail);

    private sealed class SendStats
    {
        public double MaxSendMs { get; private set; }

        public void Note(double elapsedMs)
        {
            if (elapsedMs > MaxSendMs)
                MaxSendMs = elapsedMs;
        }
    }
}
