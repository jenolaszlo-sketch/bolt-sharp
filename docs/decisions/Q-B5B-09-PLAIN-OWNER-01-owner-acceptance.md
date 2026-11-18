# Q-B5B-09-PLAIN-OWNER-01 — Owner Acceptance: Plaintext Remediation

Authority: [Q-B5B-09-OWNER-01](Q-B5B-09-OWNER-01-production-remediation-scope-decision.md)
and its plaintext implementation specification
[Q-B5B-09-PLAIN-IMPL-01](Q-B5B-09-PLAIN-IMPL-01-implementation-specification.md).
This is a documentation-only owner decision. No production source, test, public API,
project or package is modified.

## 1. Role and scope

This record is the owner's formal acceptance of the production plaintext graceful-close
remediation and its R1 correction, and of the portable typed-linger correction, following
independent Q1 and Q2 qualification. It accepts verified remediation results; it does not by
itself establish Feature completeness or release readiness. It closes findings, not release
gates.

Two independent Q2 qualifications were completed in parallel and are both preserved and
reconciled by this decision. The reconciliation record is
[`q2-reconciliation.md`](../reviews/evidence/Q-B5B-09-PLAIN-OWNER-01/q2-reconciliation.md).

## 2. Repository and identity at acceptance

| Property | Value |
|---|---|
| Repository | `D:\projects\bolt-sharp` |
| Branch | `main` |
| HEAD at acceptance | `3c2144a0d655f1e0b7187ca0bf8ac6de1bef8e8f` |
| Tree at acceptance | `400cc968a989ed6fb67a5688bc4900bfb81265e4` |
| Divergence | 18 ahead / 0 behind `origin/main` |
| Worktrees / stashes / working tree | 1 / 0 / clean |

Accepted on the basis of these pinned revisions (all verified as ancestors of HEAD):

| Role | Commit |
|---|---|
| Owner authorization | `fac10f6c5ece37dbc3f38c9c831c668d49a3d290` |
| Typed-linger correction | `dac52d55ade0a9ae703b801c8a4066e8aef2bd58` |
| Original plaintext implementation | `20fbc461c0257620db28fb78a463218b242455bc` |
| Independent Q1 qualification | `9dd2fe1b9b6f8ab2880e3fe50b8ef9ac2d43ae9c` |
| R1 implementation | `6a40c4b84993dec77deb165cef31e419df05be2e` |
| R1 producer evidence | `d7e5ac14704a9576813e32692b29050ef92ee7b8` |
| Independent Q2 — session A | `96a1a3cd0f059b554c6e7ab2ce099ef29b161ace` |
| Independent Q2 — session B | `3c2144a0d655f1e0b7187ca0bf8ac6de1bef8e8f` |

Session B is a child of session A; both qualify the identical R1 implementation tree
`a23edab4242711fffc28da63f254ff0f8b4d3082` and share the same R1 producer evidence
ancestor. There is no production-code divergence between the two qualifications.

## 3. Independent qualification status and reconciliation

Both independent qualifications are complete, and their evidence was re-verified for this
acceptance:

- **Q1** — original defect investigated, requirements matrix and findings published.
  Disposition preserved as historical evidence.
- **R1 producer verification** — implementation and evidence identities verified.
- **Q2 session A** (`96a1a3c`) — independent requalification after remediation. Verified:
  the manifest's 48 entries — 47 package content blobs plus the qualification report — with
  47 of them independently re-derived over Git blobs (46 content blobs plus the report;
  `verify-evidence.py` is self-referential and skipped); R1 source and evidence identity;
  rollback provenance (five recovered Windows campaign results byte-identical to the
  pre-rollback cleanup inventory, the remainder regenerated or freshly executed); six TRX
  campaign counters matched; nine negative-control transcripts recorded; all historical
  evidence and decision directories unmodified.
- **Q2 session B** (`3c2144a`) — a second, independent requalification. Verified: the four
  R1 source blobs and five R1 evidence blobs; the Q1 evidence integrity; the historical
  evidence and decision integrity; independent Windows and WSL2 campaign results; and an
  independent NC-08 mutation patch. Its own manifest carries no hash or byte content and
  its own verifier cannot pass on its own commit; both facts are recorded as discrepancies
  D-02 and D-03 and are immaterial to the acceptance result because the owner verifier
  re-derives both packages over Git blobs.

**Reconciliation.** Session B modified 13 files of session A's evidence package in place
and added 5, so the package directory at HEAD is a merge of two sessions rather than either
session's package. Both packages are preserved byte-exactly under the owner evidence package
(`q2-session-a/`, `q2-session-b/`), materialized from `git cat-file blob` payload bytes at
the two pinned commits. Nothing in `docs/reviews/evidence/Q-B5B-09-PLAIN-Q2/` was modified,
no history was rewritten, and no qualification campaign was re-executed.

Both qualifications reach the **same overall disposition** and the **same disposition for
every finding**, with no contradiction on any measured outcome. Eleven discrepancies
(D-01 … D-11) were identified; all concern package assembly, manifest content, verifier
implementation, labelling, scope or naming — none affects the production behaviour under
review or the qualification dispositions. The full register is in
[`q2-reconciliation.md` §6](../reviews/evidence/Q-B5B-09-PLAIN-OWNER-01/q2-reconciliation.md).

## 4. Acceptance basis

The owner accepts the following independently supported conclusions as the acceptance basis:

1. The terminal `FAILURE` remains byte-exact against the accepted session encoding.
2. Exactly one terminal response is generated per eligible terminal result.
3. The Defunct session state is preserved; no terminal-session revival.
4. Trailing commands cannot reach the parser or the execution path after a terminal
   refusal.
5. Plaintext graceful closure improves delivery in characterized queued-tail cases. The
   characterization was established in Q1 and is preserved; the canonical
   `QueuedTailComparison` oracle passes on Windows and WSL2 within the core suite.
6. Write-specific timeout ownership ends at the successful flush, so a racing observer
   callback cannot interrupt post-write processing.
7. The absolute terminal lifecycle deadline is not renewed across write, flush, shutdown or
   drain.
8. Drain byte, time, memory and concurrency limits remain enforced.
9. Socket and drain-permit cleanup are correct, including idempotent disposal and exactly
   one permit release after handle disposal.
10. Connection accounting remains correct: exactly one terminal transition per accepted
    owner, and correct completion/failure classification on both success and interruption
    paths.
11. TLS remains isolated from the plaintext closure path: no helper entry, no drain permit,
    no raw drain, and preserved TLS authentication, encryption and teardown behaviour.
12. All nine negative controls detect their intended defects, including the reintroduction
    of the original write-timeout ownership defect.

**Explicit non-claim.** This acceptance does **not** claim guaranteed delivery to the peer
application. Peer receipt remains outside the implementation guarantee, consistent with the
binding specification. The corrected behaviour is local write/flush completion plus a
bounded teardown, not a delivery assurance.

## 5. Q1 finding closure register

| Finding | Original Q1 disposition | Closure | Independent Q2 evidence |
|---|---|---|---|
| `Q-B5B-09-PLAIN-Q1-F01` write-timeout misclassification of a successful terminal write | REMEDIATION REQUIRED | **CLOSED BY VERIFIED REMEDIATION** | Session A: `q2-session-a/campaign/baseline-f01-win.json`, `r1-f01-win.json`, `baseline-f01-wsl2.json`, `r1-f01-wsl2.json` (10× repetitions per platform); `q2-session-a/core-tests.trx`, `wsl2-core-tests.trx` (`SuccessfulWriteEndsWriteTimeoutBeforeObserver`). Session B: `q2-session-b/campaign/baseline-f01-win.json`, `r1-f01-win.json`, `baseline-f01-wsl.json`, `r1-f01-wsl.json`; `q2-session-b/r1-nc-f01-mutation.patch`. |
| `Q-B5B-09-PLAIN-Q1-F02` diagnostics correlation and metadata gaps | REMEDIATION REQUIRED | **CLOSED BY VERIFIED REMEDIATION** | Session A: `q2-session-a/campaign/r1-all-win.json`, `r1-all-wsl2.json` (`Diagnostics_SuccessfulLifecycleMetadata`, `Diagnostics_CapacityFallbackMetadata`). Session B: `q2-session-b/campaign/r1-all-win.json`, `r1-all-wsl.json`. |
| `Q-B5B-09-PLAIN-Q1-F03` incidental shared transport changes | REMEDIATION REQUIRED | **CLOSED BY VERIFIED REMEDIATION** | Session A: `q2-session-a/negative-controls/nc-07.log` / `nc-07.trx` (`TlsRefusalNeverEntersPlaintextHelper`); `q2-session-a/campaign/r1-all-win.json`, `r1-all-wsl2.json` (`Tls_IsolationFromPlaintextHelper`); `q2-session-a/wsl2-core-tests.trx` (`TlsStartup_UsesDisabledLinger`, `PlaintextStartup_UsesDisabledLinger`, `RestartIsNotSupported_NewInstanceIsRequired`). Session B: `q2-session-b/campaign/r1-all-win.json`, `r1-all-wsl.json`. |

The original Q1 **REMEDIATION REQUIRED** dispositions are preserved unchanged as historical
evidence; this record supersedes them only as a forward-looking closure.

## 6. Acceptance of the Q2 qualifications

Both Q2 qualifications are accepted as **non-blocking** under the current binding
specification. Neither blocks acceptance and neither is closed by this decision; each is
recorded with an explicit disposition. Both independent reviewers reached the same
dispositions.

### 6.1 `Q-B5B-09-PLAIN-Q2-F01` — Observer resource retention

Accepted as an **inherited architectural limitation**, not an R1 defect. The owner records
explicitly:

- Synchronous diagnostic observers can outlive socket disposal.
- Connection tasks and admission slots remain occupied until the observer returns.
- A blocked observer can cause subsequent healthy connections to be refused for capacity.
- `StopAsync` can return at its configured shutdown deadline with an active connection task
  remaining, while ending admission and reporting the residual count honestly.
- Accounting and retained resources settle once the observer returns.
- Equivalent retention was demonstrated against the original implementation (baseline
  `20fbc46`): identical `ConnectionsActive` and connection-task counts during a blocked
  observer, with the original F01 misclassification.

**Bounded resource count is distinguished from bounded retention duration.** The number of
retained connections is bounded by `MaxConnections`; the retention *duration* is bounded
only by the observer. A permanently blocked observer may retain admission capacity
indefinitely. This acceptance therefore does **not** describe observer retention as
unconditionally self-recovering and does not establish observer-independent connection
reclamation.

The current specification explicitly preserves admission ownership through task retirement
and recognizes the limits of preempting arbitrary synchronous calls. The behaviour is
consistent with the binding contract; the residual risk is an architectural backlog item.

### 6.2 `Q-B5B-09-PLAIN-Q2-F02` — Backpressure evidence reuse

The documented evidence-reuse justification is accepted. The owner records that:

- Fresh exploratory probes did not establish decisive pending-write observations on either
  platform. Session A's probe recorded `no-terminal-write-pending` for all 48 attempts per
  platform; session B's file with the same subject contains canonical-oracle output rather
  than an exploratory induction, and session B's own finding remains INCONCLUSIVE.
- The relevant prior Q1 transport evidence remains applicable, based on the verified
  source-level comparison showing the physical write and cancellation semantics unchanged
  between the original implementation and R1.
- Existing R1 production-path pending-write tests passed on Windows and WSL2. Session B
  recorded the canonical `ActualPendingTerminalWriteIsInterrupted` oracle outcome directly:
  `write-deadline`, `total-deadline`, `owner-cancelled` and `listener-shutdown` during the
  write phase, with no drain admitted.
- The inconclusive exploratory trials remain correctly identified as inconclusive.

**Explicit non-claim.** Those inconclusive trials are not represented as successful
real-backpressure observations. No additional automatic backpressure investigation is
authorized.

## 7. Portable typed-linger acceptance

A separate acceptance disposition is recorded for `dac52d55ade0a9ae703b801c8a4066e8aef2bd58`:

**Accepted within the independently verified Windows and WSL2 scope.**

- Original WSL2 startup failure recorded (the boolean `SO_LINGER` call returned `EINVAL 22`
  on the native Linux socket layer).
- The typed disabled-linger correction (`LingerState = new LingerOption(false, 0)`) provides
  equivalent intended disabled-linger semantics.
- Listening and accepted socket verification completed.
- Plaintext and TLS regressions passed on both platforms.

**Native Linux release qualification remains outstanding.** WSL2 qualification is not
equated with native Linux qualification; the native Linux release gate is preserved in §9.

## 8. Regression and evidence acceptance

| Suite | Platform | Result |
|---|---|---|
| Core | Windows | 1,257 passed, 0 failed, 0 skipped |
| Core | Ubuntu WSL2 | 1,257 passed, 0 failed, 0 skipped |
| Official-driver interop | Windows | 43 passed, 0 failed, 0 skipped |
| Official-driver interop | Ubuntu WSL2 | 43 passed, 0 failed, 0 skipped |
| Driver integration (Windows only) | 75 passed, 0 failed, 10 pre-existing skips |
| Integration (Windows only) | 19 passed, 0 failed, 0 skipped |

Session B independently corroborates the same core (1,257) and official-driver (43) totals
on both platforms and adds a 171-test focused repetition campaign (3 × 57) that passed on
both platforms. Session B's report labels the 43-test suite "Driver Integration"; the owner
records it as the **official-driver interop** suite, consistent with the TRX envelopes and
with session A. Session B's own §14.10 figure of 1,270 does not reconcile with its §9.1/§9.2
table total of 1,364; the owner does not adopt either figure in place of the TRX-verified
totals above (discrepancy D-06).

Negative controls: **nine of nine detected**, on session A's transcript set (`nc-01` …
`nc-09`, each with a TRX). Session B independently corroborates NC-01 … NC-08 on Windows,
adds an observer-retention control, and ran NC-01 and NC-07 on WSL2 (discrepancy D-08).

Builds succeeded with exactly two pre-existing `xUnit2012` analyzer warnings (at
`BoltRequestDecodeTests.cs:71,72`, each echoed twice by MSBuild) and eight pre-existing
whitespace findings in `BoltMessageFixtures.cs` that are present in the pristine baseline and
were not introduced by R1. Session B's format check was scoped to the four R1 paths and does
not cover those eight findings (discrepancy D-07).

The distinction between passing, skipped and unqualified cases is preserved: the ten driver
integration skips remain pre-existing exclusions, not passes; representative production
capacity and native Linux remain unqualified. The rollback recovery and evidence provenance
assessment is recorded in the Q2 evidence package and is accepted as adequate. Historical
evidence was not modified to normalize previous results.

## 9. Dispositions and release gates

| Item | Disposition |
|---|---|
| Plaintext graceful-close remediation (Q1 findings) | **ACCEPTED WITH QUALIFICATIONS — CLOSED** |
| Portable typed-linger correction | **ACCEPTED WITH QUALIFICATIONS** |
| TLS graceful-close remediation | DEFERRED |
| Parent `Q-B5B-09` | **OPEN** |

Preserved outstanding release gates:

- Native Linux qualification.
- Representative production capacity characterization.
- Deferred TLS graceful-close design and qualification.
- Applicable TLS-specific resource and backpressure requirements.

Preserved inherited qualifications:

| Item | Status |
|---|---|
| B5-B-TS1 | CLOSED |
| Q-B5B-03 | CLOSED |
| Q-B5B-04 | OPEN |
| Q-B5B-05 | OPEN |
| B5-B-TS1, Q-B5B-03 and all other inherited dispositions | unchanged |

This decision does not imply complete release readiness.

## 10. Deferred architectural backlog — observer isolation

Recorded for a future, separately authorized commission. **Not authorized now.**

- Bounded diagnostic dispatch.
- Observer isolation.
- Admission-slot reclamation.
- Diagnostic ordering and delivery semantics.
- Subscriber exception and shutdown policies.

Any implementation requires separate owner authorization. This acceptance does not initiate
that work and does not reopen the deferred TLS graceful-close investigation.

## 11. Recommended next commission

Reviewing the remaining B5-B roadmap without performing implementation, the smallest
appropriate next commission is:

**`Q-B5B-05` — same-connection RESET-recovery demonstration.**

- **What it is.** After the observed FAILURE/IGNORED sequence, the official driver destroys
  the connection; RESET recovery of that same connection was never demonstrated. This is a
  bounded, driver-level qualification probe, not a production change.
- **Why smallest.** It closes an already-open inherited B5-B qualification with existing
  assets: the CLOSED B5-B-TS1 byte oracle, the existing official-driver interop suite, the
  `docs/reviews/probes` harness, and the B5-B multi-client isolation tests. It requires no
  new production code, no public API change, and no new dependency.
- **Prerequisites.** A recorded commissioning decision naming the probe owner and reviewer;
  reuse of the existing driver-interop harness at `Neo4j.Driver` 6.4.0; the existing
  loopback/TLS arrangements; and the standing qualification conventions (independent
  reviewer, negative controls, self-verifying evidence).
- **Why not the alternatives.** `Q-B5B-04` (F02) refinement requires a production
  classification change and broader re-qualification, and is explicitly optional. B4
  Browser/WebSocket and B6 hardening/packaging/release qualification are larger entry
  scopes. Reopening the deferred TLS graceful-close investigation or the observer-isolation
  backlog is explicitly dispreferred by this decision's own terms.

This recommendation authorizes nothing; the next implementation requires its own commission.

## 12. Protected-scope verification

This decision is documentation-only. No production source, existing test, public API,
project, package, historical evidence or previously accepted owner decision was modified.
No observer-isolation, TLS remediation or other milestone work was started. Nothing was
pushed or published.

## 13. Evidence

Self-verifying package:
[`docs/reviews/evidence/Q-B5B-09-PLAIN-OWNER-01/`](../reviews/evidence/Q-B5B-09-PLAIN-OWNER-01/README.md)
with the acceptance matrix, finding closure register, qualification ledger, observer
backlog item, next-commission recommendation, the Q2 reconciliation record, both preserved
Q2 packages, the manifest and `verify-evidence.py`.

## 14. Formal owner disposition

**PLAINTEXT REMEDIATION — ACCEPTED WITH QUALIFICATIONS — CLOSED.**

- `Q-B5B-09-PLAIN-Q1-F01` — CLOSED BY VERIFIED REMEDIATION.
- `Q-B5B-09-PLAIN-Q1-F02` — CLOSED BY VERIFIED REMEDIATION.
- `Q-B5B-09-PLAIN-Q1-F03` — CLOSED BY VERIFIED REMEDIATION.
- `Q-B5B-09-PLAIN-Q2-F01` — ACCEPTED (non-blocking inherited architectural limitation).
- `Q-B5B-09-PLAIN-Q2-F02` — ACCEPTED (evidence-reuse justification).
- Portable typed-linger correction — ACCEPTED WITHIN THE VERIFIED WINDOWS AND WSL2 SCOPE.
- Parent `Q-B5B-09` — **REMAINS OPEN.**
