# Changelog

All notable changes to SmartInspect Console are documented here.

## 1.0.1.19 — 2026-07-20

### Tests

- Added **in-process** `SmartInspectConsole.Core.Tests` lock-up suite: concurrent pipe/TCP writers with slow consumers, half-open banner client, burst flood, same-AppName multi-client.
- Extended live pipe integration tests with **render-paused concurrent flood** and explicit timeout/drain-stall failure detection.
- `PipeTestClient` supports `--payload-bytes` and `--send-timeout-ms` and reports `maxSendMs`.
- `BinaryPacketWriter.WritePacketAsync` for cancellable sends in tests and tools.

## 1.0.1.18 — 2026-07-20

### UI

- Tightened the **Connections** panel row layout (fixed ~22px rows, smaller status/mute controls) so multiple clients are easier to scan without large gaps between apps.

## 1.0.1.17 — 2026-07-20

### Bug fix — pipe ACL create storm

**Flaw:** Opening the open-to-all named-pipe ACL could fail (`UnauthorizedAccessException: Access to the path is denied`). With 64 concurrent acceptors, each attempt reported errors into the live log grid, flooding the console with hundreds of red `Console` / `[Console]` lines and drowning real application traffic.

**Fix:**

- Serialize named-pipe instance creation under a lock.
- Decide open-ACL vs current-user fallback **once** per listener lifetime.
- Do not spam `OnError` / log grid for Access Denied or ACL fallback; keep a single quiet `LastError` note when fallback is required.
- Prefer full-control open ACL when available so multi-instance waiters can be created after the first.

## 1.0.1.16 — 2026-07-20

### Critical — client hang / website freeze when the console stops draining

**Flaw:** SmartInspect clients (especially pipe, also TCP) often log on the calling thread. If the console stopped reading or left half-open connections, the OS pipe/TCP buffer filled, client `Log*` calls blocked, and **IIS/website request threads could freeze**. Shutting down the console unblocked the site immediately — classic consumer-side backpressure.

**Root causes addressed:**

- Handshake / banner read with **no timeout** (half-open instances could occupy accept slots).
- Packet reads without cancellation (mid-packet stall).
- Unbounded per-client parse/UI queues under load (memory pressure; risk of unhealthy drain).
- Small pipe buffers.
- Corrupt / huge declared packet sizes (`new byte[size]`) with no upper bound.
- TCP ACK write without a timeout (client waits for ACK).

**Fix:**

- **5s handshake timeout**; disconnect bad half-open clients.
- **30s mid-packet payload timeout** (no idle timeout while waiting for the next quiet log — long-lived clients stay connected).
- **Bounded per-client packet queue (drop oldest)** so UI lag never stops the read loop.
- Larger pipe I/O buffers (64 KB).
- **Max packet payload 16 MB** (reject corrupt sizes).
- TCP still ACKs **before** parse/UI, with ACK write timeout.
- Immediate close of all clients on console stop so apps unblock quickly.

### Multi-client accept and identity

**Flaw (production):** Console often showed only ~1 pipe + ~1 TCP while the original SmartInspect Console showed 8–10 clients. Two separate problems:

1. **Pipe security regression** — open multi-user ACL had been removed; default security only allowed the console’s Windows user. IIS app pools / services under other identities could not connect (common for multiple websites).
2. **Identity collapse** — UI and backend merged concurrent clients by `AppName@HostName`, so several live processes with the same app name appeared as one row.

**Fix:**

- Restored open local pipe ACL (world read/write / full control) with fallback if ACL create is blocked.
- Track **one connection / application row per transport `clientId`**; only merge **disconnected** reconnect stubs, not live concurrent instances.
- Backend list/count by `clientId`; mute still groups by `AppName@HostName`.

## Deploy tooling (unversioned package, same release window)

- Cake-based deploy under `deploy/`:
  - `deploy\c.ps1` — SureCourt FTP package (`si-deploy/si-c`)
  - `deploy\g.ps1` — CC3 production FTP package (`si-deploy/si-g`)
- Flow: `dotnet publish` self-contained win-x64 → zip → FTP upload + server `copy-c.cmd` / `copy-g.cmd`.
- Server install target: `C:\Tools\SmartInspectConsole\current\`.
- Server copy scripts exit immediately (no `pause`, no artificial delay after `taskkill`).
- FTP secrets stay local (`deploy/secrets/deploy.*.json` gitignored; templates committed).

## Earlier history

See git history prior to 1.0.1.16 for pipe hardening, render pause, MCP/local API, and UI features.
