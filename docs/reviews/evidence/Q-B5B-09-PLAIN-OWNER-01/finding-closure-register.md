# Finding Closure Register — Q-B5B-09-PLAIN-OWNER-01

Owner decision: [`Q-B5B-09-PLAIN-OWNER-01-owner-acceptance.md`](../../../decisions/Q-B5B-09-PLAIN-OWNER-01-owner-acceptance.md)

Original Q1 dispositions are preserved as historical evidence. Each closure below is a
forward-looking owner closure traceable to the independent Q2 evidence of **both** parallel
qualifications.

Evidence paths are relative to this directory. `q2-session-a/` is the first independent Q2
qualification (`96a1a3c`); `q2-session-b/` is the parallel independent Q2 qualification
(`3c2144a`).

| Finding | Title | Original Q1 disposition | Closure | Accepted by | Independent Q2 evidence |
|---|---|---|---|---|---|
| `Q-B5B-09-PLAIN-Q1-F01` | Write-timeout misclassification of a successful terminal write | REMEDIATION REQUIRED | CLOSED BY VERIFIED REMEDIATION | `3c2144a0d655f1e0b7187ca0bf8ac6de1bef8e8f` | A: `q2-session-a/campaign/baseline-f01-win.json`, `baseline-f01-wsl2.json`, `r1-f01-win.json`, `r1-f01-wsl2.json`, `r1-nc-f01-f01-win.json`, `nc08-probe-f01.json`; `q2-session-a/core-tests.trx`, `wsl2-core-tests.trx` (`SuccessfulWriteEndsWriteTimeoutBeforeObserver`). B: `q2-session-b/campaign/baseline-f01-win.json`, `baseline-f01-wsl.json`, `r1-f01-win.json`, `r1-f01-wsl.json`; `q2-session-b/r1-nc-f01-mutation.patch` |
| `Q-B5B-09-PLAIN-Q1-F02` | Diagnostics correlation and metadata gaps | REMEDIATION REQUIRED | CLOSED BY VERIFIED REMEDIATION | `3c2144a0d655f1e0b7187ca0bf8ac6de1bef8e8f` | A: `q2-session-a/campaign/r1-all-win.json`, `r1-all-wsl2.json` (`Diagnostics_SuccessfulLifecycleMetadata`, `Diagnostics_CapacityFallbackMetadata`). B: `q2-session-b/campaign/r1-all-win.json`, `r1-all-wsl.json` |
| `Q-B5B-09-PLAIN-Q1-F03` | Incidental shared transport changes | REMEDIATION REQUIRED | CLOSED BY VERIFIED REMEDIATION | `3c2144a0d655f1e0b7187ca0bf8ac6de1bef8e8f` | A: `q2-session-a/negative-controls/nc-07.log` / `nc-07.trx` (`TlsRefusalNeverEntersPlaintextHelper`); `q2-session-a/campaign/r1-all-win.json`, `r1-all-wsl2.json` (`Tls_IsolationFromPlaintextHelper`); `q2-session-a/wsl2-core-tests.trx` (`TlsStartup_UsesDisabledLinger`, `PlaintextStartup_UsesDisabledLinger`, `RestartIsNotSupported_NewInstanceIsRequired`). B: `q2-session-b/campaign/r1-all-win.json`, `r1-all-wsl.json` |

## Accepted Q2 qualifications (not closed)

| Finding | Title | Session A disposition | Session B disposition | Status after this decision |
|---|---|---|---|---|
| `Q-B5B-09-PLAIN-Q2-F01` | Observer resource retention | SUPPORTED WITH QUALIFICATIONS | SUPPORTED WITH QUALIFICATIONS | ACCEPTED as a non-blocking inherited architectural limitation; backlog item created |
| `Q-B5B-09-PLAIN-Q2-F02` | Backpressure evidence reuse | INCONCLUSIVE (probe limitation) | INCONCLUSIVE (probe limitation) | ACCEPTED as an evidence-reuse justification; no further investigation authorized |

## Reconciliation note

The two qualifications are unanimous on every disposition and contradict no measured
outcome. Eleven discrepancies were recorded (D-01 … D-11 in
[`q2-reconciliation.md`](q2-reconciliation.md)); all concern package assembly, manifest
content, verifier implementation, labelling, scope or naming, and none affects the
closures above.
