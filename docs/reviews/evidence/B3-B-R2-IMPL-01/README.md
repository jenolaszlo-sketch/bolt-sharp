# B3-B-R2 retained producer evidence

Remediation of **B3-B-Q2-F01** (unsafe recovery after failed cleanup) and **B3-B-Q2-F02**
(metadata acquisition outside the ownership boundary).

Entry baseline: `4ae87b4f944a5c16cbd90a734b1ebfddc9fe0236` / tree
`f4d2ebb606ac2c34f2b5ffe6e39eb700d71e7852`. Implementation commit:
`7eb9a8fcee5d4d89d333a920a6c3307e84ad4210` / tree `c62e9d21ea578c4186bf34ca0b9eb280055eae4d`.
See `../../B3-B-R2-IMPL-01.md` for the report. This is producer evidence, not independent
qualification (B3-B-Q3) and not owner acceptance.

## Reproduction

Use a fresh absolute scratch directory outside the canonical checkout; the script creates no
worktree and changes no repository file. Run it against a checkout of the R2 implementation
commit (a later documentation commit moves HEAD; the `-ExpectedHead` guard then fails by design):

```powershell
$evidence = 'D:\projects\bolt-sharp\docs\reviews\evidence\B3-B-R2-IMPL-01'
$scratch = 'C:\review-scratch\B3-B-R2'
& "$evidence\Run-R2.ps1" -Repo 'D:\projects\bolt-sharp' -Scratch $scratch `
    -ExpectedHead 7eb9a8fcee5d4d89d333a920a6c3307e84ad4210
```

Expected reviewed-implementation results: builds and all test suites exit 0 (core **1043**,
session **119**, remediation class **21**, R2 lifecycle class **18**, messages **98**, B0-B
integration **19**); unchanged Q1 probe exits **4** with 118 checks / 117 passing (historical X04
only); unchanged Q2 probe exits **4** with 181 checks / 163 passing / 18 failing / 217 trace
steps. The wrapper exit status is not the assertion result; native exit codes are logged.

The Q2 probe's remaining 18 failures are superseded pre-R2 expectations or scenarios made
unreachable by design; `q2-probe-before-after.json` classifies every check (152 pass-pass,
11 resolved, 11 superseded, 7 non-safety stale) and the report explains each group. No unresolved
safety failure remains: all five `-no-overlap` checks (`opens` must stay 1, never 2) and six F02
containment checks flipped to passing.

## Artifact map

- `baseline.json`: implementation HEAD/tree, clean status, R2 delta stat/name-status.
- `environment.txt`: toolchain captured at evidence time.
- `r2-implementation-delta.patch`: complete `src` + `tests` delta `4ae87b4..7eb9a8f`.
- `historical-integrity.json`, `historical-integrity-summary.txt`: all ten applicable historical
  manifests verified in place. Full matches: B3-B-Q1 12/12, B3-B-Q2 38/38, B3-A-Q1 14/14,
  B3-A-R1-IMPL-01 13/13, B3-A-Q2 15/15, B0-B-DRV-INT-01 24/24, B0-B-Q1 119/119. Expected source
  evolution (never concealed): B3-B-IMPL-01 19/22 and B3-B-R1 12/14 — the five entries changed by
  the authorized R1/R2 remediation; B3-A-IMPL-01 16/21 — the documented B3-A-R1 delta, retained
  for comparison.
- `core-build.log`, `core-tests.log`, `core.trx`, `session-tests.log`, `session.trx`,
  `remediation-tests.log`, `remediation.trx`, `r2-tests.log`, `r2-lifecycle.trx`,
  `message-tests.log`, `message.trx`, `integration-build.log`, `integration-tests.log`,
  `integration.trx`: complete build/test results (filtered suites overlap the core suite).
- `q1-probe-build.log`, `q1-probe.log`, `q1-probe-after/session-probe-results.json`: the unchanged
  B3-B-Q1 reviewer probe built from the current source and executed. The probe writes its summary
  to stderr; the capture retains both native streams verbatim.
- `q2-probe-build.log`, `q2-probe-after.log`, `q2-probe-after.json`: the unchanged B3-B-Q2
  adversarial probe executed against R2. The historical probe was not edited.
- `q2-probe-before-after.json`: per-check reconciliation against the retained pre-R2 result
  `docs/reviews/evidence/B3-B-Q2/q2-probe-results.json` (SHA-256
  `1ef748c7ebffb821e867c78ab2c409d65b06cdd4e2527dcc7cefe8e9f635f73d`).
- `manifest.json`: SHA-256 inventory of this directory and the report. No binaries retained.

## Notes

- The failed-retirement contract change supersedes the R1-era expectation that an exhausted
  result with a throwing disposal remains a recoverable `FAILED`. That supersession is required by
  B3-B-Q2-F01 and the R2 commission (§4.5–§4.6, §6.1) and is recorded, not silent: the three
  affected R1 tests were updated and strengthened in the implementation commit.
- `LastCleanupFailure` remains a bounded diagnostic: recording assigns the provider exception
  reference without reading `Message`, no protocol response or automatic log gains provider text,
  and the diagnostic survives (is never cleared by) the terminal transition.
- Evidence capture uses `Start-Process` stream redirection so native stdout/stderr text is
  preserved verbatim under both Windows PowerShell 5.1 and PowerShell 7.
- The R2 probe/evidence set is producer-authored; independent requalification is B3-B-Q3.
