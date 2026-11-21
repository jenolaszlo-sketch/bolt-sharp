# Recovery Audit — Q-B5B-09-PLAIN-Q2

## Rollback event

During the first Q2 session, the environment performed a workspace rollback of
`C:\Windows\Temp\opencode\q2`, restoring it to an earlier Q1 producer campaign state
(`baseline/`, `controls/`, `source/`). The rollback also removed part of the
in-progress Q2 evidence under `docs/reviews`. A cleanup record was written:

`C:\Windows\Temp\opencode\q2-removed-artifacts.txt`

That record is a **SHA-256 provenance inventory** of the Q2 evidence files that existed
before the rollback. It is the primary basis for deciding which results retain
trustworthy provenance.

## Survived (verified against the pre-rollback inventory)

| Artifact | Pre-rollback SHA-256 | Current SHA-256 | Status |
|---|---|---|---|
| `campaign/baseline-f01-win.json` | `812ff11b5e4f71cf…` | `812ff11b5e4f71cf…` | **IDENTICAL — original bytes** |
| `campaign/r1-f01-win.json` | `d7e47d8d4b48beff…` | `d7e47d8d4b48beff…` | **IDENTICAL — original bytes** |
| `campaign/r1-all-win.json` | `143cc89b79889493…` | `143cc89b79889493…` | **IDENTICAL — original bytes** |
| `campaign/r1-backpressure-win.json` | `64f104e4803623a8…` | `64f104e4803623a8…` | **IDENTICAL — original bytes** |
| `campaign/r1-nc-f01-f01-win.json` | `79fbdee81f671403…` | `79fbdee81f671403…` | **IDENTICAL — original bytes** |

These five Windows campaign results were copied out of the temporary results directory
before the rollback and are byte-identical to the pre-rollback inventory. They retain
full provenance and were **not re-run**. Each JSON carries its own execution timestamp,
runtime, platform and per-scenario detail, so the identity is self-evident.

## Lost and regenerated

| Artifact | Pre-rollback | Current | Disposition |
|---|---|---|---|
| `build-production.log` | `7d9d30d005b2c6ca…` | regenerated | Fresh Release build; identical outcome (0 warnings, 0 errors) |
| `build-solution.log` | `b7c93716e4bfabb9…` | regenerated | Fresh `-t:Rebuild` Debug build; two xUnit2012 warnings at `BoltRequestDecodeTests.cs:71,72` |
| `format-check.log` | `af273135942089e0…` | regenerated | Fresh `dotnet format --verify-no-changes`; eight pre-existing whitespace errors in `BoltMessageFixtures.cs` |
| `core-tests.trx` | `971b8bce48b41f6a…` | regenerated | Fresh run: 1,257 passed / 0 failed / 0 skipped |
| `driver-integration.trx` | `412df1203b764763…` | regenerated | Fresh run: 75 passed / 0 failed / 10 skipped |
| `driver-interop.trx` | `e9b4eab7baea2488…` | regenerated | Fresh run: 43 passed / 0 failed / 0 skipped |
| `integration.trx` | `0e498168603ec8f7…` | regenerated | Fresh run: 19 passed / 0 failed / 0 skipped |

The TRX files embed per-case timestamps and durations, so a regenerated TRX cannot be
byte-identical. Every regenerated TRX has the **same test count and outcome distribution**
as the pre-rollback run, and the log/TRX content is re-verified here.

## Never persisted (executed fresh this session)

No WSL2 campaign result was ever copied into the repository before the rollback (the
inventory contains no `*-wsl2.json` entry). Therefore all WSL2 results in this package
are fresh executions from this session:

| Artifact | Campaign | Result |
|---|---|---|
| `campaign/baseline-f01-wsl2.json` | Baseline F01 reproduction on Ubuntu WSL2 6.6.114.1, .NET 10.0.14 | Defect reproduced: phase `write`, reason `write-deadline`, admitted `false`, completed 0, failed 1 |
| `campaign/baseline-obs-wsl2.json` | Baseline blocked-observer retention measurement | Socket closed 88.3 ms while observer blocked; `active=1`, `tasks=1` retained until release |
| `campaign/r1-f01-wsl2.json` | R1 F01 correction | phase `drain`, reason `peer-eof`, admitted `true`, completed 1, failed 0 |
| `campaign/r1-obs-wsl2.json` | R1 OBS-01…OBS-06 | All 7 scenarios passed |
| `campaign/r1-all-wsl2.json` | R1 full scenario suite incl. backpressure | All passed except backpressure (environment-limited) |
| `wsl2-core-tests.trx` | Full core suite on WSL2 | **1,257 passed / 0 failed / 0 skipped** |
| `wsl2-driver-interop.trx` | Official driver suite on WSL2 | **43 passed / 0 failed / 0 skipped** |

## Documentation files (lost, rewritten)

`README.md`, `manifest.json`, `verify-evidence.py`, `independence-statement.txt`,
`negative-controls-assessment.md`, `negative-controls-assessment` transcripts and the
qualification report were removed twice by the rollback and are **rewritten in this
session** with the complete final content. The pre-rollback hashes remain in the cleanup
record for reference; the current package is verified by `verify-evidence.py`.

## Temporary workspace

A second temporary area, `C:\Windows\Temp\opencode\q2w`, was used to avoid the rollback
path. It holds:

- Two disposable Git worktrees at the pinned revisions: `r1` (`6a40c4b`) and
  `baseline` (`20fbc46`).
- `probe/` — the independent probe source (`Q2Probe.csproj`, `Support.cs`, `Program.cs`).
- `nc/run_controls.py` — the integrated negative-control runner.
- `nc-logs/` — per-control build/test logs and TRX files.

These are temporary and are not committed; the canonical repository tree is never
modified by them (each control ends with `git checkout -- src/` and a clean-status
check).

## Independence review after the rollback

- The reviewer session is unchanged: it did not implement R1, did not perform producer
  verification, and had no access to R1 internals before this commission.
- All re-executed campaigns used pristine copies extracted from the pinned Git commits.
- No test result was reconstructed from a summary: every regenerated artifact is a fresh
  execution with its own embedded timestamps.
- The previously reported Windows results were retained only where byte-identity with the
  pre-rollback inventory could be proven; all other claims were re-executed.