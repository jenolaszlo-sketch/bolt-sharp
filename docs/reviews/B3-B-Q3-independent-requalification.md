# B3-B-Q3 — Independent resource-lifecycle requalification

**Task:** B3-B-Q3 — independent qualification of B3-B-R2-IMPL-01
**Reviewed `main` HEAD:** `b9356c3fadb484b9b3a39d807205283155886184` / tree `f592acde2571e2084601b85a147eb48098b987e7`
**R2 implementation commit:** `7eb9a8fcee5d4d89d333a920a6c3307e84ad4210` / tree `c62e9d21ea578c4186bf34ca0b9eb280055eae4d`
**R2 evidence commit:** `b9356c3fadb484b9b3a39d807205283155886184`
**Protocol:** Bolt 5.8 / PackStream 1.0
**Reviewer:** DeepSeek V4.1 Flash (provider `nvidia`, OpenCode). **Independence caveat:** the
reviewer agent is the same executor session that produced B3-B-R2-IMPL-01; organizational
separation is not available in this environment. Technical independence measures taken for this
review are listed in §2; the caveat itself is retained as a standing qualification (inherited from
the Q1 author-independence caveat) and is material context for the owner's reading of this report.

## Disposition

> **B3-B-Q3 — SUPPORTED WITH QUALIFICATIONS**
>
> **Q2-F01 CLOSED; Q2-F02 CLOSED.** The two blocking defects established by B3-B-Q2 are
> independently closed by R2 under adversarial single-fault and combined-fault providers, with
> 129/129 passing reviewer checks, zero unresolved Q2 probe findings after full 181-check
> reconciliation, no material regression, and preserved historical evidence (all manifests verify;
> every superseded historical hash was re-verified against its recorded historical commit).
> Qualifications: the reviewer-independence caveat above; the retained Q1 §19 limitation list;
> the fail-closed contract supersession (now normative); and the documented unreachability of the
> `diagnostic-latest` replacement scenario. Recommendation to the owner: **B3-B owner acceptance
> with these qualifications** is supportable; this record is not acceptance.

---

## 1. Preflight

| Item | Verified |
| --- | --- |
| Branch | `main` |
| HEAD / tree | `b9356c3…` / `f592acde…` (exact match) |
| Worktrees | Exactly one, canonical |
| Working tree / index | Clean at entry |
| Prior Q3 work | None (report, probes and evidence directories did not exist) |
| Push | None; `origin/main` remains `9abf5e3724a31aff085b3810ebb9bd3a7783ea2a` |
| Ancestry | `7eb9a8f` (R2 impl) and `4ae87b4` (Q2 baseline) are both ancestors of HEAD (`merge-base --is-ancestor` exit 0) |

Read before probing: all six B3-B reports, the complete Q2 and R2 evidence directories, the current
production session sources, all session test files (including the 18 R2 tests and the three
strengthened R1 assertions), and the Q1/Q2 reviewer probes.

## 2. Review method and independence measures

- A **new reviewer probe** (`docs/reviews/probes/B3-B-Q3/IndependentProbe/`, 129 checks, 165
  trace steps, exit 0) was written from the production source, sharing no code with the producer
  tests or the Q1/Q2 probes.
- The **unchanged Q1 and Q2 probes** were rebuilt and executed against the reviewed revision; the
  Q2 results were reconciled check-by-check against the retained pre-R2 results.
- The ownership model (§5) was derived by enumerating every `_operation`, `_fieldCount` and `_state`
  mutation site in the production source, not by restating the producer's invariant table.
- **Caveat (not removable by technique):** the producer and reviewer share an executor. The owner
  should weigh this report accordingly; the technical record is complete enough for a separately
  staffed reviewer to re-verify every claim from the retained artifacts.

## 3. Historical evidence integrity

Manifest verification (in place, nothing rewritten):

| Manifest | Current | Assessment |
| --- | ---: | --- |
| B3-B-R2 | 29/29 | intact |
| B3-B-Q2 | 38/38 | intact — the 181-check pre-R2 trace is unchanged (SHA-256 `1ef748c7…` re-verified) |
| B3-B-Q1 | 12/12 | intact |
| B3-B-R1 | 12/14 | 2 entries superseded by R2 (see below) — expected evolution |
| B3-B-IMPL-01 | 19/22 | 3 entries superseded by R1/R2 — expected evolution |
| B3-A-IMPL-01 | 16/21 | the documented B3-A-R1 delta (five message sources/tests), unchanged |
| B3-A-Q1 / R1 / Q2 | 14/14, 13/13, 15/15 | intact |
| B0-B producer / Q1 | 24/24, 119/119 | intact |

The five superseded B3-B entries were **re-verified at blob level against their recorded
historical commits**, proving the mismatches are exactly authorized source evolution:

| Manifest entry | Historical commit | Blob hash matches manifest |
| --- | --- | --- |
| `src/BoltSharp/Sessions/BoltConnectionSession.cs` | `9f224cf` (B3-B impl) | `53c1274c…` ✓ |
| `src/BoltSharp/Sessions/BoltSessionOptions.cs` | `9f224cf` | `77497dda…` ✓ |
| `tests/…/AuthenticationAndTransitionTests.cs` | `9f224cf` | `1d8c35b4…` ✓ |
| R1: `src/…/BoltConnectionSession.cs` | `454ae00` (R1) | `4f7dc3b4…` ✓ |
| R1: `tests/…/CleanupAndTelemetryRemediationTests.cs` | `454ae00` | `e92e9177…` ✓ |

Change isolation: `git diff 4ae87b4..HEAD -- src tests` touches exactly the four R2 files; guarded
paths (`src/BoltSharp/Messages`, `PackStream`, `Framing`, `Handshake`) are unchanged since
`9f224cf` (exit 0); Q1 artifacts unchanged since `3caab5b` and Q2 artifacts unchanged since
`4ae87b4` (exit 0). No B3-C/B4/B5 work, no dependency or solution changes, no historical
weakening.

## 4. Reproduced regression counts

Environment: Windows 10.0.26200 / x64, .NET SDK 10.0.401, MSBuild 18.9.11 (`environment.txt`).

| Suite | Reviewer run | Expected | Exit |
| --- | --- | --- | ---: |
| Core | **1,043 / 0 / 0** | 1,043 | 0 |
| Session filter | **119 / 0** | 119 | 0 |
| R1 remediation class | **21 / 0** | 21 | 0 |
| R2 lifecycle class | **18 / 0** | 18 | 0 |
| B3-A messages | **98 / 0** | 98 | 0 |
| B0-B integration | **19 / 0** | 19 | 0 |
| Build (slnx, `--no-incremental`) | 0 errors; **2** pre-existing `xUnit2012` (`BoltRequestDecodeTests.cs:71-72`) | 0 / 2 | 0 |
| Unchanged Q1 probe | 118 checks / 117 pass / **X04** / exit 4 | same | 4 |
| Unchanged Q2 probe | 181 checks / 163 pass / 18 fail / 217 traces / exit 4 | same | 4 |
| Independent Q3 probe | **129 checks / 129 pass / 0 fail / 165 traces** | 0 failures | **0** |

**R2 test-strength assessment.** The 18 R2 tests assert state, response sequence, open counters,
disposal-attempt counters, ownership flags, diagnostic identity and post-failure permission for
every scenario — not exception-escape alone. The three strengthened R1 assertions replace the
superseded recoverable-`Failed` expectation with `Defunct` **and add** no-SUCCESS, `ShouldClose`,
exact-diagnostic-message and RESET-refusal assertions, so the original failure conditions
(throwing disposal on PULL completion, read failure and row-shape failure) remain under test with
strictly stronger coverage; no negative control was removed. The strengthened expectations are
independently justified by the fail-closed contract (Q2-F01; R2 commission §4.5–§4.6, §6.1) and
are re-proven by this review's own probe.

## 5. Independent resource-ownership model (derived from source)

Mutation-site enumeration over `src/BoltSharp/Sessions/BoltConnectionSession.cs` (line numbers of
the reviewed revision):

- `_operation` is written in exactly two places: `HandleRun:336` (publication, immediately after a
  non-null `Prepare` return and **before** `TryCaptureFields`) and
  `DetachAndDisposeOperation:601-602` (detach-before-call). No other assignment, no second
  ownership field, no local handle survives a failure path: every failure branch after
  acquisition flows through `FailRecoverable`/`FailTerminal`/`HandleReset`/`HandleGoodbye`/
  `Disconnect`, all of which retire via `DetachAndDisposeOperation`.
- `TryCaptureFields:531` is the **only** reader of `IBoltOperation.Fields`; it acquires once,
  contains null metadata/null element/any thrown `Count`/indexer access, and returns an immutable
  snapshot. `_fieldCount` (349) is taken from the snapshot, reset in `DetachAndDisposeOperation:604`,
  and is the sole input to NC-05b validation (`HandlePull:363` guard + column check).
- State writes: `HandleRun` (Streaming only after successful capture), `CompleteResult:566`
  (`Ready` on clean retirement; **`Defunct`+close on failed retirement**), `FailRecoverable:649`
  (`Failed` on clean retirement; **`Defunct`+close on failed retirement**), `FailTerminal:637`
  (`Defunct` always), `HandleReset:443` (`Interrupted` → `Ready` only when retirement returned
  success; `FailTerminal` otherwise), `HandleGoodbye:470` / `Disconnect:199` (`Defunct` always),
  `Dispose:213` (`_disposed=true` in `finally`).
- Terminality: `Receive`/`ReceiveBytes` return `None(Defunct, shouldClose:true)` for every message
  once `Defunct`, before any handler or provider call; `Defunct` has no outgoing transition, so
  the failed-retirement condition is monotonic without a separate latch.

Consequences proven below by execution: every returned handle is owned at acquisition; no metadata
is read before ownership; exactly one retirement attempt per handle; failed retirement cannot be
forgotten or cleared; no second operation after failed retirement; `Streaming` only with validated
snapshot; clean retirement preserves the qualified `Failed`/RESET recovery.

## 6. Q2-F01 closure evidence (independent)

Reviewer probe checks `f01-*` (five failure modes × eight assertions), `overlap-*` (five modes ×
the full overlap sequence), `refusal-*` (eight message classes), `f01-reset-cleanup*`,
`f01-goodbye-cleanup*`, `f01-disconnect-cleanup*`, `f01-dispose-cleanup*`,
`f01-clean-recovery*` — **all passing**. Key observations from the retained traces
(`q3-probe-results.json`):

- After the failing retirement the session is `Defunct` with `ShouldClose=true`, the primary
  FAILURE payload preserved (`50N00`/`50N42` + exact descriptions), no SUCCESS summary, exactly one
  disposal attempt, `ResourceLive=true` (the failure means the resource was **not** released — the
  report does not claim physical release), and the diagnostic is the exact thrown instance.
- The critical overlap sequence (`RUN → failure → RESET → RUN → late RESET → Disconnect →
  Dispose`): `opens` remains **1** on every mode, `RESET`/`RUN`/late `RESET` produce no response,
  the diagnostic identity is retained, and teardown does not retry (attempts stay 1).
- Every message class after failed retirement (`RESET, RUN, PULL, DISCARD, LOGOFF, TELEMETRY,
  GOODBYE, LOGON`) returns no response, leaves the session `Defunct`, opens no operation and does
  not re-invoke the credential verifier.
- Clean-retirement control: read failure with successful disposal → `FAILURE`/`Failed`
  (`ShouldClose=false`, no diagnostic) → `RESET` → `SUCCESS`/`Ready` → `RUN` → `SUCCESS`/`Streaming`
  with `opens=2`: the previously qualified recoverable behavior is preserved.
- `RESET` on an actively-owned operation with throwing disposal → `FAILURE`/`Defunct` (unchanged
  R1 fail-closed behavior); `GOODBYE` failure → no response/`Defunct`; `Disconnect` failure →
  `Defunct`, one attempt, idempotent; session `Dispose` failure → terminal, idempotent,
  `ObjectDisposedException` afterwards.

## 7. Q2-F02 closure evidence (independent)

Reviewer checks `meta-*`, `own-*`, `snap-*`, `pub-*` — **all passing**:

- First-getter throw, clean retirement: contained `FAILURE(50N00)`, `Failed`, `ShouldClose=false`,
  **one attempt**, `FieldsReads==1`, no diagnostic, `opens=1`; the subsequent `PULL` is `IGNORED`
  (proving `Streaming` was never published); `RESET` recovers; a later RUN reacquires
  (`opens=2`).
- First-getter throw + throwing disposal: contained `FAILURE`, `Defunct`, one attempt, diagnostic
  identity, RESET/RUN refusal, `opens` stays 1, teardown does not retry.
- Getter that would throw on a second access: the second access **never occurs** — `FieldsReads`
  is 1 after RUN and after the full PULL lifecycle; no exception escapes.
- Null `Fields`, null element, throwing `Count`, throwing indexer: each contained, one attempt,
  correct state; access counts recorded exactly (`FieldsReads`, `CountReads`, `IndexerReads`).
- Changing metadata: RUN publishes the first validated snapshot; the later 2-value row fails
  NC-05b **against the snapshot** (`FieldsReads` still 1) — no re-read, no silent acceptance.
- Metadata withdrawn after the snapshot: `RUN → PULL(1) → PULL(-1)` completes normally with
  `FieldsReads==1` — no metadata access occurs during PULL.
- Valid and valid-empty metadata unchanged (`SUCCESS{fields}`/`Streaming`; zero-column result
  supported).
- Ownership timing: with the first metadata access failing, `Disconnect`/`Dispose` retire the
  handle exactly once (no unaccounted handle), and with a throwing disposal no new operation can
  open.

## 8. Combined adversarial failures (independent)

All eight mandated combinations were executed fresh-session (`comb-c1`…`comb-c8`) —
metadata-throw+dirty-dispose, count-throw+dirty, indexer-throw+dirty, null-element+dirty,
read-exception+dirty, malformed-RECORD+dirty, partial-DISCARD→GOODBYE-dirty,
partial-PULL→RESET-dirty, and the full post-cleanup RESET+RUN battery — **all passing**: one
ownership path, one disposal attempt, primary-failure precedence, `Defunct` after unsuccessful
cleanup, no orphaned cursor, no successful recovery, no second provider operation.

## 9. Q2 181-check reconciliation (independent)

Method: the unchanged Q2 probe was executed against R2 (`q2-probe-after.json`); each of the 181
checks was compared with the retained pre-R2 result (`q2-probe-results.json`, SHA-256
`1ef748c7…`) and classified by this review (not by adopting the producer's grouping). Full
per-check table with original expectation, both actuals, classification, contract justification
and replacement assertion is in `q3-probe-results.json` (`q2_reconciliation.entries`).

| Classification | Count | Checks |
| --- | ---: | --- |
| PASS_UNCHANGED | 152 | all other checks, including the 48-entry state matrix and all clean-path controls |
| DEFECT_CLOSED | 11 | five `*-no-overlap`; `run-metadata-first-getter-{contained,cleanup,no-reopen,eventual-attempt}`; `run-metadata-null-element-{contained,cleanup}` |
| EXPECTATION_SUPERSEDED | 15 | five `*-primary`; five `*-ignored`; five `*-safe-reset` |
| UNREACHABLE_AFTER_CORRECTION | 3 | `diagnostic-latest`; `run-metadata-second-getter-{contained,cleanup}` |
| UNRESOLVED_FAILURE / INCONCLUSIVE | **0** | — |

Grouping note: the producer reported 152/11/11/7. This review reaches the same 18 non-passing
historical checks with a stricter split — `*-safe-reset` (5) is classified
EXPECTATION_SUPERSEDED because its `IsDefunct` component still holds and only the response-string
component is stale, and `diagnostic-latest` is classified UNREACHABLE_AFTER_CORRECTION because the
scenario (a later cleanup failure replacing an earlier one) requires recovery after failed
retirement, which R2 forbids. The substance is identical; the classification is the reviewer's own.

Independent verification of each group:

- **Resolved (11):** re-established by the Q3 overlap and metadata scenarios with provider
  counters (`opens` stays 1; exactly one attempt; containment).
- **`*-primary` (5):** the recorded actuals show the preserved primary payload
  (`RECORD,FAILURE/Defunct/The operation provider failed…`, row-shape description on the
  row-shape path); only the state component is stale. The payload is independently asserted by
  `f01-*-primary` (passing).
- **`*-ignored` (5):** the session is `Defunct` before the message arrives; the qualified Defunct
  contract is no-processing/no-response. Independently asserted by `refusal-*` (passing), with
  `opens` unchanged.
- **`*-safe-reset` (5):** `IsDefunct` holds in every recorded actual (`/Defunct;oldResourceLive=True`);
  the FAILURE-response component is stale because RESET arrives at an already-terminal session.
  The necessary-and-correctly-implemented requirement (no READY restoration, no reopening) is
  asserted by `overlap-*` and `refusal-RESET`.
- **`diagnostic-latest`:** replacement requires recovery after failed retirement — impossible by
  the corrected design; diagnostic *retention* through the terminal transition is asserted
  (`diag-secondary-separate`, `diag-mutation-inert`). A formerly reachable unsafe state becoming
  unreachable is acceptable precisely because the new reachable path (immediate `Defunct`) is
  tested.
- **`run-metadata-second-getter-*`:** single acquisition (INV-equivalent established in §5)
  eliminates the second access; the one-read invariant is asserted by
  `meta-second-never-read-*`, `snap-withdrawn` and `meta-changing-*` (all passing).

No historical failure is reinterpreted as acceptable safety behavior; the historical exit code
remains 4 and the independent Q3 assertion set contains zero failures.

## 10. Historical Q1 X04 adjudication

The unchanged Q1 probe reports 118 checks / 117 passing / exit 4, the sole mismatch being X04
(`no-throw;Failed` vs `no-throw;Defunct;FAILURE`) — identical before and after R2. This review
confirms the Q2 classification (EXPECTED CONTRACT CORRECTION) and the current contract
independently: `x04-contained`/`x04-no-recovery` assert FAILURE + `Defunct` + one attempt + no
RESET recovery + `opens==1`. R1's other corrections remain intact (verified by the passing Q1
probe, the R1 remediation class 21/21, and `f01-goodbye/disconnect/dispose-cleanup` checks).

## 11. Response-sequence correctness

Verified: `RECORD` precedes `FAILURE` with exactly one terminal summary (`pub-ordering`,
`f01-*-no-success`); no SUCCESS after terminal FAILURE for the same request; no late RECORD after
terminal failure (`refusal-PULL` — no response at all); no successful completion after cleanup
failure; no response after terminal closure (`refusal-*`, `f01-goodbye-cleanup-late`); GOODBYE
never fabricates a response; an appropriate FAILURE precedes closure where a response is
expected. The response-result abstraction preserves ordering because the terminal result object is
constructed after the state transition and is returned in the same `Receive` call; later requests
hit the `Defunct` early-return before any handler.

## 12. Failure precedence and confidentiality

- Primary classification (provider failure `50N00`, row-shape `50N42`) survives failing cleanup;
  the disposal exception is recorded only in `LastCleanupFailure` (`diag-primary-preserved`,
  `diag-secondary-separate`).
- The recorded diagnostic is the exact exception instance; mutating its `Data` cannot alter
  ownership, state or counters (`diag-mutation-inert`) — the diagnostic is never consulted for
  transitions.
- Sentinel scanning: injected read/metadata/cleanup exception text (sentinels) appears in no
  response metadata and nowhere in the recorded response trace (`diag-no-sentinel-in-response`,
  `trace-sentinel-scan`); responses carry only the sanitized bounded descriptions. Residual
  qualification: `LastCleanupFailure` exposes the raw provider exception to callers — treating it
  as potentially sensitive remains a caller obligation (retained from R2 §18; no new exposure).

## 13. Independent negative controls

Each control demonstrates that the checking predicates used by the real assertions reject a
violating sample, or that a discriminator separates compliant from violating real outcomes:

| Control | Target defect | Result |
| --- | --- | --- |
| `nc-failed-not-defunct` | failed cleanup reported `FAILED` | detected (exact state equality) |
| `nc-reset-discriminates` | RESET restoring READY | detected — clean RESET recovers (`SUCCESS/Ready`), failed-retirement RESET does not |
| `nc-second-open` | second operation after failed disposal | detected (`opens==1` strict) |
| `nc-disposal-attempt-count` | zero or two disposal attempts | detected on real handle samples (0 and 2 both rejected by `==1`) |
| `nc-second-getter` | second `Fields` invocation | detected on a real double-read sample |
| `nc-invalid-metadata-accepted` | invalid metadata accepted | detected (response-kind discriminator) |
| `nc-streaming-without-metadata` | premature STREAMING | detected (strict state comparisons) |
| `nc-duplicate-terminal-success` | duplicate terminal summary | detected (real list predicate: 2 rejected, 1 accepted) |
| `nc-sentinel-leak` | credential/exception text leak | detected (sentinel scan flags a leaking map; clean map passes) |

No blind spot was found: every mandated defect mode maps to at least one strict reviewer assertion.

## 14. Regression preservation

All §14 items verified: authentication sequencing, fail-closed auth, normal RUN/PULL/DISCARD,
bounded `PULL(n=-1)`/`has_more`, FAILED/IGNORED with safe recovery, the documented synchronous
RESET limitation (unchanged), GOODBYE no-response, NC-05b/NC-08, `ReceiveBatch` ordering,
TELEMETRY 0–3 and invalid-identifier `50N00`/`UnknownError`, B3-A zero-field IGNORED `B0 7E`
(message suite 98/98), and B0-B official-driver integration 19/19. The only behavioral change
versus the pre-R2 qualification is the commissioned fail-closed retirement itself, which
supersedes the historical FAILED/RESET-after-throwing-Dispose expectation; unrelated
recoverable-failure behavior is unchanged (clean-failure controls in §6).

## 15. Remaining qualifications (retained, plus new)

Retained from Q1 §19 / Q2: bounded `PULL(n=-1)` batching; no mid-flight RESET jump-ahead;
`ReceiveBatch` consumed-count ergonomics; provisional session/provider APIs; single-owner,
non-thread-safe session (still undocumented on the type); no cancellation-token seam; placeholder
error-code fidelity outside the corrected TELEMETRY case; wrong-typed TELEMETRY rejected at the
B3-A boundary; no real graph backend; no production TCP/TLS host; B3-A IGNORED live-driver
verification deferred to B5-B; two `xUnit2012` advisories.

New/updated by this review:

- **Reviewer-independence caveat** (§2): same-executor review; organizational attestation
  unavailable.
- **Fail-closed retirement is now the normative contract**; the historical recoverable-`Failed`
  expectation for throwing disposal is superseded everywhere (Q1 X04 remains the recorded stale
  expectation and must not be "fixed" historically).
- `diagnostic-latest` replacement semantics are unreachable after failed retirement (retention
  semantics unchanged and asserted).

R2 introduced no additional architectural limitations beyond the commissioned contract change.

## 16. Material findings

No material defect was found. Non-blocking documentation observations:

- **Q3-DOC-01 (informational):** the superseded Q2 expectations are only correctable in a future
  probe generation; until then the Q2 probe reports exit 4 by design. Recorded here and in the R2
  report; no action authorized under Q3.
- **Q3-DOC-02 (informational):** the session type still lacks an explicit single-owner/
  thread-safety doc remark (Q1 §19.5). Retained; no action under Q3.

## 17. Reviewer artifacts and evidence

- Report: `docs/reviews/B3-B-Q3-independent-requalification.md`
- Probe: `docs/reviews/probes/B3-B-Q3/IndependentProbe/` (`Program.cs`,
  `Sections.Lifecycle.cs`, `Sections.Metadata.cs`, `Sections.Reconciliation.cs`, csproj; bin/obj
  excluded)
- Evidence: `docs/reviews/evidence/B3-B-Q3/` — `Run-Q3.ps1`, raw logs and TRX for every suite,
  unchanged Q1/Q2 probe runs, `q3-probe-results.json` (129 checks + full 181-check reconciliation
  + 165-step trace), historical manifest verification, reviewed-source snapshot, README,
  `manifest.json` (SHA-256 inventory; canonical CRLF checkout form; no binaries).

## 18. Formal disposition

**B3-B-Q3 — SUPPORTED WITH QUALIFICATIONS**

Finding-level: **Q2-F01 CLOSED; Q2-F02 CLOSED.**

## 19. Owner recommendation

B3-B owner acceptance **with the qualifications in §15** is supportable on this record,
subject to the owner's weighing of the reviewer-independence caveat (§2). This record is
independent review, not acceptance. Stop here: no B3-B-OWNER-01, B3-C, B4 or B5 work was
performed or authorized by this review.
