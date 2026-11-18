# Q2 Reconciliation Record — Q-B5B-09-PLAIN-OWNER-01

Owner decision: [`Q-B5B-09-PLAIN-OWNER-01-owner-acceptance.md`](../../../decisions/Q-B5B-09-PLAIN-OWNER-01-owner-acceptance.md)

Two independent Q2 qualifications of the same plaintext R1 remediation were completed in
parallel and both are preserved by this record. This document reconciles them, states the
canonical acceptance basis and lists every material discrepancy.

## 1. The two qualifications

| | Session A | Session B |
|---|---|---|
| Commit | `96a1a3cd0f059b554c6e7ab2ce099ef29b161ace` | `3c2144a0d655f1e0b7187ca0bf8ac6de1bef8e8f` |
| Tree | `ae77886a82561fb844c5cc5c874d833cdf1c0fdf` | `400cc968a989ed6fb67a5688bc4900bfb81265e4` |
| Parent | `d7e5ac14704a9576813e32692b29050ef92ee7b8` (R1 evidence) | `96a1a3cd0f059b554c6e7ab2ce099ef29b161ace` (session A) |
| Reviewer identity | Fresh independent reviewer session (DeepSeek V4.1 Flash Free); no R1 implementation, producer verification or Q1 qualification participation | Fresh independent reviewer session (Nemotron 3 Ultra 550B); no R1 implementation, producer verification or Q1 qualification participation |
| Report | `docs/reviews/Q-B5B-09-PLAIN-Q2-qualification.md` at session A | `docs/reviews/Q-B5B-09-PLAIN-Q2-qualification.md` at session B |
| Package | `docs/reviews/evidence/Q-B5B-09-PLAIN-Q2/` at session A | `docs/reviews/evidence/Q-B5B-09-PLAIN-Q2/` at session B |
| Preserved copy | [`q2-session-a/`](q2-session-a/PROVENANCE.md) | [`q2-session-b/`](q2-session-b/PROVENANCE.md) |

Both reviewers worked from pristine copies of the pinned commits (`20fbc46` baseline,
`6a40c4b` R1, `9dd2fe1` Q1, `dac52d5` typed-linger), wrote their own independent probes,
and executed on Windows and Ubuntu WSL2. Neither participated in the R1 implementation, the
R1 producer verification or the Q1 qualification.

## 2. Ancestry

```
d7e5ac1  R1 producer evidence
   |
96a1a3c  session A Q2 qualification          <- owner acceptance base at commission time
   |
3c2144a  session B Q2 qualification          <- current HEAD
   |
   +-- (this owner acceptance commit)
```

`96a1a3c` is an ancestor of `3c2144a`; `3c2144a` is an ancestor of the owner acceptance
commit. Session B is a **child** of session A, not a sibling branch: session B branched from
session A's commit, so both qualifications share the same R1 producer evidence ancestor and
the same pinned production revisions. There is no divergence in the production code between
them — both qualify the identical R1 tree `a23edab4242711fffc28da63f254ff0f8b4d3082`.

## 3. Package collision (the central reconciliation issue)

Session B did **not** create a separate evidence package. Its commit modified 13 files of
session A's package in place and added 5 new files. The directory
`docs/reviews/evidence/Q-B5B-09-PLAIN-Q2/` at HEAD is therefore a *merge of two sessions* and
is neither session's package. Nine of session A's original artifacts (including all nine
negative-control transcript pairs, the rollback recovery audit, the observer campaigns, the
WSL2 campaign results and all six TRX campaign envelopes) survive at HEAD byte-identically,
but session A's overwritten Windows campaign results and its manifest and verifier survive
only inside commit `96a1a3c`.

Per-file state of the package directory across the two commits:

| Package path | State |
|---|---|
| `.gitattributes` | modified by B |
| `README.md` | modified by B |
| `build-production.log` | modified by B |
| `build-solution.log` | modified by B |
| `campaign/baseline-f01-win.json` | modified by B |
| `campaign/baseline-f01-wsl.json` | session B only |
| `campaign/baseline-f01-wsl2.json` | identical |
| `campaign/baseline-obs-win.json` | identical |
| `campaign/baseline-obs-wsl2.json` | identical |
| `campaign/nc08-probe-f01.json` | identical |
| `campaign/r1-all-win.json` | modified by B |
| `campaign/r1-all-wsl.json` | session B only |
| `campaign/r1-all-wsl2.json` | identical |
| `campaign/r1-backpressure-win.json` | modified by B |
| `campaign/r1-backpressure-wsl.json` | session B only |
| `campaign/r1-f01-win.json` | modified by B |
| `campaign/r1-f01-wsl.json` | session B only |
| `campaign/r1-f01-wsl2.json` | identical |
| `campaign/r1-nc-f01-f01-win.json` | identical |
| `campaign/r1-obs-win.json` | identical |
| `campaign/r1-obs-wsl2.json` | identical |
| `core-tests.trx` | identical |
| `driver-integration.trx` | identical |
| `driver-interop.trx` | identical |
| `format-check.log` | modified by B |
| `independence-statement.txt` | modified by B |
| `integration.trx` | identical |
| `manifest.json` | modified by B |
| `negative-controls-assessment.md` | modified by B |
| `negative-controls/nc-01.log` | identical |
| `negative-controls/nc-01.trx` | identical |
| `negative-controls/nc-02.log` | identical |
| `negative-controls/nc-02.trx` | identical |
| `negative-controls/nc-03.log` | identical |
| `negative-controls/nc-03.trx` | identical |
| `negative-controls/nc-04.log` | identical |
| `negative-controls/nc-04.trx` | identical |
| `negative-controls/nc-05.log` | identical |
| `negative-controls/nc-05.trx` | identical |
| `negative-controls/nc-06.log` | identical |
| `negative-controls/nc-06.trx` | identical |
| `negative-controls/nc-07.log` | identical |
| `negative-controls/nc-07.trx` | identical |
| `negative-controls/nc-08.log` | identical |
| `negative-controls/nc-08.trx` | identical |
| `negative-controls/nc-09.log` | identical |
| `negative-controls/nc-09.trx` | identical |
| `negative-controls/run_controls.py` | identical |
| `r1-nc-f01-mutation.patch` | session B only |
| `recovery-audit.md` | identical |
| `verify-evidence.py` | modified by B |
| `wsl2-core-tests.trx` | identical |
| `wsl2-driver-interop.trx` | identical |

Summary: 35 identical, 13 modified by B, 0 session-A-only, 5 session-B-only.

**Resolution.** Both packages are materialized byte-exactly under this owner evidence
package by [`extract-q2-packages.py`](extract-q2-packages.py), sourced only from
`git cat-file blob` payload bytes at the two pinned commits. Nothing in
`docs/reviews/evidence/Q-B5B-09-PLAIN-Q2/` was overwritten, moved or deleted, and no
history was rewritten. Both preserved copies carry a per-file Git blob ID, byte count and
SHA-256 inventory in their `PROVENANCE.md`.

**Byte-stability guard.** Because session B's own attributes would rewrite the inherited
CRLF evidence blobs (D-11), each preserved copy carries a governing `.gitattributes`
containing `* -text`, and the source package's own `.gitattributes` is preserved
byte-exactly beside it as `.gitattributes.session-a` / `.gitattributes.session-b`. This is
the only relocation in either preserved copy, and it is recorded in both `PROVENANCE.md`
inventories. Every preserved file — including the source `.gitattributes` — commits with
the identical Git blob ID as its source; `generate-manifest.py` enforces this identity and
refuses to write a manifest if any restored file would be altered.

## 4. Substantive convergence

Both qualifications reach the **same overall disposition** and the **same finding
dispositions**:

| Finding | Session A | Session B | Canonical |
|---|---|---|---|
| `Q-B5B-09-PLAIN-Q1-F01` | SUPPORTED (remediated) | SUPPORTED | SUPPORTED (remediated) |
| `Q-B5B-09-PLAIN-Q1-F02` | SUPPORTED (remediated) | SUPPORTED | SUPPORTED (remediated) |
| `Q-B5B-09-PLAIN-Q1-F03` | SUPPORTED (remediated) | SUPPORTED | SUPPORTED (remediated) |
| `Q-B5B-09-PLAIN-Q2-F01` observer retention | SUPPORTED WITH QUALIFICATIONS | SUPPORTED WITH QUALIFICATIONS | SUPPORTED WITH QUALIFICATIONS |
| `Q-B5B-09-PLAIN-Q2-F02` backpressure evidence | INCONCLUSIVE (probe limitation) | INCONCLUSIVE (probe limitation) | INCONCLUSIVE (probe limitation) |
| **Overall** | **SUPPORTED WITH QUALIFICATIONS** | **SUPPORTED WITH QUALIFICATIONS** | **SUPPORTED WITH QUALIFICATIONS** |

Both preserved qualification records carry the same preserved-qualification set
(B5-B-TS1 CLOSED, Q-B5B-03 CLOSED, Q-B5B-04 OPEN, Q-B5B-05 OPEN, Q-B5B-09 OPEN, TLS
graceful-close DEFERRED, native Linux and representative production capacity OUTSTANDING)
and the same recommendation: commission `Q-B5B-09-PLAIN-OWNER-01`, do not implement fixes,
do not push.

### Independent F01 reproduction — the two campaigns agree

Both reviewers reproduced the original defect on the baseline tree and confirmed the
correction on the R1 tree, with different harnesses and different result schemas:

| Property | Baseline `20fbc46` (both sessions) | R1 `6a40c4b` (both sessions) |
|---|---|---|
| Terminal FAILURE byte-exact at peer | yes | yes |
| Peer ending | EOF | EOF |
| Lifecycle reason | `write-deadline` | `peer-eof` |
| Lifecycle phase | `write` | `drain` |
| Drain admitted | false | true |
| Connections completed | 0 | 1 |
| Connections failed | 1 | 0 |
| Write timeout / observer delay | 80 ms / 140 ms | 80 ms / 140 ms |

Session A recorded the peer receipt and the handle-closure state (`handlesClosed: true`);
session B recorded the drain byte and end timings. The union is strictly more detailed
than either alone and contains no contradiction.

### Independent observer-retention assessment — the two campaigns agree

Both reviewers measured that a synchronous `MessageProcessed` observer can outlive socket
disposal, that the connection task and admission slot remain occupied until the observer
returns, and that `StopAsync` can return at its configured shutdown deadline with an
active connection task remaining. Both classified this as consistent with the binding
specification (which preserves admission ownership through task retirement and carves
arbitrary synchronous calls out of the timer's preemptive control) and recorded it as the
non-blocking qualification `Q-B5B-09-PLAIN-Q2-F01` with a bounded-resource-count versus
unbounded-retention-duration caveat.

Session A additionally demonstrated the **inheritance** of the behaviour by measuring the
same retention against the original plaintext implementation
(`campaign/baseline-obs-win.json`, `campaign/baseline-obs-wsl2.json`), and ran the
observer experiments OBS-01..OBS-06 on both platforms with separate measurement of socket
disposal, permit release, completion accounting, task completion, admission-slot release,
listener shutdown and observer completion. Session B's observer assessment is a qualitative
table with no dedicated campaign artifacts.

### Independent backpressure assessment — the two campaigns agree

Session A's exploratory probe attempted to induce a genuinely pending terminal write across
12 socket-buffer profiles × 4 stimuli on each platform and recorded
`no-terminal-write-pending` for all 48 attempts per platform; it therefore classified the
exploratory result as inconclusive and justified reuse of the Q1 transport evidence on the
verified source-level comparison that the physical write and cancellation physics are
unchanged between `20fbc46` and `6a40c4b`.

Session B captured the decisive output of R1's **canonical** oracle
`ActualPendingTerminalWriteIsInterrupted` (four stimuli) and
`SuccessfulWriteEndsWriteTimeoutBeforeObserver` (six scenarios) on Windows and WSL2. Its
file is named `r1-backpressure-*.json`, but its content is canonical-oracle output, not an
exploratory pending-write induction. Session B's own finding text nevertheless records
Q2-F02 as **INCONCLUSIVE — probe limitation**.

**Both sessions therefore support the same owner statement**: fresh exploratory probes did
not establish decisive pending-write observations, the prior Q1 transport evidence remains
applicable on the verified unchanged write/cancellation semantics, the R1 production-path
pending-write oracle passes on Windows and WSL2, and the inconclusive exploratory trials
remain correctly identified as inconclusive.

## 5. Canonical acceptance basis

Where the two qualifications overlap, the owner accepts the **union** of their evidence and
adopts the following precedence rule for each acceptance claim:

| Acceptance claim | Canonical source | Reason |
|---|---|---|
| F01 baseline reproduction and correction | Both campaigns, union of recorded fields | Independent agreement, no contradiction |
| F02 diagnostic metadata | Session A's `campaign/r1-all-win.json` and `campaign/r1-all-wsl2.json` plus session B's `campaign/r1-all-wsl.json` | Both cover the same event set |
| F03 TLS isolation | Session A's `campaign/r1-all-*.json` and `negative-controls/nc-07.*`; session B's `campaign/r1-all-wsl.json` | Session A carries the transcript pair |
| Observer retention | Session A's `campaign/r1-obs-*.json`, `campaign/baseline-obs-*.json` and `campaign/nc08-probe-f01.json` | Only session A carries the measurements and the inheritance comparison |
| Backpressure non-claim | Session A's `campaign/r1-backpressure-win.json` (the explicit `no-terminal-write-pending` record) plus session B's `campaign/r1-backpressure-wsl.json` (canonical oracle output) | Session A carries the decisive exploratory record; session B corroborates the oracle outcomes |
| Negative controls (nine of nine) | Session A's `negative-controls/nc-01..nc-09` logs and TRX | Session B committed no control transcripts; the transcripts at HEAD are session A's |
| Rollback provenance | Session A's `recovery-audit.md` and `manifest.json → rollbackRecovery` | Only session A recorded the recovery inventory |
| TRX campaign counters | Session A's six TRX envelopes | Identical at HEAD, verified over Git blobs |
| Builds and formatting | Session A's `build-solution.log`, `build-production.log` and `format-check.log` | Session A recorded the two xUnit2012 warnings and the eight pre-existing whitespace findings; session B's format check was scoped to the four R1 paths |
| Overall disposition and findings | Both | Unanimous |

## 6. Material discrepancies

| ID | Discrepancy | Assessment | Disposition |
|---|---|---|---|
| **D-01** | Session B modified 13 files of session A's evidence package in place and added 5, so the package directory at HEAD is a merge of two sessions rather than either session's package. Session A's overwritten Windows campaign results, manifest and verifier are reachable only through commit `96a1a3c`. | Evidence-preservation defect, not a qualification defect. | Both packages materialized byte-exactly under this owner evidence package; the HEAD package directory is left untouched. |
| **D-02** | Session B's `manifest.json` records `sha256: ""` and `bytes: 0` for all 19 entries. It carries no integrity content and cannot detect tampering or byte drift. | Manifest defect. | Recorded. Session B's package is preserved verbatim but is **not** relied on as the integrity anchor. Session A's manifest (48 entries, all populated and matching) is the integrity anchor; the owner verifier re-derives both manifest sets over Git blobs independently. |
| **D-03** | Session B's `verify-evidence.py` cannot pass on session B's own commit. It requires the Q2 commit's parent to equal the R1 evidence commit `d7e5ac1` (session B's parent is `96a1a3c`); it reads `manifest["files"]` (a mapping) while the manifest provides `evidenceFiles` (a list); it asserts an additions-only scope diff against `d7e5ac1`, which does not hold for a child commit; and it resolves campaign blob paths relative to the current working directory rather than to the repository root. It raises `ValueError: Unexpected Q2 qualification parent` on `3c2144a` and on HEAD. | Verifier defect. | Recorded. Session B's package is preserved verbatim; the owner verifier supplies an independent, checkout-independent, Git-blob-based verification of both packages. |
| **D-04** | Session A's `verify-evidence.py` hashes the **working-tree** copy of every manifest entry but compares against **Git-blob** SHA-256 values, so it is not checkout-independent. The package's `.gitattributes` (`* -text`) protects the 47 package files, but the report entry `../../Q-B5B-09-PLAIN-Q2-qualification.md` lies outside the package. In a fresh Windows checkout with `core.autocrlf=true` the report is CRLF-normalized (21,520 blob bytes → 21,867 worktree bytes; SHA-256 `51a6435b…` → `0bf23605…`), and the verifier ends `VERIFICATION FAILED` on exactly that one entry. | Verifier defect; the underlying evidence is sound. | Recorded. Reproduced in a disposable worktree at `96a1a3c`: all 47 package blobs and the other seven check groups pass; only the report entry fails. The owner verifier hashes Git blobs exclusively and is unaffected. |
| **D-05** | Session B's report links to `Q-B5B-09-PLAIN-OBS-01.md`, which does not exist at any pinned revision. | Dangling documentation reference. | Recorded. No OBS-01 document is created by this decision; the observer-isolation backlog item supersedes the proposal as a recorded, non-authorized item. |
| **D-06** | Session B labels the 43-test suite "Driver Integration"; session A labels the same 43 tests "Official driver interop" and reports a distinct "Driver integration" suite of 85 (75 passed, 10 skipped). Session B's §14.10 "1,270 core+integration+driver tests" does not reconcile with session B's own suite table total of 1,364 (§9.1/9.2) nor with session A's counters. | Reporting/labeling inconsistency; the underlying 43-test count is the official-driver interop suite. | Recorded. The owner acceptance uses session A's figures, which are backed by TRX envelopes verified at blob level: core 1,257, official-driver interop 43, driver integration 85 (75/10), integration 19. |
| **D-07** | Session B records `dotnet format --verify-no-changes` as "✅ (4 R1 paths)" — scoped to the four R1 files — while session A additionally recorded the 8 pre-existing whitespace findings in `BoltMessageFixtures.cs` that exist in the pristine baseline. | Scope difference, not a contradiction. | Recorded. The owner acceptance records eight pre-existing whitespace findings and two pre-existing xUnit2012 warnings, on session A's evidence, and states that they pre-date R1. |
| **D-08** | Negative-control coverage differs. Session A executed NC-01..NC-09 with transcripts and TRX on both platforms and added NC-09 to prove the resource-lifetime oracle is effective. Session B ran NC-01..NC-08 on Windows plus a new observer-retention control; NC-02..NC-06 were not run on WSL2. Session B committed no control transcripts, so the transcripts at HEAD are session A's. | Coverage difference, no contradiction. | Recorded. The owner acceptance records nine of nine detected, on session A's transcripts, and notes session B's independent Windows corroboration and its WSL2 subset. |
| **D-09** | Observer-retention measurement exists only in session A's package (dedicated OBS campaign JSONs on both platforms plus the baseline inheritance comparison). Session B's §5.2 is qualitative. | Evidence-distribution difference. | Recorded. Session A's observer artifacts are the canonical measurement; session B's independent assessment corroborates the classification. |
| **D-10** | Session B's files named `r1-backpressure-*.json` contain canonical-oracle output rather than an exploratory pending-write induction, while session A's same-named file records the exploratory probe's `no-terminal-write-pending` failures. | Naming/content mismatch. | Recorded. The backpressure non-claim rests on session A's explicit exploratory record plus both sessions' identical INCONCLUSIVE finding, and on the canonical oracle passing on both platforms. |
| **D-11** | Session B's `.gitattributes` replaced session A's `* -text` byte-exactness guard with `* text=auto` (plus `*.trx text`, `*.log text`, `*.json text`, `*.md text`, `*.patch text`, `*.py text`). 26 of the blobs session B inherited from session A — committed by session A under `* -text` — contain CRLF, so they contradict session B's own attributes. A fresh Windows checkout of session B's own commit `3c2144a` therefore reports **26 modified files** in the evidence package, and re-adding any of them under session B's attributes silently rewrites the evidence bytes. Combined with D-02, session B's package provides no tamper detection, is not checkout-clean, and is not byte-stable. | Evidence-integrity weakness, material to the package's usability as an evidence artifact. | Recorded, and empirically reproduced in a disposable worktree (`reconciliation/session-b-package-checkout-dirtiness.log`). Both preserved copies carry a `* -text` guard so the restored bytes are byte-stable and checkout-clean. |

**None of D-01 to D-11 changes either qualification's disposition, findings or
recommendation.** Every discrepancy is in package assembly, manifest content, verifier
implementation, labelling, scope or naming — not in the production behaviour under review
or in the measured outcomes.

## 7. Actions taken by this reconciliation

1. Both Q2 packages materialized byte-exactly under
   `docs/reviews/evidence/Q-B5B-09-PLAIN-OWNER-01/q2-session-a/` and
   `q2-session-b/`, with per-file blob-ID/byte/SHA-256 inventories. Every preserved file
   commits with the identical Git blob ID as its source.
2. The merged package directory `docs/reviews/evidence/Q-B5B-09-PLAIN-Q2/` was **not**
   modified, so no historical evidence was overwritten.
3. No qualification campaign was re-executed.
4. No history was rewritten; no commit was amended or rebased.
5. A single owner decision record was created — no duplicate owner decision.
6. The owner acceptance basis was established over Git blobs, independently of both
   packages' own verifiers, so that the defects in D-03 and D-04 cannot affect the
   acceptance result.
7. Two disposable verification worktrees were used to reproduce D-03 and D-04 and to
   reproduce the D-11 checkout dirtiness, and were removed afterwards.

## 8. Evidence index for this reconciliation

| File | Content |
|---|---|
| [`reconciliation/discrepancy-evidence.log`](reconciliation/discrepancy-evidence.log) | Byte-level evidence for D-02, D-04 and D-11 and the package-collision counts |
| [`reconciliation/session-a-verifier-at-own-commit.log`](reconciliation/session-a-verifier-at-own-commit.log) | Session A's verifier run in a disposable worktree at `96a1a3c` — one false FAIL on the report entry (D-04) |
| [`reconciliation/session-b-verifier-at-own-commit.log`](reconciliation/session-b-verifier-at-own-commit.log) | Session B's verifier run in a disposable worktree at `3c2144a` — `ValueError: Unexpected Q2 qualification parent` (D-03) |
| [`reconciliation/session-b-package-checkout-dirtiness.log`](reconciliation/session-b-package-checkout-dirtiness.log) | 26 modified files reported by a clean checkout of session B's own commit (D-11) |
| [`reconciliation/diagnose-line-endings.py`](reconciliation/diagnose-line-endings.py), [`reconciliation/capture-discrepancy-evidence.py`](reconciliation/capture-discrepancy-evidence.py) | Reproducible generators for the two logs above |
