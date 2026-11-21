# Q-B5B-09-TLS-LINUX-QUAL-01-R1 — Conformance Remediation Report

## Executive Summary

**Disposition: A — Ready for Independent Readiness Review**

The R1 conformance remediation harness has been implemented and committed locally. The harness addresses every Q1 finding from the independent qualification review and establishes a truthful, executable qualification instrument that can be run on native Linux (ubuntu-latest) against the pinned BoltSharp production revision `7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f`.

## Q1 Finding-by-Finding Remediation Matrix

| Q1 Finding | R1 Remediation | Status |
|------------|----------------|--------|
| **L1/L2**: Raw TCP, no TLS/BoltSharp, synthetic pending (`PendingMs=ElapsedMs`) | Real `BoltTcpServer` over TLS with `BoltTcpConnection` pipeline; genuine pending via `MessagesProcessed=3`, `Peer.Available>0`, `ProcNetTcp.TxQueue`, `SenderWritable=false` | ✅ Implemented |
| **L3/L4**: 0.14ms/0.11ms unconditional flag, no TLS shutdown | Real `SslStream.ShutdownAsync` against zero-window Python peer (`SO_RCVBUF=512`); observed pending ≥20ms; L4 adds gated TLS proxy | ✅ Implemented |
| **L5**: No independent encrypted/plaintext counters; 131072 accepted as clean; NC-1 hardcoded | Candidate TLS drain with independent `ciphertextBytes` (TLS write counter) and `plaintextBytes` (production `BoltPlaintextTerminalClose` drain-end event); 32768 cap enforced; NC-1 executed with 131072 tamper → `cap-checks-failed` | ✅ Implemented |
| **L6**: Ran 32768 cap instead of 8192; no TLS encryption measurement | Candidate TLS drain with 8192 plaintext cap; observed ciphertext bounded by 32768; production supplementary L6-PROD exercises real `BoltPlaintextTerminalClose` 8192 cap | ✅ Implemented |
| **L7**: In-process raw socket; no accounting; 0.034ms same-process receipt | Production TLS terminal failure message read by independent Python peer process (`receipt_peer.py`); receipt journal validated against campaign journal (digest + byte count); full cleanup/accounting (`Closed`, `Active=0`, permits balanced) | ✅ Implemented |
| **Repeatability**: 1 attempt/cell; no seeds; breaks on first success | Seeded determinism (1001-1007); discovery ≤4 profiles; 2 qualifying repeats on same profile; max 6 runs/cell | ✅ Implemented |
| **Negative Controls**: NC-1/NC-2 hardcoded `detected=true`, never executed | NC-1: ciphertext cap 32768→131072 → `cap-checks-failed`; NC-2: deadline 500→800ms → `induced-but-checks-failed` (elapsed > 500ms); both bounded to 3 attempts | ✅ Implemented |
| **Evidence Integrity**: Mixed journal (35 records), appended cache, no per-cell files | Fresh run-scoped results directory (fail if `campaign-journal.jsonl` exists); sealed `run-meta.json` with commit/tree; per-cell `*.jsonl` + `*.summary.json`; negative-control journals; peer receipt journals | ✅ Implemented |
| **Verifier**: None; self-tests were smoke tests | Independent `verify_r1.py` validates all O1-O11 criteria; self-test proves rejection of EXEC-05 (5 errors) and detection of all 18 Q1 false-pass conditions | ✅ Implemented |

## Architecture

### Harness (C# .NET 10)
- **Program.cs**: Campaign driver, fresh results, watchdogs, repeatability, per-cell journals
- **Production.cs**: L1, L2, L7, L6-PROD — real BoltSharp production TLS execution
- **Candidate.cs**: L3, L4, L5, L6 — candidate mechanisms explicitly declared
- **Controls.cs**: NC-1, NC-2 — executed tamper controls
- **Support.cs**: Shared infra (certs, wire, BoltPeer, PythonProcess, PlaintextCloseCapture, OS probes)

### Peer Processes (Python 3 stdlib ssl)
- **zero_window_peer.py**: TLS server, parks receive after handshake (SO_RCVBUF=512)
- **receipt_peer.py**: Independent peer application, reads terminal message, records receipt
- **gated_proxy.py**: TLS proxy observes close_notify, gates forwarding for L4

### Verifier (Python)
- **verify_r1.py**: Validates all cells against registered criteria (O1-O11)
- **verifier_selftest.py**: Proves verifier rejects EXEC-05 and detects all Q1 false-pass conditions

## Commit Identities

| Identity | Value |
|----------|-------|
| R1 Harness Commit | `7cd00537ed68d241fb44931f9e23ad9d4e2a5243` |
| R1 Harness Tree | `fe39b6d28f87a05de46233fd78f715a1aa190c15` |
| Pinned Production Commit | `7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f` |
| Pinned Production Tree | `2fdf1c3b96586465eb7547d23b6b1792240692ba` |
| Q1 Review Commit | `8ddedea438848f0ae83846551890c6d2ba31b89b` |
| Q1 Review Tree | `bc254ceb6d3236875781868400bdabd3969aafc6` |

## Verification Evidence

### Verifier Self-Test Results
```
TEST: valid run basic structure - PASS
TEST: EXEC-05 rejection - PASS (5 errors detected)
  - EXEC-05 has 4 records, expected 35 mixed
  - EXEC-05 L1 claims TLS but has no TLS
  - EXEC-05 L3 pending_ms=0.137 < 20ms
  - EXEC-05 L5 wire_bytes=131072 > 32768 but clean
  - EXEC-05 L6 declared_limit=32768 != 8192
TEST: Q1 false-pass condition detection - PASS (18/18 detected)
  - L1: pending < 20ms, sender writable, peer unread 0, buffer not configured, elapsed > 500ms,
        accounting active != 0, accounting unbalanced, terminal write completed, peer not closed, stop not bounded
  - L3: shutdown pending < 20ms, sender writable
  - L5: drain at wrong cap, tampered cap accepted
  - L6: wrong declared limit
  - L7: no receipt, receipt digest mismatch, peer unread not zero
  - NC-2: deadline not exceeded
ALL SELF-TESTS PASSED
```

## Constraints Satisfied

- ✅ Harness-only changes; no production, existing test, API, or dependency changes
- ✅ No weakening of registered criteria (32768/8192 caps, 20ms minimum, 500ms bound, 3500ms watchdog)
- ✅ All historical evidence preserved (Q1 review, E1 evidence, EXEC-05 artifacts)
- ✅ No remote writes or GitHub Actions execution
- ✅ No qualification or owner-acceptance claim
- ✅ Fresh run-scoped results, sealed manifest, explicit commit/tree verification
- ✅ Complete per-cell receipts, fail-closed aggregation, independent verifier

## Next Commission Recommendation

**Q-B5B-09-TLS-LINUX-QUAL-01-EXEC-06** — Native Linux clean re-execution on ubuntu-latest (Ubuntu2404)
- Scope: Execute R1 harness on native Linux via GitHub Actions workflow
- Gate: Owner authorization per COMMISSIONING.md; `RUNNER_OS == Linux && uname -s == Linux`
- Deliverable: Campaign artifacts + independent verification + local commit of evidence
- Stop after evidence publication; do not claim A until independent reviewer reproduces all criteria

## Implementation Handoff

The R1 harness is complete and committed. It is ready for:
1. Owner authorization to run on native Linux (per COMMISSIONING.md)
2. Independent readiness review of the harness implementation
3. Native Linux execution via the registered workflow

No further implementation work is required. The harness implements the exact mechanisms registered in `campaign-plan.json` with explicit declaration of candidate-only evidence (L3/L4 close_notify, L5/L6 caps) vs. production evidence (L1/L2/L7/L6-PROD).