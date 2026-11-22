# Q-B5B-09-TLS-LINUX-QUAL-01-R3 — Actual TLS Ciphertext Measurement Correction

**Date:** 2026-11-22  
**Base HEAD (R2):** `6948594b6771bf6e604094a1457b57f1acb8ded5` (tree `7f593078d1c54a81bc007dc3634899b1a7dd87a2`)  
**Pinned production:** `7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f` (tree `2fdf1c3b96586465eb7547d23b6b1792240692ba`, unchanged; `git diff --stat 7429c6b..HEAD -- src tests` empty)  
**Scope:** L5/L6 actual ciphertext measurement only; all other cells preserved.

---

## 1. Reconciliation with the original encrypted-cap contract

The registered contract (`campaign-plan.json` + E1 evidence) requires the declared
encrypted cap (32768 B) to be enforced **on the drain**, with an independent tamper
control NC-1 whose observed wire bytes exceed the registered cap when the cap is
tampered (32768 → 131072). The proven E1 mechanism (`TlsE1Tests` T5/T6 + NC-1
journal: drain outcome `cipher-cap`, `WireBytes=131072`, `Bytes=5715`) counted
`WireBytes` at the TLS transport boundary of a candidate drain, with `Bytes` =
decrypted plaintext, both over the post-handshake application-data phase.

R2 violated this contract: it counted **application bytes written to TLS** as a
"proxy" for encrypted bytes. R3 removes the proxy and reuses the E1 mechanism.

## 2. Actual ciphertext measurement beneath SslStream

New `harness/CandidateDrain.cs` (harness-only, candidate-only, explicitly declared):

- `WireCountingStream` sits **between SslStream and the socket's NetworkStream**.
  Every byte SslStream pulls from the socket (encrypted inbound records) and pushes
  down (encrypted outbound records) is counted exactly as it crosses the wire boundary.
- Measurement direction and accounting boundaries mirror E1: the encrypted cap binds
  **inbound encrypted bytes processed during the drain phase** (deltas taken after the
  TLS handshake, so handshake bytes are excluded); `plaintext_bytes` are the decrypted
  bytes delivered by SslStream.
- Plaintext cap enforcement is **exact**: each read is clipped to the remaining
  plaintext budget (`min(scratch=2048, cap - drained)`), so plaintext never overshoots.
- Encrypted cap enforcement is **bounded**: the counter is checked after every read; a
  single SslStream record pull may overshoot by at most one maximum TLS record
  (`MaxTlsRecordWireBytes = 5+16384+1+16 = 16406`). Any observed value beyond
  `32768 + 16406` therefore proves an undetected cap violation — TLS buffering and
  record overhead cannot hide one (R3 requirement 6).
- The flooding client wraps its own inner stream the same way, so the journal carries
  **independent** client-side wire and application counters.

## 3. R2 defects found and fixed during R3

1. **Dispatch bug (root cause of the R2 gap):** cell kinds are
   `candidate-ciphertext-cap` / `candidate-plaintext-cap`; the R2 prefix test
   `StartsWith("candidate-cap")` never matched, so `RunCapAttemptAsync` was dead code
   and L5/L6 actually ran the terminal-write flow. Fixed by matching cell IDs
   (`L5`/`L6`) in `Candidate.RunCellAsync` and in `QualifyingOutcome`.
2. **UTF-8 BOM:** .NET `Encoding.UTF8` emits a BOM that Python `json.loads` rejects.
   The harness now writes BOM-less UTF-8 (`Utf8NoBom`); the verifier reads `utf-8-sig`.
3. **Run-id binding:** the verifier bound the operator run label to the commit prefix;
   identity binding now lives in `run-meta.json` (commit/tree), with the journal
   required to carry exactly one run id matching meta.
4. **Verifier oracle scope:** the full per-attempt oracle now applies to *qualifying*
   attempts only (1 + repeats_required, repeat-classified, single profile ordinal);
   discovery misses (`precursor-not-observed`, `environment`, `attempt-watchdog`) are
   legitimate evidence and no longer false-fail a qualified cell. Cap cells (L5/L6)
   carry discovery bound 3 and no repeats, per the registered plan.

## 4. Controlled differential (R3 requirement 5) — measured, not assumed

L5 tiny-flood evidence (fresh run `r3check2`, attempt 1, Windows/WSL2 dev check):

| Counter | Value |
|---|---|
| drain outcome | `cipher-cap` (encrypted cap binds first — E1 T5 pattern) |
| **encrypted wire bytes (beneath SslStream)** | **32775** (= 32768 cap + 7 partial-record overshoot, within the declared one-record bound) |
| decrypted plaintext bytes | 1425 (< 8192) |
| client application bytes flooded | 1595 |
| TLS record overhead (wire − plaintext, drain side) | 31350 |

Application bytes (1595) vs actual ciphertext (32775) differ by **20.6×** — the
registered ciphertext measurement cannot be satisfied by an application-byte proxy.

L6 large-flood evidence (same run):

| Counter | Value |
|---|---|
| drain outcome | `plain-cap` (plaintext cap binds — E1 T6 pattern) |
| decrypted plaintext bytes | **8192 exactly** |
| encrypted wire bytes | 12354 (≤ 32768 bound verified, not binding) |
| flood overhead (client side) | 1034 |

L6 enforces **both** registered limits: 8192 plaintext exact, encrypted wire bounded
by 32768 (+ one record).

## 5. NC-1 — actually executed (R3 requirement 7)

Fresh full run `r3full1`, negative-controls/NC-1.jsonl:

| Field | Value |
|---|---|
| registered cap | 32768 |
| tampered cap | 131072 |
| drain outcome | `cipher-cap` (terminated at the tampered cap) |
| **observed encrypted wire bytes** | **131077** (> registered 32768 → tamper detected) |
| plaintext bytes | 5699 |
| `tamper_detected` | true |

This reproduces the E1 NC-1 signature (E1: `WireBytes=131072`, `Bytes=5715`;
R3: `WireBytes=131077`, `Bytes=5699`).

## 6. Verifier hardening (R3 requirement 8)

`verify_r1.py` now rejects, with fail-closed errors:
- `ciphertext_measurement != "beneath-sslstream-transport-boundary"` (any proxy/estimate);
- a `ciphertext_source` string containing "proxy" or "application bytes";
- missing/non-positive `encrypted_wire_bytes`, or wire bytes above
  `32768 + 16406` (undetected cap violation);
- zero measured TLS record overhead (counter indistinguishable from an app proxy);
- inconsistent client counters (`flood_encrypted_wire_bytes < flood_application_bytes`);
- L5 plaintext ≥ 8192 (encrypted cap must bind first), L6 plaintext ≠ 8192 exactly;
- drain termination other than a registered cap bound (`peer-eof`, faults);
- declared cap ≠ 32768 inside a qualifying cell; missing production TLS terminal
  observation inside the cell;
- NC-1 without an actually executed tamper: requires `encrypted_wire_bytes > 32768`
  and `tamper_detected=true` measured at the transport boundary.

Self-test (`verifier_selftest.py`): **valid R3 run accepted; EXEC-05 rejected
(5 errors); 30/30 adversarial false-pass conditions rejected** — 16 preserved Q1
conditions plus 14 new R3 conditions (proxy measurement, missing boundary, cap
overshoot beyond one record, zero overhead, plaintext-cap violations, drain outcome,
tampered declared cap, NC-1 not executed / under cap / proxy).

## 7. Previously supported cells — preservation check (R3 requirement 9)

Full-run verification (fresh run, fail-closed verifier):

| Cell | Result | Note |
|---|---|---|
| L1 | **qualified, verifier-clean** | total-deadline interruption oracle passes |
| L2 | **qualified, verifier-clean** | listener-cancellation oracle passes |
| L5 | **qualified, verifier-clean** | actual ciphertext 32775/32768+7 |
| L6 | **qualified, verifier-clean** | plaintext 8192 exact, wire 12354 |
| NC-1 | **executed & detected** | wire 131077 > 32768 |
| L6-PROD | warning only | supplementary; plaintext drain ≠ 8192 on this run (pre-existing, out of scope) |
| L3 | **not qualified — pre-existing defect, out of R3 scope** | close-notify path crashes with `KeyNotFoundException: elapsed_ms` (the close-notify branch never sets `elapsed_ms` before the checks block); present since R1, first exposed by actual execution |
| L4 | **not qualified — pre-existing defect, out of R3 scope** | gated proxy never started on Windows (`python` launcher stub returned no port line) |
| L7 | **not qualified — pre-existing defect, out of R3 scope** | `Production.RunReceiptAttemptAsync` is dead code — `Production.RunCellAsync` routes every production cell (including L7) through `RunTerminalAttemptAsync`; present since R1 |
| NC-2 | **executed but not detected on this run** | the control ran with deadline 800 ms, but the L1 pending prerequisite was not induced on profile 1 under Windows scheduling (`precursor-not-observed`, elapsed 2.6 ms); mechanism requires the native-Linux deep-queue profiles |

These L3/L4/L7/NC-2/L6-PROD findings are **not regressions of R3** (R3 touched only
the cap measurement path, the verifier, and NC-1); they are defects that R1/R2
inherited and that no prior commission actually executed. They are recorded here as
the concrete, named findings for the next commission. No weakening of any registered
criterion was introduced; the verifier fails closed on every one of them.

## 8. Constraints compliance

- Qualification-harness changes only (`docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01-R1/**`).
- No production changes (`git diff 7429c6b..HEAD -- src tests` empty), no API or
  dependency changes, no test changes.
- No proxy or estimated ciphertext evidence: the proxy path is both removed from the
  harness and rejected by the verifier.
- Registered criteria unchanged (32768/8192 caps, 20 ms minimum pending, 500 ms total
  bound, 3500 ms watchdog, discovery/repeat bounds).
- No remote writes, no CI execution, no native-Linux qualification claim — all local
  execution is Windows/WSL2 development evidence only.

## 9. Readiness recommendation for EXEC-06

**B — Ready with explicit non-blocking qualifications.** The R3 scope (actual TLS
ciphertext measurement, L5/L6 enforcement, NC-1) is complete, executed, and
verifier-clean, and L1/L2 evidence survives the hardened verifier. Before native-Linux
execution, the four pre-existing non-R3 defects above (L3 `elapsed_ms`, L4 proxy
launch on the runner, L7 receipt dispatch, NC-2 prerequisite) must be fixed and
re-executed; they are small, mechanical, and fully specified by this report.

**Smallest next commission (EXEC-06 pre-step):** fix the four named defects (est.
<50 lines total), re-run the full campaign + verifier locally, then execute
EXEC-06 on `ubuntu-latest` against `7429c6b` with owner authorization.

---

## Appendix: R3 commit identities

- R3 commit and tree: recorded in the git history of `main` immediately after this
  report (see `git log -1 --format="%H %T"`); identities are also stated in the
  commission response.
- Focused evidence: `docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01-R3-verification-evidence/`
  (L5/L6 per-cell journals + summaries, L1/L2 summaries, NC-1 journal, VERIFICATION.txt)
