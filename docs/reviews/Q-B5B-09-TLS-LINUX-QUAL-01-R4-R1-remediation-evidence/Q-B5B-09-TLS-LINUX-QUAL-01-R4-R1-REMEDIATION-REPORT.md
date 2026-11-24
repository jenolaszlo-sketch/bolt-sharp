# Q-B5B-09-TLS-LINUX-QUAL-01-R4-R1 — Targeted Independent Review Findings Remediation

R4 remediated: `df5011ffb39732b5f85ff852b3be33e2bde2e20b`
Frozen production base: `36d7b69bd4eb35bccc2a75ceb322171eefe32c9c`
Frozen pin: `7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f`
Q1 disposition: **B** — qualified within its explicitly limited Windows scope with findings F1–F5.

Scope of this commission: remediate Q1 findings **F1–F4**. F5 is recorded as an open
readiness obligation and is **not** implemented. No production or frozen-test change, no
expansion of the harness's behavioural scope, no native-Linux work.

---

## 1. Finding-to-evidence mapping

| Q1 finding | Severity | Remediated | Evidence |
|---|---|---|---|
| **F1** L7 verifier does not bind the journal expectation to the peer receipt | Material | **Yes** | §2 |
| **F2** L5 accepted `plaintext_bytes = 0` | Minor | **Yes** | §3 |
| **F3** Fixture count stated as 50 in the R4 documents | Documentation | **Yes** | §4 |
| **F4** `run-meta.json` line-ending normalization undocumented | Provenance | **Yes** | §5 |
| **F5** Native-Linux readiness path unexercised; no production-path buffer shrink | Readiness | **Recorded, not implemented** | §6 |

---

## 2. F1 — L7 verifier binding

`verify_r1.py::verify_cell_l7` now enforces, for every qualifying L7 attempt:

1. **Journal `expected_terminal_sha256` == peer-receipt `sha256`.**
2. **Journal `expected_terminal_message_bytes` == peer-receipt `message_bytes`.**
3. **Receipt association** — the receipt's `attempt` must equal the journal attempt and its
   `cell` must be `L7`.
4. **Fail-closed on missing / malformed / contradictory values:**
   - `expected_terminal_sha256` absent, or not a 64-character lowercase hex digest → error;
   - `expected_terminal_message_bytes` absent, or not an integer → error;
   - receipt `message_bytes` absent or non-integer → error;
   - receipt `message_hex` absent or not decodable hex → error;
   - declared byte count that disagrees with the actual framed payload length → error.

The pre-existing receipt self-consistency check (recomputed SHA-256 over the receipt's own
`message_hex`) and the protocol framing checks are **unchanged and still enforced**.

This closes precisely the R3/R4 defect class: a harness-side expectation that disagreed
with the bytes the peer actually received. Previously the verifier only rejected a receipt
that was inconsistent *with itself*.

### Adversarial confirmation (fresh, against preserved real evidence)

12 mutations of the preserved real campaign, each rejected **for its intended reason**:

| Mutation | Verifier error (abridged) |
|---|---|
| journal `expected_terminal_sha256` → `f…f` | `expected_terminal_sha256 ffff… does not match the peer receipt sha256 …` |
| journal `expected_terminal_message_bytes` → 999 | `expected_terminal_message_bytes 999 does not match the peer receipt message_bytes 169` |
| journal digest → `not-a-digest` | `is not a 64-character lowercase hex sha256` |
| journal digest removed | `expected_terminal_sha256 missing` |
| journal bytes removed | `expected_terminal_message_bytes missing` |
| receipt `attempt` → 9 | `peer receipt is associated with attempt 9 (receipt is not bound to this attempt)` |
| receipt `message_bytes` → 999 | `expected_terminal_message_bytes 169 does not match the peer receipt message_bytes 999` |
| receipt `cell` → L1 | `peer receipt is associated with cell 'L1'` |
| receipt `sha256` → 0…0 | `peer receipt digest does not match its own hex payload` (pre-existing check preserved) |
| receipt `message_hex` → `zzzz` | `peer receipt message_hex is malformed and cannot be re-encoded` |

Baseline unmodified copy → `VERIFIER: PASS`. **12/12 detected, 0 accepted.**

### Positive baseline

`test_l7_binding_accepted` proves a matched journal/receipt pair is still accepted, so the
binding does not over-reject.

---

## 3. F2 — L5 plaintext requirement

`verify_r1.py::_verify_cap_attempt` now requires `plaintext_bytes > 0` for the **L5**
tiny-flood cell.

**Consistency with the binding E1 contract:** `Q-B5B-09-TLS-E1/verify-evidence.py` asserts
`finish.get("Bytes") == 1441` for T5 — a positive, exact decrypted-byte value. Requiring
`> 0` is therefore a *subset* of the E1 requirement and introduces **no new arbitrary
minimum**; it only rejects the empty-observation case E1 also rejects. L6 already requires
`plaintext_bytes == 8192` exactly, so the ordering is unchanged there.

### Adversarial and boundary confirmation

| Case | Result |
|---|---|
| L5 `plaintext_bytes = 0` | rejected — `plaintext bytes 0 is not greater than zero` |
| L5 `plaintext_bytes = -1` | rejected — `plaintext bytes -1 is not greater than zero` |
| L5 `plaintext_bytes = 1` with wire exactly 32768 | **accepted** (positive boundary: proves no arbitrary minimum above zero) |
| Preserved R4 evidence (plaintext 1424) | accepted |

---

## 4. F3 — Fixture-count erratum

The R4 documents stated "**50** rejecting fixtures" while their own sentence stated 47
single-error + 7 two-error rejections. Corrected to **54** in both
`Q-B5B-09-TLS-LINUX-QUAL-01-R4-verification-evidence/VERIFICATION.txt` and
`…-REMEDIATION-REPORT.md`, with an in-place errata block in `VERIFICATION.txt`.

Independent reconciliation (re-derived from the committed self-test source and from an
actual run of the suite):

| Distribution | Count |
|---|---|
| dispatch-coverage-integrity cases | 7 |
| false-pass fixtures | 47 |
| **Total rejecting fixtures** | **54** |
| — reject with exactly ONE error | 47 |
| — reject with TWO coupled errors | 7 |
| positive acceptance tests (no error count) | 2 |

Post-remediation run of the suite: **65 fixture result lines = 1 EXEC-05 rejection +
56 single-error + 8 two-error.** The R1 additions (8 F1 fixtures, 2 F2 fixtures, 2 new
positive acceptance tests) bring the totals to 65 lines, of which **64 are rejecting
fixtures** (56 single-error + 8 two-error). The Q1 figure of 54 remains the correct count
**for the R4 baseline suite**; the erratum corrects the R4 documents, and this paragraph
records the post-R4-R1 distribution so neither figure is misquoted.

The original Q1 findings are preserved unchanged; this section is the correction record.

---

## 5. F4 — Evidence provenance

The R4 evidence package preserves the raw Windows campaign output. Verified:

- **48 of 49** copied evidence files are **byte-identical** to the raw run output
  (`C:\Windows\TEMP\r4-full-windows6`), compared by SHA-256.
- **`run-meta.json`** differs only in **line endings**: 20 LF in the preserved copy vs
  19 CRLF + 20 LF in the raw output (both 875 vs 894 bytes; both 20 logical lines).
- **Zero semantic field differences** — all 18 `run-meta` fields compare equal, including
  `commit`, `tree`, `status: raw-complete`, `platform`, `runtime`, `native_linux: false`,
  `wsl: false`, `unobserved_task_exceptions: 0`.
- `PROVENANCE.txt` is a producer-added summary file, not original harness output.
- The 60 absolute paths present inside peer journals are the **harness's own recorded
  self-references**, preserved verbatim — not producer-introduced edits.
- No raw evidence was rewritten in this commission; the normalization is a copy-time
  artifact of the preserve step, now documented.

---

## 6. F5 — Native-Linux readiness (open obligation, NOT implemented)

No native-Linux send-buffer or profile change was made. Recorded for a separate,
explicitly authorized commission.

### The E1 mechanism that R4 does not carry on the production path

`docs/reviews/evidence/Q-B5B-09-TLS-E1/TlsE1MechanismTests.cs.txt` explicitly **shrinks the
socket send buffer** before writing the terminal message:

```
peer.ReceiveBufferSize = probe.PeerReceive;      // line 65
server.SendBufferSize = probe.ShrinkTo;          // line 108
```

Its registered profile note reads "deep queue required on adapted WSL2; recorded as
preceding-write-did-not-complete on Windows".

### The R4 production-path difference

`Production.cs` configures only the **peer receive buffer**
(`BoltPeer.ConnectAsync(..., profile.PeerReceive, ...)`, recorded as
`peer_receive_actual`) and never shrinks the sender's `SO_SNDBUF`. The candidate cap cells
*do* shrink (`Candidate.cs:283 sender.SendBufferSize = profile.TargetSend > 0 ?
profile.TargetSend : 2048`), but the production terminal-write cells do not.

Consequence, observed in the Q1 analysis: Windows honors the requested peer receive buffer
exactly (1024 → **1024 actual**) and induces pendency at profile 4 (262144 preceding
bytes); WSL2/Linux enforces a floor (1024 → **2304 actual**) with
`/proc/sys/net/ipv4/tcp_wmem` = `4096 16384 4194304`, so the registered 262144-byte
maximum preceding write never produces persistent pending.

Additionally, the native-Linux pendency oracle in `Production.cs` —
`pendingNow = messages == 3 && available > 0 && (queue > 0 || !IsNativeLinux())` — resolves
through `IsNativeLinux() => PlatformID.Unix && !IsWsl()`. Because the WSL2 run reports
`wsl = true`, the `/proc/net/tcp` TxQueue branch is **bypassed and therefore
unexercised**.

### Open obligation (design and implementation reserved)

1. Decide, per platform, whether to shrink `SO_SNDBUF` on the production sender (mirroring
   E1) or to extend the registered profile set beyond 262144 preceding bytes.
2. Exercise and validate the native-Linux `queue > 0` oracle on a native Linux host.
3. Re-run the full campaign on native Linux and verify with `verify_r1.py`.

Until then, native-Linux qualification remains unmet, and no native-Linux qualification is
claimed by R4 or by this remediation.

---

## 7. Verification performed

| Control | Result |
|---|---|
| Verifier self-test suite (complete) | `ALL SELF-TESTS PASSED`, exit 0 |
| — fixtures | 65 result lines (56 single-error, 8 two-error, 1 EXEC-05) |
| Fresh adversarial controls, F1 | 12/12 rejected for the intended reason |
| Fresh adversarial controls, F2 | 2/2 rejected; positive boundary accepted |
| Preserved R4 Windows evidence re-verified | `VERIFIER: PASS` |
| Fresh R4-R1 Windows campaign + verifier | `VERIFIER: PASS` |
| Positive baseline (valid run) | accepted |
| `git diff 7429c6b..HEAD -- src tests` | **EMPTY** |

Fresh R4-R1 campaign (`run-id r4r1full`, platform Microsoft Windows NT 10.0.26200.0,
runtime 10.0.12, `status raw-complete`, `unobserved_task_exceptions 0`):
all of L1–L7 qualified; L5 wire **32768 exactly** / plaintext 1424 / drain `cipher-cap`;
L6 wire 12354 ≤ cap / plaintext **8192 exact** / drain `plain-cap`; L7 169 bytes,
`5cdf80e60e813e45ddb60777b10c920fb9f940ff49adc865c5157c2c56c7c444`, 3 attempts
peer-receipt-verified; NC-1 wire 131072 exact, tamper detected; NC-2 elapsed 800.937 > 500,
`prerequisite_confirmed true`, `deadline_tamper_detected true`; dispatch audit all 7
implementations invoked, `all_required_invoked true`.

Files changed: `verify_r1.py`, `verifier_selftest.py`, and the two R4 evidence documents.
**No harness, production, peer, or test code was modified.**

---

## 8. Disposition and next action

**Disposition: A — R4-R1 remediation is independently qualified within its explicitly
limited Windows scope.** F1–F4 are remediated and independently confirmed by adversarial
controls. The Windows-scope qualification of R4 stands on fresh evidence produced with the
updated verifier.

**Recommended next action: independent R4-R1 qualification.** All required evidence is
satisfactory: the self-test suite passes, the fresh adversarial controls reject every
intended mutation for its intended reason, the positive baseline is accepted, the preserved
R4 evidence still verifies, a fresh campaign verifies, and the frozen production scope is
byte-identical to the pin.

Native-Linux qualification remains a separate, unmet requirement (F5) and is neither
claimed nor advanced by this remediation.