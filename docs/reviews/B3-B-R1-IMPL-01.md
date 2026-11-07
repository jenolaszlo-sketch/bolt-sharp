# B3-B-R1-IMPL-01 — Session cleanup robustness and TELEMETRY status remediation

**Task:** B3-B-R1-IMPL-01 — bounded remediation of B3-B-Q1 findings F-01 and F-02
**Baseline (entry) HEAD:** `3caab5bafad0af651e5e066b29278606555388d6` / tree `e1399dcb0d2080323c529cf5986e14c60721cb71`
**Qualification under remediation:** B3-B-Q1 — REMEDIATION REQUIRED
**Implementation commit:** `454ae005245d6511b672f8962092829839fb7153` / tree `f93388ca7a23bcf727b6c76b8435d0b1f609f5ad`
**Protocol:** Bolt 5.8 / PackStream 1.0
**Producer executor:** GLM-5.3-Flash (`nvidia`, OpenCode). Not independent qualification; not owner
acceptance.

---

## 1. Starting HEAD/tree and preflight

```text
git branch --show-current -> main
git rev-parse HEAD        -> 3caab5bafad0af651e5e066b29278606555388d6 (matches the commission)
git rev-parse HEAD^{tree} -> e1399dcb0d2080323c529cf5986e14c60721cb71
git worktree list         -> one worktree (D:/projects/bolt-sharp); clean at entry
prior B3-B-R1 work        -> none; B3-B-Q1 artifacts present; nothing pushed
```

Historical baselines (from the B3-B-Q1 evidence, unchanged): core 1003/0/0, session 79/0, messages
98/0, B0-B integration 19/0/0.

## 2. Q1 findings and exact reproductions

The unmodified B3-B-Q1 reviewer probe was re-run against the entry commit
(`evidence/B3-B-R1-IMPL-01/q1-probe-before-fix/`): **118 checks, 8 failing**, exactly the two
findings:

- **F-01** (`X01`–`X04`): a provider `Dispose()` that throws escaped the API and left the session
  non-terminal — `GOODBYE`, `Disconnect()`, session `Dispose()` (all observed as
  `THREW InvalidOperationException;Streaming`) and `RUN` with null `Fields` (`THREW
  InvalidOperationException;Ready`).
- **F-02** (`T-invalid-{−1,4,9001,long.MaxValue}-status`): out-of-domain integer identifiers returned
  `22G03 | Neo.ClientError.Request.Invalid`.

## 3. Root-cause analysis

**F-01.** Operation disposal was performed by `DisposeOperationQuietly()`, which called
`operation.Dispose()` unguarded. Only the RESET and PULL-completion paths happened to run disposal
inside an exception boundary; `HandleGoodbye`, `Disconnect`, `Dispose` and the `RUN`-null-`Fields`
branch did not, so a throwing provider `Dispose()` propagated out of the session and the terminal
state assignment (`_state = Defunct`) never executed. `Session.Dispose()` also left `_disposed`
false, breaking idempotency.

**F-02.** `TelemetryFailure()` used `gql_status 22G03` (the specification's status for an invalid
value *type*) together with `Neo.ClientError.Request.Invalid`, whereas the Bolt 5.8 message
specification's own `TELEMETRY 9001` example returns `gql_status 50N00` with
`neo4j_code Neo.DatabaseError.General.UnknownError` for an out-of-domain **value**.

## 4. Production changes (one file)

`src/BoltSharp/Sessions/BoltConnectionSession.cs`:

1. **Guarded disposal.** `DisposeOperationQuietly()` replaced by `DetachAndDisposeOperation()` plus a
   static `DisposeGuarded(IBoltOperation?)`; a throwing provider `Dispose` is caught, recorded via
   `RecordCleanupFailure(...)` and returned — never propagated. Disposal is attempted at most once
   (the operation reference is detached before the call), so a failed attempt is not retried.
2. **Terminal-state safety.** `GOODBYE`, `Disconnect()` and `FailTerminal(...)` establish
   `Defunct` independently of the cleanup outcome; `Dispose()` sets `_disposed` in a `finally`, so it
   is idempotent and never throws from cleanup.
3. **RUN with null `Fields`.** The invalid result is never installed, never enters `Streaming`, and is
   disposed exactly once; a clean disposal yields the recoverable `FAILURE → Failed`, while a
   throwing disposal yields `FAILURE → Defunct` (terminal, never falsely usable). No empty field list
   is substituted.
4. **Primary-failure precedence.** `FailRecoverable`/`FailTerminal` no longer surface cleanup failures;
   the primary protocol failure is preserved. `CompleteResult` keeps the Q1-recoverable
   `FAILURE → Failed` behaviour when an exhausted result cannot be released. `HandleReset` still
   detects a cleanup failure and fails closed (`FAILURE → Defunct`).
5. **Diagnostic recording.** New read-only property
   `BoltConnectionSession.LastCleanupFailure` exposes the most recent contained cleanup failure
   (null on clean paths) — the documented surfacing mechanism; it never changes a protocol outcome.
6. **F-02.** `TelemetryFailure()` now emits `gql_status 50N00` and
   `neo4j_code Neo.DatabaseError.General.UnknownError` (description and `diagnostic_record`
   unchanged; state transition to `Failed` preserved).

No change to: HELLO/LOGON/LOGOFF sequencing, fail-closed verification, RUN/PULL/DISCARD behaviour,
`PULL(n=-1)` bounded streaming, NC-05b/NC-08, FAILED/IGNORED, RESET recovery semantics, pipelining,
GOODBYE no-response semantics, B3-A contracts, PackStream/Framing/Handshake.

## 5. New tests

`tests/BoltSharp.Tests/Sessions/CleanupAndTelemetryRemediationTests.cs` (new, 20 test cases) plus a
corrected assertion in the existing `Telemetry_InvalidIdentifier_IsFailure_AndFailed` theory (now
asserting `50N00` + `Neo.DatabaseError.General.UnknownError`, and extended with `long.MaxValue`).

F-01 coverage: GOODBYE/Disconnect/Dispose with a throwing `Dispose` (Defunct, one attempt, no
response, recorded); `Dispose` idempotency; GOODBYE→Disconnect, Disconnect→Dispose, repeated
Dispose, GOODBYE×2 (no repeated disposal); late operation after terminal cleanup (refused, provider
not re-opened); RUN null-`Fields` with clean and throwing disposal; RUN null-`Fields` never streams;
RESET cleanup failure fails closed; PULL-completion cleanup failure stays `FAILURE → Failed`;
primary provider failure + cleanup failure; row-shape failure + cleanup failure (primary preserved);
cleanup recorded vs null.

F-02 coverage: invalid integers (`-1`, `4`, `9001`, `long.MaxValue`) exact status/code/description;
FAILED→IGNORED→RESET recovery; wrong-typed TELEMETRY rejected by the B3-A codec
(`InvalidFieldType`) before session processing.

## 6. Cleanup invariants and observed counters

`evidence/B3-B-R1-IMPL-01/q1-probe-after-fix/` (unmodified Q1 probe, remediated assembly):
**118 checks, 1 failing** — the single remaining mismatch is the historical probe's stale
pre-remediation expectation for `X04`, which expected the null-`Fields` cleanup failure to leave a
*recoverable* `Failed` session:

```
X04-run-null-fields-dispose-throws:
  stale expected = [no-throw;Failed]
  observed       = [no-throw;Defunct;FAILURE]
```

The observed behaviour is the safer outcome the F-01 finding required (terminal, never falsely
usable) and is an **intended remediation difference**, not a regression. All other F-01 checks now
report `no-throw;Defunct;disposeCount=1` (`X01`–`X03`) and `FAILURE/Defunct` (`X05` RESET control).

Cleanup invariants observed: each owned resource receives exactly one disposal attempt across every
terminal combination; repeated cleanup never revives a session; the session stays `Defunct`; the
original failure is never overwritten; no provider operation executes after terminal cleanup.

## 7. TELEMETRY response metadata (F-02)

| Identifier | Observed |
| --- | --- |
| 0,1,2,3 (READY) | `SUCCESS` → `Ready` (unchanged) |
| −1, 4, 9001, `long.MaxValue` | `FAILURE {gql_status: 50N00, neo4j_code: Neo.DatabaseError.General.UnknownError, description, diagnostic_record}` → `Failed` |
| non-integer field | B3-A codec rejects with `InvalidFieldType` (session byte path → `FAILURE` → `Defunct`) |

## 8. Regression test counts

| Set | Command | Result |
| --- | --- | --- |
| Core solution | `dotnet test BoltSharp.slnx -c Debug` | **1025 passed / 0 failed / 0 skipped** (1003 + 22 new) |
| Session tests | `dotnet test … --filter FullyQualifiedName~BoltSharp.Tests.Sessions` | **101 passed / 0 failed** |
| B3-A message tests | `dotnet test … --filter FullyQualifiedName~BoltSharp.Tests.Messages` | **98 passed / 0 failed** |
| B0-B integration | `dotnet test tests/BoltSharp.IntegrationTests/… -c Debug` | **19 passed / 0 failed / 0 skipped** |
| Build | `dotnet build BoltSharp.slnx -c Debug --no-incremental` | 0 errors; **2 warnings** |

No historical test was weakened. The two build warnings are the pre-existing `xUnit2012` advisories
in the unchanged B3-A file `tests/BoltSharp.Tests/Messages/BoltRequestDecodeTests.cs`; the
remediation introduces no new warnings.

## 9. Change isolation

`git diff 3caab5b..454ae00` touches exactly: `src/BoltSharp/Sessions/BoltConnectionSession.cs`,
`tests/BoltSharp.Tests/Sessions/AuthenticationAndTransitionTests.cs`,
`tests/BoltSharp.Tests/Sessions/CleanupAndTelemetryRemediationTests.cs`. No PackStream/Framing/
Handshake change, no B3-A schema/contract change, no solution/config change, no new dependency, no
public API redesign (one additive diagnostic property `LastCleanupFailure`). Historical B3-B-Q1
report, probe and evidence were not modified (Q1 evidence manifest still verifies 12/12).

## 10. Evidence, manifest and commits

- Report: `docs/reviews/B3-B-R1-IMPL-01.md`
- Evidence: `docs/reviews/evidence/B3-B-R1-IMPL-01/` (`q1-probe-before-fix[/-run.log]`,
  `q1-probe-after-fix[/-run.log]`, `core-solution-tests.log`, `session-tests.log`,
  `message-tests.log`, `integration-tests.log`, `core-build.log`, `environment.txt`,
  `manifest.json`)
- Implementation commit: `454ae005245d6511b672f8962092829839fb7153` (tree
  `f93388ca7a23bcf727b6c76b8435d0b1f609f5ad`, parent `3caab5b…`)
- Evidence commit: recorded in the final handoff / evidence manifest.

## 11. Historical evidence integrity

B3-B-Q1 report, probe source and evidence remain byte-intact; the B3-B implementation report and
evidence, B3-A artifacts, B0-B evidence/fixtures and both owner decisions are unchanged. The Q1
probe was executed unmodified against a fresh output directory.

## 12. Remaining qualifications

All B3-B-Q1 qualifications are preserved: bounded `PULL(n=-1)``, non-jump-ahead RESET,
`ReceiveBatch` ownership ergonomics, the wrong-typed-TELEMETRY seam gap (B3-A boundary), the
undocumented single-owner/non-thread-safe session contract, the provisional public API, placeholder
FAILURE codes, and the two pre-existing build advisories. The new `LastCleanupFailure` diagnostic is
additive; it does not alter any protocol outcome.

## 13. Independent B3-B-Q2 handoff

Q2 should focusedly establish: GOODBYE/Disconnect/Dispose reach `Defunct` despite throwing cleanup
with exactly-once disposal attempts; RUN-null-`Fields` cannot leave a usable active cursor (terminal
when cleanup fails); earlier failures retain precedence; RESET cleanup failure still fails closed;
invalid integer TELEMETRY identifiers produce `50N00`/`UnknownError` while valid identifiers and the
FAILED/IGNORED/RESET sequence are unchanged and wrong-typed input is still rejected by B3-A;
historical tests pass and Q1 evidence is unchanged. Q2 must reclassify the `X04` stale expectation
as an intended remediation difference.
