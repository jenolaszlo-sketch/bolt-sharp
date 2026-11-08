# B3-B-Q3 retained reviewer evidence

Independent requalification of B3-B-R2-IMPL-01 (resource ownership, metadata acquisition and
fail-closed retirement). Reviewed revision: `b9356c3fadb484b9b3a39d807205283155886184` / tree
`f592acde2571e2084601b85a147eb48098b987e7`; R2 implementation `7eb9a8fcee5d4d89d333a920a6c3307e84ad4210`.
See `../../B3-B-Q3-independent-requalification.md` for the report. Reviewer evidence; not owner
acceptance.

## Reproduction

Use a fresh absolute scratch directory outside the canonical checkout; the script creates no
worktree and changes no repository file. The reviewer probe and evidence directories are additive
untracked artifacts during capture.

```powershell
$evidence = 'D:\projects\bolt-sharp\docs\reviews\evidence\B3-B-Q3'
$scratch = 'C:\review-scratch\B3-B-Q3'
& "$evidence\Run-Q3.ps1" -Repo 'D:\projects\bolt-sharp' -Scratch $scratch `
    -ExpectedHead b9356c3fadb484b9b3a39d807205283155886184
```

Expected reviewed-revision results: builds and all test suites exit 0 (core **1043**, session
**119**, remediation class **21**, R2 lifecycle class **18**, messages **98**, B0-B integration
**19**); unchanged Q1 probe exits **4** (118 checks / 117 passing, historical X04 only);
unchanged Q2 probe exits **4** (181 checks / 163 passing / 18 failing / 217 trace steps — all
superseded or unreachable, classified per-check); independent Q3 probe exits **0**
(**129 checks / 129 passing**, 165 trace steps). Native exit codes are logged; the wrappers'
PowerShell exit status is not the assertion result.

## Artifact map

- `baseline.json`: reviewed HEAD/tree, clean production state, change isolation since the Q2
  baseline (`4ae87b4`): exactly the four R2 files plus the additive reviewer artifacts.
- `environment.txt`: toolchain at capture time.
- `r2-source-delta.patch`: the complete `src`+`tests` delta `4ae87b4..HEAD` (the R2 change under
  review).
- `historical-integrity.json`, `historical-integrity-summary.txt`: all eleven applicable
  manifests verified in place. Full matches: B3-B-Q1 12/12, B3-B-Q2 38/38, B3-B-R2 29/29,
  B3-A-Q1 14/14, B3-A-R1 13/13, B3-A-Q2 15/15, B0-B-DRV-INT-01 24/24, B0-B-Q1 119/119. Expected
  source evolution (each superseded entry re-verified against its recorded historical commit —
  see report §3): B3-B-IMPL-01 19/22, B3-B-R1 12/14; B3-A-IMPL-01 16/21 (documented B3-A-R1
  informational delta).
- `reviewed-inputs.json`, `reviewed-session-source.txt`: reviewer source snapshot (current
  production session source and its inputs, with informational SHA-256).
- `core-build.log`, `core-tests.log`, `core.trx`, `session-tests.log`, `session.trx`,
  `remediation-tests.log`, `remediation.trx`, `r2-tests.log`, `r2-lifecycle.trx`,
  `message-tests.log`, `message.trx`, `integration-build.log`, `integration-tests.log`,
  `integration.trx`: complete build/test results (filtered suites overlap the core suite).
- `q1-probe-build.log`, `q1-probe.log`, `q1-probe-after/session-probe-results.json`: unchanged
  Q1 reviewer probe (118 checks; only the historical X04 expectation fails; exit 4).
- `q2-probe-build.log`, `q2-probe.log`, `q2-probe-after.json`: unchanged Q2 adversarial probe
  against R2 (181/163/18/217; exit 4). The probe was not edited.
- `q3-probe-build.log`, `q3-probe.log`, `q3-probe-results.json`: the independent Q3 probe
  (129 checks / 129 passing / 165 trace steps; exit 0). The results embed the full 181-check Q2
  reconciliation (per-check classification, justification and replacement assertion outcome) and
  the deterministic per-action traces behind every report claim.
- `manifest.json`: SHA-256 inventory of this directory and the report (canonical CRLF checkout
  form). No binaries retained.

## Reviewer notes

- The Q1/Q2 probes were executed unmodified; their retained expectations are superseded or
  unreachable exactly as classified in `q3-probe-results.json` and report §9. No historical
  failure is reinterpreted as acceptable safety behavior.
- The Q3 probe shares no code with the producer tests or earlier probes; scenarios use fresh
  sessions/providers with instrumented providers tracking opens, disposal attempts, resource
  liveness and metadata access counts.
- Independence caveat: the reviewer session is the same executor as the R2 producer session;
  report §2 records the measures taken and the residual limitation.
- Evidence capture uses `Start-Process` stream redirection so native stdout/stderr (including the
  Q1 probe's stderr summary) is preserved verbatim under Windows PowerShell 5.1 and PowerShell 7.
