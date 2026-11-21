# Q-B5B-05-PROBE-01 evidence

Producer report: [`Q-B5B-05-PROBE-01-producer-report.md`](../../../reviews/Q-B5B-05-PROBE-01-producer-report.md)

**Producer disposition: PRODUCTION LIMITATION IDENTIFIED — OWNER DECISION REQUIRED.**
`Q-B5B-05` remains OPEN.

## Contents

| File | Description |
|---|---|
| `commissioning-record.md` | Requirements, probe-owner identity, frozen production revision, authorized scope, experimental matrix — recorded before execution |
| `requirements-matrix.md` | Exact protocol-sequence and connection-identity requirements with the five independent evidence lines |
| `driver-behavior-assessment.md` | How the official Neo4j.Driver 6.4.0 actually initiates RESET and what it does after a recoverable FAILURE |
| `negative-controls-results.md` | NC-01..NC-04: intent, injection, observed path, detecting oracle, result |
| `windows/p01…d04,nc01…nc04` | Per-platform correlated traces: response hex, server counters, connection ids, provider counts, full diagnostics trace |
| `wsl2/…` | The same artifacts produced on Ubuntu WSL2 |
| `q5b05-probe.trx`, `q5b05-probe-wsl2.trx` | Producer probe suite envelopes (13 tests each) |
| `q5b05-core-windows.trx`, `q5b05-core-wsl2.trx` | Core solution envelopes (1,257 tests each) |
| `q5b05-driver-interop-windows.trx`, `q5b05-driver-interop-wsl2.trx` | Official-driver interop envelopes (43 tests each) |
| `build-probe-release.log` | Release build of the probe project (0 warnings, 0 errors) |
| `format-check.log` | Scoped `dotnet format --verify-no-changes` result |
| `manifest.json` | SHA-256 and byte count for every package file (except itself) |
| `verify-evidence.py` | Self-verifying evidence verifier over Git blobs |
| `q5b05-evidence.json` | Consolidated machine-readable results summary |

All captured output is credential-free: LOGON payloads are redacted by the proxy and never
stored.

## Verify

```text
python docs/reviews/evidence/Q-B5B-05-PROBE-01/verify-evidence.py
```

The verifier works over Git blob payloads only, so it is independent of checkout line-ending
normalization. It checks the sole parent and tree, complete inventory, additions-only scope,
byte counts and SHA-256 over exact Git blob payloads, the five TRX campaign counters, the
credential-redaction invariant, the per-platform trace artifacts, and the negative-control
detection records.

## Results summary

| Suite | Windows | Ubuntu WSL2 |
|---|---|---|
| Producer probe suite (13 probes) | 13 passed | 13 passed |
| Core solution | 1,257 passed | 1,257 passed |
| Official-driver interoperability | 43 passed | 43 passed |
| Negative controls | 4 of 4 detected | 4 of 4 detected |

Principal finding: the production server supports recoverable FAILURE → RESET → subsequent
successful operation on one physical connection; the official driver does not RESET after a
recoverable FAILURE and instead recovers on a new physical connection with a new handshake and
a new authentication.