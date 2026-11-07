# B3-B-Q1 — Independent qualification of the Bolt 5.8 connection state machine

**Task:** B3-B-Q1 — independent qualification of B3-B-IMPL-01
**Reviewed `main` HEAD:** `81c0af9547a8b4e2a6f7310226763bea0819cad9` / tree `27097120fc5f64d5c9cece16ba0091e9aa5aed82`
**Implementation commit:** `9f224cf41656e4baf155532e682b862ed8e910b0`
**Baseline (B3-A owner acceptance):** `c2881ea5bee1d42629ff121eb25449bfeda52d79`
**Protocol:** Bolt 5.8 / PackStream 1.0
**Reviewer:** GLM-5.3-Flash (`nvidia`, OpenCode); executor-level separation from the B3-B producer
session is recorded, organizational attestation is not available (author-independence caveat
retained). This record is **not** owner acceptance.

## Disposition

> **B3-B-Q1 — REMEDIATION REQUIRED** (bounded).
>
> The connection state model, authentication gating, bounded `PULL(n=-1)` streaming,
> FAILED/IGNORED/RESET recovery, pipelining causality, cursor ownership, and NC-05b/NC-08
> contextual validation are independently supported by 118 reviewer probe checks (110 passing) and
> the reproduced producer suites. **Two bounded defects are established** and must be remediated
> before B3-B acceptance:
>
> - **F-01 (moderate, blocking):** a provider `Dispose()` that throws escapes the session API and
>   leaves the connection in a non-terminal state on **GOODBYE, `Disconnect()`, session
>   `Dispose()` and `RUN` with null `Fields`** — unlike RESET, which handles the same failure
>   deterministically. The task requires deterministic handling of provider exceptions and safe
>   cleanup on GOODBYE/disconnect.
> - **F-02 (minor, blocking for conformance of claimed behaviour):** an out-of-domain `TELEMETRY`
>   identifier produces `gql_status 22G03` (a *type-error* status) instead of the specification's
>   `50N00` / `Neo.DatabaseError.General.UnknownError` for an invalid identifier value.
>
> Remaining limitations (bounded `PULL(n=-1)`, non-jump-ahead RESET, batch ownership ergonomics,
> the wrong-typed-TELEMETRY seam gap, provisional public API) are documented qualifications and do
> not by themselves block acceptance.

---

## 1. Reviewed HEAD and tree

`81c0af9547a8b4e2a6f7310226763bea0819cad9` / `27097120fc5f64d5c9cece16ba0091e9aa5aed82`; one
worktree (`main`), clean at entry; linear ancestry from the B3-A owner acceptance `c2881ea…`;
nothing pushed (`origin/main` `9abf5e3` remains an ancestor). No prior B3-B-Q1 work existed.

## 2. Preflight findings

No unexpected baseline difference; no concurrent writer; no unrelated working-tree modifications.
B3-B-IMPL-01 manifest **22/22**. B3-A-Q1 **14/14**, B3-A-R1 **13/13**, B3-A-Q2 **15/15**, B0-B
producer **24/24**, B0-B-Q1 **119/119**. B3-A-IMPL-01 verifies **16/21**, the 5 non-matches being
exactly the files changed by the authorized B3-A-R1 remediation (pre-R1 snapshot semantics already
recorded in the B3-A owner acceptance) — not a new issue.

## 3. Evidence integrity and change isolation

- Changes since `c2881ea` are exactly the new `src/BoltSharp/Sessions/` (4 files),
  `tests/BoltSharp.Tests/Sessions/` (5 files), and the B3-B report/evidence.
- **PackStream, Framing, Handshake: unchanged. B3-A message schemas: unchanged. Historical B3-A
  tests, B0-B evidence/fixtures and both owner decisions: unchanged (guarded-path diff empty).**
- No dependency change, no `BoltSharp.slnx`/`Directory.Build.props` change; no runtime
  `Neo4j.Driver` reference in `Sessions/` (the only "neo4j" strings are `neo4j_code` metadata
  values).
- Build advisories: the full rebuild emits **2 warnings, both `xUnit2012` in the unchanged B3-A
  file `tests/BoltSharp.Tests/Messages/BoltRequestDecodeTests.cs` (lines 71–72)**; B3-B files emit
  none. The two advisories are pre-existing (unchanged file, unchanged lines) — not a clean-warning
  build is *not* claimed.

## 4. Reproduced test counts

| Suite | Command | Result |
| --- | --- | --- |
| Core solution | `dotnet test BoltSharp.slnx -c Debug` | **1003 / 0 / 0** |
| B3-B session tests | `dotnet test … --filter FullyQualifiedName~BoltSharp.Tests.Sessions` | **79 / 0** |
| B3-A message tests | `dotnet test … --filter FullyQualifiedName~BoltSharp.Tests.Messages` | **98 / 0** |
| B0-B integration | `dotnet test tests/BoltSharp.IntegrationTests/… -c Debug` | **19 / 0 / 0** |
| Build | `dotnet build BoltSharp.slnx -c Debug --no-incremental` | 0 errors; 2 pre-existing advisories |

Test audit (§5): all 79 session tests assert concrete responses/states/counters; no
swallowed-exception or conditional assertions were found; failures were traced to my own probe
expectations, never to producer tests. Independent probes (§17/§18) do not reuse producer test
code.

## 5. Independent state-transition matrix (spec-derived, executed)

Fresh matrix built from `evidence/B3-B-IMPL-01/spec/{server-state,version-5,message}.adoc` and
executed by the probe (checks `S01`–`S23`, `A*`, `R*`, `I*`, `T*`, `U*`). Selected results:

| State | Request | Spec expectation | Observed | Classification |
| --- | --- | --- | --- | --- |
| Negotiation | HELLO | SUCCESS → Authentication | SUCCESS{server,connection_id,hints} → Authentication | CONFORMANT |
| Negotiation | LOGON/RUN/TELEMETRY/RESET | protocol error | FAILURE → Defunct | CONFORMANT |
| Negotiation | GOODBYE | disconnect, no response | (none) → Defunct | CONFORMANT |
| Authentication | LOGON ok / rejected | SUCCESS→Ready / FAILURE→Defunct | SUCCESS→Ready / FAILURE{auth}→Defunct | CONFORMANT |
| Ready | RUN ok / provider fail | SUCCESS→Streaming / FAILURE→Failed | same | CONFORMANT |
| Ready | LOGOFF | SUCCESS→Authentication | SUCCESS→Authentication | CONFORMANT |
| Ready | TELEMETRY 0..3 / other | SUCCESS→Ready / FAILURE→Failed | SUCCESS / FAILURE (metadata — see F-02) | CONFORMANT WITH QUALIFICATION |
| Ready | RESET / GOODBYE | SUCCESS→Ready / disconnect | same | CONFORMANT |
| Ready | PULL/DISCARD | protocol error | FAILURE→Defunct | CONFORMANT |
| Streaming | PULL/DISCARD remaining / exhausted / provider fail | RECORD…+SUCCESS(has_more) / SUCCESS / FAILURE | same | CONFORMANT |
| Streaming | RESET / GOODBYE | SUCCESS→Ready / disconnect | same | CONFORMANT |
| Streaming | RUN | protocol error | FAILURE→Defunct | CONFORMANT |
| Failed | RUN/PULL/DISCARD/LOGOFF/TELEMETRY | IGNORED→Failed | IGNORED→Failed | CONFORMANT |
| Failed | HELLO/LOGON | not a listed transition → fatal | FAILURE→Defunct | CONFORMANT WITH QUALIFICATION (bounded reading: only work messages + LOGOFF/ROUTE are IGNORED; HELLO/LOGON are fatal) |
| Failed | RESET | SUCCESS→Ready | SUCCESS→Ready | CONFORMANT |
| Interrupted | RESET / other | SUCCESS→Ready / ignored | (transient) SUCCESS→Ready / IGNORED | CONFORMANT |
| Defunct | anything | no processing | no response | CONFORMANT |

`TX_READY`/`TX_STREAMING` are out of scope and are **not** alias-mapped onto auto-commit.

## 6. Authentication findings

- Init: HELLO-in-Negotiation OK; repeated HELLO, LOGON-before-HELLO, RUN-before-LOGON all → FAILURE
  → Defunct; discovery failures never create an authenticated session (`A*`, `S14`–`S16`).
- Credential verification: missing verifier → deny (`A01`); throwing verifier → deny (`A02`);
  wrong credentials → `Neo.ClientError.Security.Unauthorized`/`50N42` (`A07`); `none` scheme with no
  verifier is **not** a bypass (`A08`); verifier invoked exactly once per LOGON (`A04`/`A06`);
  re-authentication after LOGOFF works (`A05`); RUN after LOGOFF refused (`S21`).
- Confidentiality: synthetic sentinel credentials never appear in response text (`A03`); no
  credential value is rendered by the engine's responses.

## 7. F-Q1 investigation: `PULL(n=-1)` — CONFORMANT WITH QUALIFICATIONS

Probe checks `P01`–`P10` (cap = 3, >cap rows):

| Case | Result |
| --- | --- |
| finite `n` < cap | `n` records + `SUCCESS{has_more:true}` → Streaming |
| finite `n` = cap | cap records + `has_more:true` → Streaming |
| finite `n` > cap | **cap** records + `has_more:true` → Streaming (documented cap) |
| `n=-1` < cap | all rows + `has_more:false` → Ready |
| `n=-1` = cap | all rows + `has_more:false` → Ready |
| `n=-1` > cap | cap rows + `has_more:true` → Streaming, then remaining rows + `has_more:false` → Ready |
| zero remaining | `SUCCESS{has_more:false}` → Ready |
| provider fails mid-stream | prior RECORDs + FAILURE → Failed |
| PULL after terminal | FAILURE → Defunct (no second SUCCESS) |

**Semantic verdict:** the implementation never signals completion while undispatched records
remain (`has_more:false` only when the provider returns null), never exceeds the cap, preserves
record order across bounded passes, and streams incrementally (no whole-result buffering). The
bounded pass size is a legitimate server-side allocation limit expressed through the standard
`has_more` mechanism; a client that requested `n=-1` may need an additional PULL to obtain the
remainder — that is exactly the protocol's incremental delivery contract, not a redefinition of
`n=-1`. Classification: **CONFORMANT WITH QUALIFICATION** (documented cap; `MaxRecordsPerResponse`
is an internal limit, not a protocol change). Not a defect.

## 8. F-Q2 investigation: RESET interruption — bounded (B), safe (checks `R01`–`R08`)

| Scenario | Observed |
| --- | --- |
| RESET in Ready / Failed / Streaming | SUCCESS → Ready; active cursor disposed exactly once |
| Buffered look-ahead row present | RESET discards it; a following PULL in Ready → FAILURE → Defunct (no late RECORD) |
| Cursor disposal throws during RESET | FAILURE → Defunct (no false SUCCESS) |
| RESET before Ready | FAILURE → Defunct |
| Multiple RESETs | each SUCCESS → Ready |
| Blocked provider call | deterministic handshake proves the synchronous single-owner session cannot deliver RESET while a provider call is executing; RESET is observed only at sequential processing points (`R08`) |

**Verdict: classification B** — a bounded synchronous RESET recovery that is safe but does not
implement jump-ahead interruption, exactly as the producer states. No cancelled work executes, no
request is lost, no resource leaks, and no false SUCCESS is returned. This is an accepted
milestone limitation (the spec explicitly forbids claiming preemption of non-interruptible
backends); full jump-ahead will require an interruptible/cancellable provider contract and an
async/interruptible session API — a **B3-C/B5 dependency**, not a B3-B defect. It does not by
itself make the B3-B interface unable to support future conformance, provided the limitation stays
visible.

## 9. F-Q3 investigation: `ReceiveBatch` and pipelining — CONFORMANT WITH QUALIFICATIONS (`B*`)

- Ordered causality verified for `HELLO/LOGON/RUN/PULL`, `RUN/PULL/PULL/DISCARD`,
  `RUN→FAILURE/PULL→IGNORED/RESET→SUCCESS/RUN`, `RUN/PULL→FAILURE/RESET/RUN`, multiple RESETs.
- A recoverable FAILED state does **not** stop the batch: subsequent work is IGNORED and RESET
  recovers (`B03`), preserving causality and preventing a later success from bypassing FAILED.
- A terminal outcome (GOODBYE/Defunct) stops the batch (`B05`); the unprocessed requests are
  **not silently lost** — they remain in the caller's own list, and the returned envelope count is
  smaller than the request count (`B05b`/`B05c`), so a caller can recover the remainder.
- Oversized batch → `ArgumentOutOfRangeException` (bounded); at-capacity batch processes fully
  (`B08`/`B09`).
- Qualification (API ergonomics, not a defect): the batch result does not expose an explicit
  consumed/remaining count; ownership is conveyed implicitly by the envelope count and the
  caller-owned input. Recommend an explicit `ConsumedCount`/`Remaining` for B3-C/B5 clarity.

## 10. FAILED / IGNORED verification (`I*`)

- RUN/PULL/DISCARD/LOGOFF/TELEMETRY after a recoverable failure → **IGNORED**, state stays
  FAILED; the original failure is not overwritten (`I02`–`I07`).
- **IGNORED wire form is exactly `B0 7E`** (zero fields) — verified by encoding
  `IgnoredResponse.Instance` through the B3-A codec (`I01`), matching the spec and the B3-A
  acceptance; the historical one-field form is not reproduced.
- FAILURE metadata carries `gql_status`, `neo4j_code`, `description`, `diagnostic_record` — 5.7+
  shape. Producer-defined placeholder codes are documented as bounded placeholders, not Neo4j code
  fidelity (B0-B-Q1 D04 semantics preserved). The core transition (one FAILURE, then FAILED) is
  correct.

## 11. RUN/PULL/DISCARD cursor ownership (`C*`, `P08`, `R03`, `W*`)

Provider-observable counters confirm: no `Prepare` before authentication (`C01`); exactly one open
per RUN; a second RUN while STREAMING is refused and does **not** open another operation (`C02`/
`C03`); exactly-once disposal on completion, failure, discard exhaustion, RESET, GOODBYE and
disconnect (`C04`–`C06`, `P08b`, `R03b`); disconnect is idempotent and no late read occurs
(`W01`/`W02`/`W04`). Disposal is verified as an observable provider property, not an internal flag.

## 12. NC-05b and NC-08 findings (`N*`)

- **NC-05b:** a row whose value count differs from the RUN `fields` never emits a RECORD; earlier
  valid records are retained before the FAILURE; the cursor is disposed; state → FAILED (`N01`).
  No truncation or padding occurs. Correct-column results emit normally (`N02`).
- **NC-08:** a PULL after terminal completion is a protocol violation → FAILURE → Defunct, with no
  second SUCCESS and no RECORD (`N03`); DISCARD emits no RECORD (`N04`); no RECORD can precede a
  RUN (`N05`). No invalid sequence escapes the state machine in any probe.

## 13. TELEMETRY and unsupported operations (`T*`, `U*`)

- Valid identifiers 0–3 → SUCCESS → Ready in READY (`T0`–`T3`). Invalid identifiers (-1, 4, 9001,
  `long.MaxValue`) → FAILURE → Failed — **but see F-02** for the metadata.
- BEGIN/COMMIT/ROLLBACK/ROUTE → FAILURE → Defunct, never fabricated SUCCESS (`U01`–`U04`);
  unknown signatures and structurally malformed messages → FAILURE → Defunct (`U05`/`U06`),
  distinct from unsupported-but-valid features.
- **Seam gap (qualification, §11 of the commission):** a `TELEMETRY` with a wrong *wire type* is
  rejected by B3-A as a malformed message, so B3-B responds FAILURE → Defunct rather than the
  specification's FAILURE → FAILED for a valid structure with an invalid value type. Correct
  handling would require B3-A to expose the distinction; recorded as a gap rather than an
  unapproved codec change.

## 14. Provisional public API assessment

New public surface: `BoltConnectionSession`, `BoltConnectionState`, `BoltSessionOptions`,
`BoltSessionResult`, `BoltResponseEnvelope`, `IBoltCredentialVerifier`, `IBoltOperationProvider`,
`IBoltOperation`. Assessment:

1. Visibility is necessary: the test project is a separate assembly and the future B5 host must
   drive the session; `InternalsVisibleTo` was **correctly rejected** because it changed overload
   resolution for the existing B1 immutability tests (the two B1 tests are byte-unchanged and pass
   — re-verified by the full suite).
2. No mutable internal state is exposed (messages/responses/options are immutable or
   init-only; the operation results are read-only).
3. **`BoltConnectionSession` is not thread-safe and not documented as single-owner/non-concurrent.**
   The probe deliberately avoided concurrent access; this should be documented on the type (and, if
   B5 needs concurrent reads, a scheduler owned by the host). Qualification (documentation), not a
   defect.
4. The provider/operation pair is explicitly marked provisional, so B3-C is not frozen into it.
5. No test-only convenience has become a production contract (verifiers/providers are interfaces
   implemented in the test project).

## 15. B3-C integration readiness

The provider seam is **sufficient but minimal**: query preparation, field metadata, a finite
row source, discard-by-consumption and dispose cover a deterministic read-only graph backend;
pagination, contextual validation, error mapping and bounded buffering are owned by the session.
Gaps B3-C must address: read-only/capability enforcement at the backend boundary; an explicit
result-limit failure (today the cap is expressed as `has_more`, which is adequate for streaming but
does not let the backend *refuse* an over-limit request); a cancellation/interruption seam for
RESET jump-ahead; and richer run options. The abstraction is not overcommitted. Passing scripted
tests do not demonstrate real backend readiness.

## 16. Race and resource findings (`W*`)

Deterministic (event-coordinated, no sleeps) schedules exercised: disconnect during RUN and during
PULL, disconnect idempotency, GOODBYE during active result, reset after a completed cursor, late
result after Defunct. No duplicate disposal, no use-after-dispose, no read after dispose, no
response after Defunct, and no unauthorized execution were observed. **Scope is not exhaustive:**
the session is single-owner/synchronous, so true concurrent interleavings are not part of the
supported usage contract — the documented single-owner model must therefore be an explicit part of
the API contract (see §14.3).

## 17. Negative controls and falsification

The probe's checks are falsification attempts with independently predicted outcomes (118 checks, 8
failing = the findings). A producer-test-style pass was not sufficient: the two defects were found
only by *not* trusting the suite — deliberately injecting a throwing provider disposal and an
out-of-domain TELEMETRY identifier. Blind spots identified: (a) producer tests never inject a
throwing `Dispose` on the GOODBYE/disconnect path; (b) producer tests assert the TELEMETRY
*transition* but not the spec-prescribed status metadata; (c) producer tests do not exercise a
wrong-typed TELEMETRY through the byte path. No producer test was weakened.

## 18. Findings

### B3-B-Q1-F01 — Provider cleanup failure escapes the API and leaves the session non-terminal — **BLOCKING**

- **Severity:** moderate.
- **Reproduction (probe checks `X01`–`X04`):** a scripted operation whose `Dispose()` throws.
  `X01` GOODBYE during an active result, `X02` `Disconnect()`, `X03` session `Dispose()`, `X04`
  RUN whose provider returns an operation with `Fields == null`.
- **Expected:** the cleanup failure is contained; the connection terminates deterministically
  (`Defunct`, or `Failed` for the RUN-fields case) with a single disposal attempt and **no
  exception escaping the session API** (matching the RESET path, which already handles the same
  failure: check `X05` → FAILURE → Defunct).
- **Actual:** `InvalidOperationException` escapes `Receive`/`Disconnect`/`Dispose`; state remains
  `Streaming` (or `Ready`); the session is left non-terminal; `Session.Dispose()` is then not
  idempotent (`_disposed` never set) and a caller's protocol loop aborts.
- **Reference:** `docs/implementation-tasks.md` / commission §7 "deterministic handling of provider
  cancellation and exceptions"; commission §17 "provider throwing during cleanup"; §20
  "duplicate or missing resource cleanup".
- **Affected scope:** GOODBYE, transport disconnect, session dispose, RUN-fields-null — i.e. every
  cleanup path except RESET/PULL-completion.
- **Blocks acceptance:** yes.
- **Minimal remediation:** route all operation disposal through a guarded path that contains a
  throwing `Dispose` (catch → terminate to `Defunct`, or `Failed` for the recoverable RUN case),
  set terminal/disposed state before/independently of the disposal outcome, and make
  `Disconnect`/`Dispose` idempotent. One file (`BoltConnectionSession.cs`) plus tests.

### B3-B-Q1-F02 — Out-of-domain TELEMETRY identifier uses a type-error status — **BLOCKING (minor)**

- **Severity:** minor (metadata only; transition is correct).
- **Reproduction (probe checks `T-invalid-{−1,4,9001,long.MaxValue}-status`):**
  `TELEMETRY 9001` (and the other out-of-domain integers) in READY.
- **Expected (spec):** the Bolt 5.8 message specification's own example
  (`evidence/B3-B-IMPL-01/spec/message.adoc`, "Example 3", `C: TELEMETRY 9001`) returns
  `FAILURE {"neo4j_code": "Neo.DatabaseError.General.UnknownError", "gql_status": "50N00", …}` for
  an invalid **identifier value**. `22G03` is the specification's status for an invalid value
  **type** (Example 2, `TELEMETRY "oh no!"`).
- **Actual:** `gql_status 22G03` + `neo4j_code Neo.ClientError.Request.Invalid`.
- **Affected scope:** TELEMETRY invalid-identifier responses only.
- **Blocks acceptance:** yes (incorrect failure metadata for a claimed behaviour).
- **Minimal remediation:** use `50N00` + `Neo.DatabaseError.General.UnknownError` for
  out-of-domain identifiers; one metadata constant in `BoltConnectionSession.cs` plus an updated
  test assertion.

## 19. Remaining qualifications

1. **`PULL(n=-1)` bounded batching** — conformant with the documented `MaxRecordsPerResponse` cap
   (§7); a client may need an additional PULL. Not a defect.
2. **RESET without jump-ahead interruption** — classification B, safe; full conformance needs an
   interruptible provider + async session API (B3-C/B5 dependency) (§8).
3. **`ReceiveBatch` ownership ergonomics** — remainder preserved in the caller's list; recommend an
   explicit consumed/remaining count (§9).
4. **Wrong-typed TELEMETRY seam gap** — B3-A collapses type errors into "malformed", so B3-B
   cannot emit the spec's FAILURE→FAILED for that case; documented gap, no B3-A change proposed
   (§13).
5. **Session is single-owner/non-thread-safe and not documented as such** — document the contract
   (§14.3).
6. **Provisional public session/provider API** — acceptable before package stabilisation; marked
   provisional; B3-C may refine it (§14).
7. **Placeholder FAILURE codes** — bounded placeholders, not Neo4j code fidelity (§10).
8. **Two pre-existing B3-A build advisories** remain (not a clean-warning build) (§3).
9. **No cancellation token in the verifier/provider seam** — "cancelled verifier"/"cancellation
   during batch" cannot be represented; a limitation to carry into B3-C/B5.
10. Author-independence caveat retained.

## 20. Reviewer artifacts, manifest, commit

- Report: `docs/reviews/B3-B-Q1-independent-qualification.md`
- Probe: `docs/reviews/probes/B3-B-Q1/SessionProbe/` (118 checks; results in evidence)
- Evidence: `docs/reviews/evidence/B3-B-Q1/` (`core-solution-tests.log`, `session-tests.log`,
  `message-tests.log`, `integration-tests.log`, `core-build.log`, `environment.txt`,
  `probe/session-probe-results.json`, `session-probe-run.log`, `manifest.json`)
- Commit/tree: recorded in the final handoff and the evidence manifest.

**Owner recommendation:** **do not accept B3-B yet.** Authorise a bounded remediation of **F-01**
(guarded cleanup on all paths; idempotent disconnect/dispose) and **F-02** (correct TELEMETRY
invalid-identifier status), then a focused independent requalification (B3-B-Q2). Everything else
in the milestone is independently supported and may be accepted with the qualifications in §19;
the RESET jump-ahead and PULL-cap limitations should remain visible to B3-C/B5.
