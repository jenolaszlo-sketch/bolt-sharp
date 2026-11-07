# B3-B-R2-IMPL-01 — Resource ownership, metadata acquisition and fail-closed recovery

**Task:** B3-B-R2-IMPL-01 — bounded remediation of B3-B-Q2-F01 and B3-B-Q2-F02
**Baseline (entry):** HEAD `4ae87b4f944a5c16cbd90a734b1ebfddc9fe0236` / tree `f4d2ebb606ac2c34f2b5ffe6e39eb700d71e7852`
**Implementation commit:** `7eb9a8fcee5d4d89d333a920a6c3307e84ad4210` / tree `c62e9d21ea578c4186bf34ca0b9eb280055eae4d`
**Protocol:** Bolt 5.8 / PackStream 1.0
**Producer executor:** DeepSeek V4.1 Flash (provider `nvidia`, OpenCode). This is producer evidence:
not independent qualification (B3-B-Q3) and not owner acceptance.

## 1. Preflight and starting state

| Item | Verified value |
| --- | --- |
| Branch | `main` |
| Starting HEAD | `4ae87b4f944a5c16cbd90a734b1ebfddc9fe0236` (matches commission) |
| Starting tree | `f4d2ebb606ac2c34f2b5ffe6e39eb700d71e7852` (matches commission) |
| Worktrees | Exactly one, canonical checkout |
| Working tree / index | Clean; no untracked files; no concurrent writer |
| Prior R2 work | None; `docs/reviews/B3-B-R2-IMPL-01.md` and its evidence did not exist |
| Push | None; `origin/main` remains `9abf5e3724a31aff085b3810ebb9bd3a7783ea2a` |

Read before editing: `docs/reviews/B3-B-IMPL-01.md`, `B3-B-Q1-independent-qualification.md`,
`B3-B-R1-IMPL-01.md`, `B3-B-Q2.md`, the complete `docs/reviews/evidence/B3-B-Q2/` set (including
the unchanged Q2 probe source and `q2-probe-results.json`), the Q1 probe under
`docs/reviews/probes/B3-B-Q1/SessionProbe/`, the production session sources, all six existing
session test files, the B3-A owner acceptance and the B3-B acceptance requirements. The actual
source was inspected before the change was chosen.

## 2. Q2-F01 root cause

Retirement outcomes were recorded (`LastCleanupFailure`) but **not authoritative**: neither the
exhausted-result path nor the recoverable-failure path consulted the failed-retirement result when
choosing the next state.

- `CompleteResult` (PULL/DISCARD exhaustion) called `DetachAndDisposeOperation()`, discarded its
  result and unconditionally entered `Failed` (reporting `FAILURE`, staying open).
- `FailRecoverable` (read failure, row-shape failure, metadata failure) did the same.
- After the failed attempt the operation reference was already detached, so a later `RESET` found
  "no outstanding work", returned `SUCCESS`, and restored `READY`.
- A subsequent `RUN` therefore opened another provider operation while the first resource was
  still live: the reviewer-observed `DisposeAttempts=1; ResourceLive=true; RESET→SUCCESS/Ready;
  opens=2`.

The defect existed on five reachable paths: PULL exhaustion, DISCARD exhaustion, PULL read
failure, DISCARD read failure and RECORD column-shape failure.

## 3. Q2-F01 correction

One consistent session retirement mechanism, and an authoritative result at every call site
(`src/BoltSharp/Sessions/BoltConnectionSession.cs`):

- `DetachAndDisposeOperation()` remains the single retirement point; a non-null return means the
  one permitted `Dispose()` attempt did not complete.
- `CompleteResult` and `FailRecoverable` now branch on that result. Failed retirement sets
  `BoltConnectionState.Defunct` with `ShouldClose=true` and still reports the **primary FAILURE
  exactly once** (never a SUCCESS summary). Successful retirement preserves the previously
  qualified recoverable behavior (`Failed`, `ShouldClose=false`, RESET permitted).
- No new state type or latch was introduced: `Defunct` is already terminal for every transition,
  and `Receive`/`ReceiveBytes` return a no-response `None(Defunct, shouldClose:true)` for every
  later message. Once set it can never be cleared, so the fail-closed condition is monotonic by
  construction. `RESET`, `LOGON`, `RUN`, `PULL`, `GOODBYE` on a failed-retirement session emit no
  response and cannot reactivate it; `Disconnect()`/`Dispose()` are idempotent and never retry the
  disposal.
- `HandleReset`, `FailTerminal`, `HandleGoodbye`, `Disconnect` and `Dispose` were already
  fail-closed for failed retirement and are unchanged.

Normal success behavior is untouched: a clean retirement still returns `Ready` (completion),
`Failed` (recoverable failure), or `Defunct` (protocol/terminal failures) exactly as before.

## 4. Q2-F02 root cause

`IBoltOperationProvider.Prepare()` results were not owned before metadata access:

- `operation.Fields is null` was evaluated **outside any exception boundary**; a throwing getter
  escaped `HandleRun`/`Receive` with the session still `Ready`, the returned handle unowned, and
  **zero** disposal attempts — even after disconnect and session disposal (the local handle was
  unreachable from the session).
- Successful validation assigned `_operation` and set `Streaming` **before** a second
  `operation.Fields` read used to build the RUN response; a later getter/null-element throw escaped
  after installation, leaving `Streaming` without any RUN response having been returned.
- Column validation in `HandlePull` re-read `_operation.Fields` on every PULL, touching the
  untrusted provider property after publication.

## 5. Q2-F02 correction

In `HandleRun`:

1. **Immediate ownership.** On a non-null `Prepare` return, `_operation` is assigned before any
   provider metadata is touched. Every later failure retires the handle through the same single
   retirement path.
2. **One acquisition, one snapshot.** `TryCaptureFields` reads `Fields` exactly once, contains
   null metadata, null field names, and any exception thrown while reading (including `Count` or
   indexer access on custom/deferred lists), and converts the names into an immutable
   `PackStreamList`. This is the only place the session reads `IBoltOperation.Fields`.
3. **Publication boundary.** `_fieldCount` is taken from the snapshot and `Streaming` is entered
   only after capture succeeds; the RUN SUCCESS response is built from the same snapshot.
4. **Failure behavior.** Capture failure returns the existing provider FAILURE and retires the
   handle exactly once (`FailRecoverable`): clean retirement → recoverable `Failed` (RESET is
   permitted); failed retirement → `Defunct`. A valid empty field list remains a valid zero-column
   result; `null` metadata remains invalid — the two are never conflated.
5. **No re-reads.** NC-05b column validation compares against the stored RUN-time `_fieldCount`
   instead of touching `operation.Fields` again.

The `IBoltOperation` ownership contract in `BoltSessionOptions.cs` was updated to document this
session-owned, single-acquisition, fail-closed model. No public API surface, signature or
behaviour of the type was removed or renamed.

## 6. Required invariants — implementation mapping

| Invariant | Where established |
| --- | --- |
| INV-01 immediate ownership | `HandleRun`: `_operation = operation` before `TryCaptureFields` |
| INV-02 exactly one owner | only `_operation` holds a handle; retirement only via `DetachAndDisposeOperation` |
| INV-03 one retirement attempt | detach-before-call; every cleanup path nulls the reference first |
| INV-04 failed retirement irreversible | failed retirement sets terminal `Defunct`; `Defunct` has no outgoing transitions |
| INV-05 no false recovery | no path sets `Ready`/`Failed` after a failed retirement result |
| INV-06 no unsafe replacement | `Defunct` rejects `RUN` before `Prepare`; provider opens stay 1 (traces below) |
| INV-07 validation precedes publication | `TryCaptureFields` + `_fieldCount`; `Streaming` only on success |
| INV-08 failure precedence | primary FAILURE emitted; cleanup exception only in `LastCleanupFailure` |
| INV-09 terminal response correctness | no SUCCESS completion or RESET SUCCESS after failed retirement |
| INV-10 single-owner model | no new concurrency, no async/cancellation changes |

## 7. Acquisition-to-retirement transition table

| Stage | Owner after stage | Disposal attempts | State / response |
| --- | --- | --- | --- |
| `Prepare` throws | provider (nothing returned) | 0 | `FAILURE`/`Failed`, open |
| `Prepare` returns null | provider | 0 | `FAILURE(50N42)`/`Failed`, open |
| Acquired; metadata invalid; clean retirement | retired | 1 | `FAILURE(50N00)`/`Failed`, open |
| Acquired; metadata invalid; retirement throws | detached | 1 | `FAILURE(50N00)`/`Defunct`, close |
| Metadata valid | session (`_operation`, `_fieldCount`) | 0 | `SUCCESS{fields}`/`Streaming` |
| PULL/DISCARD partial | session (look-ahead retained) | 0 | `SUCCESS{has_more}`/`Streaming` |
| PULL/DISCARD exhaustion, clean | retired | 1 | `SUCCESS` summary/`Ready` |
| PULL/DISCARD exhaustion, retirement throws | detached | 1 | `FAILURE(50N00)`, no SUCCESS/`Defunct`, close |
| Read failure, clean | retired | 1 | prior RECORDs + `FAILURE`/`Failed`, open |
| Read failure, retirement throws | detached | 1 | prior RECORDs + `FAILURE`/`Defunct`, close |
| Column-shape failure, clean | retired | 1 | `FAILURE(50N42)`/`Failed`, open |
| Column-shape failure, retirement throws | detached | 1 | `FAILURE(50N42)`/`Defunct`, close |
| RESET, clean | retired | ≤1 | `SUCCESS`/`Ready` |
| RESET, retirement throws | detached | 1 | `FAILURE(50N00)`/`Defunct`, close |
| GOODBYE / Disconnect / Dispose | detached | ≤1 | no response/`Defunct`; never retried |
| Any message after failed retirement | detached | 0 | no response/`Defunct` |

## 8. Cleanup-failure persistence and recovery refusal

The unchanged Q2 probe, executed against R2, records the exact ordered trace for the original
defect (`pull-complete` scenario, `q2-probe-after.json`):

| Action | Response | State | Opens | Disposal attempts | Resource live |
| --- | --- | --- | ---: | ---: | --- |
| RUN | `SUCCESS` | `Streaming` | 1 | 0 | true |
| PULL (exhaustion, retirement throws) | `RECORD,FAILURE` | **`Defunct`**, close | 1 | **1** | true |
| RESET | *(no response)* | `Defunct` | 1 | 1 | true |
| RUN | *(no response)* | `Defunct` | **1** | 1 | true |
| Disconnect | *(void)* | `Defunct` | 1 | 1 | true |
| Dispose | *(void)* | `Defunct` | 1 | 1 | true |

The key negative assertion now holds on all five paths: after failed cleanup `opens` remains
**1**, never 2, and `RESET` never returns `SUCCESS`/`READY`. Exactly one disposal attempt is made;
teardown never retries it.

## 9. Metadata failure handling

| Injected condition | Observed (R2) |
| --- | --- |
| First `Fields` getter throws; disposal succeeds | contained `FAILURE(50N00)`; `Failed`; 1 attempt; no cursor; RESET re-permits RUN |
| First getter throws; disposal throws | contained `FAILURE`; `Defunct`+close; 1 attempt; diagnostic recorded; RUN/RESET refuse, `opens` stays 1 |
| Null `Fields` (R1-preserved) | clean → `Failed`; throwing → `Defunct`; one attempt |
| Null field element; clean / throwing disposal | `Failed` / `Defunct`; never `Streaming`; one attempt |
| Getter called repeatedly (deferred/internal state) | getter read exactly **once** per acquisition, across RUN→PULL→completion |
| Metadata changes between reads; row then mismatches snapshot | NC-05b failure against the RUN-time snapshot (`FieldsReads==1`); no record emitted |
| `Count` or indexer throws on the returned list | contained `FAILURE`/`Failed`; one attempt |
| Valid empty field list | `SUCCESS{fields:[]}`/`Streaming` unchanged; zero-column result supported |
| Valid metadata | unchanged: `SUCCESS{fields}`/`Streaming`, then normal PULL/DISCARD |

## 10. Primary and secondary failure precedence

| Trigger | Protocol primary | Cleanup diagnostic | Final state |
| --- | --- | --- | --- |
| PULL/DISCARD exhaustion + retirement throws | `50N00` / `Neo.DatabaseError.General.UnknownError` | disposal exception (reference) | `Defunct` |
| Read failure + retirement throws | `50N00` / `UnknownError` | disposal exception | `Defunct` |
| Column-shape failure + retirement throws | `50N42` / `UnknownError`, exact row-shape description | disposal exception | `Defunct` |
| Metadata failure + retirement throws | `50N00` / `UnknownError` | disposal exception | `Defunct` |
| Any of the above with clean retirement | unchanged primary FAILURE | none | `Failed` (recoverable) |

Both facts remain independently observable: the FAILURE metadata carries the primary
classification; `LastCleanupFailure` returns the exact disposal exception instance (the same
reference the provider threw, assigned without reading `Message`). Cleanup failure never replaces
the primary response and never produces a second terminal summary.

**Code-classification limitation (documented):** the protocol contract defines no dedicated
"cleanup did not complete" GQL status. R2 therefore reuses the existing bounded
`ProviderFailure` (`50N00`/`UnknownError`) and `RowShapeFailure` (`50N42`/`UnknownError`)
constructions; this is the same bounded placeholder classification retained from Q1 §19.7 and no
new status was invented.

## 11. Exactly-once disposal evidence

- Q2 probe (unchanged) after R2: every `DisposeAttempts` counter equals 1 exactly where a
  retirement occurred, and remains 1 across RESET/RUN/GOODBYE/Disconnect/Dispose sequences
  (traces in `q2-probe-after.json`); partial PULL/DISCARD passes leave the counter at 0 until the
  operation is actually retired.
- New R2 tests assert the counter immediately after the failing operation and after every
  subsequent terminal message (`FailedCleanup_ThenTerminalMessages_NoSecondAttempt`,
  `FailedCleanup_ThenResetLogonRun_NeverRecovers_NeverReopens`,
  `Metadata_FirstGetterThrows_ThrowingDispose_Defunct_OneAttempt_NoReuse`).
- The probe observes detachment ordering directly: `DisposeObservedOwned=false` at the provider
  disposal call, i.e. the session reference is cleared before the attempt, so a throwing
  `Dispose` cannot be retried by a later detach.

## 12. Before/after Q2 probe results

The historical B3-B-Q2 probe was executed **unmodified**. Retained before-result:
`docs/reviews/evidence/B3-B-Q2/q2-probe-results.json`, SHA-256
`1ef748c7ebffb821e867c78ab2c409d65b06cdd4e2527dcc7cefe8e9f635f73d` (verified 38/38 manifest
match). After-result: `q2-probe-after.json` in this evidence directory.

| Run | Checks | Passed | Failed | Trace steps | Exit |
| --- | ---: | ---: | ---: | ---: | ---: |
| Before R2 (retained Q2 evidence) | 181 | 163 | 18 | 217 | 4 |
| After R2 (unchanged probe) | 181 | 163 | 18 | 217 | 4 |

The totals are numerically identical but the failure sets are disjoint in meaning; the
machine-readable classification is `q2-probe-before-after.json`:

| Transition | Count | Checks |
| --- | ---: | --- |
| `pass-pass` | 152 | all other checks, including the whole 48-entry state matrix |
| **`resolved`** | **11** | five `*-no-overlap` (`opens` must stay 1); `run-metadata-first-getter-{contained,cleanup,no-reopen,eventual-attempt}`; `run-metadata-null-element-{contained,cleanup}` |
| `superseded` | 11 | five `*-primary`; five `*-ignored`; `diagnostic-latest` |
| `fail-fail` | 7 | five `*-safe-reset`; `run-metadata-second-getter-{contained,cleanup}` |

Individual explanations — **no unresolved safety failure exists in any group**:

- **Resolved (11).** The two Q2 defects themselves: failed cleanup can no longer be followed by
  reopening (`opens` stays 1) and metadata failures are contained with one guarded retirement.
- **Superseded `*-primary` (5).** These checks require `State==Failed` after the failing
  PULL/DISCARD plus a preserved primary FAILURE payload. R2 (per the commission §4.5/§4.6 and
  §6.1) now establishes `Defunct` at that moment; the FAILURE payload component still matches (the
  recorded actual shows the exact primary description). The state expectation is stale, and the
  payload is independently asserted by the new R2 tests.
- **Superseded `*-ignored` (5).** The probe sends RUN after the failure expecting the pre-R2
  `Failed`-state `IGNORED`. Under R2 the session is already `Defunct`, so no work message is
  processed at all (`opens` visible as 1) — the stronger behaviour that §4.3 requires. Expectation
  stale.
- **Superseded `diagnostic-latest` (1).** The scenario needs a *later* cleanup failure to replace
  an earlier diagnostic; that requires recovery plus a new acquisition after a failed retirement,
  which R2 forbids by design. The scenario is unreachable, not failing: the diagnostic is retained
  through the terminal transition (asserted by `-mutable-alias` and `-retained`, both pass).
- **Fail-fail `*-safe-reset` (5).** The check requires `IsDefunct` **and** the literal response
  string `FAILURE` from the post-failure RESET. The `IsDefunct` half holds; the response half is
  stale because the session is already terminal when RESET arrives, and the Defunct contract
  (qualified in Q1/Q2) is "no response to any message". Safety — no READY recovery, no reopening —
  holds and is independently asserted by the resolved `*-no-overlap` checks and the R2 tests.
- **Fail-fail `run-metadata-second-getter-*` (2).** The scenario is constructed to make the
  *second* `Fields` access throw. Single acquisition (INV-07) removes the second access, so the
  (valid) first and only read produces a normal RUN SUCCESS; the checks' premise no longer exists.
  This is the designed elimination of the defect class, asserted by
  `Metadata_GetterReadExactlyOnce_AcrossFullLifecycle` and
  `Metadata_ChangingBetweenReads_ColumnCheckUsesRunTimeSnapshot`.

The Q2 probe was not edited to obtain these results, and no failure is classified as an accepted
safety defect. The historical Q1 probe also runs unchanged: **118 checks / 117 passing**, exit 4,
with only the historical X04 expectation (`no-throw;Failed` vs the R1-established
`no-throw;Defunct;FAILURE`) — identical before and after R2.

## 13. New regression tests and updated expectations

New file `tests/BoltSharp.Tests/Sessions/ResourceLifecycleRemediationTests.cs`
(`ResourceLifecycleRemediationTests`, 18 tests):

- F01: `PullCompletion_ThrowingDispose_NoSuccessDefunct_OneAttempt`;
  `PullReadFailure_ThrowingDispose_PreservesPrimary_AndRecordsCleanup_Defunct`;
  `PullBadColumnCount_ThrowingDispose_NoRecord_RowShapePrimary_Defunct`;
  `DiscardCompletion_ThrowingDispose_NoFalseSuccess_Defunct`;
  `DiscardPartial_ThenThrowingCompletion_Defunct_NoRetry`;
  `FailedCleanup_ThenResetLogonRun_NeverRecovers_NeverReopens` (asserts verifier not re-invoked,
  `PrepareCount==1`, no late RECORD);
  `FailedCleanup_ThenTerminalMessages_NoSecondAttempt`;
  `ReadFailure_CleanDispose_RemainsRecoverable_ResetAndRunPreserved`;
  `PullCompletion_CleanDispose_CompletesNormally`;
  `RowShapeFailure_CleanDispose_RemainsRecoverable`.
- F02: `Metadata_FirstGetterThrows_CleanDispose_ContainedFailureFailed_OneAttempt` (incl.
  re-acquisition after clean retirement);
  `Metadata_FirstGetterThrows_ThrowingDispose_Defunct_OneAttempt_NoReuse`;
  `Metadata_NullElement_CleanDispose_Failed_NoStreaming_OneAttempt`;
  `Metadata_NullElement_ThrowingDispose_Defunct_OneAttempt`;
  `Metadata_GetterReadExactlyOnce_AcrossFullLifecycle`;
  `Metadata_ChangingBetweenReads_ColumnCheckUsesRunTimeSnapshot`;
  `Metadata_ThrowingListMembers_AreContained` (throwing `Count` and throwing indexer);
  `Metadata_ValidEmptyFields_RunSucceeds_Unchanged`.

Every test asserts state, response sequence, open count, disposal-attempt count, active-ownership,
diagnostic information and whether later RESET/RUN is permitted — not merely the absence of an
exception.

Updated in `CleanupAndTelemetryRemediationTests.cs` (R1 file; the failed-retirement expectation
was superseded by this task's contract, never weakened):

- `PullCompletion_WithThrowingDispose_IsFailureDefunct_OneAttempt_NoRecovery` (was
  `…IsFailureFailed…`): now also asserts no SUCCESS response, `ShouldClose`, and RESET refusal.
- `PrimaryProviderFailure_ThenCleanupFailure_KeepsPrimaryAndRecordsCleanup`: `Defunct` now, plus
  the exact cleanup diagnostic message.
- `RowShapeFailure_ThenCleanupFailure_KeepsRowShapePrimary`: `Defunct` now, plus the exact cleanup
  diagnostic message.

No other existing test was modified. The class-level comment records the supersession.

## 14. Regression counts

Environment: Windows 10.0.26200 / x64, .NET SDK 10.0.401, MSBuild 18.9.11; projects target net8.0
(`environment.txt`).

| Execution | Passed | Failed | Skipped | Notes |
| --- | ---: | ---: | ---: | --- |
| Core test project | **1,043** | 0 | 0 | 1,025 pre-R2 + 18 new R2 tests; floor 1,025 exceeded |
| Session filter | **119** | 0 | 0 | 101 pre-R2 + 18 new; floor 101 exceeded |
| R1 remediation class | 21 | 0 | 0 | subset of sessions, unchanged count |
| R2 lifecycle class | 18 | 0 | 0 | new class, all pass |
| B3-A message filter | 98 | 0 | 0 | floor 98 met, unchanged |
| B0-B integration | 19 | 0 | 0 | floor 19 met, unchanged |
| Unchanged Q1 probe | 117 | 1 | 0 | 118 checks; only historical X04; exit 4 (unchanged) |
| Unchanged Q2 probe | 163 | 18 | 0 | 181 checks / 217 trace steps; superseded/stale set per §12 |

No existing test was excluded, skipped or weakened.

## 15. Builds and warnings

- Core full rebuild: **2 warnings, 0 errors** — both pre-existing `xUnit2012` advisories at
  `tests/BoltSharp.Tests/Messages/BoltRequestDecodeTests.cs:71` and `:72`. No new warning is
  attributable to R2.
- Integration build and both historical probe builds: **0 warnings, 0 errors**.
- Full logs: `core-build.log`, `integration-build.log`, `q1-probe-build.log`, `q2-probe-build.log`.

## 16. Historical manifest verification

All manifests were verified in place and none was modified:

| Manifest | Match | Delta |
| --- | ---: | --- |
| B3-B-IMPL-01 | 19/22 | 3 expected source/test evolutions (§ below) |
| B3-B-Q1 | 12/12 | — |
| B3-B-R1-IMPL-01 | 12/14 | 2 entries changed by R2 |
| B3-B-Q2 | 38/38 | Q2 evidence fully intact, including the 181-check before-trace |
| B3-A-IMPL-01 | 16/21 | documented B3-A-R1 informational delta (unchanged from Q1) |
| B3-A-Q1 | 14/14 | — |
| B3-A-R1-IMPL-01 | 13/13 | — |
| B3-A-Q2 | 15/15 | — |
| B0-B-DRV-INT-01 (producer) | 24/24 | — |
| B0-B-Q1 | 119/119 | — |

The five non-matching entries are exactly the files legitimately changed by the authorized
remediation, recorded rather than concealed:

| Manifest entry | Expected (at manifest time) | Actual (R2) | Changed by |
| --- | --- | --- | --- |
| `src/BoltSharp/Sessions/BoltConnectionSession.cs` | `53c1274c…` | `5fa1028e…` | R1 (`454ae00`) then R2 (`7eb9a8f`) |
| `src/BoltSharp/Sessions/BoltSessionOptions.cs` | `77497dda…` | `ff59dd03…` | R2 (ownership contract documentation) |
| `tests/…/AuthenticationAndTransitionTests.cs` | `1d8c35b4…` | `ad737aa9…` | R1 (TELEMETRY assertion) |
| R1: `src/…/BoltConnectionSession.cs` | `4f7dc3b4…` | `5fa1028e…` | R2 |
| R1: `tests/…/CleanupAndTelemetryRemediationTests.cs` | `e92e9177…` | `7de3651b…` | R2 (superseded expectations updated) |

Full SHA-256 lists are in `historical-integrity.json`; summary counts in
`historical-integrity-summary.txt`. R1's own report recorded the same pattern for B3-A-IMPL-01;
this is the accepted mechanism for authorized source evolution, not an integrity failure. The Q2
manifest still verifies 38/38, so the reviewer evidence (including `q2-probe-results.json`,
SHA-256 `1ef748c7…`) is unchanged.

## 17. Source and test change isolation

Implementation commit `7eb9a8f` touches exactly four files:

- `src/BoltSharp/Sessions/BoltConnectionSession.cs` (production fix + XML docs)
- `src/BoltSharp/Sessions/BoltSessionOptions.cs` (XML documentation of the ownership contract)
- `tests/BoltSharp.Tests/Sessions/CleanupAndTelemetryRemediationTests.cs` (three superseded
  expectations updated and strengthened)
- `tests/BoltSharp.Tests/Sessions/ResourceLifecycleRemediationTests.cs` (new, 18 tests)

No PackStream, framing, handshake, B3-A message schema, solution build property or dependency
change; no other session file changed; the public API surface is unchanged (documentation only).
`r2-implementation-delta.patch` and `baseline.json` contain the exact delta and file list.

## 18. Diagnostics and confidentiality

`LastCleanupFailure` behavior is unchanged in mechanism and now explicitly documented as
surviving the terminal transition: recording assigns the provider exception reference without
reading `Message` (the unchanged Q2 `hostile-exception-diagnostic` check passes), no protocol
response contains provider text, no automatic logging or exception serialization was added, and
`RESET` cannot clear the diagnostic — after a failed retirement it cannot reach the code that
would clear anything because the session is terminal. A clean retirement still leaves the property
unchanged (most-recent-failure semantics retained).

## 19. Remaining qualifications

- All ten B3-B-Q1 §19 qualifications remain visible and unchanged: bounded `PULL(n=-1)`
  batching; non-jump-ahead RESET; implicit batch-remainder ownership; wrong-typed TELEMETRY seam
  gap; single-owner/non-thread-safe session not explicitly documented as such (still open);
  provisional session/provider API; bounded placeholder error-code fidelity; the two `xUnit2012`
  advisories; no cancellation-token seam; historical author-independence caveat.
- The R2 contract change **supersedes** one R1-era expectation: an exhausted result whose
  `Dispose()` throws no longer leaves a recoverable `Failed` session; it is permanently `Defunct`.
  This is required by B3-B-Q2-F01 and the R2 commission (§4.5–§4.6, §6.1) and is recorded here and
  in the updated tests — not silently applied.
- The unchanged Q2 probe's remaining expectations are stale in the precise sense classified in
  §12; the probe itself must not be edited historically. A future reviewer probe should expect
  `Defunct` immediately on failed retirement, no response to later messages, and `opens==1`.
- `diagnostic-latest` (replacement of an earlier diagnostic by a later cleanup failure) is now
  reachable only through clean-first-failure sequences; the retention/replacement semantics
  themselves are unchanged and remain asserted by the passing checks.

## 20. Evidence, manifest and provenance

- Report: `docs/reviews/B3-B-R2-IMPL-01.md`
- Evidence: `docs/reviews/evidence/B3-B-R2-IMPL-01/` — `Run-R2.ps1` (reproduction), raw logs and
  TRX results, both historical probe runs, the before/after reconciliation, historical manifest
  verification, the complete R2 delta patch and `manifest.json` (SHA-256 inventory).
- Hash convention: the manifest records the retained files' bytes in the repository's canonical
  text checkout form (CRLF, `core.autocrlf=true`), so the hashes remain valid after any future
  checkout. Binaries and scratch outputs are excluded.
- **Provenance note (commission §12):** the B3-B-Q2 report's byline names the prior reviewer
  session as "Codex"; the R2 task brief describes that session as "Astra Extra High". Neither
  label is verifiable from repository artifacts. The historical byline and all historical
  provenance were left unmodified; this note records the uncertainty explicitly. The R2 producer
  executor is stated in the header; independence for qualification remains with B3-B-Q3.

## 21. Commits and B3-B-Q3 handoff

- Production/tests: `7eb9a8fcee5d4d89d333a920a6c3307e84ad4210`, tree
  `c62e9d21ea578c4186bf34ca0b9eb280055eae4d`, parent `4ae87b4f944a5c16cbd90a734b1ebfddc9fe0236`.
- Report/evidence: the documentation commit adding this report; its HEAD/tree are recorded in the
  final handoff and derivable from the commit (not recursively embedded).

Q3 should independently establish: failed retirement on every path leaves a permanently
unusable session; RESET/RUN/LOGON cannot reactivate it; exactly one disposal attempt occurs and is
never retried; primary failure classification remains observable alongside the cleanup diagnostic;
clean retirement preserves the previously qualified recoverable behavior; every returned operation
is owned before metadata access; first-getter and repeated-member metadata failures are contained;
metadata is acquired exactly once with no STREAMING state lacking a valid RUN response; no
provider handle is silently abandoned; and all historical preservation requirements still hold.

## 22. Formal disposition

**B3-B-R2-IMPL-01 — IMPLEMENTED, READY FOR INDEPENDENT B3-B-Q3 REQUALIFICATION**

This is not owner acceptance. No push was performed; one worktree; working tree and index are
clean at the final verification recorded in the handoff.
