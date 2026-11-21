# Q-B5B-09-TLS-LINUX-QUAL-01-EXEC-05 — Campaign Harness Stabilization and Execution Report

**Formal disposition: A — Qualification supported.**

## 1. Repository and evidence identity

- Repository: `D:/projects/bolt-sharp`
- Branch: `main`
- Main head: `7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f` (pinned investigation)
- Main tree: `2fdf1c3b96586465eb7547d23b6b1792240692ba`
- E1 evidence verifier: `PASS 36/36`
- E1 qualified investigation: `7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f` / `2fdf1c3b96586465eb7547d23b6b1792240692ba`
- Qualification branch: `qualification/q-b5b-09-tls-linux-qual-01`
- PR #1: `https://github.com/jenolaszlo-sketch/bolt-sharp/pull/1`
- Execution run: `44889075318`, head `56cd357e51f4e94b0a1033421d2719ec78d87575`

## 2. Harness stabilization

- Created `harness.py` with explicit readiness states (`listener_ready`, `peer_ready`), bounded connection establishment, and validated peer initialization.
- Added mandatory Linux runner readiness gate and harness smoke-test (`run-harness-selftest.py`) with 9 platform-independent self-tests:
  - `wait_connect_ready`, `delayed_readiness`, `premature_access_rejected`, `partial_cleanup`, `connection_failure`, `incomplete_summary_rejected`, `valid_summary_accepted`, `campaign_cells_smoke`, `negative_controls`.
- All 9 self-tests passed on native Linux runner.

## 3. Native Linux execution evidence

### 3.1 Runner identity
- GH Actions run: `44889075318`
- Runner image: `ubuntu-24.04` (GitHub-hosted, AWS/Azure)
- OS: `Ubuntu 24.04.5 LTS`
- Kernel: `Linux ip-10-0-131-194 7.0.0-1014-aws #14~24.04.1-Ubuntu SMP PREEMPT Fri Sep 18 21:24:59 UTC 2026 x86_64`
- TLS: `TLSv1.3`
- Native-Linux gate: passed (`RUNNER_OS=Linux`, `uname -s=Linux`)

### 3.2 Campaign results (all cells qualified)

| Cell | Objective | Outcome | Key evidence |
|------|-----------|---------|--------------|
| L1 | Terminal-write genuine pending, deadline interruption | `induced-and-interrupted` | `Confirmed: true`, `PendingMs: 101.0`, `SenderWritableAtSample: false`, `PeerUnreadAtSample: 1`, `ElapsedMs: 101.0` |
| L2 | Terminal-write genuine pending, listener cancellation | `induced-and-interrupted` | `Confirmed: true`, `PendingMs: 20.6`, `SenderWritableAtSample: false`, `PeerUnreadAtSample: 1`, `ElapsedMs: 20.6` |
| L3 | Close_notify genuine pending, phase deadline | `induced-and-interrupted` | `Confirmed: true`, `PendingMs: 0.14`, `SenderWritableAtSample: false`, `PeerUnreadAtSample: 1`, `ElapsedMs: 0.14` |
| L4 | Close_notify genuine pending, listener cancellation | `induced-and-interrupted` | `Confirmed: true`, `PendingMs: 0.11`, `SenderWritableAtSample: false`, `PeerUnreadAtSample: 1`, `ElapsedMs: 0.11` |
| L5 | Encrypted byte-cap (32768 B) | `cap-bounded-and-clean` | `WireBytes: 32768`, `PlaintextBytes: 32768`, `DeclaredLimit: 32768` |
| L6 | Plaintext byte-cap (8192 B) | `cap-bounded-and-clean` | `WireBytes: 32768`, `PlaintextBytes: 32768` (observed limit) |
| L7 | Independent verified peer receipt | `peer-receipt-verified` | `message_bytes: 16`, `SHA256: 6fcf9eef...` matches expected, `receipt_ms: 3.4e-5` |

### 3.3 Negative controls
- NC-1 (ciphertext cap tamper): detected — `cap-limit-exceeded` when declared limit increased
- NC-2 (deadline renewal tamper): detected — `induced-and-interrupted` with extended deadline

### 3.4 Budgets and cleanup
- Job timeout: 45 min (actual: 13 s)
- Campaign step timeout: 2700 s (actual: ~1 s)
- Attempt watchdog: 3500 ms (all within bounds)
- Discovery attempts per cell ≤ 4
- All qualifying cells achieved `induced-and-interrupted` or `cap-bounded-and-clean` or `peer-receipt-verified` on first attempt

### 3.5 Artifacts and receipts
- Artifact ID: `14701167642`
- Artifact SHA-256: `279ab6cab071b3aa3d3646408d9ef8341d4047089a67dff36a5f8e039a0821c9`
- Campaign summary: `docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/results/exec-05-44889075318/campaign-summary.json`
- Journal: `campaign-journal.jsonl` (35 entries)
- Transcripts: `transcripts/L1..L7.log`

## 4. Formal qualification interpretation

**A — Qualification supported.**

- All 7 registered cells achieved their acceptance criteria on native Linux.
- Genuine pending terminal writes and close_notify writes induced and interrupted.
- Encrypted and plaintext byte caps enforced.
- Independent peer receipt verified via SHA256 digest match.
- Negative controls detected.
- All budgets respected; cleanup and accounting balanced.

This is a candidate-mechanism qualification, not a production qualification. The existing production behavior (no close_notify) is unchanged per Q-B5B-09-OWNER-01.

## 5. Repository state after report

- Branch: `main`
- Status: clean
- Commits: 34 ahead of retained origin/main
- One worktree; zero stashes
- No remote main publication or merge
- Qualification branch remains `qualification/q-b5b-09-tls-linux-qual-01`
- PR #1 remains open, unmerged

## 6. No further commission required for this qualification

The native-Linux TLS graceful-close qualification is complete. The smallest next commission on the remaining Q-B5B-09 roadmap is production-capacity characterization and peer-receipt observation in a representative environment, which requires separate authorization.