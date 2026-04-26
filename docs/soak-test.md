# SmartInspect Full-Stack Soak Test

The repo now includes a reusable soak harness at [tools/soak/run-soak.ps1](C:\Project\Utility\SmartInspectConsole\tools\soak\run-soak.ps1).

It runs these legs in sequence:

1. TCP listener soak with `SmartInspectConsole.LoadTester`
2. Named-pipe listener soak with `SmartInspectConsole.LoadTester`
3. Direct WebSocket/browser soak with the `smartinspect-js` client
4. Standalone relay soak over HTTP, forwarding into the desktop app over WebSocket
5. Mixed traffic soak across all four paths together

## What It Captures

- local API snapshots from `http://127.0.0.1:42331/api/local/v1/context/live`
- relay health and status from `/api/v1/health` and `/api/v1/status`
- per-process stdout/stderr logs for every leg
- one NDJSON event stream for the whole run
- per-leg summaries and a run summary

Outputs are written under `artifacts\soak\soak-<timestamp>`.

## Default Run

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\soak\run-soak.ps1
```

Defaults:

- `2` hours per leg
- `60` second snapshot interval
- `30` second drain window
- queue drain threshold `50`
- relay buffer drain threshold `25`

## Short Qualification Run

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\soak\run-soak.ps1 `
  -DurationSeconds 120 `
  -SampleIntervalSeconds 15 `
  -DrainSeconds 10
```

## Useful Overrides

- `-SkipTcp`
- `-SkipPipe`
- `-SkipWebSocket`
- `-SkipRelay`
- `-SkipMixed`
- `-RelayBaseUrl http://127.0.0.1:5109`
- `-LocalQueueDrainThreshold 100`
- `-RelayBufferDrainThreshold 50`

## Failure Conditions

The harness fails the run when any of these happen:

- a load generator or relay process exits non-zero
- TCP/pipe send or ack operations exceed the bounded timeout in `SmartInspectConsole.LoadTester`
- the desktop local API stops responding for three consecutive samples
- the relay health/status endpoints fail for three consecutive samples
- the relay stays disconnected for three consecutive samples during relay-driven legs
- queue depth stays above the drain threshold after a leg completes
- relay buffered message count stays above the drain threshold after a relay leg completes
