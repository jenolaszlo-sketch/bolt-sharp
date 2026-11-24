# Q-B5B-09-TLS-LINUX-QUAL-01-R4-R2 — F6 Evidence Uniqueness Remediation

Starting revision: `1eb03bdee1c1c322983bfdde333be76f053319a9` (tree `e2d28d015e99ffc57252c586e87ceb0c1d2bcf2d`)
Frozen production base: `36d7b69bd4eb35bccc2a75ceb322171eefe32c9c`
Frozen pin: `7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f`
Closes: independent R4-R1-Q1 finding **F6**.

---

## 1. The defect

The R4-R1-Q1 reviewer demonstrated that `Verifier.load_peer_receipt` returned only the
**first** record with `record_type == "peer-receipt"` and ignored any others, so an L7
attempt's receipt journal could contain additional duplicate, contradictory, or unrelated
records and verification would still pass. Three cases were demonstrated to produce
`VERIFIER: PASS` on contradictory evidence. F1–F4 were already closed and were not reopened.

## 2. The change

`verify_r1.py`:

- `load_peer_receipt` was replaced by `load_peer_receipts`, which returns **every**
  record typed `peer-receipt` in the attempt's journal.
- `verify_cell_l7` now requires **exactly one** such record per qualifying attempt:
  - zero → `peer receipt journal record missing`;
  - more than one → `exactly one peer-receipt record is required, found N`;
  - and, when the multiplicity error fires, each extra record bound to a different attempt
    or cell is also reported explicitly.

The `record_type` filter is what keeps the check narrow: legitimate diagnostic records the
real peer emits (`peer-start`, `tls`, `peer-eof`) carry **no** `record_type` and remain
valid. This is asserted by a dedicated positive fixture.

All pre-existing L7 contracts are preserved unchanged: journal↔receipt SHA-256 equality,
exact message-byte-count equality, attempt and cell binding, valid Bolt framing, and
receipt self-consistency.

**Files changed:** `verify_r1.py` and `verifier_selftest.py` only.

## 3. Adversarial verification

Ten independent mutations of the preserved real evidence, all rejected for the intended
reason, with the three reviewer-demonstrated defects reproduced:

| Case | Mutations | Errors | Attributed error |
|---|---|---|---|
| D1 duplicate receipt, contradictory sha256 | reviewer case 1 | 1 | `exactly one peer-receipt record is required, found 2` |
| D2 duplicate receipt, different payload+bytes | reviewer case 2 | 1 | `exactly one peer-receipt record is required, found 2` |
| D3 attempt-2 journal + appended attempt-3 records | reviewer case 3 | 2 | `exactly one peer-receipt record is required, found 2` |
| D4 identical duplicate receipt | — | 1 | `exactly one peer-receipt record is required, found 2` |
| D5 attempt-3 records prepended before attempt-2's | — | 3 | multiplicity + mis-bound + belongs-to-attempt-3 |
| D6 receipt missing for one attempt | — | 1 | `peer receipt journal record missing` |
| D7 all receipts missing | — | 3 | missing, one per attempt |
| D8 `record_type` stripped (receipt invisible) | — | 1 | `peer receipt journal record missing` |
| D9 duplicate receipts on every attempt | — | 3 | multiplicity, one per attempt |
| D10 three extra receipts in one journal | — | 1 | `exactly one peer-receipt record is required, found 4` |

Baseline unmodified copy → `VERIFIER: PASS`. **10/10 detected for the intended reason.**

## 4. Self-test suite

Eight new negative fixtures were added to `verifier_selftest.py`, covering all F6 failure
modes, plus one new **positive** fixture (`test_f6_legitimate_journal_accepted`) that
appends untyped diagnostic records to every receipt journal and requires the run to still
pass — proving the uniqueness rule does not broaden rejection beyond the authorized
journal schema.

Result: `ALL SELF-TESTS PASSED`, exit 0. **73 fixture result lines = 60 single-error +
9 two-error + 3 three-error + 1 EXEC-05.**

Every 3-error fixture was individually inspected and is attributable, not vacuous: D7 and
D9 produce exactly one targeted error per attempt, and D5 produces the multiplicity error
plus the two mis-binding errors caused by the prepended records.

## 5. Regression qualification

| Control | Result |
|---|---|
| Complete verifier self-test suite | `ALL SELF-TESTS PASSED`, exit 0 |
| Independent F6 adversarial control | 10/10 for the intended reason |
| Preserved R4 Windows evidence | `VERIFIER: PASS` |
| Preserved R4-R1 Windows evidence | `VERIFIER: PASS` |
| Fresh R4-R2 Windows campaign (`r4r2full`) + verifier | `VERIFIER: PASS` |
| L7 receipt journals in the fresh run | exactly **1** `peer-receipt` record per attempt |
| Positive baseline | accepted |
| `git diff 7429c6b..HEAD -- src tests` | **EMPTY** |

Fresh R4-R2 campaign (Windows NT 10.0.26200.0, runtime 10.0.12, `raw-complete`, 0
unobserved exceptions): L1–L7 all qualified; L5 wire **32768 exactly** / plaintext 1424 /
`cipher-cap`; L6 wire 8236 / plaintext **8192 exact** / `plain-cap`; L7 169 bytes,
`5cdf80e60e813e45ddb60777b10c920fb9f940ff49adc865c5157c2c56c7c444`; NC-1 131072 exact;
NC-2 elapsed 801.788 > 500, `prerequisite_confirmed` and `deadline_tamper_detected` true;
dispatch audit all 7 implementations invoked.

No regressions: evidence generated before this change still validates, and no legitimate
evidence is now rejected. F5 remains open and untouched; no native-Linux work was done.