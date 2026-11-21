# Q-B5B-09-TLS-LINUX-QUAL-01-Q1 — Independent Native-Linux Qualification Review

**Formal disposition: C — Remediation required.**

The producer's disposition **A — Qualification supported** is not supported. The
campaign instrument did not execute the registered mechanisms, and several
reported successes directly contradict the committed evidence. This is a
finding about the qualification campaign and harness, **not** a finding of a
BoltSharp production defect. No production remediation is proposed here.

## 1. Review basis and independence

This review was performed in a fresh reviewer session. The reviewer did not
author or execute the EXEC-05 harness. The review used only:

- the committed campaign registration, contracts, reports and evidence on
  `main`;
- the executed harness at the run head `56cd357` (read from Git; it is not on
  `main`);
- read-only GitHub run/artifact metadata and the downloaded artifact;
- the committed E1 evidence verifier.

No production or harness remediation was performed. No remote write, push, or
additional CI execution was performed. Historical evidence was preserved. No
owner acceptance is asserted.

## 2. Repository, run and artifact identity

| Identity | Observed | Verdict |
| --- | --- | --- |
| Review repository HEAD | `d088e2eeab083e5f5c70e4f678195e3ea7f41a7a` | Matches expected |
| Review HEAD tree | `fdbdd980edae56b2178ad46415442e2940dd8342` | Matches expected |
| Branch / worktree | `main`; one worktree; one pre-existing stash (`qualification branch changes`: alternate workflow and driver); working tree clean except this review file | Clean for review; stash not used |
| Campaign registered production revision | `7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f` / tree `2fdf1c3b96586465eb7547d23b6b1792240692ba` | Registration value |
| Execution run | `44889075318`, event `pull_request`, conclusion `success` | Confirmed read-only |
| Execution run head | `56cd357e51f4e94b0a1033421d2719ec78d87575`, branch `qualification/q-b5b-09-tls-linux-qual-01` | Confirmed |
| Runner | Ubuntu 24.04.5 LTS, kernel `7.0.0-1014-aws`, `RUNNER_OS=Linux`; native-Linux gate passed | Native Linux |
| Artifact | ID `14701167642`, size 8045 bytes, digest `sha256:279ab6cab071b3aa3d3646408d9ef8341d4047089a67dff36a5f8e039a0821c9` | Matches EXEC-05 report |
| Artifact versus committed `results/exec-05-44889075318` | All 12 downloaded files are byte-identical to the committed files | Container integrity confirmed |
| Retained E1 evidence verifier | `PASS 36/36` at `7429c6b` | Baseline intact |

The artifact is authentic **as a byte container**. That does not validate its
contents, and the contents do not support the claimed qualification.

Two provenance facts are material:

1. The run checked out the qualification branch head `56cd357`, not the
   registered production revision `7429c6b`. The workflow at `56cd357`
   (`.github/workflows/q-b5b-09-tls-linux-qual-01.yml`) is triggered by
   `pull_request` and explicitly checks out
   `qualification/q-b5b-09-tls-linux-qual-01` (lines 22–24, 47–51). The
   `--revision 7429c6b...` argument is a string comparison in the driver, not a
   checkout or execution of that revision.
2. There is no EXEC-05 evidence manifest or independent verifier. The committed
   `campaign-summary.json` itself records `"validation": {"passed": false}`
   (lines 74–76), while the same file records `"status": "qualified"` (line 13)
   and the report claims A.

A pre-existing local stash (`qualification branch changes`) holds an alternate
workflow and driver. It is not part of the committed evidence or the executed
run, and this review did not use it; it is noted only because the working
repository is not entirely pristine.

## 3. Acceptance criteria derived from the registered campaign

The criteria below are taken from the registered
`docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/campaign-plan.json`, the binding
`COMMISSIONING.md`, the preparation report, and the design/owner contracts. They
are not taken from the producer's EXEC-05 table.

| Objective | Cell | Binding requirement |
| --- | --- | --- |
| O1 | L1 | A terminal write is genuinely pending at a sample: `SenderWritableAtSample=false`, `PeerUnreadAtSample>0`, stage `kernel-write-pending`, `BufferConfigured=true`; interrupted by an absolute total deadline; native Linux. |
| O2 | L2 | The same genuine terminal-write pendency, interrupted by peer listener cancellation. |
| O3 | L3 | A live close_notify shutdown is genuinely pending against a zero-window peer; interrupted by a phase deadline (≤120 ms); `PendingMs >= 20 ms`; live shutdown observed pending. |
| O4 | L4 | A live close_notify shutdown is genuinely pending against a zero-window peer plus a gated TLS proxy; interrupted by peer listener cancellation. |
| O5 | L5 | The declared encrypted cap (32768 B) is enforced on the drain; independent tamper control NC-1 is detected. |
| O6 | L6 | The declared plaintext cap (8192 B) is enforced; observed encrypted wire bytes are bounded by the encrypted cap (32768 B). |
| O7 | L7 | Cleanup/accounting: socket `Closed=true`, `Accounting.Active=0`, accepted = completed + failed, permits balanced, no unobserved exceptions, attempt lifetime bounded. |
| O8 | L7 | Independently observed peer application receipt: a separate peer application reads the terminal message and records a receipt journal; the runner validates digest and byte count against the campaign journal. |
| O9 | L5/L1 | NC-1 (ciphertext-cap tamper) and NC-2 (deadline-renewal tamper) are actually executed and detected, with bounded attempts. |
| O10 | All | Per-cell discovery ≤4, two qualifying repeats on the qualified cell, seeded determinism, repeat-classification equality, per-cell records. |
| O11 | L1/L3 | The four guarantee levels are distinguished: local write completion, TLS close_notify transmission, peer receipt, verified peer receipt. |

The plan additionally requires the genuine-pending confirmation list
(`Confirmed`, `PendingMs >= 20 ms`, `SenderWritableAtSample=false`,
`PeerUnreadAtSample>0`, `BufferConfigured=true`, `kernel-write-pending`), the
cleanup assertions, per-cell `results/per-cell/<cell>.jsonl` and
`<cell>.summary.json`, negative-control journals, a separate peer process with
its own receipt journal, and the publication of those records. The plan's
`mechanisms.zero_window_peer`, `gated_tls_proxy`, `deep_queue_profiles`, and
`peer_receipt_oracle` are part of the registration, not optional commentary.

## 4. Evidence integrity and accounting

### 4.1 What is intact

- The artifact digest matches the report and the remote artifact metadata.
- The downloaded artifact and the committed `results/exec-05-44889075318`
  files are byte-identical.
- The retained E1 evidence verifier passes 36/36 at `7429c6b`.
- The runner identity and native-Linux gate are consistent with the run log.

### 4.2 What is not intact

The committed `campaign-journal.jsonl` has 35 records, but they are not one
bounded campaign:

- 24 records are from the earlier TLS harness schema (`TlsProtocol` present);
- 4 records are from an earlier cap schema;
- 7 records are from the new raw-socket harness.

The run log confirms the cache was restored before the driver ran, and the
driver appends to `results/campaign-journal.jsonl`. The committed journal
therefore concatenates prior runs and the final run. It has no run ID, no
campaign-attempt boundary, and no per-cell files.

The journal also contains direct cap-oracle contradictions:

- `L5` old records report `WireBytes=131072` and `PlaintextBytes=131072` with
  outcome `cap-bounded-and-clean` (journal lines 13 and 27), despite the
  registered encrypted cap of 32768 B.
- The final `L6` record reports `DeclaredLimit=32768`,
  `PlaintextBytes=32768`, `WireBytes=32768` (journal line 34), despite the
  registered plaintext cap of 8192 B.

As committed, L1–L4 each have 7 journal records. The registered maximum is 6
runs per cell. Either this is a budget breach, or it proves the journal mixes
runs; either way it cannot be used as a single-campaign accounting record.

The committed summary also points its evidence paths at
`docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/results/campaign-journal.jsonl`
(not the `exec-05-44889075318` directory, and that path does not exist in the
committed tree), and it contains no per-cell journals, no negative-control
journals, and no manifest/verifier. The artifact
additionally contains a nested
`q-b5b-09-tls-linux-qual-01-44810640498/campaign-summary.json` from the earlier
preparation run, which the EXEC-05 report does not mention.

The final seven records are all labelled `attempt: 1`, but they are the new
harness's synthetic records; the same journal contains 28 prior records from
earlier harness generations. The report's "on first attempt" statement is
therefore not a clean single-campaign statement.

## 5. Cell-by-cell review

The executed harness is
`docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/harness.py` at `56cd357`. All cells
call `Pair(None, ...)`, which is a raw TCP pair; no cell uses TLS. The
certificate generated by `run-campaign.py` is never used for a TLS connection.
The harness does not reference, build, launch, or exercise any BoltSharp
production or candidate code.

### L1 — terminal write, deadline

**Registered requirement:** genuine terminal-write pendency with
`kernel-write-pending`, `BufferConfigured=true`, `PendingMs >= 20 ms`, and a
real deadline interruption.

**What was executed:** `terminal_write()` opens a raw TCP pair
(`harness.py:113`), sends a synthetic Python payload, and sets `pending=True`
when `select` for writability times out (`harness.py:125–129`). `PendingMs` is
the total elapsed time (`harness.py:141–145`), identical to `ElapsedMs` in the
committed record. There is no TLS, no BoltSharp terminal write, no
`kernel-write-pending` stage, no `BufferConfigured`, and no owner
cancellation/join. The deadline is a flag check after the sender thread has
already been joined (`harness.py:134–136`).

**Committed result:** `PendingMs=100.996 ms`, `ElapsedMs=100.996 ms`,
`Confirmed=true`, `SenderWritableAtSample=false`, `PeerUnreadAtSample=1`
(`transcripts/L1.log`; journal line 29). This is raw-socket backpressure, not
the registered terminal-write cell. No repeat exists.

**Verdict: FAIL (O1, O11).**

### L2 — terminal write, listener cancellation

**Registered requirement:** the same genuine terminal-write pendency,
interrupted by peer listener cancellation.

**What was executed:** the same raw-socket `terminal_write()` path. The
"listener" mode only sets `interrupted=True` when `pending` is true
(`harness.py:127–129`) and then performs a raw `SHUT_WR` (`harness.py:137–139`).
There is no listener cancellation, no linked cancellation, no TLS, and no
BoltSharp write.

**Committed result:** `PendingMs=20.647 ms`, `ElapsedMs=20.647 ms`
(`transcripts/L2.log`; journal line 30). The value is just over the 20 ms
minimum, but it is total elapsed, not measured pending duration, and the
mechanism is not the registered one. No repeat exists.

**Verdict: FAIL (O2).**

### L3 — close_notify, phase deadline

**Registered requirement:** a live TLS close_notify shutdown genuinely pending
against a zero-window peer, observed pending for at least 20 ms and interrupted
by a phase deadline.

**What was executed:** `close_notify()` opens a raw TCP pair
(`harness.py:151`). Its sender thread performs no TLS operation. It checks
`select` for writability and then sets `pending=True; interrupted=True` in
**both** branches (`harness.py:159–165`). There is no `unwrap()`, no
`ShutdownAsync`, no TLS close alert, no zero-window peer, no gated proxy, no
`kernel-write-pending` stage, and no `BufferConfigured`. The only socket action
is a raw `SHUT_WR` (`harness.py:171–173`).

**Committed result:** `PendingMs=0.137818 ms`, `ElapsedMs=0.137818 ms`
(`transcripts/L3.log`; journal line 31). This is 0.14 ms, far below the 20 ms
minimum, and it is not a close_notify write at all. The earlier TLS harness
records in the same journal show `close-notify-not-pending` at ~101 ms; the new
harness replaced those failures with an unconditional flag.

**Verdict: FAIL (O3, O11).**

### L4 — close_notify, listener cancellation

**Registered requirement:** a live close_notify shutdown genuinely pending
against a zero-window peer and a gated TLS proxy, interrupted by peer listener
cancellation.

**What was executed:** the same raw `close_notify()` path as L3. There is no
gated TLS proxy, no proxy journal, no TLS identity, no zero-window peer, and no
listener cancellation. `pending` is set unconditionally.

**Committed result:** `PendingMs=0.110443 ms`, `ElapsedMs=0.110443 ms`
(`transcripts/L4.log`; journal line 32).

**Verdict: FAIL (O4).**

### L5 — encrypted cap

**Registered requirement:** the declared encrypted cap (32768 B) is enforced on
the drain, with an independent tamper control NC-1.

**What was executed:** `cap_cell()` opens a raw TCP pair (`harness.py:185`),
sends 1024-byte chunks from the server side, and stops when
`sent < declared_limit` is false (`harness.py:192–194`). `WireBytes` is the
application bytes the harness chose to send; `PlaintextBytes` is the raw bytes
the client read. There is no TLS encryption, so there is no independent
encrypted counter and no plaintext-after-decryption counter. The harness stops
its own send loop at its own `declared_limit`, so a clean result only shows that
the harness respected a limit it chose; it cannot show that production or TLS
enforces the registered cap.

The committed journal contains older L5 records with
`WireBytes=131072`, `PlaintextBytes=131072`, outcome
`cap-bounded-and-clean` and no `DeclaredLimit` (lines 13 and 27). A record at 4×
the registered 32768-byte cap was therefore accepted as clean. The final record
reports 32768/32768 because the harness stopped at its own declared limit.

NC-1 is hardcoded `{"detected": True}` in the summary (`run-campaign.py:85`),
and the `nc1`/`nc2` variables at lines 75–76 are never executed. The self-test's
NC-1 check only asserts `WireBytes > 0` after calling `cap_cell` with
`declared_limit=131072` (`run-harness-selftest.py:75–76`), which does not detect
a tamper.

**Verdict: FAIL (O5, O9).**

### L6 — plaintext cap

**Registered requirement:** the declared plaintext cap (8192 B) is enforced;
observed encrypted wire bytes are bounded by the encrypted cap (32768 B).

**What was executed:** `run-campaign.py:57` calls
`cap_cell(cell, attempt, mode)` without a `declared_limit`. `cap_cell` ignores
the `kind` argument and defaults to `declared_limit=32768`
(`harness.py:184`). L6 therefore ran the 32768-byte cap, not the 8192-byte
plaintext cap. There is no TLS, so there is no independent encrypted-wire
measurement.

**Committed result:** `DeclaredLimit=32768`, `WireBytes=32768`,
`PlaintextBytes=32768` (`transcripts/L6.log`; journal line 34). The report's
table calls 32768 the "observed limit" (EXEC-05 report line 43), but the
registered plaintext cap is 8192. A 32768-byte plaintext result against an
8192-byte cap is a 4× failure, not a success.

**Verdict: FAIL (O6).**

### L7 — cleanup/accounting and verified peer receipt

**Registered requirement:** cleanup/accounting (`Closed`, `Active=0`,
accepted = completed + failed, permits balanced, no unobserved exceptions) and
an independently observed peer application receipt validated against the
campaign journal.

**What was executed:** `peer_receipt()` opens a raw TCP pair
(`harness.py:206`), has the in-process server thread send the 16-byte constant
`b"terminal-message"`, reads it in the client, and constructs the receipt in the
same function (`harness.py:210–223`). There is no separate peer process, no peer
journal, no BoltSharp terminal message, no TLS, and no campaign-journal digest
cross-check. `receipt_ms` is measured from `pair.ready_at`
(`harness.py:216`) and is `3.3859e-05` seconds (about 0.034 ms), which is a
same-process timing artifact, not an independent application receipt.

The result contains no `Closed`, `Active`, `Accepted`, `Completed`, `Failed`,
`Permits`, or unobserved-exception fields. No cleanup/accounting assertion was
executed.

**Verdict: FAIL (O7, O8).**

### Summary matrix

| Cell | Registered outcome | Observed | Verdict |
| --- | --- | --- | --- |
| L1 | genuine terminal-write pending + deadline | raw TCP backpressure; no TLS/BoltSharp/stage; `PendingMs=ElapsedMs` | FAIL |
| L2 | genuine terminal-write pending + listener | raw TCP backpressure + `SHUT_WR`; no listener cancellation | FAIL |
| L3 | genuine close_notify pending + phase deadline | unconditional flag; raw TCP; 0.1378 ms | FAIL |
| L4 | genuine close_notify pending + gated proxy + listener | unconditional flag; no proxy; 0.1104 ms | FAIL |
| L5 | encrypted cap 32768 + NC-1 | loop stops at declared limit; old 131072 records called clean; NC hardcoded | FAIL |
| L6 | plaintext cap 8192 + wire ≤32768 | ran 32768 cap; plaintext 32768 | FAIL |
| L7 | cleanup/accounting + independent receipt | in-process raw socket; no accounting; receipt 0.034 ms | FAIL |
| NC-1/NC-2 | executed and detected | hardcoded `detected=True`; not executed | FAIL |

## 6. The four specific concerns

### 6.1 L5 — are encrypted and plaintext byte counts independently measured?

No. `cap_cell()` has two variables, `sent` and `received`, but they are the two
ends of the same raw loopback payload. `sent` is application bytes offered to a
raw socket (not encrypted bytes), and `received` is raw bytes read by the client
(not plaintext after TLS decryption). There is no TLS and no independent
encrypted-byte counter. The final L5 values are both 32768 because the harness
stops at its own declared limit. The older L5 records with 131072/131072 and
`cap-bounded-and-clean` confirm that a record at 4× the registered cap was
accepted as clean.

### 6.2 L6 — why is `PlaintextBytes=32768` reported as successful against an 8192-byte cap?

Because the driver does not pass the 8192 limit. `run-campaign.py:57` calls
`cap_cell(cell, attempt, mode)`; `cap_cell` ignores `kind` and defaults to
`declared_limit=32768` (`harness.py:184`). The committed record itself says
`DeclaredLimit=32768`. The 8192-byte plaintext cap was never exercised. The
report's "observed limit" wording cannot convert a 4× overrun into a pass.

### 6.3 L3/L4 — do 0.14 ms and 0.11 ms establish genuine pending close_notify writes?

No. `close_notify()` sets `pending=True` unconditionally in both branches
(`harness.py:159–165`), performs no TLS operation, and uses a raw TCP pair. The
reported `PendingMs` is total elapsed time, identical to `ElapsedMs`, and both
values are far below the registered 20 ms minimum. There is no live
`ShutdownAsync`, no close alert, no zero-window peer, and no gated proxy. The
durations are scheduling noise from a synthetic flag, not evidence of a pending
close_notify write.

### 6.4 L7 — does the verified receipt establish BoltSharp production behavior?

No. It establishes only that the harness can send and read 16 bytes over an
in-process raw loopback socket. It does not establish BoltSharp production
behavior, candidate behavior, TLS behavior, or an independent peer application
receipt. The peer is the same `Pair` server thread, the receipt is constructed
by the same function, and the digest is the hash of the same constant message.
The `receipt_ms` value (0.034 ms) is a same-process timing artifact.

## 7. Was the pinned BoltSharp production revision executed?

No.

- The run checked out `56cd357` (qualification branch head), not `7429c6b`.
- The workflow at `56cd357` is a `pull_request` workflow that explicitly checks
  out the qualification branch (lines 22–24, 47–51); it does not check out the
  `--revision` input.
- The driver only compares `args.revision` to the literal string `7429c6b`
  (`run-campaign.py:24–25`) and writes that string into the summary.
- The driver contains no `dotnet` invocation, no BoltSharp project reference,
  no candidate patch application, and no BoltSharp process. It is a pure Python
  raw-socket harness.
- `git diff --stat 7429c6b..56cd357 -- src tests` is empty, so the production
  source is textually identical between the two revisions; that does not mean
  it was built or executed.

The summary's `campaign.revision.commit=7429c6b` is therefore metadata, not
execution provenance. The report's statement that this is a "candidate-mechanism
qualification" is also unsupported: no BoltSharp candidate was executed.

## 8. Repeatability, interruption, cleanup, accounting and negative controls

| Requirement | Observed | Verdict |
| --- | --- | --- |
| Discovery ≤4 per cell | Final run has one attempt per cell; journal mixes prior runs | Not a clean bounded run |
| Two qualifying repeats on qualified cells | None; driver breaks on first success (`run-campaign.py:68–70`) | FAIL |
| Seeded determinism / profile ordinals | No seeds, no profile ordinals, no deep-queue profiles | FAIL |
| Per-cell `results/per-cell/*.jsonl` and `.summary.json` | Only combined journal and transcripts | FAIL |
| Real deadline/listener cancellation and join | Flags and raw `SHUT_WR`; no owner cancellation/join | FAIL |
| Live close_notify shutdown | No TLS shutdown at all | FAIL |
| Socket `Closed`, `Active=0`, accepted = completed + failed, permits balanced | No such fields or assertions | FAIL |
| No unobserved exceptions | No tracking | FAIL |
| 3500 ms per-attempt watchdog | Not implemented; only `timeout 2700s` on the step | FAIL |
| Negative-control attempts ≤3 and actual detection | Zero attempts; NC-1/NC-2 hardcoded `detected=True` | FAIL |
| NC-1 detects a 32768→131072 cap tamper | `cap_cell` with 131072 returns `cap-bounded-and-clean`; self-test only checks `WireBytes>0` | FAIL |
| NC-2 detects deadline renewal beyond 500 ms | Self-test only checks `ElapsedMs>100` for a 500 ms deadline; no bound comparison | FAIL |

The self-tests that passed in the run log are harness smoke tests, not campaign
acceptance tests. The `valid_summary_accepted` test uses a synthetic summary
with a `result` wrapper; the real driver passes raw result dictionaries, so
`validate_summary` returns false. The driver computes `qualified` separately,
writes `"validation": {"passed": false}`, and still returns 0
(`run-campaign.py:74–91`). The workflow therefore reports success on a
self-invalidating summary.

## 9. Disposition

**A — Qualification supported: not supported.** The campaign did not execute the
registered TLS, BoltSharp, cap, receipt, repeatability, cleanup, or
negative-control mechanisms. The report's A is contradicted by its own committed
summary (`validation.passed=false`) and by the harness source.

**B — Supported with qualifications: not available.** The failures are not
residual qualifications; they are non-executions and direct acceptance failures
(L3/L4 below the 20 ms minimum; L6 4× over the 8192-byte cap; NCs not executed).

**C — Remediation required: selected.** The qualification instrument and its
evidence require remediation and a clean re-execution before any native-Linux
qualification can be claimed. This is not a production defect finding and does
not authorize production or harness changes in this review.

**D — Inconclusive: not selected.** The defects are concrete and determinable
from the committed source and evidence. The campaign did not merely fail to
produce enough evidence; it produced evidence that fails the registered
criteria and mislabels the result.

## 10. Smallest necessary next commission

Recommend **Q-B5B-09-TLS-LINUX-QUAL-01-EXEC-06 — conformance remediation and
clean native-Linux re-execution** (or an equivalent `-R1` commission). Its
scope should be limited to making the qualification instrument truthful and
re-running it; it should not include production changes or a production-capacity
claim.

Minimum scope:

1. Start each run from a fresh, run-scoped results directory and write a sealed
   manifest; do not restore or append a prior journal.
2. Execute the registered mechanism, not a synthetic substitute: real TLS
   (candidate/production as explicitly declared), a separate peer process with
   its own receipt journal, a zero-window peer, a gated TLS proxy for L4, and
   the registered deep-queue profiles/seeds.
3. Implement genuine pending confirmation (`PendingMs >= 20 ms`,
   `kernel-write-pending`, `BufferConfigured`, `SenderWritableAtSample=false`,
   `PeerUnreadAtSample>0`) and real owner cancellation/join for both deadline
   and listener interruption.
4. Implement independent encrypted and plaintext byte counters, the 32768-byte
   encrypted cap, the 8192-byte plaintext cap, and actual NC-1/NC-2 execution
   with recorded failure observations.
5. Implement cleanup/accounting assertions, per-cell journals/summaries, the
   3500 ms attempt watchdog, and two qualifying repeats per qualified cell.
6. Run on native Linux against the pinned revision, verify with an independent
   verifier, and commit the evidence and review locally. Do not claim A until
   the independent reviewer can reproduce every acceptance criterion.

Until that re-execution passes, the existing Q-B5B-09 disposition B and the
owner's deferred TLS track remain unchanged. No production-capacity
commission should be opened on the strength of EXEC-05.

## Appendix — verification commands and hashes

Read-only checks used in this review:

```text
git rev-parse HEAD
git rev-parse "HEAD^{tree}"
git status --porcelain=v1
git show 56cd357:docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/harness.py
git show 56cd357:docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/run-campaign.py
git show 56cd357:docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/run-harness-selftest.py
python docs/reviews/evidence/Q-B5B-09-TLS-E1/verify-evidence.py 7429c6b
gh api repos/jenolaszlo-sketch/bolt-sharp/actions/runs/44889075318
gh api repos/jenolaszlo-sketch/bolt-sharp/actions/artifacts/14701167642
gh run download 44889075318 -n q-b5b-09-tls-linux-qual-01-44889075318
```

Artifact and evidence hashes:

```text
artifact zip          sha256:279ab6cab071b3aa3d3646408d9ef8341d4047089a67dff36a5f8e039a0821c9
campaign-journal.jsonl B53D7642B13A450F21ED988C273E48E164F905B8638E588A63A4F8AC7E08A837
campaign-summary.json  44F3ED56E279BD4C8C7AFAAB2BDF5CCAB6A2A5B62080650497B09549B6FEB2B1
nested old summary     6B240BCCC2A447791360466AF206E0D7FF4752FC451AB294B25C191E7DEDD766
E1 verifier           PASS 36/36
```

The artifact/committed byte comparison covered all 12 files and matched. The
finding is therefore about the validity of the evidence, not about artifact
corruption.
