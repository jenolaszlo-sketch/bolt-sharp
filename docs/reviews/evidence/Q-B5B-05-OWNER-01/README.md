# Q-B5B-05-OWNER-01 evidence

Owner decision: [`Q-B5B-05-OWNER-01-owner-acceptance.md`](../../../decisions/Q-B5B-05-OWNER-01-owner-acceptance.md)

**Q-B5B-05 — ACCEPTED WITH QUALIFICATIONS — CLOSED.**

## Contents

| File | Description |
|---|---|
| `qualification-ledger.md` | Before/after status of every qualification touched by this decision |
| `verification.json` | Preflight, scope, evidence-integrity and campaign reconciliation recorded by the owner entry |
| `verify-evidence.py` | Self-verifying package verifier over Git blobs |

## Verify

```text
python docs/reviews/evidence/Q-B5B-05-OWNER-01/verify-evidence.py
```

The verifier checks the decision commit's parent, additions-only documentation scope, the
package manifest over Git blob payloads, and the live repository invariants.

## Reconciliation summary

The owner accepted the Q1 independent qualification and the producer demonstration on the
following recorded points, each reconciled against committed evidence rather than re-executed:

| Accepted record | Evidence anchor |
|---|---|
| Same-connection server-side RESET recovery qualified | Q1 `10-findings-register.md` F01; `windows/r-01/r-02/r-03/r-06-recovery.txt`, `r04-tls-recovery.txt`, `r05-concurrent.txt` (and WSL2) |
| Physical connection identity preservation qualified | Q1 `03-identity-matrix.md`; per-scenario verdicts in the same evidence files |
| Official-driver post-failure RESET interoperability not demonstrated | Q1 `05-driver-behavior-assessment.md` D-01..D-05 (both platforms) |
| Official-driver connection replacement accepted behavior | Q1 `06-negative-controls.md` + D-02/D-05; B5-B §19/F02 |
| No production defect requiring remediation identified | Q1 F01/F04/F05/F08; full regression campaigns green (`windows-focused-campaign.txt`, `wsl2-focused-campaign.txt`, six TRX envelopes) |
| Evidence integrity | Q1 `09-integrity-verification.txt`; Q1 package verifier `VERIFICATION PASSED` at the owner entry HEAD |