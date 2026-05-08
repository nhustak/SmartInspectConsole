# Tests

This directory contains the named-pipe concurrency test harness for SmartInspect Console.

## Projects

### `SmartInspectConsole.PipeTestClient`

A small console executable. One process = one named-pipe connection. It connects, performs the SmartInspect banner handshake, sends a `LogHeader` plus N deterministic `LogEntry` packets, then exits. It is the "small app" used to simulate a real client.

CLI:

```
SmartInspectConsole.PipeTestClient.exe
  --pipe-name <name>           Named pipe to connect to. Default: smartinspect
  --client-id <int>            Unique numeric identifier (required)
  --count <int>                Number of LogEntry packets to send. Default: 100
  --connect-timeout-ms <int>   ConnectAsync timeout in ms. Default: 10000
  --overall-timeout-ms <int>   Hard upper bound on the whole run. Default: 60000
  --help                       Show help
```

Each `LogEntry` it sends is deterministic, which is what makes corruption detectable on the receiving side:

| Field         | Value                                           |
|---------------|-------------------------------------------------|
| `AppName`     | `client-{id}`                                   |
| `SessionName` | `client-{id}`                                   |
| `Title`       | `client-{id} seq-{i:D6}` for `i` in `[0, count)` |
| `Data`        | ASCII bytes of `client-{id}/seq-{i}`            |
| `ProcessId`   | `Environment.ProcessId` of the subprocess       |

Exit codes: `0` on success, `1` on any failure (with the exception written to stderr).

### `SmartInspectConsole.Pipe.IntegrationTests`

xUnit project. Spawns N subprocesses of `PipeTestClient` simultaneously against the real running SmartInspect Console named pipe (`smartinspect`) and asserts on what the console reports through its in-process MCP endpoint at `http://127.0.0.1:42331/mcp`.

**Important:** the test now requires a real SmartInspect Console process to already be running. It does not start a private in-process listener and it does not skip when the console is unavailable. If MCP cannot be reached, or the pipe listener is not enabled as `pipe://smartinspect`, the test fails with a setup message.

The single test method is parameterized with `[InlineData(clientCount, packetsPerClient)]`:

```csharp
[InlineData(5, 100)]
public async Task LiveConsoleConcurrentClients_AllPacketsArriveUncorrupted(int clientCount, int packetsPerClient)
```

For each row it asserts:

- Every subprocess exited with code 0.
- The live console MCP endpoint is reachable.
- The live console pipe listener is enabled at `pipe://smartinspect`.
- Exactly `clientCount` distinct test `AppName` values were seen through MCP, one per subprocess.
- For each subprocess: exactly `packetsPerClient` `LogEntry` packets received.
- For each subprocess: titles cover `seq-000000..seq-{N-1:D6}` exactly once each, in any order (catches drops and duplicates).
- For each packet: `Data` bytes equal the expected `{uniqueAppName}/seq-{seqFromTitle}` exactly. **This is the cross-connection corruption check** — if a shared mutable parser ever interleaved between two connections, one client's payload bytes would surface inside another client's parsed packet, and this assertion would catch it.

Each test run uses a unique `pipe-live-{Guid:N}` app/session prefix so MCP queries do not match retained logs from earlier runs. When the test fails, the output reports each failing subprocess's exit code and stderr, MCP live context before/after the run, and concrete examples of any packet anomalies (missing seqs, malformed titles, cross-client titles, payload mismatches).

## Building and running

```powershell
# Build everything (from the repo root)
dotnet build SmartInspectConsole.slnx -c Debug

# Run the integration tests. SmartInspectConsole must already be running.
dotnet test tests\SmartInspectConsole.Pipe.IntegrationTests --logger "console;verbosity=detailed"
```

The test class resolves `PipeTestClient.exe` at runtime by walking up from its own bin folder to the sibling project's bin folder. Both projects target `net10.0-windows`.

## Observed results

Captured against this branch on Windows 11 (run twice; results stable):

| Clients | Packets/client | Result | Connected | LogEntry packets received | Listener `Error` events |
|--------:|---------------:|--------|----------:|--------------------------:|------------------------:|
| 1       | 100            | PASS   | 1 / 1     | 100                       | 0 |
| 4       | 500            | PASS   | 4 / 4     | 2,000                     | 0 |
| 10      | 500            | PASS   | 10 / 10   | 5,000                     | 0 |
| 25      | 500            | FAIL   | 1–2 / 25  | 500–1,000                 | 0 |

**Failure mode at 25 clients:** 23–24 of 25 subprocesses exit with code 1 and stderr `TimeoutException: The operation has timed out.` from `System.IO.Pipes.NamedPipeClientStream.ConnectInternal`. These are **client-side connect timeouts**, not server-side parsing errors. The listener's `ClientConnected` event count matches the number of subprocesses that got through; everyone else never connected.

The whatever-arrived packets are intact: titles parse, sequences are contiguous, `Data` payloads match. No `Error` events on the listener. So the `BinaryPacketReader` shared-state race that's structurally present in [SmartInspectPipeListener.cs](../src/SmartInspectConsole.Core/Listeners/SmartInspectPipeListener.cs) (one shared `_packetReader` field used by all connections) is not what manifests here — the bottleneck is upstream of parsing.

The implicated production setting is [`PendingAcceptors = 4`](../src/SmartInspectConsole.Core/Listeners/SmartInspectPipeListener.cs#L21). Only four `NamedPipeServerStream` instances are ever in `WaitForConnectionAsync` simultaneously. Between 10 and 25 simultaneous connect attempts, the OS-level retry-on-busy on the client side runs out before an acceptor frees up.

## Driving the live console (manual)

To send traffic to the actual running SmartInspect Console GUI outside xUnit, run the client directly against the `smartinspect` pipe:

```powershell
$exe = 'tests\SmartInspectConsole.PipeTestClient\bin\Debug\net10.0-windows\SmartInspectConsole.PipeTestClient.exe'

# Single client, watch the UI
& $exe --pipe-name smartinspect --client-id 1 --count 100

# 10 simultaneous clients (this works against the live console)
1..10 | ForEach-Object {
    Start-Process -FilePath $exe -ArgumentList @(
        '--pipe-name','smartinspect',
        '--client-id',"$_",
        '--count','200'
    ) -WindowStyle Hidden
}
```

A 25-client simultaneous spawn against the live console reproduces the same connect-timeout symptom seen in the integration test (most clients fail with `TimeoutException`).

## File layout

```
tests/
  README.md                                   This file
  SmartInspectConsole.PipeTestClient/
    SmartInspectConsole.PipeTestClient.csproj
    Program.cs                                Small client app
  SmartInspectConsole.Pipe.IntegrationTests/
    SmartInspectConsole.Pipe.IntegrationTests.csproj
    PipeListenerConcurrencyTests.cs           xUnit theory test
```

## Related code

- Listener under test: [src/SmartInspectConsole.Core/Listeners/SmartInspectPipeListener.cs](../src/SmartInspectConsole.Core/Listeners/SmartInspectPipeListener.cs)
- Packet parsing (single shared instance in the listener): [src/SmartInspectConsole.Core/Parsing/BinaryPacketReader.cs](../src/SmartInspectConsole.Core/Parsing/BinaryPacketReader.cs)
- Wire format (also used by the test client): [src/SmartInspectConsole.Core/FileIO/BinaryPacketWriter.cs](../src/SmartInspectConsole.Core/FileIO/BinaryPacketWriter.cs)
- Existing in-process load harness (different scope, same wire format): [src/SmartInspectConsole.LoadTester/](../src/SmartInspectConsole.LoadTester/)
