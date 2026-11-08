# B3-C-IMPL-01 retained producer evidence

Deterministic read-only reference graph backend (B3-C) over the accepted B3-A codec and B3-B
session engine. Entry baseline `aae3d0e986c3cef1dd4e0e49356cffafd06094d4` / tree
`ef144bfd289e9b2b3599ed5c2b3f46111f5bee47`; implementation commit
`37fe40633624a62bc4c6bd6162de2c884e1f0188` / tree `4fc8d822d36352d002252421543fb60db89cc64e`.
See `../../B3-C-IMPL-01.md` for the report. Producer evidence; not independent qualification and
not owner acceptance.

## Reproduction

Use a fresh absolute scratch directory outside the canonical checkout; the script creates no
worktree and changes no repository file.

```powershell
$evidence = 'D:\projects\bolt-sharp\docs\reviews\evidence\B3-C-IMPL-01'
$scratch = 'C:\review-scratch\B3-C'
& "$evidence\Run-B3C.ps1" -Repo 'D:\projects\bolt-sharp' -Scratch $scratch `
    -ExpectedHead 37fe40633624a62bc4c6bd6162de2c884e1f0188
```

Expected: all suites exit 0 — core **1,137**, B3-C backend filter **94**, sessions **119**,
R1 remediation **21**, R2 remediation **18**, messages **98**, B0-B integration **19**; build
**0 errors / 2 pre-existing xUnit2012 warnings**; scoped `dotnet format --verify-no-changes`
on the new paths exits 0. Native exit codes are logged verbatim.

## Artifact map

- `baseline.json`: implementation HEAD/tree, clean status, change isolation since the B3-B owner
  acceptance (exactly the twelve new B3-C files).
- `environment.txt`: toolchain at capture time.
- `historical-integrity.json`, `historical-integrity-summary.txt`: B3-B-Q3 33/33, B3-B-R2 29/29,
  B3-B-Q2 38/38, B3-B-Q1 12/12, B3-A-Q1 14/14, B3-A-R1 13/13, B3-A-Q2 15/15, B0-B producer
  24/24, B0-B-Q1 119/119; informational evolution: B3-B-IMPL-01 19/22, B3-A-IMPL-01 16/21
  (documented authorized remediations).
- `owner-decisions-diff.txt`: both owner decisions byte-identical across the B3-C commit.
- `reviewed-inputs.json`: SHA-256 of the new production and test sources.
- `core-build.log`, `format-check.log`, `core-tests.log`, `core.trx`, `backend-tests.log`,
  `b3c-backend.trx`, `session-tests.log`, `session.trx`, `r1-remediation-tests.log`,
  `r1-remediation.trx`, `r2-remediation-tests.log`, `r2-remediation.trx`, `message-tests.log`,
  `message.trx`, `integration-build.log`, `integration-tests.log`, `integration.trx`: complete
  build/test results (filtered suites overlap the core suite).
- `manifest.json`: SHA-256 inventory of this directory and the report (canonical CRLF checkout
  form). No binaries retained.

## Notes

- The Level C byte-path tests compare responses against the retained B0-B golden fixtures
  (`RunSuccessFieldsAnswer`, `Record42`, `RunSuccessFieldsGraph`, `RecordGraph`) — those golden
  byte vectors live in the test assembly (`BoltMessageFixtures.B0B`), not duplicated here; the
  TRX results record their passing assertions.
- The report's §18 limitations (exact-form matching, refusal granularity, fixed parameter name,
  bounded materialization, the B3-B `has_more`-absence observation) are documented boundaries for
  B3-C-Q1, not defects.
- Historical reports, probes, evidence and both owner decisions were not modified.
