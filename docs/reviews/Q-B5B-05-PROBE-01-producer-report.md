# Q-B5B-05-PROBE-01 — Producer Report: Same-Connection RESET Recovery Demonstration

**Producer disposition: PRODUCTION LIMITATION IDENTIFIED — OWNER DECISION REQUIRED**

Bounded producer demonstration addressing the inherited `Q-B5B-05` qualification, using existing
production functionality and official-driver integration assets. No production implementation is
authorized or performed. Independent qualification and formal owner closure remain separate
commissions. `Q-B5B-05` remains **OPEN**.

---

## 1. Role and scope

This report is the producer's bounded demonstration for `Q-B5B-05`:

> Q-B5B-05 — Same-connection recovery after IGNORED not established. After the observed
> FAILURE/IGNORED sequence the driver destroys the connection; RESET recovery of that connection
> was not demonstrated.

It answers three questions by observation: (a) does the BoltSharp server support a recoverable
FAILURE followed by RESET and a successful subsequent operation on the **same physical
connection**; (b) how does the genuine, pinned Neo4j.Driver 6.4.0 actually behave in that
situation; and (c) does the same-connection oracle reject false-positive recovery scenarios.

It does not implement a production fix, does not extend the qualification, does not close
`Q-B5B-05`, and does not claim independent qualification.

## 2. Repository identity

| Property | Value |
|---|---|
| Repository | `D:\projects\bolt-sharp` |
| Base commit | `e0b762a5ae716544c7d3a5fa3f4e7c08372b7ac1` |
| Base tree | `42c64bcc93c5d11206430fe611382fd665d5eb30` |
| Branch | `main` |
| Divergence at start | 19 ahead / 0 behind `origin/main` |
| Worktrees / stashes / working tree | 1 / 0 / clean |

Production source, existing tests, public APIs, projects, packages, historical evidence and
accepted owner decisions are all unmodified. The only new files are the probe project
(`docs/reviews/probes/Q-B5B-05-PROBE-01/`) and its evidence
(`docs/reviews/evidence/Q-B5B-05-PROBE-01/`).

## 3. What the contract actually permits (verified before execution)

The production session state machine (`src/BoltSharp/Sessions/BoltConnectionSession.cs`) and
the reference backend were read first, because the commission forbids assuming RESET support in a
particular state:

- **A recoverable FAILURE exists.** The reference backend's provider seam returns `null` for a
  query the allowlist does not recognize, and the session deterministically maps that to the
  **recoverable** `50N42 Neo.ClientError.Request.Invalid` FAILURE, leaving the connection open
  (`BoltConnectionState.Failed`).
- **Work messages are ignored, not refused, in the failed state.** RUN/PULL/DISCARD/LOGOFF/
  TELEMETRY in `Failed` (or `Interrupted`) answer the byte-exact zero-field `IGNORED B0 7E`.
- **RESET is admissible exactly from Ready, Streaming, Failed and Interrupted**, answering
  `SUCCESS` and returning the connection to Ready. From Negotiation, Authentication or Defunct
  it is a protocol violation (terminal FAILURE plus closure).
- **RESET in the failed state retires any outstanding result first.** A provider
  `Dispose` failure during that retirement is irreversible (B3-B-R2 fail-closed retirement):
  the connection becomes Defunct instead of claiming recovery.
- **A Defunct session processes nothing.** `Receive`/`ReceiveBytes` return no responses and close,
  so a RESET after a terminal failure produces no answer at all.

The recoverable failure used throughout is therefore the **unsupported-query refusal**, which the
official driver itself naturally produces by pipelining RUN+PULL in one client write.

## 4. The same-physical-connection oracle

The principal acceptance gate (§6) is physical connection identity. The probe never relies on
driver-instance reuse, logical driver sessions, query success, connection-pool membership or
driver retry success. It combines five independent lines of evidence:

1. **Server connection identity** — the production listener mints one monotonic `conn-NNNN` per
   accepted socket and records it on every diagnostics event. Exactly one accepted id must exist
   and every `MessageProcessed` event of the sequence must carry it.
2. **Server connection counters** — `ConnectionsAccepted`, `ConnectionsActive`,
   `ConnectionsCompleted`, `ConnectionsFailed`, `MessagesProcessed`. A recoverable FAILURE is not
   a transport fault, so `ConnectionsFailed` must stay `0`.
3. **Client socket identity** — the client's local ephemeral port, which necessarily changes if
   the physical socket is replaced, plus whether the socket was ever observed closed.
4. **Per-connection provider identity** — production creates exactly one provider instance per
   negotiated connection, so one instance ⇔ one physical connection.
5. **Proxy transcript ordering** — the producer-owned byte-exact proxy assigns one identity per
   accepted socket and records both directions in arrival order.

A replacement connection, an extra handshake, an extra authentication or a retry over a fresh
socket each break at least one line. The oracle also distinguishes a verdict captured *during*
the sequence (the connection must still be admitted and active) from one captured *after* the
orderly GOODBYE (the connection must have completed exactly once and never failed).

## 5. Results — server-side and raw protocol (the protocol capability)

All five raw scenarios pass on both Windows and Ubuntu WSL2, 13/13 probes per platform.

| ID | Case | Result |
|---|---|---|
| P-01 | Plaintext: valid operation → recoverable FAILURE → IGNORED → RESET → subsequent valid operation → orderly close | **Same physical connection** on every line |
| P-02 | Plaintext: three recoverable FAILURE/RESET cycles on one connection, then a successful operation | **Same physical connection**; `RESET` observed server-side 3× |
| P-03 | Plaintext: the exact Q-B5B-05 pipelined RUN+PULL answering FAILURE(50N42) + the byte-exact `IGNORED B0 7E`, then RESET recovery | **Same physical connection**; one RESET server-side |
| P-04 | TLS loopback: the same sequence on one encrypted socket (TLS before Bolt, no STARTTLS) | **Same physical connection**; `TlsHandshakeFailures=0` |
| P-05 | Plaintext: an unaffected concurrent client while another session is failed, then independent recovery of both | Two distinct connections, each recovered and completed independently, neither failed |

Representative wire sequence from P-01 (client → server `|` server → client, one proxied
connection):

```text
HELLO → SUCCESS(server, connection_id, hints) | LOGON → SUCCESS |
RUN "RETURN 42 AS answer" → SUCCESS(fields) | PULL → RECORD(R2 42 1A) → SUCCESS |
RUN "MATCH (n) DETACH DELETE n" → FAILURE(gql_status 50N42, neo4j_code
  Neo.ClientError.Request.Invalid) | PULL → IGNORED(B0 7E) |
RESET(B0 0F) → SUCCESS(B1 70 A0) |
RUN "RETURN 42 AS answer" → SUCCESS(fields) | PULL → RECORD(R2 42 1A) → SUCCESS |
GOODBYE(B0 02)
```

with server counters `accepted=1 active=1 completed=0 failed=0 messagesProcessed=10` captured
during the sequence and `accepted=1 active=0 completed=1 failed=0 messagesProcessed=10` after
the orderly GOODBYE, one accepted connection id, one provider instance, and an unchanged client
local port.

## 6. Results — official driver (the operational question)

| ID | Case | Observed |
|---|---|---|
| D-01 | The pipelined RUN/PULL refusal | `ProtocolException code=Neo.ClientError.Request.Invalid gqlStatus=50N42 gqlClassification=UNKNOWN retriable=False`; wire: `HELLO → SUCCESS`, `LOGON → SUCCESS`, **`RESET, RESET → SUCCESS, SUCCESS`**, `RUN → FAILURE`, `PULL → IGNORED(B0 7E)` |
| D-02 | Recovery after the recoverable failure | **Recovery on a new physical connection** with a new handshake and a new authentication. Server `accepted=2 completed=2 failed=0`; providers `#1 prepare=1 success=0`, `#2 prepare=1 success=1` |
| D-03 | The retriable-class provider failure (50N00) with a retry time configured | `DatabaseException code=Neo.DatabaseError.General.UnknownError gqlStatus=50N00 gqlClassification=UNKNOWN retriable=False`; wire: `RUN → FAILURE`, `PULL → IGNORED(B0 7E)`, then **`GOODBYE` — no RESET after the failure** |
| D-04 | Three sequential sessions of one driver instance | **One** physical connection for three sessions, each session preceded by `RESET → SUCCESS` on that same connection; values 42, 42, 42 |

The complete assessment is in
[`driver-behavior-assessment.md`](evidence/Q-B5B-05-PROBE-01/driver-behavior-assessment.md).

## 7. Negative controls

| Control | Result |
|---|---|
| NC-01 replacement connection | **Detected** |
| NC-02 missing RESET | **Detected** |
| NC-03 incorrect recovery response/state (injected disposal failure during RESET) | **Detected** |
| NC-04 terminal failure substituted for a recoverable one | **Detected** |

Details: [`negative-controls-results.md`](evidence/Q-B5B-05-PROBE-01/negative-controls-results.md).

## 8. Platform and regression results

| Suite | Windows | Ubuntu WSL2 |
|---|---|---|
| Producer probe suite (13 probes) | 13 / 0 / 0 | 13 / 0 / 0 |
| Core solution (`BoltSharp.slnx`, 1,257 tests) | 1,257 / 0 / 0 | 1,257 / 0 / 0 |
| Official-driver interoperability (43 tests) | 43 / 0 / 0 | 43 / 0 / 0 |

Environments: Windows NT 10.0.26200.0 with SDK 10.0.401 and `Microsoft.NETCore.App 8.0.31`;
Ubuntu WSL2 6.6.114.1 (Ubuntu 26.04.1 LTS) with SDK 10.0.114 and `DOTNET_ROLL_FORWARD=Major`
because only .NET 10 is installed there. Native Linux remains an inherited, outstanding release
gate and is not claimed. The probe project builds in Release with 0 warnings and 0 errors, and a
scoped `dotnet format --verify-no-changes` over the probe project reports no changes required.

## 9. Failure-handling determination (commission §12)

Same-connection RESET recovery **after a recoverable FAILURE** could not be demonstrated
**through the official driver**. The cause, determined against the five candidate causes:

- **Not a production session-state defect.** P-01..P-05 demonstrate the full
  failure → IGNORED → RESET → subsequent-operation sequence on one physical connection on both
  platforms, including over TLS, with all five identity lines satisfied.
- **An official-driver behavior limitation combined with the bounded error classification.** The
  driver destroys the connection after a `ProtocolException`/`DatabaseException` classified
  `retriable=False`, and recovery then necessarily uses a new physical connection with a new
  handshake and new authentication (D-02). The driver *does* send RESET and reuse the same
  connection in its pooled-connection protocol (D-04, and the two acquisition-time RESETs in
  D-01/D-02/D-03), so the missing piece is specifically the post-failure recovery path.
- **Not a probe or oracle limitation.** The oracle passes for the true same-connection sequence
  and fails for the replacement connection (NC-01), the missing RESET (NC-02), the non-SUCCESS
  recovery response (NC-03) and the terminal failure substitution (NC-04).
- **Not an unsupported protocol scenario.** The protocol sequence is admissible and the server
  executes it; the contract permits RESET from the failed state.
- **Not an environmental limitation.** The behavior is identical on Windows and WSL2.

The counterexample is preserved: the D-01/D-02/D-03 transcripts and the per-connection provider
counters are the evidence.

## 10. Findings

| ID | Statement | Class |
|---|---|---|
| `Q-B5B-05-PROBE-01-F01` | The production server fully supports a recoverable FAILURE followed by RESET and a successful subsequent operation on the same physical connection, on plaintext and TLS loopback, verified on Windows and WSL2 with five independent connection-identity lines | Demonstrated capability |
| `Q-B5B-05-PROBE-01-F02` | The official Neo4j.Driver 6.4.0 does not RESET a connection that has received a recoverable FAILURE in the auto-commit path; it destroys the connection and the subsequent operation runs on a new physical connection with a new handshake and a new authentication | Official-driver behavior limitation interacting with the bounded error classification |
| `Q-B5B-05-PROBE-01-F03` | The official driver does use same-connection RESET in its pooled-connection protocol (three sequential sessions on one connection; two acquisition-time RESETs per connection), so the capability exists but is not invoked after a failure | Official-driver behavior observation |
| `Q-B5B-05-PROBE-01-F04` | The same-connection oracle rejects all four false-positive recovery scenarios | Control evidence |
| `Q-B5B-05-PROBE-01-F05` | A provider `Dispose` failure during RESET retirement is irreversible: the recovery answers the terminal `50N00` FAILURE and the connection becomes Defunct (NC-03), confirming the B3-B-R2 fail-closed contract under this demonstration | Contract observation |

## 11. Preserved qualifications

| Item | Status |
|---|---|
| `Q-B5B-05` | **OPEN** — not closed by this producer commission |
| `Q-B5B-09` | OPEN |
| `Q-B5B-04` | OPEN (the relevant error-classification qualification) |
| `Q-B5B-03` | CLOSED |
| B5-B-TS1 | CLOSED |
| Plaintext graceful-close remediation | CLOSED (owner-accepted) |
| Portable typed-linger correction | ACCEPTED (Windows and WSL2 scope) |
| TLS graceful-close remediation | DEFERRED |
| Observer isolation | DEFERRED (backlog) |
| Native Linux release qualification | OUTSTANDING |
| Representative production capacity characterization | OUTSTANDING |

Not claimed: guaranteed peer delivery; native Linux qualification; production capacity
characterization; full mid-flight RESET interruption (explicitly excluded by the engine's bounded
RESET semantics and by the B5-B non-claims); any transaction or routing behavior; any other
driver version.

## 12. Recommended next commission

**`Q-B5B-05-Q1` — Independent RESET Recovery Qualification.**

- **Scope.** Qualify the protocol-level demonstration (P-01..P-05 and NC-01..NC-04) as an
  independent artifact: the reviewer verifies the producer's oracles, re-derives the
  connection-identity evidence from the pinned commits, and independently re-executes a bounded
  subset on Windows and WSL2.
- **Why it is smallest.** The demonstration is complete, bounded and negatively controlled; it
  needs no new production code, no public API change, no dependency change and no new harness.
  Its assets already exist and are committed: the probe project, the evidence package, the
  self-verifying manifest, and the concurrently preserved oracles in the core suite and the
  official-driver interop suite.
- **Prerequisite.** A recorded commissioning decision naming an independent reviewer who did not
  author this probe and did not perform this producer verification, plus the standing
  qualification conventions (independent reviewer, negative controls, retained failures and
  skips, self-verifying evidence, Windows and WSL2 execution).

**Owner decision required** on the driver-interaction question, separately from the
qualification: whether to (a) accept fresh-connection recovery and close `Q-B5B-05` with this
recorded limitation, or (b) authorize a separately scoped investigation of the `50N42`
error classification (`Q-B5B-04` refinement, already optional and separately authorized) to
determine whether a different classification would make the official driver choose RESET on the
same connection. This commission does not authorize either path.

## 13. Evidence

Self-verifying package:
[`docs/reviews/evidence/Q-B5B-05-PROBE-01/`](evidence/Q-B5B-05-PROBE-01/README.md)

| File | Content |
|---|---|
| `commissioning-record.md` | Requirements, owner identity, frozen revision, scope and matrix registered before execution |
| `requirements-matrix.md` | Protocol sequence, same-connection proof lines, matrix, negative controls, platform and regression results |
| `driver-behavior-assessment.md` | Official-driver RESET behavior and evidence classification |
| `negative-controls-results.md` | NC-01..NC-04 intent, injection, path, oracle and result |
| `windows/`, `wsl2/` | Per-platform correlated traces and counters (13 artifacts each) |
| `*.trx` | Five TRX campaign envelopes |
| `build-probe-release.log`, `format-check.log` | Build and format evidence |
| `manifest.json`, `verify-evidence.py` | Self-verifying package over Git blobs |

Probe source (separately traceable from documentation and evidence):
`docs/reviews/probes/Q-B5B-05-PROBE-01/` — `BoltSharp.Q5B05.Probe.csproj`, `ResetRecovery.cs`,
`TlsRecovery.cs`, `DriverResetBehavior.cs`, `NegativeControls.cs`, and `Infra/` (`ProbeHost.cs`,
`ProbeClient.cs`, `ProbeMessages.cs`, `ConnectionIdentity.cs`). The project is deliberately not
part of `BoltSharp.slnx`, following the repository convention for driver-dependent test projects.

## 14. Formal producer disposition

**PRODUCTION LIMITATION IDENTIFIED — OWNER DECISION REQUIRED.**

The production server supports same-connection RESET recovery (demonstrated, both platforms,
plaintext and TLS, five identity lines, four false-positive controls rejected). The official
driver does not invoke it after a recoverable FAILURE in the auto-commit path; it destroys the
connection and recovery runs on a new physical connection with a new handshake and a new
authentication. The limitation is a driver behavior interacting with the bounded error
classification, not a production session-state defect.

`Q-B5B-05` remains **OPEN**. No owner closure, no independent qualification, no production fix,
and no push is issued by this commission.