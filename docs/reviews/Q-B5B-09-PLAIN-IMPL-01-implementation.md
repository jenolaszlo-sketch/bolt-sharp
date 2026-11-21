# Q-B5B-09-PLAIN-IMPL-01 — Producer implementation report

**IMPLEMENTED — READY FOR INDEPENDENT QUALIFICATION**

The implementation applies only to an authenticated unsupported-command refusal on a configured plaintext listener. The session retires the operation, enters Defunct, and marks the original FAILURE as eligible. The connection retires the Bolt reader and uses the original response codec and writer for one terminal response. Once write and flush finish, the connection attempts immediate drain admission, shuts down the send side if admitted, and discards queued bytes through its owned cleartext stream. No discarded data goes to the Bolt parser or command provider.

## Repository and change inventory

| Component | Commit | Tree |
| --- | --- | --- |
| Starting owner baseline | `fac10f6c5ece37dbc3f38c9c831c668d49a3d290` | `d464e85f810b50a00f768a9111dcc2a7f60cd699` |
| Typed linger prerequisite | `dac52d55ade0a9ae703b801c8a4066e8aef2bd58` | `dcc1687c6c8fe8dd6b55863f7324e5c64c478416` |
| Plaintext implementation and tests | `20fbc461c0257620db28fb78a463218b242455bc` | `116ee015f1ae9a30bf991c2db470996db47996c0` |

Preflight verified clean `main`, one worktree, zero stashes, and 10 ahead and 0 behind local `origin/main` at `79f7965a0e8e351baf2dd388218290804c31389a`. The owner package verified all seven blobs, the exact TLS-GATE-01 parent and its prior-evidence replay. The implementation and linger commits are direct successors and touch only the approved paths.

| Change | Paths |
| --- | --- |
| Portable linger and startup tests | `BoltTcpServer.cs`, `TcpListenerTests.cs`, `TlsIntegrationTests.cs` |
| Internal result classification | `BoltConnectionSession.cs`, `BoltSessionResult.cs` |
| Selection, owner cancellation and accounting | `BoltTcpConnection.cs`, `BoltTcpServer.cs` |
| Bounded lifecycle and internal events | `BoltPlaintextTerminalClose.cs`, `BoltPlaintextTerminalCloseDiagnostics.cs` |
| Production-path tests | `PlaintextTerminalCloseTests.cs`, `PlaintextTerminalCloseInteropTests.cs` |
| Producer documentation and evidence | This report and its `Q-B5B-09-PLAIN-IMPL-01` evidence directory in a separate commit |

## Specification compliance

| Requirement | Implementation and producer verification |
| --- | --- |
| Session-owned classification and exact wire behavior | Authenticated Ready, Streaming and Failed share the established unsupported-command refusal. TS1's standalone byte oracle remains untouched. New loopback cases assert the same wire bytes and EOF for standalone, small, large, partial, malformed and delayed tails. Independent response, parser/message and provider checks cover extra responses and queued commands. |
| Deadline and cancellation | A 250 ms monotonic budget starts before serialization. The write phase respects the smaller of existing `WriteTimeout` and remaining total time. Drain uses the smaller of 75 ms and the remaining budget. Listener, owner, phase and total cancellation meet at the connection owner. The first interruption records its reason, cancels active I/O and closes the socket. The code awaits actual completion and settles callbacks before releasing any permit. |
| Write, half-close and discard | Existing framing and local write/flush precede `MessageProcessed`. Admission is a zero-wait permit check. The admitted connection sends FIN with send-side shutdown, then uses a single 2 KiB scratch allocation and reads at most 8 KiB total. Write failure, cancellation or unavailable admission bypasses drain. |
| Cleanup and accounting | Idempotent socket disposal precedes permit release. Admission remains occupied through task retirement. Tests check real safe-handle closure, active and terminal counts, permit recovery, and a healthy client during pressure. Listener stop uses the configured `ShutdownTimeout` across accept-loop quiescence and owner joins. A controlled accept/stop race checks that a newly accepted owner remains accounted for. |
| Observable fallback | Internal events carry correlation ID, phase, elapsed time, bytes, admission, and bounded reason codes. The tests distinguish EOF, reset, byte limit, time limit, capacity, owner cancellation and listener shutdown. Unexpected I/O errors remain connection failures. The terminal event excludes application and peer data. |
| TLS isolation | Explicit configuration gates both construction and entry. Eligible TLS refusal takes the existing encrypted writer and disposal path with zero plaintext helper events. The inherited TLS suite covers authentication, encrypted traffic, shutdown and fail-closed behavior. The driver comparison reproduces refusal classification and pooled replacement in both modes. |
| Portable disabled linger | `LingerState = new LingerOption(false, 0)` replaces the boolean setter before listener start. Startup errors still propagate. Windows had accepted the old setter, while INV-03 recorded WSL2 `EINVAL` (22). Both production startup modes now pass on both platforms. An isolated readback probe verifies disabled linger and zero time on the listener and the actual accepted sockets in both modes. The production runs use the committed setter without an adapter. |
| Integrated regressions and protected scope | Windows and WSL2 each passed 1,250 core and 43 pinned driver tests. Production, solution and driver builds and scoped formatting passed. Existing tests, projects, dependencies, public options, owner decisions and historical evidence were preserved. Only the two startup assertions were added to existing test files. |

## Bounds and measured transport behavior

The proposed defaults use 250 ms total and one drain per listener, clamped to `MaxConnections - 1` and therefore zero at `MaxConnections = 1`. The 75 ms discard phase and 8 KiB cap retain the measured mechanism with at most 2 KiB of discard scratch per listener. The 250 ms deadline preserved the Windows queued-tail improvement while shortening maximum retention from the 300 ms trial. One permit preserved healthy-client progress in the laboratory and halves the prototype's maximum discard scratch and simultaneous drain ownership. Cases exercised zero, one and three contenders, with no admission wait and recovery on the next eligible connection. This verifies enforcement at laboratory scale. It does not establish an optimal host-wide budget, workload throughput, or production headroom. Framing allocations and kernel socket buffers are separate from the discard scratch cap. Multiple listeners each have their own budget. The public defaults remain 30 seconds for writes and 5 seconds for listener shutdown.

| Platform | Linger-only immediate reader | Linger-only delayed reader | Implemented immediate and delayed readers |
| --- | --- | --- | --- |
| Windows | Exact FAILURE then reset | No exact FAILURE, reset | Exact FAILURE and orderly EOF in both |
| Ubuntu WSL2 | Exact FAILURE and EOF | Exact FAILURE and EOF | Exact FAILURE and orderly EOF in both |

These are loopback observations. Local write completion and send-side shutdown describe local transport progress. Peer receipt depends on the peer and network. The archived comparison harness removes only the candidate receipt assertions for observations against the linger-only source.

Real pending terminal writes were separately traced after the earlier PULL response completed. Each sample persisted through the observation window. The test then checks that the original pending await settles, that no terminal `MessageProcessed` is emitted, that no permit was taken, and that the handle and connection accounting settle.

| Platform | Total deadline | Connection-owner cancellation | Listener cancellation |
| --- | ---: | ---: | ---: |
| Windows | 253.91 ms | 38.18 ms | 27.57 ms |
| Ubuntu WSL2 | 250.93 ms | 25.93 ms | 21.32 ms |

Elapsed values start at entry to the terminal lifecycle and include OS and scheduler latency. The pending-write cases also independently assert prompt signal-to-cleanup after owner or listener cancellation. The deadline test spends 205 ms at the write diagnostic boundary and verifies the entire lifecycle still uses its original timer. Pending-drain stop took 0.19 ms on Windows and 0.14 ms on WSL2 in the final run. Synchronous provider retirement keeps its established contract. A synchronous in-process diagnostic observer must return before its invoking task can retire, while the timer can still interrupt and close the socket.

## Seven integrated negative controls

The Windows controls started from the same tested production source, used disposable copies, and restored byte-identical source around every mutation. Their original 20-case unmodified run passed. The final unchanged campaign also includes the added stop race, refined pressure fixtures and formatting. The evidence contains filters, mutation records and raw failing TRX runs.

| Control | Detection | Independent observation |
| --- | --- | --- |
| NC-01 Trailing-command execution | 5 failed of 6 | Small and delayed tails reached provider preparation once with no additional reply. |
| NC-02 Socket retention | 1 failed of 1 | The handle remained physically open after stop even though accounting completed. |
| NC-03 Unlinked listener cancellation | 1 failed of 1 | The observed cause was drain deadline instead of listener shutdown. |
| NC-04 Renewed deadline | 1 failed of 1 | The original budget oracle detected 478.13 ms against its 205–370 ms window. |
| NC-05 Drain byte limit | 1 failed of 6 | The large case discarded 16,384 bytes against the 8,192 byte cap. |
| NC-06 Drain concurrency limit | 1 failed of 3 | Two simultaneous drains were admitted against the one-permit limit. |
| NC-07 Incorrect TLS mode | 1 failed of 1 | TLS emitted plaintext helper events after incorrect mode selection. The isolated mutant stopped before any raw read. |

## Platforms, regressions and release gates

The Windows Debug `net8.0` tests used SDK 10.0.401 and .NET 8.0.31. Ubuntu WSL2 kernel `6.6.114.1-microsoft-standard-WSL2` ran the portable Debug `net8.0` assemblies on Linux .NET 10.0.14 via explicit major roll-forward. Windows and WSL2 each reported zero failures and zero skips in the final runs. Core includes terminal refusal, multi-client, transport, sessions, messages, backend, TLS, and the two listener-startup checks. Driver interop pins `Neo4j.Driver 6.4.0`. Production and driver builds had no warnings. The solution build retained exactly two existing `xUnit2012` warnings in `BoltRequestDecodeTests.cs`. The archive retains the earlier WSL2 run with four fixture failures, the subsequent targeted pass and the final runs. Refinements were confined to the new test's finite, platform-specific backpressure profiles and abortive peer reset fixture.

Native Linux and representative production capacity remain explicit release gates. No authorized native Linux environment was available for this campaign. The available Ubuntu was a WSL2 distribution. Independent production qualification and owner disposition remain pending. B5-B-TS1 and Q-B5B-03 remain CLOSED. Q-B5B-04, Q-B5B-05 and Q-B5B-09 remain OPEN. TLS graceful-close remediation remains DEFERRED.

The recommended next commission is **Q-B5B-09-PLAIN-Q1 — Independent Production Qualification** in a fresh reviewer session pinned to the implementation and linger commits above. The producer stopped at handoff. No push or publication occurred.

[Binding implementation specification](../decisions/Q-B5B-09-PLAIN-IMPL-01-implementation-specification.md) · [Evidence and reproduction commands](evidence/Q-B5B-09-PLAIN-IMPL-01/README.md)
