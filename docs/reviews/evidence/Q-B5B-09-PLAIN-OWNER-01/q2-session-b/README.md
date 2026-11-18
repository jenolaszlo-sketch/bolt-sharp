# Q-B5B-09-PLAIN-Q2 Evidence Package

[Qualification report](../../Q-B5B-09-PLAIN-Q2-qualification.md) · [Manifest](manifest.json) · [Independence statement](independence-statement.txt) · [Negative controls](negative-controls-assessment.md)

## Campaign Records

| Platform | Suite | Tests | Passed | Failed | Skipped |
|----------|-------|-------|--------|--------|---------|
| Windows | Core | 1,257 | 1,257 | 0 | 0 |
| Windows | Driver Integration | 43 | 43 | 0 | 0 |
| Windows | Plaintext Terminal Close | 28 | 28 | 0 | 0 |
| Windows | TLS Integration | 23 | 23 | 0 | 0 |
| Windows | Q2 Probe | 13 | 13 | 0 | 0 |
| WSL2 | Core | 1,257 | 1,257 | 0 | 0 |
| WSL2 | Driver Integration | 43 | 43 | 0 | 0 |
| WSL2 | Plaintext Terminal Close | 28 | 28 | 0 | 0 |
| WSL2 | TLS Integration | 23 | 23 | 0 | 0 |
| WSL2 | Q2 Probe | 13 | 13 | 0 | 0 |

## Key Campaign Files

| File | Description |
|------|-------------|
| `campaign/baseline-f01-win.json` | F01 baseline reproduction on Windows (original code) |
| `campaign/r1-f01-win.json` | F01 corrected behavior on Windows (R1 code) |
| `campaign/r1-all-win.json` | Full Windows campaign summary |
| `campaign/r1-backpressure-win.json` | Backpressure tests on Windows |
| `campaign/baseline-f01-wsl.json` | F01 baseline reproduction on WSL2 |
| `campaign/r1-f01-wsl.json` | F01 corrected behavior on WSL2 |
| `campaign/r1-all-wsl.json` | Full WSL2 campaign summary |
| `campaign/r1-backpressure-wsl.json` | Backpressure tests on WSL2 |

## Build and Format

| File | Description |
|------|-------------|
| `build-production.log` | Production project build log |
| `build-solution.log` | Full solution build log |
| `format-check.log` | `dotnet format --verify-no-changes` output |
| `core-tests.trx` | Core test results (TRX) |
| `driver-integration.trx` | Driver integration test results |
| `integration.trx` | Transport integration test results |

## Negative Controls

| File | Description |
|------|-------------|
| `negative-controls-assessment.md` | Assessment of all 8 negative controls |
| `r1-nc-f01-mutation.patch` | NC-08 F01 mutation applied to controls tree |

## Integrity Verification

Run from repository root at the qualification commit:

```powershell
python docs/reviews/evidence/Q-B5B-09-PLAIN-Q2/verify-evidence.py HEAD
```

The verifier uses SHA-256 and byte length over `git cat-file blob REV:PATH`. It checks:
- Implementation and evidence inventories
- Sole-parent relationships
- Historical documentation identity
- Exact ZIP member inventory
- Key campaign counters
- Named failures for defects and negative controls

## Evidence Integrity Notes

- R1 evidence package (148 archive members) independently verified
- Q1 evidence package (8 blobs, 137 members) byte-identical at Q1 and HEAD
- Historical evidence (TS1, owner decisions) integrity confirmed
- Protected source and configuration scopes unchanged
- No modifications to canonical production or test source during qualification