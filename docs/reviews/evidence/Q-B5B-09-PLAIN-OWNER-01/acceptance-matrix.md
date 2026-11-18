# Acceptance Matrix — Q-B5B-09-PLAIN-OWNER-01

Owner decision: [`Q-B5B-09-PLAIN-OWNER-01-owner-acceptance.md`](../../../decisions/Q-B5B-09-PLAIN-OWNER-01-owner-acceptance.md)

Evidence paths are relative to this directory. `q2-session-a/` is the first independent Q2
qualification (`96a1a3c`); `q2-session-b/` is the parallel independent Q2 qualification
(`3c2144a`). Both are preserved byte-exactly — see
[`q2-reconciliation.md`](q2-reconciliation.md).

## A. Requirement acceptance

| # | Requirement (binding specification) | Accepted basis | Independent Q2 evidence | Accepted |
|---|---|---|---|---|
| A1 | Terminal `FAILURE` byte-exact | Byte-identical against the accepted session encoding on Windows and WSL2 | `q2-session-a/campaign/*-f01-win.json`, `*-f01-wsl2.json`; `q2-session-a/core-tests.trx`, `wsl2-core-tests.trx` (`ExactFailureAndNoTrailingExecution`); `q2-session-b/campaign/*-f01-win.json`, `*-f01-wsl.json` | yes |
| A2 | Exactly one terminal response | One response observed; no trailing response in any scenario | `ExactFailureAndNoTrailingExecution`; `QueuedTailComparison`; session B `q2-session-b/campaign/r1-all-*.json` | yes |
| A3 | Defunct state preserved | Session stays Defunct; no terminal-session revival | `q2-session-a/core-tests.trx`, `wsl2-core-tests.trx` (`CleanupAndTelemetryRemediationTests`, `FailureAndRecoveryTests`) | yes |
| A4 | Trailing commands cannot reach parser or execution | Zero `PrepareCount` after terminal refusal | `ExactFailureAndNoTrailingExecution` (asserts `provider.PrepareCount == 0`); `q2-session-a/negative-controls/nc-01.*` | yes |
| A5 | Queued-tail delivery improvement in characterized cases | Q1 characterization preserved; canonical oracle passes on both platforms | Q1 `docs/reviews/Q-B5B-09-PLAIN-Q1-qualification.md` (preserved, byte-verified); `q2-session-a/core-tests.trx`, `wsl2-core-tests.trx` | yes |
| A6 | Write-specific timeout ownership ends at flush | 80 ms write timeout with a 140 ms observer completes and drains | `q2-session-a/campaign/baseline-f01-*` vs `r1-f01-*` (10× repetition per platform); `q2-session-b/campaign/baseline-f01-*` vs `r1-f01-*`; `q2-session-b/r1-nc-f01-mutation.patch` | yes |
| A7 | Absolute deadline not renewed | Single deadline covers write/flush/shutdown/drain | `OneDeadlineCoversWriteAndDrain` (Windows + WSL2); `q2-session-a/negative-controls/nc-04.*` | yes |
| A8 | Drain byte/time/memory/concurrency limits enforced | 8 KiB cap, 75 ms sub-budget clamped to remaining total, fixed 2 KiB scratch, `MaxConnections - 1` permits | `ExactFailureAndNoTrailingExecution(scenario: "large")`; `DrainAdmissionAndRecovery`; `q2-session-a/negative-controls/nc-05.*`, `nc-06.*`, `nc-09.*` | yes |
| A9 | Socket and drain-permit cleanup correct | Idempotent disposal; exactly one permit release after handle disposal | `ListenerStopJoinsActualHandleAndReturnsPermit`; `q2-session-a/negative-controls/nc-02.*`, `nc-09.*` | yes |
| A10 | Connection accounting correct | Exactly one terminal transition; correct completion/failure classification | F01 baseline vs R1 comparison in both sessions; `Diagnostics_SuccessfulLifecycleMetadata` | yes |
| A11 | TLS isolation from the plaintext path | Zero plaintext helper events over TLS; no permit, no raw drain | `TlsRefusalNeverEntersPlaintextHelper`; `Tls_IsolationFromPlaintextHelper`; `q2-session-a/negative-controls/nc-07.*` | yes |
| A12 | Negative controls effective | Nine of nine detected | `q2-session-a/negative-controls/nc-01.log` … `nc-09.log` + `.trx`; `q2-session-a/negative-controls/run_controls.py` | yes |

**Non-claim**: no row above asserts guaranteed delivery to the peer application.

## B. Finding closure

| Finding | Original disposition | Closure | Q2 evidence |
|---|---|---|---|
| `Q-B5B-09-PLAIN-Q1-F01` | REMEDIATION REQUIRED | CLOSED BY VERIFIED REMEDIATION | Both sessions' F01 baseline/corrected campaigns; 10× repeats per platform |
| `Q-B5B-09-PLAIN-Q1-F02` | REMEDIATION REQUIRED | CLOSED BY VERIFIED REMEDIATION | Both sessions' diagnostics metadata and correlation scenarios |
| `Q-B5B-09-PLAIN-Q1-F03` | REMEDIATION REQUIRED | CLOSED BY VERIFIED REMEDIATION | TLS isolation probes, comment restoration, typed-linger preservation |

## C. Q2 qualifications accepted as non-blocking

Both independent reviewers reached the same dispositions.

| Finding | Disposition | Key recorded facts |
|---|---|---|
| `Q-B5B-09-PLAIN-Q2-F01` observer resource retention | ACCEPTED (inherited architectural limitation) | Observers outlive socket disposal; tasks and admission slots held until return; blocked observer can refuse healthy connections; `StopAsync` returns at its deadline with an active task; bounded resource count but **not** bounded retention duration; equivalent retention on the original implementation |
| `Q-B5B-09-PLAIN-Q2-F02` backpressure evidence | ACCEPTED (evidence reuse) | Exploratory probes inconclusive on both platforms; Q1 evidence applicable via verified unchanged write/cancellation semantics; R1 pending-write oracles pass; trials remain inconclusive |

## D. Typed-linger acceptance

| Item | Record |
|---|---|
| Commit | `dac52d55ade0a9ae703b801c8a4066e8aef2bd58` |
| Original WSL2 failure | boolean `SO_LINGER` returned `EINVAL 22` |
| Correction | typed `LingerState = new LingerOption(false, 0)` |
| Scope | Windows and WSL2 verified; native Linux outstanding |
| Disposition | ACCEPTED WITHIN THE VERIFIED WINDOWS AND WSL2 SCOPE |

## E. Regression acceptance

| Suite | Windows | WSL2 |
|---|---|---|
| Core | 1,257 / 0 / 0 | 1,257 / 0 / 0 |
| Official-driver interop | 43 / 0 / 0 | 43 / 0 / 0 |
| Driver integration (Windows) | 75 passed, 10 pre-existing skips | — |
| Integration (Windows) | 19 / 0 / 0 | — |

Session B corroborates core 1,257 and official-driver 43 on both platforms and adds a
171-test focused repetition campaign (3 × 57) that passed on both platforms.

## F. Two-qualification reconciliation

| Item | Result |
|---|---|
| Ancestry | session B (`3c2144a`) is a child of session A (`96a1a3c`); same R1 tree, same producer ancestor |
| Package collision | session B modified 13 files of session A's package in place and added 5 (D-01) |
| Both packages preserved | Yes — `q2-session-a/` (49 files) and `q2-session-b/` (54 files), byte-exact from Git blobs |
| Dispositions | Unanimous on overall disposition and on all five findings |
| Material discrepancies | 11 (D-01 … D-11), all in package assembly, manifest content, verifier implementation, labelling, scope or naming |
| Effect on this acceptance | None — the owner verifier re-derives both packages over Git blobs, independently of either package's own verifier |
| Preservation | Both copies commit with the identical Git blob ID as their source file; a `* -text` guard makes the restored bytes checkout-clean and byte-stable |
