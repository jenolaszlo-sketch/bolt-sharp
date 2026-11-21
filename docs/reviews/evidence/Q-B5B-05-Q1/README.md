# Q-B5B-05-Q1 evidence — Independent Same-Connection RESET Recovery Qualification

Reviewer report: [`Q-B5B-05-Q1-independent-qualification.md`](../../../reviews/Q-B5B-05-Q1-independent-qualification.md)

**Formal disposition: SUPPORTED WITH QUALIFICATIONS** (server-side capability demonstrated and
independently reproduced; official-driver post-failure RESET interoperability not demonstrated and
not claimed). `Q-B5B-05` remains **OPEN** — no owner decision is issued by this commission.

Reviewer independence: this commission was executed by a fresh reviewer session that did not
author the Q-B5B-05-PROBE-01 probe, did not execute the producer verification, and did not write
the producer report or its conclusions. Producer evidence was used as input only. All
qualification requirements were re-derived from the binding contracts
(`02-contract-interpretation.md`).

## Contents

| File | Description |
|---|---|
| `01-preflight.txt` | Starting repository identity: branch, HEAD, tree, parent, divergence, worktrees, stashes, cleanliness, producer commit scope |
| `02-contract-interpretation.md` | The original Q-B5B-05 requirement verbatim and its independent derivation — including whether driver-initiated RESET is mandated |
| `03-identity-matrix.md` | Same-physical-connection identity matrix: the six identity lines and their per-scenario verdicts |
| `04-protocol-traces.md` | Required byte-exact markers, canonical wire sequences, state transitions, handshake bytes |
| `05-driver-behavior-assessment.md` | Independent D-01..D-05 with Neo4j.Driver 6.4.0 (own proxy, own code) |
| `06-negative-controls.md` | Independent NC-01..NC-04: intent, injection, path, oracle, result |
| `07-campaign-windows.md` | Windows campaign record: environment, builds, commands, counts |
| `08-campaign-wsl2.md` | Ubuntu WSL2 campaign record: environment, builds, commands, counts |
| `09-integrity-verification.txt` | Producer-package verification: producer self-check + reviewer-independent blob verification |
| `10-findings-register.md` | Findings F01..F08 per commission §15 categories |
| `11-owner-recommendation.md` | Evidence-backed owner decision preparation (Option A recommended) |
| `windows/`, `wsl2/` | Reviewer harness per-scenario evidence (15 scenarios: response bytes, transcripts, counters, connection ids, provider counts, server traces, verdicts) + machine-readable results |
| `q5b05q1-core-windows.trx`, `q5b05q1-core-wsl2.trx` | Core solution envelopes (1,257 tests each, reviewer-run) |
| `q5b05q1-interop-windows.trx`, `q5b05q1-interop-wsl2.trx` | Official-driver interoperability envelopes (43 tests each, reviewer-run) |
| `q5b05q1-probe-windows.trx`, `q5b05q1-probe-wsl2.trx` | Producer probe suite envelopes (13 tests each, reviewer-run in the reviewer clones) |
| `windows-focused-campaign.txt`, `wsl2-focused-campaign.txt` | Focused suites and bounded repeated runs |
| `manifest.json`, `verify-evidence.py` | Self-verifying package over Git blobs |

All evidence is credential-free: LOGON payloads are redacted by construction (raw client) and by
the proxy (driver scenarios).

## Verify

```text
python docs/reviews/evidence/Q-B5B-05-Q1/verify-evidence.py
```

The verifier works over Git blob payloads only (checkout line-ending independent). It checks the
sole parent, additions-only scope, the complete package inventory, SHA-256 + byte counts + Git
blob IDs, the six reviewer TRX campaign counters, the per-scenario verdicts and assertion counts
in both platform result files, and the credential-redaction invariant.