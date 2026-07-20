# Tests

Harnesses for SmartInspect Console, including **lock-up / drain-stall** regressions (clients hang when the console stops reading pipe/TCP).

## Projects

| Project | Purpose | Needs live GUI? |
|---------|---------|-----------------|
| `SmartInspectConsole.Core.Tests` | In-process pipe/TCP listener lock-up, multi-client, half-open, burst flood | **No** |
| `SmartInspectConsole.Pipe.IntegrationTests` | Live console multi-client integrity + paused-render lock-up via MCP | **Yes** |
| `SmartInspectConsole.PipeTestClient` | One-process = one pipe client (used by live integration tests) | Against console |

## What was broken (and what we assert now)

Production symptom: website/IIS froze while SmartInspect Console was running; killing the console unblocked the site.

**Cause class:** client `Log*` writes block when the console stops draining the OS pipe/TCP buffer (half-open handshakes, stalled reads, unbounded consumer backlog, multi-client accept issues).

### Core.Tests (`ListenerDrainAndLockupTests`) — primary lock-up coverage

| Test | Asserts |
|------|---------|
| `Pipe_ConcurrentClients_WritesCompleteDespiteSlowConsumer` (8×50, 16×100) | Concurrent pipe writers finish with **per-send &lt; 3s** even if `PacketReceived` sleeps (slow UI). Stall ⇒ fail. |
| `Tcp_ConcurrentClients_WritesAndAcksCompleteDespiteSlowConsumer` | Same for TCP including **ACK** wait. |
| `Pipe_HalfOpenClient_DoesNotBlockOtherClients` | Client that never finishes banner does not prevent other clients completing. |
| `Pipe_BurstFlood_CompletesWithinBound` | 12 clients × 80 packets × 4KB complete in &lt; 25s (buffer-fill hang would timeout). |
| `Pipe_SameAppNameConcurrentClients_AllDeliverDistinctClientTraffic` | 8 concurrent connections with **same AppName** all deliver; distinct transport `clientId`s. |

### Live integration (`PipeListenerConcurrencyTests`)

| Test | Asserts |
|------|---------|
| `LiveConsoleConcurrentClients_AllPacketsArriveUncorrupted` (5 &amp; 25 clients) | All subprocesses exit 0; no TimeoutException; packets uncorrupted; distinct AppNames. |
| `LiveConsoleConcurrentClients_DoNotHangWhileRenderPaused` | 12 clients × 150 × 2KB while **render paused**; tight overall timeout; no client hang. |
| `LiveConsoleRenderPause_CanBeControlledThroughMcp` | MCP pause/resume control. |

### PipeTestClient options (lock-up relevant)

```
--payload-bytes <int>      Larger payloads fill pipe buffers faster if server stops reading
--send-timeout-ms <int>    Per write/flush timeout (default 5000) — hangs surface as TimeoutException
--overall-timeout-ms <int> Whole-run budget
--connect-timeout-ms <int> Connect budget
```

Stdout on success includes `maxSendMs` for manual diagnosis.

## Building and running

```powershell
# From repo root
dotnet build SmartInspectConsole.slnx -c Debug

# In-process lock-up suite (no GUI required) — run this first
dotnet test tests\SmartInspectConsole.Core.Tests --logger "console;verbosity=normal"

# Live console suite (SmartInspectConsole must be running with pipe + MCP)
dotnet test tests\SmartInspectConsole.Pipe.IntegrationTests --logger "console;verbosity=detailed"
```

## Related load / soak tools

- `src/SmartInspectConsole.LoadTester` — sustained TCP/pipe load with operation timeouts (manual / soak).
- `tools/soak/run-soak.ps1` — multi-leg long soak; fails if load-tester send/ack timeouts fire.

These complement unit/integration tests but are not a substitute for the Core lock-up suite.

## File layout

```
tests/
  README.md
  SmartInspectConsole.Core.Tests/
    ListenerDrainAndLockupTests.cs
  SmartInspectConsole.PipeTestClient/
    Program.cs
  SmartInspectConsole.Pipe.IntegrationTests/
    PipeListenerConcurrencyTests.cs
```

## Related product code

- [SmartInspectPipeListener.cs](../src/SmartInspectConsole.Core/Listeners/SmartInspectPipeListener.cs) — multi-acceptor, open ACL, handshake timeout, bounded queue
- [SmartInspectTcpListener.cs](../src/SmartInspectConsole.Core/Listeners/SmartInspectTcpListener.cs) — ACK-before-parse, timeouts
- [BinaryPacketWriter.cs](../src/SmartInspectConsole.Core/FileIO/BinaryPacketWriter.cs) — `WritePacketAsync` for cancellable sends
- [CHANGELOG.md](../CHANGELOG.md) — 1.0.1.16+ lock-up / multi-client notes
