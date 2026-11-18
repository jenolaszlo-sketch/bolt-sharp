# Q-B5B-09-PLAIN-Q2 Evidence Package

Independent qualification of the R1 plaintext terminal graceful-close implementation.

- **Qualification report**: [`../Q-B5B-09-PLAIN-Q2-qualification.md`](../../Q-B5B-09-PLAIN-Q2-qualification.md)
- **Disposition**: SUPPORTED WITH QUALIFICATIONS
- **Binding specification**: [`Q-B5B-09-PLAIN-IMPL-01`](../../decisions/Q-B5B-09-PLAIN-IMPL-01-implementation-specification.md)

## Repository identity

| Property | Value |
|---|---|
| Branch | `main` |
| HEAD | `d7e5ac14704a9576813e32692b29050ef92ee7b8` |
| HEAD tree | `b98b57723ac6e8754961261e235a74013ca76058` |
| Divergence | 16 ahead / 0 behind `origin/main` |
| Stashes | 0 |
| Final worktrees | 1 (main) |

Pinned: original plaintext `20fbc461c0257620db28fb78a463218b242455bc`, Q1
`9dd2fe1b9b6f8ab2880e3fe50b8ef9ac2d43ae9c`, R1 `6a40c4b84993dec77deb165cef31e419df05be2e`
(tree `a23edab4242711fffc28da63f254ff0f8b4d3082`), R1 evidence `d7e5ac1…`, typed linger
`dac52d55ade0a9ae703b801c8a4066e8aef2bd58`.

## Contents

### Qualification documents

| File | Description |
|---|---|
| `README.md` | This index |
| `manifest.json` | Self-verifying manifest (SHA-256, byte counts, campaign counters) |
| `verify-evidence.py` | Integrity verifier |
| `independence-statement.txt` | Reviewer identity and independence basis |
| `recovery-audit.md` | Rollback recovery and evidence provenance inventory |
| `negative-controls-assessment.md` | Negative-control results and interpretation |

### Campaign results — Windows 10.0.26200 (.NET 8.0.31, SDK 10.0.401)

| File | Description |
|---|---|
| `campaign/baseline-f01-win.json` | F01 defect reproduced on baseline (`20fbc46`) — original bytes, hash-verified |
| `campaign/r1-f01-win.json` | F01 corrected on R1 |
| `campaign/r1-obs-win.json` | OBS-01…OBS-06 observer/resource experiments |
| `campaign/r1-all-win.json` | Full R1 scenario suite |
| `campaign/r1-backpressure-win.json` | Dedicated backpressure attempts |
| `campaign/baseline-obs-win.json` | Baseline blocked-observer retention (inheritance proof) |
| `campaign/r1-nc-f01-f01-win.json` | Earlier partial F01-mutation probe (superseded by NC-08) |
| `campaign/nc08-probe-f01.json` | F01 probe against the fully reintroduced defect |

### Campaign results — Ubuntu WSL2 6.6.114.1 (.NET 10.0.14, SDK 10.0.114)

| File | Description |
|---|---|
| `campaign/baseline-f01-wsl2.json` | F01 defect reproduced on WSL2 |
| `campaign/baseline-obs-wsl2.json` | Baseline blocked-observer retention on WSL2 |
| `campaign/r1-f01-wsl2.json` | F01 corrected on WSL2 |
| `campaign/r1-obs-wsl2.json` | OBS-01…OBS-06 on WSL2 |
| `campaign/r1-all-wsl2.json` | Full R1 scenario suite incl. backpressure on WSL2 |

### Regression TRX

| File | Suite | Outcome |
|---|---|---|
| `core-tests.trx` | Full core, Windows | 1,257 passed / 0 failed / 0 skipped |
| `wsl2-core-tests.trx` | Full core, WSL2 | 1,257 passed / 0 failed / 0 skipped |
| `driver-interop.trx` | Official driver, Windows | 43 passed / 0 failed / 0 skipped |
| `wsl2-driver-interop.trx` | Official driver, WSL2 | 43 passed / 0 failed / 0 skipped |
| `driver-integration.trx` | Driver integration, Windows | 75 passed / 0 failed / 10 skipped |
| `integration.trx` | Integration, Windows | 19 passed / 0 failed / 0 skipped |

### Negative controls (`negative-controls/`)

`nc-01.log` … `nc-09.log`, matching `.trx`, and `run_controls.py`. Nine controls, all
detected; tree restored and verified clean after each control.

### Build and format logs

| File | Description |
|---|---|
| `build-production.log` | `dotnet build BoltSharp.slnx -c Release` |
| `build-solution.log` | `dotnet build BoltSharp.slnx -c Debug -t:Rebuild` (exactly two xUnit2012 warnings) |
| `format-check.log` | `dotnet format --verify-no-changes` (eight pre-existing whitespace errors) |

## Verify

```powershell
python docs/reviews/evidence/Q-B5B-09-PLAIN-Q2/verify-evidence.py
```

The verifier checks the four R1 source blobs against `6a40c4b`, the five R1 evidence
blobs against `d7e5ac1`, the surviving-evidence hash provenance, every evidence file's
SHA-256 and byte count, the campaign counters, the build/format warnings, and the
cleanliness of all historical evidence directories.

## Independent probe (temporary, not committed)

`C:\Windows\Temp\opencode\q2w\probe\` — `Q2Probe.csproj`, `Support.cs`, `Program.cs`.
Scenarios: F01 baseline/correction, observer delay and exception, OBS-01…OBS-06,
genuine pending-write attempts, diagnostics metadata, TLS isolation, repeated runs.
Structured JSON with timing, phase, reason, admission, accounting, handle and permit
state.