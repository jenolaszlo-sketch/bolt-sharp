# Q-B5B-CI-RACE-REMED-01-R2-Q1 — Independent Qualification of the Published Listener-Shutdown Remediation

| Item | Value |
|---|---|
| Commission | `B5B-CI-RACE-REMED-01-R2-Q1` |
| Production baseline (parent) | `a276ec278ce41ea5b5f0a38cee466d043a945617` |
| WIP branch (superseded) | `e66f2eb8a862b7a28722662da75abfd483362c88` (`codex/preserve-b5b-ci-race-wip`) |
| Remediation reviewed here | `0ebc38cb7fc677edd0b1923651bffcb54fd5a27e` (= `origin/main`) |
| Author / committer | `jenol <jenolaszlo-sketch@users.noreply.github.com>` (verified on `git show -s`) |
| Hosted CI run | `45587935213` (push → `main`, conclusion `success`) |
| Reviewer environment | Windows (native), .NET SDK Release build, `net8.0`; no native-Linux run, no F5 diagnostics — both out of scope per commission |
| Evidence directory | `docs/reviews/Q-B5B-CI-RACE-REMED-01-R2-Q1-independent-qualification/` |

Identity verified independently before commencing: the reviewed commit's parent is
the production baseline; `origin/main == HEAD == 0ebc38c`; the WIP branch is not an
ancestor and was never merged; no tag points at HEAD; the working tree is clean.

---

# DISPOSITION: **C. REMEDIATION REQUIRED**

The remediation **does** fix the commissioned defect. The frozen handshake-race
test is green across 30 isolated runs, an independent reproducer fails 3/3 on the
baseline and passes 3/3 on the remediation, and an independent adversarial probe
matrix (~20 scenario families, 1 500+ individual rounds) found **no peer left
open** in any accepted, rejected, sentinel, drain, flood, short-budget, concurrent,
repeated or degraded-wake path. That part of the work is established.

It is nonetheless **not ready for owner acceptance** because two concrete defects
remain in the committed production code, one of which is reachable through
ordinary client behaviour (a peer that connects and resets) and one of which
directly contradicts the remediation's own cancellation claim. Both are small,
well-localized corrections; neither requires redesign.

---

# 1. Subject and production diff

`git diff --stat a276ec2 0ebc38c` — four files, +863 / −63:

| File | Change | Blob at `0ebc38c` |
|---|---|---|
| `src/BoltSharp/Transport/BoltTcpServer.cs` | rewritten accept/shutdown loop, registry, sentinel | `7c0010ea399228e7f2ecbfd62d18b81603ca3a4f` |
| `src/BoltSharp/Transport/BoltTcpConnection.cs` | TLS guard + shutdown classification | `1116696a7f25e352ccfafe3e616742dbc3db6731` |
| `src/BoltSharp/Transport/BoltShutdownOutcome.cs` | new public enum | `c201bd5c8ee04b2c8d9478e1c553ad319d53f517` |
| `tests/BoltSharp.Tests/Transport/ListenerShutdownLifecycleTests.cs` | 7 new tests | `88fcd18852bcbf402933d6de6119ad59d591d08f` |

The frozen test `TcpHandshakeTests.ListenerShutdownDuringNegotiation_CompletesBounded`
is **unchanged** (`git diff --stat a276ec2 0ebc38c -- tests/.../TcpHandshakeTests.cs`
is empty). `BoltMessageFixtures.cs` is unchanged between baseline and remediation.

No unrelated production change, no test weakening, no reformatting of the frozen
file. `dotnet format --verify-no-changes` on the four touched files exits 0; the
whole-solution format check fails only on the pre-existing `BoltMessageFixtures.cs`
whitespace findings, reproduced identically at `a276ec2` — pre-existing, not
introduced by this remediation.

# 2. Hosted CI verification

`gh run view 45587935213 --json …` (saved as `evidence-logs/ci-run-45587935213.json`):

- `headSha = 0ebc38cb7fc677edd0b1923651bffcb54fd5a27e` — exactly the reviewed revision.
- `headBranch = main`, `event = push`, `conclusion = success`.
- Single job "Build, test and secret scan" succeeded; all steps succeeded, including
  "Reject private key material", "Secret scanner controls", Build (Release) and Test (Release).

The hosted run therefore qualifies the exact subject revision, not an ancestor.

# 3. Independent code inspection

Read directly from the committed blobs, not from the implementation agent's report.

**Established correct:**

- *Registration before execution.* `BoltTcpServer.AcceptLoopAsync` assigns
  `connectionId`, checks capacity, reports `ConnectionAccepted`, then `Register(...)`
  (lines 480–496) before the per-connection task can start; the `ConnectionEntry`
  startup gate holds the pipeline until registration is visible. Ownership stays with
  the registry until retirement; `_connections` registration precedes pipeline start.
- *Accept-loop termination and queue draining.* The accept is deliberately
  non-cancellable (line 446); shutdown wakes it with a bound loopback sentinel,
  consumes the sentinel, then `DrainQueuedConnections(listener)` disposes anything that
  raced with the close (lines 464–477). This is the mechanism that fixes the defect:
  a pending AcceptEx whose completion is retrieved still yields its socket to the
  application, which closes it — the peer observes closure.
- *Sentinel identification and lifecycle.* The sentinel port is recorded before
  connect; identification is by endpoint, and the ambiguity scenario (sentinel port
  colliding with a real peer's source port) was probed benign — 3/3 rounds, `Terminal=100`.
- *Bounded graceful + forced termination.* Graceful drain runs first; forced
  termination reserves a share of the remaining budget; `BoltShutdownOutcome.Incomplete`
  is exposed publicly when termination could not be established. `StopAsync` is
  idempotent under concurrency (shares one cached task/outcome), caller cancellation
  is applied via `WaitAsync(callerToken)` and does not abandon the shutdown, and
  `DisposeAsync` disposes `_shutdownCts` only when the outcome is `Completed`.
- *Additive public API.* `BoltShutdownOutcome` is a new public enum; no existing
  public member changed semantics; `dotnet pack` succeeds.

**Findings — see §6.**

# 4. Independent execution

All numbers below were produced by this reviewer, not inherited.

## 4.1 Test matrix

| Suite / scenario | Command | Result |
|---|---|---|
| Full offline suite | `dotnet test BoltSharp.slnx -c Release` | **1264/1264 passed** |
| Full suite repeat | same, 2 runs | 2/2 passed |
| Transport suite | `--filter FullyQualifiedName~BoltSharp.Tests.Transport` | **127/127 passed** |
| Frozen test, isolated ×30 | `--filter …ListenerShutdownDuringNegotiation_CompletesBounded` | **30/30 passed** (`evidence-logs/frozen-isolated-30.txt`) |
| Lifecycle class ×5 | `--filter …ListenerShutdownLifecycleTests` | 5/5 class runs, 7 tests each, **0 failures** (`evidence-logs/lifecycle-5x.txt`) |

## 4.2 Independent reproducer across three revisions

A baseline-compatible reproducer (`IndependentShutdownRepro.cs`, added temporarily
per revision, never committed) was run 3× per revision:

| Revision | Result |
|---|---|
| `a276ec2` (baseline) | **FAIL ×3** — `Expected: "closed"  Actual: "open"` |
| `e66f2eb` (WIP) | **FAIL ×3** — same |
| `0ebc38c` (remediation) | **PASS ×3** |

This independently reproduces the commissioned defect at the baseline and confirms
the remediation fixes it, without relying on the remediation's own test.

## 4.3 Adversarial probe matrix (`0ebc38c`)

An out-of-tree console probe (`controls/B5BQ1Probe/Program.cs`) exercised the
committed `BoltSharp.dll` directly. Every scenario family ended with **0 peers left
open** unless noted; logs are under `evidence-logs/`:

| Family | Cells | Result |
|---|---|---|
| IPv4 loopback, plaintext negotiation | 10 rounds | bounded, peers closed, `Completed`, `active=0` |
| IPv6 loopback, plaintext negotiation | 10 rounds | bounded, peers closed |
| `IPAddress.IPv6Any` bind + TLS | 5 rounds | **IPv6-only** on this host (IPv4 connect refused); v6 peers closed |
| IPv4 wildcard `Any` + TLS | 3 rounds | bounded, peers closed |
| Non-loopback NIC address + TLS | 3 rounds | bounded, peers closed |
| Short shutdown budgets (unaccepted peer) | 1/5/10/50 ms ×10 | bounded, peers closed 10/10 |
| Race sweep (connect delay × budget) | 6×4 = 240 cells ×10 | bounded 240/240; `peerOpen=0/240`; one cell reported `Incomplete` (bounded, observable) |
| NIC short budgets (TLS) | 8 cells ×10 | bounded 10/10, `incomplete=0`, peers closed |
| Degraded wake path (`BoundEndPoint` set to an unroutable address) | 6 delays ×5 | bounded 30/30, `Incomplete` observable, pending peer closed 0/30 open — **no orphan** |
| Connection flood, backlog 128 | 163 attempts | 163 peers closed, `accepted=100 terminal=100`, bounded |
| Aggressive flood (32 workers, 2 000 cap) | 187 attempts, 155 connected | all 155 closed, bounded |
| Backlog-1 flood | 69 attempts | all closed, `accepted=60 terminal=60` |
| Concurrent `StopAsync`/`DisposeAsync` | 5 rounds | bounded, peers closed |
| External listener stop during shutdown | 3 rounds | bounded, peers closed |
| Sentinel port ambiguity | 3 rounds | benign, no false sentinel match |
| TLS authentication cancelled mid-handshake | 3 rounds | `tlsFailures=0` (correct classification), peers closed |

## 4.4 OS-level accept-race control (`RawSocketProbe`)

To separate an established OS-level observation from an inferred AcceptEx
internal, a raw `TcpListener` control (3 runs each, identical every run):

| Case | accept result | peer state | server-side state |
|---|---|---|---|
| cancel only | socket returned | **closed** | `FinWait2` |
| close listener only | `SocketException` | **OPEN** | `Established` (orphan) |
| cancel + close | `OperationCanceledException` | **OPEN** | `Established` (orphan) |

This confirms the *mechanism* the remediation rests on — the peer is only released
when the pending accept's completion is retrieved — as an established observation.
The AcceptEx-specific explanation in the remediation report remains an inference
(Windows sockets in .NET do not expose AcceptEx directly); it is consistent with the
observation above but is not itself independently established. This distinction is
carried into the report-claim corrections in §7.

# 5. Coverage of the commissioned scenarios

| Commissioned scenario | Directly covered | Evidence |
|---|---|---|
| Registration before execution | structurally (no direct test) | §3 above — code-only |
| Accept-loop termination + queue drain | yes | test 1; probe `ipv4/ipv6-loopback-negotiation`; `raw-socket-probe-3x` |
| Sentinel identification/lifecycle | yes | probe `sentinel-port-ambiguity`, `external-listener-stop` |
| TLS cancellation propagation | **contradicted — F-01** | §6.1 |
| Bounded graceful + forced shutdown | yes | tests 4, 5; probe `nic-short-budget`, `short-budget-unaccepted` |
| Task retirement / fault observation | **contradicted — F-01** | §6.1 |
| Concurrent / repeated shutdown | yes | tests 6; probe `concurrent-dispose` |
| Caller cancellation | yes | test 5 (`CallerCancellation_DoesNotAbandonShutdown`) |
| Observable incomplete termination | yes | test 4; probe `race-delay` (`Incomplete` cells), `wake-failure` |
| Public API compatibility | yes | `dotnet pack`; additive enum only |

# 6. Findings

## F-01 (MAJOR) — TLS shutdown cancellation is applied to the *await*, not the handshake; abandoned task faults go unobserved

`src/BoltSharp/Transport/BoltTcpConnection.cs` lines 59–65:

```csharp
await sslStream.AuthenticateAsServerAsync(
        options.Tls.ServerCertificate,
        options.Tls.ClientCertificateRequired,
        options.Tls.EnabledProtocols,
        checkCertificateRevocation: false)
    .WaitAsync(tlsCts.Token)          // ← cancels the WAIT only
    .ConfigureAwait(false);
```

`Task.WaitAsync(CancellationToken)` cancels the *waiting*, never the underlying
operation. When shutdown (or `TlsHandshakeTimeout`) fires mid-handshake, the
handshake task keeps running on the socket until the kernel aborts it, then faults —
unobserved. This violates two commissioned requirements directly: *no abandoned
tasks, faults observed* and *cancellation propagation to underlying TLS
operations*.

**Independent evidence** (`controls/B5BTlsAbandonProbe`, 100 shutdown rounds + forced
GC with `UnobservedTaskException` interception):

| Revision | Unobserved exceptions |
|---|---|
| `a276ec2` (baseline) | **100/100** (`evidence-logs/tls-abandon-a276ec2.txt`) |
| `0ebc38c` (remediation) | **100/100** (`evidence-logs/tls-abandon-0ebc38c.txt`) |

The remediation does not change this behaviour at all. Under the fuller in-tree probe
run (`evidence-logs/probe-full-0ebc38c.txt`, `evidence-logs/probe-tls-abandoned-task-detail.txt`) the
counts vary between runs (99 and 145 unobserved over 100 rounds; samples all
`IOException: Unable to read data from the transport connection: The I/O operation
has been aborted…`, stack rooted at
`Socket.AwaitableSocketAsyncEventArgs…GetResult`) — the *presence* of unobserved
faults is deterministic, the exact count is scheduling-dependent.

The cancellable overload is available and confirmed by reflection
(`evidence-logs/probe-tls-overloads.txt`): `AuthenticateAsServerAsync(SslServerAuthenticationOptions, CancellationToken)`
and `(ServerOptionsSelectionCallback, object, CancellationToken)` both exist on the
target framework. The fix is a small, localized change plus a regression assertion.

## F-02 (MAJOR) — Accept-loop fault reporting is not accounting-neutral; `ConnectionsActive` goes negative on ordinary client aborts

`src/BoltSharp/Transport/BoltTcpServer.cs` lines 456–461 — the accept loop's general
`catch` reports `BoltTransportEventKind.ConnectionFailed` with id `"listener"`.
`BoltTransportDiagnostics` (lines 111–113) decrements `ConnectionsActive`
**unconditionally** on `ConnectionFailed`, but nothing incremented it for a
connection that was never accepted. Any `SocketException` that is not
"listener stopped" therefore unbalances public accounting.

This is reachable through completely normal client behaviour: connect and reset
before the server accepts (aborted probes, port scans, flaky clients, load
balancers). The remediation made this path *more* reachable than the baseline, not
less: at `a276ec2` the accept loop's first filter returned on **any**
`SocketException`, so an aborted peer terminated the (broken) loop without reporting;
the remediation deliberately keeps the loop alive and routes non-stop
`SocketException`s to the general `catch`, which reports. The unconditional
decrement itself is pre-existing in `BoltTransportDiagnostics`; the remediation
widens its reachability without making it neutral. (The
`Register`-failure path at lines 498–502 is *correct*: it reports only after
`ConnectionAccepted` was already reported.)

**Independent evidence** (`evidence-logs/probe-accept-reset.txt`, clients connect then RST
before accept; 10 rounds):

```
round=0 accepted=5  completed=5 failed=69 active=-69 balanced=False
round=1 accepted=5  completed=5 failed=68 active=-68 balanced=False
… accepted=1..9 completed=accepted failed=64..70 active=-64..-70  (10/10 unbalanced)
```

A second run with the probe's default `MaxConnections` still shows the same defect in
3 of 10 rounds (`evidence-logs/probe-full-0ebc38c.txt`): `accepted=99 completed=99 failed=1
active=-1`, `accepted=5 completed=5 failed=67 active=-67`. The invariant
`accepted == completed + failed` is violated whenever the path fires, and
`ConnectionsActive` is publicly observable as a negative number — a data-integrity
defect in the public API surface, not merely an internal counter.

# 7. Remediation-report claim corrections

1. *"Cancellation propagation"* — not established for the TLS handshake (F-01).
   What *is* established: shutdown cancellation is correctly **classified**
   (not counted as `TlsHandshakeFailed`), and the Bolt-handshake/read/write phases
   are genuinely cancellable. The claim as written overstates the remediation.
2. *"Eight scenarios"* — the commit message names eight behaviours
   (repeated negotiation shutdown, TLS authentication cancellation, concurrent
   accepts during shutdown, forced termination, observable incomplete shutdown,
   caller cancellation, repeated and concurrent stop, graceful shutdown preservation)
   but adds **seven** test methods; "forced termination" and "observable incomplete
   shutdown" share `BlockedConnection_IsForced_Bounded_AndReportsIncomplete`. This is
   an imprecise report statement, not a false test count.
3. *"AcceptEx internals"* — the OS-level observation (§4.4) is established; the
   AcceptEx mechanism description is an inference. It should be stated as such.

# 8. Residual limitations and scope

1. **Windows-only.** No native-Linux qualification was executed (out of scope per
   commission). The accept-race mechanism is kernel- and runtime-specific; the
   remediation must not be assumed portable without native-Linux requalification.
2. **Dual-stack not reachable** via the public API on this host: `TcpListener`
   bound to `IPv6Any` is IPv6-only here (IPv4 connect refused). Wildcard/NIC binds
   were exercised through TLS only, because plaintext `BindAddress` validation
   restricts binds to loopback. This is an environment limitation, not a finding.
3. **Wake-failure path** is not publicly inducible; the degraded-wake control used
   reflection on `BoundEndPoint` (30 rounds: bounded, `Incomplete` observable, no
   orphan).
4. No F5 diagnostics and no formal native-Linux qualification were executed, per
   commission scope.

# 9. Requalification requirements for the successor

A successor remediation that resolves F-01 and F-02 must be requalified as a whole
(not incrementally against this record):

1. **F-01 fix, either form:**
   - propagate cancellation into the handshake — construct
     `SslServerAuthenticationOptions` (certificate, `ClientCertificateRequired`,
     `EnabledSslProtocols`, `CertificateRevocationCheckMode = NoCheck`) and call
     `AuthenticateAsServerAsync(options, tlsCts.Token)`; or
     `ServerOptionsSelectionCallback` + token; **or**
   - keep the wait-based form but **observe** the abandoned handshake task's fault
     deterministically (e.g. continuation into `TlsHandshakeFailed`/shutdown log)
     so no task fault goes unobserved.

   Must add a regression assertion: after repeated TLS shutdowns with forced GC,
   **0 unobserved task exceptions** (the reviewer's `B5BTlsAbandonProbe` is the
   template; it must be capable of running as a committed test).
2. **F-02 fix:** make listener-level accept-fault reporting accounting-neutral —
   either a distinct non-decrementing diagnostic kind for listener faults, or a
   `ConnectionFailed` report only where `ConnectionAccepted` was previously reported
   for the same id. Regression assertion: after clients connect-and-reset,
   `ConnectionsActive == 0` **and** `accepted == completed + failed`.
3. **Re-run the full qualification set:** frozen test ≥ 20 isolated runs; full
   offline suite; lifecycle class ×5; the adversarial matrix in §4.3 including
   `accept-reset-accounting` (expect `balanced=True` every round) and
   `tls-abandoned-task` (expect `unobserved=0`).
4. **Impact statements:** L1/L2/L7 campaign impact must be restated (the accept
   retry loop and the TLS connection lifecycle both changed); the F5 L3/L4
   separate-`SslStream` path distinction must be preserved — the TLS writer
   refactor from the plaintext remediation is untouched by this fix and must stay
   untouched.
5. **Hosted CI requalification** on the successor revision with exact `headSha`
   match and a saved run record, as in §2.

# 10. Disposition rationale

**C. REMEDIATION REQUIRED.** The commissioned defect is genuinely fixed and
independently verified; nothing in §6 undermines that fix. But two MAJOR findings
remain in committed production code, one of which is reachable through ordinary
client behaviour (F-02) and one of which directly contradicts the remediation's own
cancellation claim (F-01). Selecting **A** (ready) would require ignoring a
publicly observable accounting defect; selecting **B** (supported with
qualifications) is reserved here for findings that do not require a production
change — both of these do. **D** (reject) is not warranted: the core defect fix is
correct, verified, and the residual corrections are small and well-localized.

No owner acceptance is granted by this record. This is a reviewer disposition; the
acceptance gate remains the owner's.
