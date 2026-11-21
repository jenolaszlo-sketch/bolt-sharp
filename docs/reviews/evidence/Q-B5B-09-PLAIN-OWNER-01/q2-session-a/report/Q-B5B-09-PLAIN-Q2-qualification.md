# Q-B5B-09-PLAIN-Q2 Qualification Report

## Final disposition

**SUPPORTED WITH QUALIFICATIONS**

The R1 implementation (commit `6a40c4b84993dec77deb165cef31e419df05be2e`) satisfies the
binding owner-authorized plaintext implementation specification
([Q-B5B-09-PLAIN-IMPL-01](../decisions/Q-B5B-09-PLAIN-IMPL-01-implementation-specification.md)).
All three Q1 findings are remediated and independently re-verified on Windows and Ubuntu
WSL2. The observer resource-retention behaviour is contract-consistent and inherited, but
is operationally notable and therefore carried as an explicit qualification
(`Q-B5B-09-PLAIN-Q2-F01`). The backpressure evidence is carried with an explicit
evidence-reuse justification (`Q-B5B-09-PLAIN-Q2-F02`).

Owner acceptance is recommended as `Q-B5B-09-PLAIN-OWNER-01` with those two
qualifications acknowledged. Q-B5B-09 remains open.

## Reviewer identity and independence

- Fresh reviewer session. No participation in R1 implementation, producer verification,
  or Q1 qualification; no prior exposure to R1 source or evidence internals.
- All evidence was produced from pristine copies extracted from the pinned Git commits.
- Basis and rollback impact are recorded in
  [independence-statement.txt](evidence/Q-B5B-09-PLAIN-Q2/independence-statement.txt) and
  [recovery-audit.md](evidence/Q-B5B-09-PLAIN-Q2/recovery-audit.md).

## Repository identity

| Property | Value |
|---|---|
| Working directory | `D:\projects\bolt-sharp` |
| Branch | `main` |
| HEAD | `d7e5ac14704a9576813e32692b29050ef92ee7b8` |
| HEAD tree | `b98b57723ac6e8754961261e235a74013ca76058` |
| Divergence | 16 ahead / 0 behind `origin/main` |
| Stashes | 0 |
| Worktrees (final) | 1 (main) |
| Status at start | Clean |

Two disposable worktrees at the pinned revisions were used during the campaign and were
removed before the final verification.

### Pinned revisions

| Artifact | Commit | Tree |
|---|---|---|
| Original plaintext | `20fbc461c0257620db28fb78a463218b242455bc` | — |
| Q1 qualification | `9dd2fe1b9b6f8ab2880e3fe50b8ef9ac2d43ae9c` | `f0f1d38dbe648ee36c07a490f6217b47a179dd16` |
| R1 implementation | `6a40c4b84993dec77deb165cef31e419df05be2e` | `a23edab4242711fffc28da63f254ff0f8b4d3082` |
| R1 evidence | `d7e5ac14704a9576813e32692b29050ef92ee7b8` | `b98b57723ac6e8754961261e235a74013ca76058` |
| Typed-linger correction | `dac52d55ade0a9ae703b801c8a4066e8aef2bd58` | — |

## Rollback recovery and evidence provenance

The environment rolled back the first session's temporary workspace mid-campaign,
removing part of the in-progress evidence. A SHA-256 cleanup inventory was produced and
used as the provenance basis:

- Five Windows campaign JSONs were recovered **byte-identical** to the pre-rollback
  inventory and retained without re-running (`campaign/*-win.json`).
- Build/format logs and all four TRX suites were regenerated fresh with identical
  outcomes; TRX content embeds per-case timestamps.
- No WSL2 result had ever been persisted, so **all** WSL2 results in this package are
  fresh executions from this session.
- All qualification documentation was rewritten in full.

Full detail: [recovery-audit.md](evidence/Q-B5B-09-PLAIN-Q2/recovery-audit.md).

## Delta qualification matrix

Production delta is exactly four files; no unauthorized changes.

| File | Change | F01 | F02 | F03 | Regression |
|---|---|---|---|---|---|
| `src/BoltSharp/Transport/BoltPlaintextTerminalClose.cs` | Write-scoped CTS/timer; post-write observer admission; permit release after handle disposal | corrected | corrected | — | covered |
| `src/BoltSharp/Transport/BoltTcpConnection.cs` | Plaintext terminal writer extraction; shared TLS writer; `write-pending` diagnostic; observer moved after flush | corrected | corrected | corrected | covered |
| `src/BoltSharp/Transport/BoltTcpServer.cs` | Listener shutdown, admission and typed linger; em-dash lifecycle comment restored from `dac52d5` | — | — | corrected | TS1 covered |
| `tests/BoltSharp.Tests/Transport/PlaintextTerminalCloseTests.cs` | 28 canonical oracles | covered | covered | covered | — |

Typed-linger correction from `dac52d5` is preserved verbatim; no public API, package,
configuration or dependency change.

## F01 — independent reproduction and correction

Original counterexample: `WriteTimeout = 80 ms`, observer `140 ms`, peer shutdown after the
terminal request.

| Platform | Implementation | Phase | Reason | Admitted | Completed | Failed | Drain admitted |
|---|---|---|---|---|---|---|---|
| Windows 10.0.26200 | baseline `20fbc46` | `write` | `write-deadline` | false | 0 | 1 | 0 |
| Windows 10.0.26200 | R1 `6a40c4b` | `drain` | `peer-eof` | true | 1 | 0 | 1 |
| Ubuntu WSL2 6.6.114.1 | baseline `20fbc46` | `write` | `write-deadline` | false | 0 | 1 | 0 |
| Ubuntu WSL2 6.6.114.1 | R1 `6a40c4b` | `drain` | `peer-eof` | true | 1 | 0 | 1 |

Both platforms delivered the exact terminal FAILURE (`B17FA48A…`) and the peer observed
EOF in the corrected case. R1 ends write-timeout ownership at the successful flush, so a
racing observer callback can no longer interrupt post-write processing. Repeated 10× per
platform without failure; the shorter-observer (30 ms) case repeated 10× per platform
without failure.

## F02 — diagnostics verification

| Check | Result (both platforms) |
|---|---|
| Single correlation ID across `write-start`, `write-complete`, `send-shutdown`, `drain-admitted`, `drain-start`, `drain-end`, `disposed` | pass |
| Phase labels: write / post-write / shutdown / drain | pass |
| Cause, numeric elapsed time, byte count, admission flag | pass |
| Exactly one terminal accounting transition; no failed accounting on success | pass |
| `write-pending` reported for an incomplete write | covered by canonical oracle |
| No payload, credential, query or peer-receipt representation | pass |
| `MessageProcessed` remains evidence of local write/flush completion | pass |
| TLS diagnostics unchanged; TLS never enters the plaintext helper | pass |

## F03 — transport restoration

- TLS writer/timeout behaviour preserved; TLS listener never enters the plaintext helper,
  acquires no permit and performs no raw drain (verified by oracle and probe).
- The lifecycle comment was restored to `…shutdown policy. Restart is not supported —
  create a new instance.` matching `dac52d5`.
- The typed-linger correction (`LingerState = new LingerOption(false, 0)`) is preserved on
  listener and accepted sockets.
- No raw TLS drain and no incidental shared-transport changes remain.
- TLS graceful-close remediation remains **DEFERRED** (out of scope for R1).

## Observer resource-retention analysis

### Measurements (Windows and WSL2, identical structure)

| Experiment | Socket disposal | Drain permit | Completion accounting | Connection task | Admission slot | Listener shutdown | Observer |
|---|---|---|---|---|---|---|---|
| **OBS-01** observer 30 ms (inside 80 ms write timeout) | n/a (no interrupt) | released on drain end | completed=1 at ~35 ms | settles | settles | n/a | returns normally |
| **OBS-02** observer 300 ms (> 250 ms total) | **closed at ~256–270 ms while observer active** | never acquired (drain not admitted) | deferred until observer returns | retained | retained | n/a | returns at ~300 ms |
| **OBS-03** observer blocked indefinitely, total deadline | **closed while observer blocked** | never acquired | deferred | **retained (tasks=1)** | **retained (active=1)** | n/a | released by the test |
| **OBS-04** two blocked observers, `MaxConnections=2` | sockets closed per owner deadline | not acquired | deferred | retained (2) | **retained (2); a healthy third connection is refused for capacity** | n/a | released |
| **OBS-05/05b** listener stop / owner cancellation while blocked | closed 0.15 ms / 1.98 ms after the signal | not acquired | deferred | retained | retained | **`StopAsync` had not completed before release**; it completes at the shutdown bound | released |
| **OBS-06** `ShutdownTimeout=500 ms`, observer blocked | closed at the deadline | baseline count unchanged (0 permits at `MaxConnections=1`) | deferred | retained (1) | retained (1) | **`StopAsync` returned at ~509 ms with `activeRemaining=1 stop=deadline`; new connections refused** | released; all resources then settle |

Every experiment released the blocked observer in a `finally` block; no indefinite hang was
introduced. After release in every case: `ConnectionsActive → 0`, connection tasks → 0,
permit count → baseline, and a healthy replacement connection completes normally.

### Comparison with the inherited implementation

The same blocked-observer measurement against the original plaintext baseline
(`20fbc46`) produced:

- socket closed at **88.3 ms** (the inherited write deadline) while the observer blocked;
- **`ConnectionsActive = 1` and connection tasks = 1 retained** until the observer returned;
- classification `phase=write`, `reason=write-deadline`, `failed=1` — the F01 defect.

R1 produces the same retention counts (active=1, tasks=1) with corrected classification
(`phase=post-write`, `reason=total-deadline`, `completed=1`). **The retention behaviour is
therefore identical across both implementations and is inherited from the original
plaintext code, not introduced by R1.**

### Contract interpretation

The binding specification answers each question directly:

1. **Is an admission slot required to be released within the terminal lifecycle deadline?**
   No — the opposite: *"Retain the existing connection admission slot through disposal and
   task retirement."* (specification §4 item 4). Retention through task retirement is
   mandated.
2. **Does the deadline govern only transport cleanup or the whole connection lifecycle?**
   *"The new I/O deadline covers serialization through teardown after the classified
   result."* It is an I/O deadline, not a connection-lifetime deadline.
3. **Are synchronous diagnostics an existing intentional contract?**
   Yes — *"Keep the existing synchronous session operation-retirement contract"*, and
   *"MessageProcessed remains evidence of local write/flush completion."*
4. **Can observers legally retain connection execution after socket disposal?**
   Yes, as the direct consequence of (1)–(3): disposal, permit release and task retirement
   are ordered, and arbitrary synchronous work is explicitly outside the timer's
   preemptive control (*"An arbitrary synchronous provider call remains outside the timer's
   preemptive control."*).
5. **Can a blocked observer prevent admission of healthy connections?**
   Yes, temporarily, and only for connections not yet accepted: the blocked connection
   already holds its own `MaxConnections` slot, which the specification requires it to
   retain until task retirement. Excess connections are refused immediately at acceptance
   rather than queued.
6. **Can repeated blocked observers cause resource exhaustion?**
   Bounded by `MaxConnections`. N blocked observers occupy at most N already-accepted
   slots; the (N+1)th connection is refused for capacity. All resources settle when the
   observers return, and the listener itself remains healthy throughout.
7. **Does `StopAsync` returning with `activeRemaining=1` satisfy the shutdown contract?**
   Yes — *"Listener stop ends admission, signals accepted owners, and observes completion
   within the configured shutdown policy."* The implementation waits the full
   `ShutdownTimeout`, ends admission (new connections are refused), reports the residual
   count honestly in the listener diagnostic, and the connection settles once the observer
   returns.

### Classification

- **Existing architectural limitation**: yes — the synchronous `MessageProcessed`
  delivery predates R1 and is preserved by the specification.
- **Defect introduced by R1**: **no** — proven by the identical baseline measurement.
- **Violation of the binding owner specification**: **no** — the specification explicitly
  mandates slot retention through task retirement, scopes the deadline to I/O, and carves
  synchronous work out of timer control.
- **Improvement opportunity outside authorized scope**: an asynchronous or timeout-bounded
  diagnostic dispatch would change the diagnostic contract and is outside the authorized
  file set. It is recorded as an owner-direction item, not as required remediation.

**Conclusion: the observer retention does not violate a mandatory resource or shutdown
guarantee and does not trigger REMEDIATION REQUIRED.** It is carried as
`Q-B5B-09-PLAIN-Q2-F01` because a pathological synchronous observer can hold a connection
slot indefinitely, and only owner direction can decide whether bounded diagnostic dispatch
is wanted.

## Backpressure evidence disposition

### Prior Q1 evidence applicability

Source-level comparison of the write and cancellation paths between `20fbc46` and
`6a40c4b`:

- **Unchanged**: `stream.WriteAsync(payload, cancellation)` loop with partial-write
  handling, `FlushAsync` after the loop, the `write-pending` diagnostic on an incomplete
  write, the byte/delay limits, and every cancellation signal (write timeout, total
  deadline, owner cancellation, listener shutdown).
- **Changed**: only the *scope* of the token passed to the terminal write (write-scoped
  CTS instead of the owner token) and the timeout's application site.

Because the physical write and cancellation semantics are preserved, Q1's backpressure
evidence remains applicable to R1's write path.

### Independent bounded experiment

The independent probe could **not** establish a genuinely pending terminal write on either
platform: 12 buffer profiles × 4 stimuli, zero decisive observations
(`no-terminal-write-pending` in all 48 attempts on Windows and all 48 on WSL2).
Contemporary kernel socket buffers (Windows default send buffer and Linux WSL2's up to
~4 Mi) absorb the small terminal response before the write blocks. This is a probe
environment limitation, identical for baseline and R1, and is **not** characterized as a
production defect.

### Canonical oracles

R1's own authorized oracles `ActualPendingTerminalWriteIsInterrupted` (write-timeout,
deadline, owner, listener) pass on Windows and WSL2, establishing deadline, cancellation
and listener-stop behaviour with actual await return, timely disposal and balanced
accounting, including under induced backpressure.

**Disposition: evidence reuse justified** for the unchanged write physics, verified by the
passing canonical oracles; the independent probe backpressure result is INCONCLUSIVE and
recorded as a probe limitation (`Q-B5B-09-PLAIN-Q2-F02`).

## Negative controls

Executed in an isolated R1 worktree; each control ends with restoration and a clean-tree
check. Detections match Q1 exactly.

| Control | Requirement | Mutation | Detecting oracle | Detected |
|---|---|---|---|---|
| NC-01 | R04 trailing execution | Re-enable Ready after refusal; read a queued frame with the retired reader inside the terminal write delegate | `ExactFailureAndNoTrailingExecution` — 5 of 6 scenarios failed | **yes** |
| NC-02 | R09/R10 retention | Suppress the helper socket close and the connection client disposal | `ListenerStopJoinsActualHandleAndReturnsPermit` — actual handle closure | **yes** |
| NC-03 | R06 linked cancellation | Remove the listener-token registration | `ListenerStopJoinsActualHandleAndReturnsPermit` — expected `listener-shutdown`, actual `drain-deadline` | **yes** |
| NC-04 | R05 deadline renewal | Reset the total timer at drain entry with a fresh elapsed origin | `OneDeadlineCoversWriteAndDrain` | **yes** |
| NC-05 | R08 byte cap | 8 KiB → 12 KiB drain cap | `ExactFailureAndNoTrailingExecution(scenario: "large")` | **yes** |
| NC-06 | R09 concurrency | Two permits per listener | `DrainAdmissionAndRecovery(connectionLimit: 4, simultaneous: 3, admitted: 1)` | **yes** |
| NC-07 | R12 TLS isolation | Pass the plaintext helper into an authenticated TLS connection with a zero-permit policy and the raw `NetworkStream` | `TlsRefusalNeverEntersPlaintextHelper` | **yes** |
| **NC-08** | F01 ownership | Reintroduce the full original defect: owner-scoped write token, owner interrupt from the write timer, observer published inside the write phase | 17 of 28 `PlaintextTerminalCloseTests` failed; `SuccessfulWriteEndsWriteTimeoutBeforeObserver(scenario: "delay")` expected `peer-eof`, actual **`write-deadline`**; independent probe reported the misclassified lifecycle | **yes** |
| **NC-09** (added) | resource-lifetime oracle effectiveness | Never release an acquired drain permit | 5 tests failed including `DrainAdmissionAndRecovery` | **yes** |

Transcripts and TRX: `evidence/Q-B5B-09-PLAIN-Q2/negative-controls/`.
Runner: `evidence/Q-B5B-09-PLAIN-Q2/negative-controls/run_controls.py`.

## Regression campaigns

| Suite | Platform | Total | Passed | Failed | Skipped |
|---|---|---|---|---|---|
| Full core | Windows | 1,257 | 1,257 | 0 | 0 |
| Full core | Ubuntu WSL2 | 1,257 | 1,257 | 0 | 0 |
| Official driver interop | Windows | 43 | 43 | 0 | 0 |
| Official driver interop | Ubuntu WSL2 | 43 | 43 | 0 | 0 |
| Driver integration | Windows | 85 | 75 | 0 | 10 (pre-existing TLS-dependent) |
| Integration | Windows | 19 | 19 | 0 | 0 |

Focused/deadline/cancellation, observer-lifecycle, TLS-isolation and typed-linger
regressions are inside the core suite and explicitly re-verified on WSL2
(`PlaintextStartup_UsesDisabledLinger`, `TlsStartup_UsesDisabledLinger`,
`RestartIsNotSupported_NewInstanceIsRequired`, `TlsRefusalNeverEntersPlaintextHelper`,
`OneDeadlineCoversWriteAndDrain`, `ListenerStopJoinsActualHandleAndReturnsPermit`,
all six `SuccessfulWriteEndsWriteTimeoutBeforeObserver`, all four
`ActualPendingTerminalWriteIsInterrupted`).

Runtime: Windows SDK 10.0.401 with .NET 8.0.31; Ubuntu WSL2 6.6.114.1 with SDK 10.0.114
and .NET 10.0.14 selected via `DOTNET_ROLL_FORWARD=Major` because only .NET 10 was
installed.

### Builds and formatting

| Check | Result |
|---|---|
| Solution build (DEBUG, rebuild) | success, exactly **2** xUnit2012 warnings at `BoltRequestDecodeTests.cs:71,72` |
| Solution build (RELEASE) | success, 0 warnings |
| `dotnet format --verify-no-changes` | 8 whitespace errors in `BoltMessageFixtures.cs` — **pre-existing in the pristine baseline**, not introduced by R1 |

## Findings register

| Finding | Q1 disposition | Q2 assessment | Q2 disposition |
|---|---|---|---|
| `Q-B5B-09-PLAIN-Q1-F01` write-timeout misclassification | REMEDIATION REQUIRED | Corrected; reproduced on baseline and corrected on R1, both platforms; NC-08 re-detects the defect | **SUPPORTED** (remediated) |
| `Q-B5B-09-PLAIN-Q1-F02` diagnostics metadata | REMEDIATION REQUIRED | Corrected; correlation, phase, cause, timing, drain events verified | **SUPPORTED** (remediated) |
| `Q-B5B-09-PLAIN-Q1-F03` incidental shared transport changes | REMEDIATION REQUIRED | Corrected; TLS writer extracted, lifecycle comment restored, typed linger preserved, no raw TLS drain | **SUPPORTED** (remediated) |
| `Q-B5B-09-PLAIN-Q2-F01` observer resource retention | — | Synchronous observers retain the connection slot and task until they return; socket disposal, permit accounting, bounded shutdown and recovery all satisfy the specification; inherited from the original plaintext implementation; bounded by `MaxConnections`; only owner direction can bound diagnostic dispatch | **SUPPORTED WITH QUALIFICATIONS** |
| `Q-B5B-09-PLAIN-Q2-F02` backpressure evidence | — | Independent probe could not induce genuine terminal-write backpressure on either platform (kernel buffers); Q1 evidence applicable because write/cancellation physics are unchanged; canonical R1 oracles pass | **INCONCLUSIVE** (probe limitation; evidence reuse justified; not a defect) |

## Preserved qualifications

| Item | Status |
|---|---|
| B5-B-TS1 | CLOSED |
| Q-B5B-03 | CLOSED |
| Q-B5B-04 | OPEN |
| Q-B5B-05 | OPEN |
| Q-B5B-09 | OPEN (this qualification; owner acceptance pending) |
| TLS graceful-close remediation | DEFERRED |
| Native Linux release qualification | OUTSTANDING (WSL2 only) |
| Representative production capacity | OUTSTANDING |

## Historical integrity

All Q1, TS1, OWNER-01, TLS-GATE-01 and INV-01/02/03 evidence directories are unmodified
(verified by Git status at the qualification commit and by the R1/Q1 evidence
verifiers). The R1 verifier passes against the pinned implementation and evidence
commits; the Q1 verifier passes when run at the Q1 commit.

## Recommended next commission

**Recommend `Q-B5B-09-PLAIN-OWNER-01`** with these conditions:

1. F01, F02 and F03 are remediated per the binding specification and independently
   verified on Windows and WSL2.
2. The observer-retention qualification (`Q2-F01`) is acknowledged: it follows from the
   mandated slot retention through task retirement and the explicit synchronous-work
   carve-out. Any bounded/dispatch-asynchronous diagnostic observer is a **separate,
   owner-authorized** change and must not be folded into acceptance.
3. The backpressure qualification (`Q2-F02`) is acknowledged: Q1 evidence is reused with
   the documented justification and R1's canonical oracles pass.
4. Native Linux and representative production capacity verification remain outstanding
   release gates.
5. Q-B5B-09 is closed by owner acceptance, not by this qualification.

## Evidence

Self-verifying package: `docs/reviews/evidence/Q-B5B-09-PLAIN-Q2/`
([README](evidence/Q-B5B-09-PLAIN-Q2/README.md), `manifest.json`, `verify-evidence.py`).