# B3-B-IMPL-01 — Bolt 5.8 connection state machine

**Task:** B3-B — connection state machine (per `docs/implementation-tasks.md`, governed by
`docs/boltsharp-spec.md`)
**Baseline (entry) HEAD:** `c2881ea5bee1d42629ff121eb25449bfeda52d79` / tree `4285faa8579033d981fa50919925583aea2e19c5`
**Dependency:** B3-A — ACCEPTED WITH QUALIFICATIONS — CLOSED (`docs/decisions/B3-A-OWNER-01-owner-acceptance.md`)
**Protocol:** Bolt 5.8 / PackStream 1.0
**Producer executor:** GLM-5.3-Flash (`nvidia`, OpenCode). This record is **not** independent
qualification and **not** owner acceptance.

---

## 1. Starting HEAD and tree

```text
git branch --show-current -> main
git rev-parse HEAD        -> c2881ea5bee1d42629ff121eb25449bfeda52d79 (matches the commission)
git rev-parse HEAD^{tree} -> 4285faa8579033d981fa50919925583aea2e19c5
git worktree list         -> one worktree (D:/projects/bolt-sharp)
git status --porcelain    -> clean at entry; no prior B3-B implementation; nothing pushed
Baselines                 -> core 924/0/0, B0-B integration 19/0/0, build 0/0 (re-run, recorded under evidence/baseline/)
```

Normative sources fetched and preserved under `evidence/B3-B-IMPL-01/spec/`: `server-state.adoc`,
`appendix/version-5.adoc`, `bolt/message.adoc` (upstream `neo4j-drivers/docs-bolt`, 2026-11-07).

## 2. State model and complete transition matrix

`BoltConnectionState`: `Negotiation`, `Authentication`, `Ready`, `Streaming`, `Failed`,
`Interrupted`, `Defunct` (`DISCONNECTED` is pre-connection and not modelled; `TX_READY` /
`TX_STREAMING` are out of scope and are **not** silently mapped onto auto-commit).

Implemented transitions (response → new state), with `*` marking a protocol violation that emits a
FAILURE and transitions to `Defunct`:

| State | Request | Responses | New state |
| --- | --- | --- | --- |
| Negotiation | HELLO | SUCCESS{server, connection_id, hints} | Authentication |
| Negotiation | GOODBYE | (none) | Defunct |
| Negotiation | LOGON / RUN / PULL / DISCARD / LOGOFF / TELEMETRY / RESET | FAILURE | Defunct* |
| Authentication | LOGON (verified) | SUCCESS | Ready |
| Authentication | LOGON (rejected / no verifier / verifier throws) | FAILURE{auth} | Defunct |
| Authentication | anything else | FAILURE | Defunct* |
| Ready | RUN (provider prepares) | SUCCESS{fields} | Streaming |
| Ready | RUN (provider null / throws) | FAILURE | Failed |
| Ready | RUN (fields null) | FAILURE | Failed |
| Ready | LOGOFF | SUCCESS | Authentication |
| Ready | TELEMETRY api∈0..3 | SUCCESS | Ready |
| Ready | TELEMETRY other api | FAILURE | Failed |
| Ready | RESET | SUCCESS | Ready (via Interrupted) |
| Ready | GOODBYE | (none) | Defunct |
| Ready | PULL / DISCARD / HELLO / LOGON | FAILURE | Defunct* |
| Streaming | PULL (records, more remain) | RECORD… + SUCCESS{has_more:true} | Streaming |
| Streaming | PULL (exhausted) | RECORD… + SUCCESS | Ready |
| Streaming | PULL (provider throws mid-stream) | RECORD… + FAILURE | Failed |
| Streaming | PULL (row column mismatch) | RECORD… + FAILURE | Failed |
| Streaming | DISCARD (more remain) | SUCCESS{has_more:true} | Streaming |
| Streaming | DISCARD (exhausted) | SUCCESS | Ready |
| Streaming | DISCARD (provider throws) | FAILURE | Failed |
| Streaming | RESET | SUCCESS | Ready (via Interrupted) |
| Streaming | GOODBYE | (none) | Defunct |
| Streaming | RUN / LOGOFF / TELEMETRY / HELLO / LOGON | FAILURE | Defunct* |
| Failed | RESET | SUCCESS | Ready |
| Failed | GOODBYE | (none) | Defunct |
| Failed | RUN / PULL / DISCARD / LOGOFF / TELEMETRY / HELLO / LOGON | IGNORED | Failed |
| Interrupted | RESET | SUCCESS | Ready |
| Interrupted | GOODBYE | (none) | Defunct |
| Interrupted | anything else | IGNORED | Interrupted |
| Defunct | anything | (none) | Defunct |

### 2.1 Deliberately unsupported transitions (documented, never fabricated as SUCCESS)

- **Explicit transactions** (`BEGIN`/`COMMIT`/`ROLLBACK`) and `ROUTE`: valid Bolt 5.8 signatures
  that this bounded server does not implement. Reached through the byte path, they produce a
  FAILURE (`Neo.ClientError.Request.Invalid`, `gql_status 08N06`) and `Defunct`. They are kept
  distinct from unknown/malformed signatures (see §7).
- `TX_READY`/`TX_STREAMING`: not modelled.

### 2.2 Resolved specification ambiguity

The version-5 appendix lists `READY`+`LOGOFF`→`FAILURE`→`FAILED`, while the message description
states that a LOGOFF outside `READY` produces FAILURE followed by immediate closure. This engine has
no failing LOGOFF path in `READY`, so the appendix's FAILED branch is unreachable; LOGOFF outside
`READY` (except FAILED/INTERRUPTED, where it is IGNORED per the appendix's ignored-work rows and the
INTERRUPTED prose) is treated as a terminal protocol violation. Recorded here rather than inventing
behaviour.

## 3. Implemented and deliberately unsupported requests

Implemented: `HELLO`, `LOGON`, `LOGOFF`, `RUN`, `PULL`, `DISCARD`, `RESET`, `GOODBYE`, `TELEMETRY`.
Recognized-but-unsupported (refused, not fabricated): `BEGIN`, `COMMIT`, `ROLLBACK`, `ROUTE`.
Unknown and malformed payloads are refused as protocol errors.

## 4. Authentication verifier design

`IBoltCredentialVerifier.Verify(LogonRequest) → bool`, injected through
`BoltSessionOptions.Verifier`. The production default is **fail closed**: when no verifier is
configured, or the verifier throws, every LOGON is rejected with the documented Bolt 5.8
authentication FAILURE (`Neo.ClientError.Security.Unauthorized`, `gql_status 50N42`) and the
connection closes. There is **no anonymous fallback** and no identity management. Test verifiers
(exact-match, accept-all, throwing) live only in the test project. `LOGOFF` clears the authenticated
context (state → `Authentication`) so another `LOGON` is required before further queries. No
credential value is ever included in a response, exception or `ToString()` (verified by sentinel
tests).

## 5. Scripted operation-provider boundary

`IBoltOperationProvider.Prepare(RunRequest) → IBoltOperation?` and
`IBoltOperation { Fields; ReadRow(); Dispose() }` are the smallest seams needed to exercise the
state machine. They are **provisional** (B3-C owns the real backend/result architecture) and public
only because the test project is a separate assembly — an `InternalsVisibleTo` grant was
investigated and **rejected** (see §7.4). The provider decides whether a query is supported and may
refuse (return `null`) or fail (throw); the session surfaces that as a recoverable FAILURE.

## 6. RUN/PULL/DISCARD, cursor ownership and bounded results

- **RUN** in `Ready`: prepare → `SUCCESS{fields}` → `Streaming`. Provider null/throw/`Fields==null`
  → FAILURE → `Failed`.
- **PULL**: honours finite `n`; `n = -1` is served in bounded passes of at most
  `MaxRecordsPerResponse` records (default 1000) with `has_more:true` while rows remain, so an
  unbounded result is never drained into memory. Rows are emitted in order; a one-row look-ahead
  buffer determines `has_more`.
- **DISCARD**: discards up to `n` rows (all for `-1`), emits **no** RECORD, reports remaining state,
  releases the cursor on exhaustion.
- **Cursor ownership**: at most one active auto-commit result; exactly-once disposal on completion,
  failure, discard exhaustion, RESET, GOODBYE, transport disconnect and session dispose. A second
  RUN while `Streaming` is a protocol violation (refused), and no query executes before
  authentication.

## 7. FAILED / IGNORED / RESET, pipelining and terminal handling

### 7.1 FAILED/IGNORED
A recoverable failure emits exactly one FAILURE, enters `Failed`, and every subsequent work message
(RUN/PULL/DISCARD/LOGOFF/TELEMETRY/HELLO/LOGON) is answered with the specification-correct
**zero-field IGNORED** and does not execute. The original failure is never overwritten. Records read
before a mid-stream failure precede the FAILURE (`[RECORD …] FAILURE`).

### 7.2 RESET (bounded)
`RESET` in `Ready`/`Streaming`/`Failed`/`Interrupted` returns to `Ready` with SUCCESS, cancelling
and disposing outstanding work (via the provider's `Dispose`); the `<INTERRUPT>`/`Interrupted` state
is modelled transiently. **Documented limitation:** this synchronous engine does not implement
mid-flight jump-ahead interruption of a non-interruptible provider call — a call already executing
inside `Prepare`/`ReadRow` cannot be preempted, and the engine claims no SUCCESS for work that is
still active. If disposal of outstanding work throws, RESET becomes FAILURE → `Defunct` rather than
falsely reporting recovery. `RESET` before `Ready` is FAILURE → `Defunct`.

### 7.3 Pipelining and cleanup
`ReceiveBatch` processes an ordered batch (bounded by `MaxBatchSize`, default 64) and stops at the
first terminal outcome, preserving protocol causality (a later success can never bypass an earlier
FAILED state). `GOODBYE` emits no response, disposes outstanding work and terminates. Unexpected
`Disconnect()` disposes the cursor, clears authentication context, is idempotent, and emits nothing.
No processing occurs after `Defunct`; `Dispose()` is idempotent and `Receive` after dispose throws
`ObjectDisposedException`.

### 7.4 A rejected approach that would have weakened historical coverage (recorded finding)
The first revision exposed the session to tests via
`<InternalsVisibleTo Include="BoltSharp.Tests" />`. This had a **material side effect**: it made the
pre-existing *internal* ownership-taking constructors of `PackStreamPath`, `PackStreamStructure`
etc. visible to the test assembly, changing C# overload resolution so that pre-existing B1
immutability tests (`B1R1F1ImmutabilityTests.ConstructorInputMutation_DoesNotChangeEncodedOutput`,
`B1Q2_F1_Path_ConstructorInputArrays_…`) bound to the no-copy internal constructors and failed —
i.e. historical assertions were silently weakened. The grant was removed; the session API is public
(documented as provisional) instead, and the two B1 tests pass again. No PackStream change was made.
This is recorded for the reviewer as an interaction to re-verify.

## 8. Contextual response validation (NC-05b / NC-08)

- **NC-05b** (column count): the session validates every row's value count against the RUN SUCCESS
  `fields` before emitting a RECORD. A mismatch emits no record for the bad row, disposes the
  cursor, emits FAILURE and enters `Failed`.
- **NC-08** (terminal sequencing): each request yields exactly one terminal summary; a PULL after a
  completed result is a protocol violation (no second SUCCESS, no RECORD); DISCARD emits no RECORD;
  no RECORD can precede a successful RUN; nothing is emitted after `Defunct`.

## 9. TELEMETRY

`TELEMETRY` is accepted in `Ready` only for `api ∈ {0,1,2,3}` → SUCCESS → `Ready`; any other
identifier → FAILURE (`gql_status 22G03`) → `Failed`; in `Failed`/`Interrupted` it is IGNORED like
other work messages; in any other state it is a protocol violation. B3-A parsing is unchanged (the
value domain is a B3-B decision).

## 10. B0-B fixture / integration results

Authentic `Neo4j.Driver` 6.4.0 request captures (t03 `RUN …{mode:r}`, `PULL{n:1000}`,
`PULL{n:1}`, `DISCARD{n:-1}`, `RESET`, `GOODBYE`) decode through B3-A and drive the session to the
expected responses; a driver-like byte sequence
(`HELLO, LOGON, RESET, RESET, RUN, PULL, RESET, GOODBYE`) is exercised end-to-end, and a RUN payload
is driven through the B2 framing reader → B3-A decode → B3-B handling → B3-A encode → B2 framing
writer and decoded back. The B0-B integration project (real driver against the fake server) remains
19/19 — B3-B does not sit on that path, and no historical fixture was replaced. No real-driver
end-to-end claim is made for B3-B.

## 11. Regression test counts

| Set | Command | Result |
| --- | --- | --- |
| Core solution | `dotnet test BoltSharp.slnx -c Debug` | **1003 passed / 0 failed / 0 skipped** (924 historical + 79 new) |
| New session tests | `dotnet test … --filter FullyQualifiedName~BoltSharp.Tests.Sessions` | **79 passed / 0 failed** |
| B3-A message tests | `dotnet test … --filter FullyQualifiedName~BoltSharp.Tests.Messages` | **98 passed / 0 failed** |
| B0-B integration | `dotnet test tests/BoltSharp.IntegrationTests/… -c Debug` | **19 passed / 0 failed / 0 skipped** |
| Build | `dotnet build BoltSharp.slnx -c Debug --no-incremental` | 0 errors; **2 warnings**, both the pre-existing B3-A xUnit2012 advisories (unchanged) |

No historical test was weakened; the two B1 immutability tests are byte-unchanged and pass. New
source introduces no new warnings. Logs: `evidence/B3-B-IMPL-01/`.

## 12. Known protocol limitations

1. **No mid-flight RESET jump-ahead interruption** of a non-interruptible provider call (§7.2).
2. **Explicit transactions / ROUTE unimplemented** and refused (§2.1).
3. **Capability enforcement delegated**: RUN `mode`/`db`/`imp_user`/bookmarks are not interpreted by
   the session; the provider seam decides support (a refusing provider yields FAILURE, not silent
   acceptance). Real capability policy belongs to B3-C.
4. `Interrupted` is transient in this synchronous engine; there is no queued-but-unexecuted work
   inside the session (pipelining is caller-driven and order-preserving).
5. Authentication is a boolean verifier seam; no identity system, credential store or scheme policy
   beyond B3-A structural validation.
6. Public `IBoltOperationProvider`/`IBoltOperation` are provisional, not a frozen B3-C contract.
7. Response FAILURE codes are bounded placeholders (documented) rather than a claim of Neo4j code
   fidelity; `neo4j_code` semantics follow B0-B-Q1 D04 observations.

## 13. B3-C integration requirements

B3-C supplies the real backend: a query/parameter contract, a deterministic Alice/Bob/KNOWS graph
provider, read-only enforcement at the transaction boundary, `PULL(n)`/`has_more`/`DISCARD`
semantics and disposal — implementing (or replacing) the `IBoltOperationProvider`/`IBoltOperation`
seam. B3-C must preserve the session's contextual validations (NC-05b/NC-08) and the bounded
`MaxRecordsPerResponse` behaviour.

## 14. Changed files

Production (new `src/BoltSharp/Sessions/`): `BoltConnectionState.cs`, `BoltSessionOptions.cs`,
`BoltSessionResult.cs`, `BoltConnectionSession.cs`. No PackStream/Framing/Handshake change; no
`BoltSharp.slnx`/`Directory.Build.props` change; no dependency change; no B3-A schema change.
Tests (new `tests/BoltSharp.Tests/Sessions/`): `SessionTestHarness.cs`,
`AuthenticationAndTransitionTests.cs`, `FailureAndRecoveryTests.cs`, `ResultAndSequenceTests.cs`,
`IntegrationAndCleanupTests.cs`.

## 15. Implementation commit and tree

```text
Implementation commit: 9f224cf41656e4baf155532e682b862ed8e910b0
Implementation tree:   bae5940dfd255a80eba6a67ce44208cd398d4b5e
Parent:                c2881ea5bee1d42629ff121eb25449bfeda52d79
Changed paths:         9 new files (4 production, 5 test) under src/BoltSharp/Sessions and
                       tests/BoltSharp.Tests/Sessions; 2,131 insertions; no other path.
```

(The report and evidence are recorded in a follow-up documentation commit; the evidence manifest
captures the hashes of both.)

## 16. Independent reviewer handoff

B3-B-Q1 should independently challenge: legal/illegal transitions; authentication bypass and
fail-closed behaviour; duplicate terminal summaries; cursor cleanup and exactly-once disposal;
FAILED/IGNORED ordering and non-overwrite; bounded RESET semantics and the no-interruption claim;
pipelined ordering and batch bounds; invalid TELEMETRY identifiers; wrong RECORD column counts;
provider exceptions; disconnect/cancellation races; bounded buffering; B3-A contract preservation;
and the §7.4 `InternalsVisibleTo`/overload-resolution interaction. Where behaviour is bounded, the
reviewer must require the explicit bounded claim above rather than assuming full protocol coverage.
