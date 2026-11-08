# B3-C-Q1 retained reviewer evidence

Independent qualification of B3-C-IMPL-01 (deterministic read-only reference graph backend).
Reviewed revision `79ddd04da40d5c6eb7fb8dc0177ef7188b613e75` / tree
`bdc50fb27f86bc6f9e7fbbf50157ec5f3d320adf`; B3-C implementation `37fe406…`. See
`../../B3-C-Q1-independent-qualification.md` for the report. Reviewer evidence; not owner
acceptance.

## Reproduction

Use a fresh absolute scratch directory outside the canonical checkout; the script creates no
worktree and changes no repository file.

```powershell
$evidence = 'D:\projects\bolt-sharp\docs\reviews\evidence\B3-C-Q1'
$scratch = 'C:\review-scratch\B3-C-Q1'
& "$evidence\Run-B3CQ1.ps1" -Repo 'D:\projects\bolt-sharp' -Scratch $scratch `
    -ExpectedHead 79ddd04da40d5c6eb7fb8dc0177ef7188b613e75
```

Expected: all suites exit 0 — core **1,137**, B3-C backend filter **94**, sessions **119**,
messages **98**, B0-B integration **19**; build **0 errors / 2 pre-existing xUnit2012 warnings**;
scoped format check exit 0; independent reviewer probe exit 0 with **125 checks / 125 passing /
180 traces**, and the repeat run byte-identical (determinism). Native exit codes are logged
verbatim. Build commands use `--nodeReuse:false` because persistent MSBuild nodes holding
redirected stdout handles were observed to hang `Start-Process -Wait` captures.

## Artifact map

- `baseline.json`: reviewed HEAD/tree, clean status, change isolation since the B3-B owner
  acceptance (exactly the twelve B3-C files plus the B3-C report/evidence).
- `environment.txt`: toolchain at capture time.
- `historical-integrity.json`, `historical-integrity-summary.txt`: all twelve applicable manifests
  verified in place — B3-C-IMPL-01 26/26, B3-B-Q3 33/33, B3-B-R2 29/29, B3-B-Q2 38/38, B3-B-Q1
  12/12, B3-A-Q1 14/14, B3-A-R1 13/13, B3-A-Q2 15/15, B0-B producer 24/24, B0-B-Q1 119/119;
  informational evolution B3-B-IMPL-01 19/22, B3-A-IMPL-01 16/21 (superseded entries re-verified
  at blob level against `9f224cf`/`454ae00`).
- `owner-decisions-diff.txt`: both owner decisions byte-identical across the B3-C commits.
- `reviewed-inputs.json`: SHA-256 of the reviewed B3-C production and test sources.
- `core-build.log`, `format-check.log`, `core-tests.log`, `core.trx`, `backend-tests.log`,
  `b3c-backend.trx`, `session-tests.log`, `session.trx`, `message-tests.log`, `message.trx`,
  `integration-build.log`, `integration-tests.log`, `integration.trx`: complete build/test/format
  results (filtered suites overlap the core suite).
- `q1-probe-build.log`, `q1-probe.log`, `q1-probe-results.json`: the independent reviewer probe
  (125 checks / 125 passing / 180 traces / exit 0) — graph oracle, allowlist battery, option
  matrix, parameter battery, cursor lifecycle, fail-closed fault matrix, byte oracle, negative
  controls, with per-action traces.
- `q1-probe-repeat.log`, `q1-probe-results-repeat.json`, `q1-probe-determinism.txt`: a second
  fresh-process run; results **byte-identical** (determinism evidence).
- `t04-fixture-provenance.txt`: verification that the authentic t03/t04 golden hex vectors are
  unchanged since B3-A `d99096a` (pickaxe single-introduction) and match the B0-B-Q1 independent
  decode frame.
- `manifest.json`: SHA-256 inventory of this directory and the report (canonical CRLF checkout
  form). No binaries retained.

## Notes

- The reviewer probe shares no code with the producer tests; its oracle constants come from the
  spec (§7) and the B0-B-Q1 standalone decode of the authentic t04 frame.
- **Independence caveat:** the reviewer session is the same executor as the B3-C producer
  session; report §1 discloses this prominently, and the B3-B owner decision's bounded waiver and
  future different-executor requirement apply.
- The `B3-B-Q1` probe targets the B3-B session engine, not B3-C; it was not re-run here. B3-B
  regression suites were fully re-executed instead.
