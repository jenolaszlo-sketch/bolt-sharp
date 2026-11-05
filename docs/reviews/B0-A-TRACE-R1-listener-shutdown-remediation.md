# B0-A-TRACE-R1 — Deterministic listener shutdown remediation

**Status:** COMPLETE — MAJOR finding `TRACEQ1-F01` remediated; ready for targeted independent
requalification (B0-A-TRACE-Q2)
**Date:** 2026-11-05
**Task:** B0-A-TRACE-R1 (remediation of `TRACEQ1-F01`), authorized after
[B0-A-TRACE-Q1](B0-A-TRACE-Q1-independent-qualification.md)
**Canonical branch:** `main`
**Baseline commit/tree:** `f24afb79ddf9737583e17887e2874c0e781ab354` /
`0ced7b6d95e02db24c16567888ddb8d827609c84`
**Scope:** the test-only loopback handshake listener only. No production TCP transport, protocol
session, authentication, query processing or B3 work was performed, and no qualification or
acceptance is claimed here.

---

## 1. Baseline and exclusive-writer gate

Entry `main` was at `f24afb7` / tree `0ced7b6`, one worktree, working tree and index clean. Two
gate snapshots 95 s apart (16:33:21 / 16:37:04 UTC) recorded HEAD/tree pinned, zero status entries,
and **no repository writes for 10+ minutes**; the `dotnet` host processes present at 16:32:52 were
unchanged across both snapshots (static build servers, no `testhost`), so no competing writer was
modifying the checkout. No unexpected state needed preservation.

## 2. Original failure reproduction (R1)

The defect was reproduced **before** any code change, deterministically, through the producer's own
listener (evidence: `docs/reviews/evidence/B0-A-TRACE-R1/pre-fix-regression.txt`,
`pre-fix-failure-excerpt.txt`; 10 new lifecycle tests, **5 failed / 5 passed** pre-fix).

| New test | Pre-fix result | Observed failure |
| --- | --- | --- |
| `R1_02_EndpointCapture_WithClosedSocket_IsUnknown_NotNre` | FAIL | `NullReferenceException` at `SafeRemoteEndPoint` (line 657) — deterministic, via reflection with a closed `TcpClient` |
| `R1_10_Handler_WithAlreadyClosedClient_DoesNotFault` | FAIL | `NullReferenceException` at `SafeRemoteEndPoint` (657) → `HandleConnectionAsync` (308) — deterministic, real handler invoked with a closed client |
| `R1_08_AfterDisposal_PortIsClosed_AndNoPendingTasks` | FAIL | same `NullReferenceException` escaping through `DisposeAsync` (race) |
| `R1_09_AcceptDisposeRace_Repeated_NoUnhandledFailure` | FAIL | **`ObjectDisposedException` at `HandleConnectionAsync` line 318 (`client.GetStream()`)** — a second, sibling prologue escape reproduced by the accept/dispose race |
| `R1_03_DisposalDuringActiveConnection_IsClean` | FAIL | trace classified `ERROR` for a socket closed mid-read (no `ObjectDisposedException` handling) |

The pre-existing historical reviewer probe T10 and the original preserved stack trace
(`docs/reviews/evidence/B0-A-TRACE-Q1/nre-observation/`) are **unchanged**; this remediation only
adds new tests.

## 3. Root-cause analysis

`HandleConnectionAsync` performed several operations **outside** its `try` block:

```text
line 308-313  trace construction  →  SafeRemoteEndPoint(client)      (client.Client may be null)
line 315-316  lifetime CTS
line 318      client.GetStream()                                     (throws if already disposed)
line 319-322  tracing stream
line 323-324  handshake timeout CTS
line 326+     try { negotiation and observation } ...
```

`DisposeAsync` closes the clients captured in `_clients` while a just-accepted handler may still be
in its prologue. `TcpClient.Close()` clears the underlying socket, so:

1. `SafeRemoteEndPoint` dereferenced `client.Client.RemoteEndPoint` → `NullReferenceException`.
2. `client.GetStream()` (and `client.NoDelay`) throw `ObjectDisposedException`/`NullReferenceException`.

Because these calls sat outside the `try`, the exception faulted the **handler task**, and
`DisposeAsync` surfaced it through `Task.WhenAll(handlers)` — violating the deliverable's
deterministic-shutdown requirement. Mid-operation `ObjectDisposedException` (a socket closed while
being read) likewise fell to the generic handler and was mislabelled `ERROR`.

Only the first manifestation was reported in B0-A-TRACE-Q1; this remediation also reproduced and
fixes the `GetStream`/read siblings of the same root cause.

## 4. Implementation correction (R2)

Minimal, lifecycle-focused change in `LoopbackHandshakeListener.cs`:

1. **Endpoint captured before the socket can close.** The accept loop now reads
   `SafeRemoteEndPoint(client)` immediately after `AcceptTcpClientAsync` (client guaranteed alive)
   and passes the string to `HandleConnectionAsync(int, TcpClient, string)`. Trace construction no
   longer touches the socket.
2. **`SafeRemoteEndPoint` is null-safe:** `client.Client?.RemoteEndPoint?.ToString() ?? "UNKNOWN"`
   (remote endpoint is diagnostic data and is never assumed available after disposal).
3. **All socket access moved inside the `try`**, with `lifetime`/`handshakeTimeout`/`tracing`
   declared nullable before it (the catch filters and `finally` need them) and disposed in `finally`.
4. **A narrow acquisition guard** classifies `ObjectDisposedException | SocketException |
   NullReferenceException | InvalidOperationException` from `NoDelay`/`GetStream` as a `CLOSED`
   trace and returns — cleanup still completes; genuinely unexpected failures still fall through to
   the generic `ERROR` handler (no broad swallowing).
5. **`ObjectDisposedException` and `SocketException` catches** added to the main handler so a
   socket closed during negotiation/observation is recorded as `CLOSED`, not `ERROR`.

Ownership and disposal contracts are unchanged: the caller still owns the stream, the handler still
closes its client in `finally`, and `DisposeAsync` still performs a bounded cancellation plus
handler join. No new abstraction or shutdown infrastructure was introduced.

## 5. Finding-to-fix-to-test traceability

| Finding | Root cause | Fix | Regression tests |
| --- | --- | --- | --- |
| `TRACEQ1-F01` — NRE in `SafeRemoteEndPoint` propagating through `DisposeAsync` | endpoint captured inside the handler after `close` may null `client.Client`; call outside `try` | accept-time capture + null-safe helper + prologue inside `try` (items 1–3) | R1_02, R1_10, R1_08, R1_01, R1_06 |
| (sibling) `ObjectDisposedException` from `GetStream`/`NoDelay` | socket access outside `try` | acquisition guard → `CLOSED` (item 4) | R1_10, R1_09 |
| (sibling) mid-operation `ObjectDisposedException` mislabelled `ERROR` | no ODE catch | ODE/SocketException catches → `CLOSED` (item 5) | R1_03 |
| shutdown with pending read/accept | cancellation + join | unchanged, re-verified | R1_07, R1_08, R1_09, R1_05 |

## 6. New regression cases (R3)

`tests/BoltSharp.DriverIntegrationTests/HandshakeTrace/ListenerShutdownRegressionTests.cs`
(10 tests, all pass post-fix):

| Test | Coverage (R3 item) |
| --- | --- |
| `R1_01_ClientClosesBeforeListenerDisposal_ShutdownIsClean` | 1 |
| `R1_02_EndpointCapture_WithClosedSocket_IsUnknown_NotNre` | 2 (deterministic) |
| `R1_03_DisposalDuringActiveConnection_IsClean` | 3 |
| `R1_04_CancellationConcurrentWithClientClosure_IsClean` | 4 (0 or 1 trace accepted; bounded, exception-free) |
| `R1_05_RepeatedDisposal_IsClean` | 5 |
| `R1_06_ClientAbort_DuringShutdown_IsClean` | 6 (RST via `LingerOption(true,0)`) |
| `R1_07_ShutdownWithPendingRead_IsClean` | 7 |
| `R1_08_AfterDisposal_PortIsClosed_AndNoPendingTasks` | 8 (connect refused after disposal) |
| `R1_09_AcceptDisposeRace_Repeated_NoUnhandledFailure` | race coverage ×200 |
| `R1_10_Handler_WithAlreadyClosedClient_DoesNotFault` | deterministic handler-level interleaving |

Determinism was achieved by handing the **real** helper/handler an already-closed socket
(R1_02/R1_10) instead of relying on repetition; R1_09 widens race coverage only. No test converts a
genuine failure into success: R1_03/R1_04 assert reached-outcome classification, and the handler
still records `ERROR` for unexpected exceptions.

## 7. Test commands and results (R5)

Environment: Windows 11 build 10.0.26200, win-x64, .NET SDK 10.0.401 (MSBuild 18.9.11), target
`net8.0`, runtime 8.0.31, `Neo4j.Driver` 6.4.0. All commands from the repository root; logs and TRX
under `docs/reviews/evidence/B0-A-TRACE-R1/`.

| Suite | Expected baseline | Observed | Result |
| --- | --- | --- | --- |
| Full solution | 826 passed | **826 passed / 0 failed / 0 skipped** | match |
| Driver integration project | 37 passed, 10 skipped | **47 passed / 0 failed / 10 skipped** (37 baseline + 10 new; D01–D10 still gated) | pass |
| B2-A-Q2 | 18 | **18 passed** | match |
| B2-B-Q1 | 67 | **67 passed** | match |
| B2-B-Q2 | 46 | **46 passed** | match |
| B2-C-Q2 | 40 | **40 passed** | match |
| B0-A-TRACE-Q1 reviewer probes | 14 | **14 passed** (incl. T10) | match |
| Authentic handshake probes | 2 | **2 passed** | match |
| New shutdown regression tests | — | **10 passed / 0 failed** (5 failed pre-fix) | pass |

```text
dotnet build BoltSharp.slnx -c Debug                                  → exit 0
dotnet test  BoltSharp.slnx --no-build -c Debug                       → 826/0/0
dotnet test  tests/BoltSharp.DriverIntegrationTests/...csproj         → 47 passed, 10 skipped
dotnet test  tests/... --filter FullyQualifiedName~ListenerShutdownRegressionTests → 10 passed
dotnet test  tests/... --filter FullyQualifiedName~HandshakeTraceProbeTests        → 2 passed
dotnet test  docs/reviews/probes/B0-A-TRACE-Q1/B0ATRACEQ1.Probes.csproj → 14 passed
dotnet test  docs/reviews/probes/B2-A-Q2 / B2-B-Q1 / B2-B-Q2 / B2-C-Q2 → 18 / 67 / 46 / 40
```

Note on T10: the historical reviewer probe was written to assert the **correct** behaviour
(exception-free, bounded disposal) and therefore detects the original defect; pre-fix it failed when
the race interleaved and passed otherwise. Post-fix it passes deterministically because the
remediated source is linked into the reviewer probe project. Its file is unchanged.

## 8. Authentic handshake regression evidence (R5)

The authentic `Neo4j.Driver` 6.4.0 probes were re-run after remediation with
`BOLTSHARP_TRACE01_EVIDENCE_DIR` pointing at the R1 directory. All four handshake byte files are
**byte-identical** to the committed producer evidence
(`driver-{manifest,legacy}-{client-handshake,server-response}.hex`), and the mode/selection
(Manifest v1 → 5.8 + caps `0x0`; legacy → `00 00 08 05`), HELLO(155) → pipelined LOGON(15) and the
expected post-handshake `ServiceUnavailableException` are unchanged. The fix alters only lifecycle
handling, not protocol bytes.

## 9. Remaining limitations

- `TRACEQ1-F02` (unreachable `CaptureTruncated`) remains a non-blocking qualification finding, by
  scope decision; it is unchanged.
- The remediation narrows exception classification for socket-acquisition; a genuinely unexpected
  prologue exception still surfaces as an `ERROR` trace, and any exception the `finally` itself
  could throw would still propagate (unchanged from before; none observed).
- Race coverage combines deterministic interleavings (R1_02/R1_10) with a bounded repetition loop
  (R1_09); repetition alone is not claimed as proof of race freedom.
- Windows/net8.0 test-only evidence; no cross-platform or production-transport claim.
- Independent requalification (B0-A-TRACE-Q2) is required before owner acceptance; this document
  does not self-approve the remediation.

## 10. Deliverables and identities

| Artifact | Path |
| --- | --- |
| Fix + tests | `tests/BoltSharp.DriverIntegrationTests/HandshakeTrace/LoopbackHandshakeListener.cs` (modified), `ListenerShutdownRegressionTests.cs` (new) |
| Evidence | `docs/reviews/evidence/B0-A-TRACE-R1/` (console logs, TRX, pre/post-fix runs, authentic reproduction, `sha256.json`) |
| This record | `docs/reviews/B0-A-TRACE-R1-listener-shutdown-remediation.md` |

Unmodified (verified): B0-A-TRACE-01 producer evidence, B0-A-TRACE-Q1 reviewer evidence and probes,
`src/BoltSharp`, accepted B1/B2 implementation and evidence.

Commit identities are recorded in the final handoff message (this file is part of the change-set).

```text
Task: B0-A-TRACE-R1
Baseline commit/tree: f24afb79ddf9737583e17887e2874c0e781ab354 / 0ced7b6d95e02db24c16567888ddb8d827609c84
Finding remediated: TRACEQ1-F01 (MAJOR) — disposal-race prologue escapes in the test-only listener
Fix: accept-time endpoint capture; null-safe helper; socket access inside the handler try;
     ODE/SocketException -> CLOSED; narrow acquisition guard; no broad swallowing
New tests: 10 (5 failed pre-fix, 10 pass post-fix)
Validation: solution 826; driver 47+10 gated; B2-A-Q2 18; B2-B-Q1 67; B2-B-Q2 46; B2-C-Q2 40;
            reviewer probes 14 (incl. T10); authentic 2; handshake bytes byte-identical
Stop: awaiting separate B0-A-TRACE-Q2 independent requalification; no push; no B3/B4/B5
Owner acceptance: PENDING
```
