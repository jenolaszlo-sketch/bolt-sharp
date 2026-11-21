# Q-B5B-09-TLS-LINUX-QUAL-01 — Preparation Report

**Campaign status: B — prepared, not claimable yet.**

## 1. Campaign

- Commission: **Q-B5B-09** — see
  [docs/reviews/Q-B5B-09-TLS-DESIGN-01-design-review.md](docs/reviews/Q-B5B-09-TLS-DESIGN-01-design-review.md)
  (formal disposition B: supported with qualifications; native Linux,
  production-capacity, verified peer receipt, and native-Linux close_notify
  qualification outstanding).
- Campaign ID: **Q-B5B-09-TLS-LINUX-QUAL-01**
- Kind: bounded native-Linux TLS graceful-close qualification campaign
  (observational; no production/test/API/dependency changes).
- Revision: commit `7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f`, tree
  `2fdf1c3b96586465eb7547d23b6b1792240692ba`, branch `main`
  (the specified current HEAD values; `git rev-parse 7429c6b^{tree}` was
  verified to equal `2fdf1c3b96586465eb7547d23b6b1792240692ba`).
- Campaign plan:
  [docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/campaign-plan.json](docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/campaign-plan.json)
- Commissioning:
  [docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/COMMISSIONING.md](docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/COMMISSIONING.md)
- GH Actions workflow skeleton:
  [.github/workflows/q-b5b-09-tls-linux-qual-01.yml](.github/workflows/q-b5b-09-tls-linux-qual-01.yml)

## 2. Evidence verification

Ran `python docs/reviews/evidence/Q-B5B-09-TLS-E1/verify-evidence.py 7429c6b`
(read-only; no evidence, journal or code files modified):

```text
PASS 36/36 blob hashes, exact parent, additions-only inventory, bounded journals, matrix reconciliation, mechanism controls, and production characterization
Commit 7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f
Tree   2fdf1c3b96586465eb7547d23b6b1792240692ba
```

Result: **PASS 36/36**. Interpretation: the committed E1 evidence package
(`docs/reviews/evidence/Q-B5B-09-TLS-E1`) is internally consistent at the
qualified investigation revision. It provides:

- real Windows terminal-write and close_notify interruption evidence;
- WSL2 terminal-write evidence and an open (unqualified) WSL2 close_notify
  limitation;
- encrypted/plaintext cap evidence;
- bounded production characterization with peer-EOF observations.

It does **not** provide native-Linux close_notify pendency or verified
peer-application receipt — those remain open (E1 report §5, §8). E1's
disposition B is retained: no native-Linux or production-capacity claim is
derived from it here.

## 3. Environment evaluation

### 3.1 Local terminal (current)

- Platform: **Windows 11** (win32).
- Accepted native Linux execution: none available.
- WSL2: not accepted as native Linux (it is not; WSL2 is a Windows-hosted
  environment with a Linux userspace).

### 3.2 GitHub Actions evaluation from public documentation only

- [docs.github.com/en/actions/reference/runners/github-hosted-runners](https://docs.github.com/en/actions/reference/runners/github-hosted-runners):
  "With the exception of single-CPU runners, each GitHub-hosted runner is a new
  virtual machine (VM) hosted by GitHub." The standard runner table lists
  `ubuntu-latest` (and `ubuntu-24.04`) as **Linux**, x64, 4 vCPU / 16 GB RAM /
  14 GB SSD for public repositories. The `ubuntu-slim` label is explicitly the
  single-CPU case ("a container on a shared VM") and is not used as a standard
  native runner.
- [docs.github.com/en/actions/concepts/runners](https://docs.github.com/en/actions/concepts/runners):
  "GitHub offers hosted virtual machines to run workflows."
- [github.com/actions/runner-images](https://github.com/actions/runner-images)
  (README): "the source code used to create the VM images for GitHub-hosted
  runners"; `ubuntu-latest` maps to the Ubuntu2404 VM image.

**Conclusion: accepted as native Linux.** `ubuntu-latest` (image Ubuntu2404, x64,
4 vCPU/16 GB/14 GB per the public runner spec) is a GitHub-hosted Linux VM —
native Linux, not WSL. The workflow skeleton uses `runs-on: ubuntu-latest`; the
"Verify native Linux runner" gate (`RUNNER_OS == Linux`, `uname -s == Linux`)
fails the job on any non-Linux environment. GH Actions is the evaluated
environment because the current terminal is Windows 11 and no other native
Linux is available here.

## 4. Campaign essentials

### 4.1 Mechanism design

- **Fixed link:** ephemeral IPv4 loopback TLS listener; the peer parks its
  receive direction after the pipelined terminal write; no relay, no delayed
  encrypted-write await, no privileged network change, no external endpoint
  (adapted from Q-B5B-09-TLS-E1 `fixed_link` for native Linux).
- **Zero-window peer:** the peer is a separate process running `python3` stdlib
  `ssl` server; it sets `SO_RCVBUF` to the minimum allowed and stops reading
  after the handshake; the kernel advertises a zero TCP receive window, so the
  client send queue backs up and the terminal write (or the close_notify write)
  stays pending.
- **Deep queue profiles (L1/L2):** preceding 36864 / 45056 / 49152 / 262144
  bytes (profile ordinals 1–4), with requested peer receive and target send
  2048/1024/512/2048 bytes (ordinal 4 is the deep native-Linux profile).
- **Gated TLS proxy (L4):** a `python3` ssl-based TCP proxy between the campaign
  sender and the peer; it observes the close_notify record on the sender side
  and holds forwarding for a bounded gate interval; the proxy journal records
  the gate decision and the TLS identity (protocol version, cipher).
- **Deadline interruption:** absolute monotonic deadline interrupts the pending
  write or pending shutdown via owner cancellation (L1 total-deadline, L3
  phase-deadline ≤ 120 ms).
- **Listener interruption:** the peer stops its listener after a bounded
  interval; the pending write or shutdown is cancelled through linked
  cancellation, mirroring Q-B5B-09-TLS-E1 P4-deep-listener-stop.
- **Genuine-pending confirmation:** `Confirmed == true`, `PendingMs >= 20 ms`,
  `SenderWritableAtSample == false`, `PeerUnreadAtSample > 0`,
  `BufferConfigured == true`, the `kernel-write-pending` stage present; for
  close_notify cells, the live shutdown itself is observed pending.

### 4.2 Distinguishing the four guarantee levels

1. **Local write completion** — `WriteAsync`/`FlushAsync` returned after TLS
   encryption and kernel acceptance (journaled as `WriteCompleted`/
   `FlushCompleted`; observed in the smaller production-like cases).
2. **TLS close_notify transmission** — the close alert is delivered to the TLS
   layer and transmitted; native-Linux cells L3/L4 establish it via the
   live-pending observation (a close_notify write observed pending and bounded);
   not established for production HEAD (candidate-only evidence).
3. **Peer receipt** — the peer observes EOF or reads the terminal message
   (journal: `PeerRead`, `PeerUnread`, clean TLS EOF).
4. **Verified peer receipt** — the peer application confirms reading the
   terminal message; the peer records a receipt (message bytes + digest) to the
   peer journal; the runner validates the receipt against the campaign journal
   (digest + byte count); at least one valid receipt is required for the
   peer-receipt cell (L7). Distinct from local write completion and transport
   completion.

### 4.3 Peer-receipt oracle

The peer application is an independent process with its own journal. The runner
validates receipts against the campaign journal; a peer receipt without a
matching campaign digest is a journal inconsistency, not a qualification.

### 4.4 Cleanup/accounting assertions (every run)

- socket `Closed == true`; `Accounting.Active == 0`;
  `Accounting.Accepted == Accounting.Completed + Accounting.Failed`;
  `Permits == ReleasedPermits`; no unobserved task exceptions;
  `ElapsedMs <= 500 ms` on qualifying attempts.

## 5. Min matrix

| Cell | Objective(s) | Outcome | Stimulus | Interruption | Bound |
| --- | --- | --- | --- | --- | --- |
| L1 | O1, O11 | `induced-and-interrupted` | pipelined terminal write to a parked zero-window peer; deep queue profiles 1–4 (preceding 36864/45056/49152/262144 B; peer receive 2048/1024/512/1024 B; target send 2048/1024/512/2048 B) | absolute total deadline (terminal-write pending bound 310 ms; total elapsed bound 500 ms) | discovery ≤ 4, repeats ≤ 6 |
| L2 | O2 | `induced-and-interrupted` | same as L1 | peer listener cancellation after a bounded interval | discovery ≤ 4, repeats ≤ 6 |
| L3 | O3, O11 | `induced-and-interrupted` | peer parks its receive direction after the handshake; close_notify write to the zero-window peer | phase deadline (≤ 120 ms close_notify sub-budget; total elapsed bound 500 ms) | discovery ≤ 4, repeats ≤ 6 |
| L4 | O4 | `induced-and-interrupted` | zero-window peer + gated TLS proxy (the proxy journals the close_notify observation and gates forwarding) | peer listener cancellation after a bounded interval | discovery ≤ 4, repeats ≤ 6 |
| L5 | O5, O9 | `cap-bounded-and-clean` | peer flood (tiny 1 B; large 4096 B); declared encrypted cap 32768 B; preceding fixture 4096 B | none required; the drain terminates at the encrypted-byte cap | cap cell attempts ≤ 3 |
| L6 | O6 | `cap-bounded-and-clean` | peer flood up to 131072 B; declared plaintext cap 8192 B | none required; the drain terminates at the plaintext-byte cap; observed WireBytes ≤ 32768 B | cap cell attempts ≤ 3 |
| L7 | O7, O8 | `peer-receipt-verified` | cooperative peer application reads the terminal message; receipt journal record validated by the runner (digest + byte count) | none required | discovery ≤ 4, repeats ≤ 6 |

Caps: encrypted `32768 B`, plaintext `8192 B`; attempt lifetime watchdog
`3500 ms`.

## 6. Budget (bounded)

| Budget | Value |
| --- | --- |
| Runner | `ubuntu-latest` (GitHub-hosted Linux VM; public spec 4 vCPU / 16 GB / 14 GB, x64; image Ubuntu2404) |
| Job time | `timeout-minutes: 45` (hard cap); expected max 20 min |
| Campaign step time | `timeout 2700 s` (coreutils `timeout` on the runner) |
| Per-cell discovery bound | ≤ 4 attempts per cell, in ordinal order |
| Per-cell cap-cell bound | ≤ 3 attempts |
| Per-cell total bound | ≤ 6 runs per cell |
| Negative-control attempts | ≤ 3 per control |
| Attempt lifetime watchdog | 3500 ms |
| Network | ephemeral IPv4 loopback only; no external endpoints |
| Concurrency | serialized (`max-parallel: 1`) |
| Seeds | L1–L7: 1001–1007 (deterministic per cell) |

## 7. Dispositions

- **Q-B5B-09-TLS-E1:** B retained — Windows/WSL2 evidence; WSL2 is not native
  Linux; close_notify pendency on native Linux, verified peer receipt, and
  production capacity remain outstanding. No production changes.
- **Q-B5B-09-TLS-LINUX-QUAL-01:** B — prepared, not claimable yet (§8).

## 8. Final plan

1. **Owner authorization** for remote execution of `q-b5b-09-tls-linux-qual-01.yml`
   (manual `workflow_dispatch`; `permissions: contents: read`).
2. **Execute:** the workflow checks out the pinned revision `7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f`,
   passes the native-Linux gate on `ubuntu-latest`, restores the private campaign
   cache, runs the bounded driver (L1–L7 with the budgets above), and saves the
   private campaign cache.
3. **Publication:** artifacts `q-b5b-09-tls-linux-qual-01-<run_id>`
   (`results/campaign-summary.json`, `results/per-cell/*`, `results/transcripts/*`)
   with 30-day retention; a job summary (cell → outcome); tags prefixed
   `Q-B5B-09-TLS-LINUX-QUAL-01` carrying the commit/tree/runner identity.
   No push; no GitHub workflow registration.
4. **Local standing document:** this preparation report and the campaign plan;
   `docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/results/` is where a native-Linux run
   writes its results.
5. **Stop condition:** if the executing environment is not native Linux, the
   job stops at the gate (see `COMMISSIONING.md`) and records a blocked status;
   no native-Linux qualification is claimed.

## 9. Deliverables

- `.github/workflows/q-b5b-09-tls-linux-qual-01.yml` — workflow skeleton.
- `docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/campaign-plan.json` — objectives, environment, matrix, budgets, negative controls, repeatability, mechanisms, guarantee levels, peer-receipt oracle, publication plan.
- `docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/COMMISSIONING.md` — B-not-claimable, owner authorization, stop condition.
- This report.

## 10. Repository state

- Branch: `main`; campaign revision: `7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f`
  (tree `2fdf1c3b96586465eb7547d23b6b1792240692ba` — the specified current HEAD values).
- Preparation deliverables are committed locally on `main` as a local commit; not
  pushed; no GitHub workflow registration.
- No production/test/API/dependency changes: the campaign is observational.
