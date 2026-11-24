# Q-B5B-09-TLS-LINUX-QUAL-01-R4 — Integrated Harness Stabilization Remediation Report

Revision under test: `36d7b69bd4eb35bccc2a75ceb322171eefe32c9c` / tree `5d176f30765d710894a1286c397dc6106556cc0c`
Production pin: `7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f` (unch; `git diff 7429c6b..HEAD -- src tests` is empty)

All changes are harness-only under `docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01-R1/`
(`harness/**`, `peer/**`, `verifier/**`). No production, test, API, or dependency
change was made, and no registered criterion was weakened.

---

## 1. Defects resolved in R4

### 1.1 L7 — `elapsed_ms` / live-shutdown and dead receipt dispatch

Two independent defects kept L7 from ever qualifying.

**(a) Harness-side double framing (digest mismatch).**
`BoltMessageWriter.WriteMessage` already emits Bolt chunk framing
(`[2-byte length][payload][0x0000]`). `ExpectedTerminalFailure` in
`harness/Support.cs` additionally applied `Wire.Frame(...)` to that output, so the
message was framed twice. The `expected_terminal_message_bytes` and
`expected_terminal_sha256` values therefore described a doubly-framed payload that
the peer never saw, and every L7 receipt comparison failed.

Fix: `ExpectedTerminalFailure` now returns the writer's output directly, with no
extra `Wire.Frame` call.

Result: L7 now records a 169-byte framed terminal message with
`expected_terminal_sha256 = 5cdf80e60e813e45ddb60777b10c920fb9f940ff49adc865c5157c2c56c7c444`,
and the peer receipt matches on every attempt (3 attempts, repeat markers 1 and 2).

**(b) Journal-side repeat classification.**
`Production.RunReceiptAttemptAsync` did not emit the `repeat`, `phase`, and
`discovery_index` fields that `RunTerminalAttemptAsync` emits, so L7's repeat
attempts were never classified as repeats. The verifier's repeat-classification
oracle therefore reported "repeat classification incomplete".

Fix: `RunReceiptAttemptAsync` now accepts an `int? repeat` parameter and records
`phase`, `discovery_index`, and `repeat`; both `RunCellAsync` dispatch sites pass
the value.

### 1.2 L3 — `elapsed_ms` KeyNotFound and live shutdown

L3 previously failed on a `KeyNotFound` for `elapsed_ms` and did not evidence a live
shutdown. L3 now records, on all three attempts:

| Attempt | interrupted | shutdown_pending | shutdown_pending_ms | elapsed_ms |
|---------|-------------|------------------|---------------------|------------|
| 1       | true        | true             | 21.587              | 320.399    |
| 2       | true        | true             | 21.747              | 320.486    |
| 3       | true        | true             | 22.581              | 321.270    |

The peer is the zero-window python3 peer with path saturation (write until
`Poll(SelectWrite)` is false), so `close_notify` stays pending and the live shutdown
is observable rather than instantaneous.

### 1.3 L4 — gated proxy launch

L4 keeps path saturation with the gated proxy engaged (`ProxyForwardBoundBytes = 0`)
and peer-listener cancellation so `close_notify` remains pending. Each L4 attempt now
records `proxy_port`, `proxy_journal`, and `peer_listener_cancelled = true`, and the
proxy journals are preserved as evidence.

### 1.4 L6-PROD — supplementary classification

`L6-PROD` is a supplementary (non-qualifying) cell. Its observations that are not
registered pass/fail criteria — plaintext drain count and ciphertext count — are
recorded as **warnings**, while the structural requirement that the production drain
was actually admitted is an **error**. This preserves the distinction between a
supplementary observation and a qualification failure. In the Windows full run the
supplementary cell admitted the production drain and both supplementary warnings are
clear (`drain_admitted = true`).

### 1.5 NC-1 — prerequisite and actual execution

NC-1 now runs the E1-faithful candidate TLS drain with the declared encrypted cap
tampered 32768 → 131072. It is detected only on **actual measured ciphertext**:
`encrypted_wire_bytes` must equal exactly 131072 and exceed the registered 32768.
Windows evidence: `encrypted_wire_bytes = 131072`, `tamper_detected = true`,
`outcome = cap-checks-failed`.

### 1.6 NC-2 — prerequisite profile selection (the R3 defect)

R3 ran the deadline-renewal control on discovery profile 1 only. Profile 1 does not
induce the terminal-write pendency on every platform, so the control reported
`precursor-not-observed` with `elapsed_ms ≈ 2.6 ms` and the extended deadline was
never exercised.

R4 fix (`harness/Controls.cs`): the control reuses the qualifying L1 profile when
`L1.summary.json` reports a qualified cell, and otherwise walks the discovery
profiles within the registered 3-attempt bound. It records
`prerequisite_profile_ordinal`, `prerequisite_source`, and `prerequisite_confirmed`.

Windows evidence: `prerequisite_profile_ordinal = 4`, `prerequisite_confirmed = true`,
`elapsed_ms = 800.122` (> the 500 ms bound), `outcome = induced-but-checks-failed`,
`deadline_tamper_detected = true`.

### 1.7 NC-2 — detection flag not persisted (found and fixed in R4)

`RunNC2DeadlineRenewalAsync` wrote the control record to the journal **before**
setting `deadline_tamper_detected`, so the computed detection flag reached only the
transcript and never the auditable journal record. The write now happens after every
detection field is set. The preserved Windows evidence contains
`deadline_tamper_detected = true`, and the verifier requires it.

---

## 2. Strict encrypted-cap reconciliation to the binding E1 contract

The binding contract is `docs/reviews/evidence/Q-B5B-09-TLS-E1/verify-evidence.py`
against `configuration.json` → `candidate_limits` (`ciphertext = 32768`,
`plaintext = 8192`). That verifier enforces **equality**, not inequality:

```
T5   Finish.WireBytes == 32768     Finish.Bytes == 1441
T6   Finish.Bytes     == 8192     Finish.WireBytes <= 32768
NC-1 Finish.WireBytes == 131072
```

R3 permitted a one-record overshoot (`cap + 16406`) on the encrypted cap. That
allowance is **not** part of the registered contract and has been removed.

R4 enforcement is at the **transport boundary**, not after the fact:
`WireCountingStream` (`harness/CandidateDrain.cs`) arms an inbound budget
(`ArmInboundLimit`) and clips each inbound read to the remaining encrypted budget
(`ClipInbound`), so the cap binds on the wire. This is what makes an *exact* 32768
termination achievable and verifiable rather than merely "not exceeded by much".

Verifier oracles (`verifier/verify_r1.py`):

| Cell | Oracle |
|------|--------|
| L5 | `encrypted_wire_bytes != 32768` → reject; `drain_outcome == "cipher-cap"` |
| L6 | `encrypted_wire_bytes > 32768` → reject; `plaintext_bytes != 8192` → reject; `drain_outcome == "plain-cap"` |
| L5/L6 | `checks.encrypted_cap_strict` must be true; TLS record overhead must be > 0 |
| NC-1 | `encrypted_wire_bytes != 131072` → reject; must exceed 32768; `tamper_detected == true` |

Measured Windows evidence: **L5** `encrypted_wire_bytes = 32768` (exactly the cap),
`plaintext_bytes = 1424`, drain `cipher-cap`. **L6** `encrypted_wire_bytes = 8236`
(inside the cap), `plaintext_bytes = 8192` (exact), drain `plain-cap`.

---

## 3. Dispatch reachability audit

Every registered cell and negative control must be reachable **and** actually
invoked. Three mechanisms now enforce this:

1. Each per-attempt journal record carries `dispatch_target`, and the verifier
   rejects any record whose `dispatch_target` is not the registered target.
2. The campaign journal carries a `dispatch-audit` record listing every registered
   implementation with `invoked` and `invocations`.
3. In `--mode full` the verifier requires `all_required_invoked == true` and errors on
   any registered implementation that is absent from the audit, or present with
   `invoked != true`.

Windows full-run audit: all seven registered implementations `invoked = true`
(`Production.RunTerminalAttemptAsync` 13 invocations, `Candidate.RunCloseNotifyAttemptAsync`
6, `Candidate.RunCapAttemptAsync` 3, `Production.RunReceiptAttemptAsync` 3,
`Production.RunPlaintextCapAttemptAsync` 1, `Controls.RunNC1CiphertextCapTamperAsync` 1,
`Controls.RunNC2DeadlineRenewalAsync` 1), `all_required_invoked = true`.

---

## 4. Verifier and self-test changes

### 4.1 New R4 oracles in `verifier/verify_r1.py`

- `verify_dispatch_audit` — the reachability audit described above.
- `dispatch_target` match assertions in `_terminal_write_oracle`,
  `_close_notify_oracle`, `_verify_cap_attempt`, `verify_cell_l6_prod`,
  `verify_cell_l7`, and `verify_negative_controls`.
- `interrupted` must be `true` for the close-notify cells (L3/L4).
- Strict encrypted-cap oracles (Section 2), including `encrypted_cap_strict`.
- L7 peer-receipt journal must exist **and** re-validate: the digest in the receipt
  record must equal the SHA-256 of its own `message_hex` payload.
- NC-2 additionally requires `outcome == "induced-but-checks-failed"`,
  `prerequisite_confirmed == true`, and `deadline_tamper_detected == true`.

### 4.2 `verifier/verifier_selftest.py` rebuilt (mutation-based)

The previous fixtures were **vacuously rejecting**: each case wrote a single cell's
journal and dropped every other cell's summary, so every fixture was rejected for
9–11 *unrelated* "summary missing" errors rather than for the condition it claimed to
test. That made the suite unable to detect a verifier regression.

Each fixture now starts from a **complete valid run** and mutates exactly one thing,
so a rejection is attributable. 50 rejecting fixtures now yield exactly one error in
47 cases and two coupled errors in 7 cases (where a single mutation trips two directly
related assertions, e.g. `encrypted_cap_strict = false` failing both the direct
assertion and the required-checks loop). No fixture rejects vacuously.

New fixtures added:

- **Dispatch integrity** — 7 cases, including a record dispatched to the wrong
  implementation (`L7` → `Production.RunTerminalAttemptAsync`, the dead dispatch),
  a missing audit record, a never-invoked implementation, and absent audit rows.
- **Required interruption evidence** — `interrupted = false` and `interrupted`
  entirely absent on close-notify cells.
- **Strict cap boundary** — `32760` (below the cap), `32768 + 16406` (one-record
  overshoot, formerly tolerated), `encrypted_cap_strict = false` on L5 and L6.
- **NC-2 unconfirmed prerequisite** — the R3 defect reproduced as a negative control,
  plus `precursor-not-observed` outcome and missing detection flag.
- **L7 peer-receipt binding** — receipt journal absent, and a receipt whose stored
  digest does not match its own hex payload.
- **A positive acceptance test** — L5 exactly `32768` and L6 inside the cap must both
  still qualify, proving the strict contract does not over-reject.

The suite reports `ALL SELF-TESTS PASSED` (exit code 0).

### 4.3 Repair of a corrupted self-test file

`verifier/verifier_selftest.py` had been corrupted by a runaway edit that duplicated
`base["interrupted"] = True` 246 times (plus stray duplicate lines), leaving the file
at 636 lines while the correct content required 388. The duplication was semantically
inert (identical, unconditional dict assignments) so the suite still ran and passed,
but it was an unmaintainable artifact.

The file was repaired by collapsing consecutive duplicate lines and restoring the
PEP8 blank-line structure between top-level definitions, verified by compiling and by
re-running the suite to the same `ALL SELF-TESTS PASSED` result. The repaired and
extended file is 524 lines.

---

## 5. Evidence preserved

`docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01-R4-verification-evidence/`

- `VERIFICATION.txt`
- `Q-B5B-09-TLS-LINUX-QUAL-01-R4-REMEDIATION-REPORT.md` (this file)
- `verifier-windows-full.txt` — `VERIFIER: PASS`
- `verifier-wsl2-development.txt` — `VERIFIER: FAIL` (development-only, see below)
- `verifier-selftest.txt` — `ALL SELF-TESTS PASSED`
- `windows-full-run/` — full Windows campaign evidence (run-id `r4full6`), re-verified
  in place to `VERIFIER: PASS`
- `wsl2-development-run/` — WSL2 development evidence (run-id `r4wsl2dev1`)

Generated TLS certificate and key material in the raw results directories was
deliberately **not** copied into this package.

---

## 6. WSL2 development run — explicitly not qualification

A campaign was executed under WSL2 (Ubuntu, kernel `6.6.114.1-microsoft-standard-WSL2`,
.NET 10.0.114, Python 3.14.4). The independent verifier **fails** it.

| Cell | WSL2 outcome |
|------|--------------|
| L1, L2, L3, L4 | `precursor-not-observed` on every attempt → **not qualified** |
| L5 | qualified |
| L6 | `cap-checks-failed` → **not qualified** |
| L6-PROD | qualified |
| L7 | qualified (3 successes) |
| NC-1 | executed, `encrypted_wire_bytes = 131072`, tamper detected |
| NC-2 | `precursor-not-observed`, `elapsed_ms = 2.7` → not evidenced |

The run-meta self-declares `platform = Unix 6.6.114.1`, `wsl = true`,
`native_linux = false`. The WSL2 kernel does not induce the kernel-write pendency
precursors that L1–L4 require, which also explains why NC-2's prerequisite is
unconfirmed there — the same R3 failure mode, reproduced on a different platform.

**No native-Linux qualification is claimed.** Native-Linux qualification would require
an execution on a native Linux host, which was not performed and is not available in
this environment. WSL2 evidence is development-only.

---

## 7. Result and disposition

Windows full campaign (`r4full6`): `VERIFIER: PASS`, all cells and negative controls
executed, dispatched, journaled, and verifier-checked.

Remaining qualification:
- No native-Linux campaign has been executed. WSL2 is development-only and its
  verifier result is FAIL.
- Windows-only platform qualification.
- Owner acceptance is not granted and was not requested.