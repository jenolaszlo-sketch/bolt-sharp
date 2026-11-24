# Q-B5B-09-TLS-LINUX-QUAL-01-R4-R2-Q1 — Independent F6 Remediation Qualification

| Item | Value |
|---|---|
| Reviewed commit | `784cc3365cce16afdeec189e4d1349e4b844f250` |
| Reviewed tree | `13ac2e99f4c3ff9abf58791df9f6a06da147c6ff` |
| R4-R1 predecessor | `1eb03bdee1c1c322983bfdde333be76f053319a9` |
| Production base | `36d7b69bd4eb35bccc2a75ceb322171eefe32c9c` |
| Pin | `7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f` |
| Author / committer | `jenol <jenolaszlo-sketch@users.noreply.github.com>` (verified) |

Identity verified independently before commencing. Parent of the reviewed commit is
the R4-R1 commit. Worktree clean apart from a transient `__pycache__` produced by my
own interpreter runs.

---

# F6 DISPOSITION: **B. SUPPORTED WITH QUALIFICATIONS**

The F6 remediation is independently established and correctly implemented. Residual
limitations are explicitly bounded and listed in §7. They do not undermine the
remediation, but one of them (the F5 causal model, §6) is material to the next
commission and must not be carried forward unexamined.

---

## 1. Independent F6 verification — code inspection

`load_peer_receipt` was replaced by `load_peer_receipts` (verify_r1.py:92-105), which
returns **every** record whose `record_type == "peer-receipt"`. `verify_cell_l7`
(verify_r1.py:484-518) then:

- zero typed receipts → `peer receipt journal record missing`;
- more than one → `exactly one peer-receipt record is required, found N`, plus, for each
  extra record bound elsewhere, `a receipt record in this journal belongs to attempt X
  cell Y`;
- otherwise binds `receipts[0]` and applies the unchanged F1 checks.

The `record_type` filter is what keeps the rule narrow. Legitimate diagnostic records
(`peer-start`, `tls`, `peer-eof`) carry **no** `record_type` and are not counted — this
is the deliberate mechanism that prevents over-rejection, and it is verified empirically
in §3 (X5, X6) and by the live evidence in §4.

All pre-existing L7 contracts remain in the same function, unchanged: SHA-256 equality
between journal expectation and receipt (529), exact byte-count equality (533-545),
receipt↔attempt binding (513), receipt↔cell binding (516), Bolt framing via
`expected_terminal_message_bytes` vs actual framed payload length, and receipt
self-consistency (506-511).

## 2. Independent adversarial control — 16 cases, all correct

I reconstructed and executed all ten reported F6 cases plus **six cases of my own
design**, against copies of the preserved R4-R2 real evidence. Baseline unmodified copy
→ `VERIFIER: PASS`, 0 errors.

| # | Case | Result |
|---|---|---|
| D1 | duplicate receipt, contradictory sha256 | **1 error**, multiplicity |
| D2 | duplicate receipt, different payload+bytes | **1 error**, multiplicity |
| D3 | attempt-2 journal + appended attempt-3 records | **2 errors**, multiplicity + belongs-to-attempt-3 |
| D4 | identical duplicate receipt | **1 error**, multiplicity |
| D5 | attempt-3 records prepended before attempt-2's | **3 errors**, multiplicity + belongs-to-attempt-3 + mis-bound primary |
| D6 | receipt missing for one attempt | **1 error**, missing |
| D7 | all receipts missing | **3 errors**, one per attempt |
| D8 | `record_type` stripped (receipt invisible) | **1 error**, missing |
| D9 | empty receipt journal | **1 error**, missing |
| D10 | three extra receipts in one journal | **1 error**, `found 4` |
| X1 | **ordering: contradictory FIRST, valid LAST** | **3 errors** — ordering cannot bypass |
| X2 | **ordering: wrong-attempt receipt primary** | **3 errors** — ordering cannot bypass |
| X3 | **journal contains only another attempt's records** | **1 error**, `peer receipt is associated with attempt 3` |
| X4 | **duplicate carrying no `attempt` field** | **2 errors**, multiplicity + binding |
| X5 | **POSITIVE: receipt-like but UNTYPED extra record** | `VERIFIER: PASS` |
| X6 | **POSITIVE: untyped diagnostic records appended to every attempt** | `VERIFIER: PASS` |

X3 deserves note: with exactly one typed receipt present, rejection comes through the
**binding** path rather than multiplicity. That is the correct outcome, and it
demonstrates the two paths are independent rather than one masking the other.

Two of my initial expectation substrings were wrong (X3 expected multiplicity; I
initially expected `found 3` for D10 where the correct message is `found 4`). I
re-verified both directly rather than recording them as verifier defects. Both are
correct rejections.

## 3. Regression qualification matrix

**Independently executed by me:**

| Control | Result |
|---|---|
| Complete verifier self-test suite | `ALL SELF-TESTS PASSED`, exit 0 |
| — classification | 73 fixture result lines = 60 single-error + 9 two-error + 3 three-error + 1 EXEC-05 |
| — vacuity check | **0** fixtures with more than 3 errors |
| F6 adversarial control (§2) | 16/16 correct behaviour |
| Preserved R4 Windows evidence | `VERIFIER: PASS` |
| Preserved R4-R1 Windows evidence | `VERIFIER: PASS` |
| Preserved R4-R2 Windows evidence | `VERIFIER: PASS` |
| **Fresh reviewer campaign `r2q1full`** (I executed it) | `VERIFIER: PASS` |
| Positive control: legitimate untyped diagnostic records | PASS (X5, X6, and live evidence) |

**Inspected historical evidence (not re-executed by me):** preserved R4 and R4-R1
campaign journals, verified by re-running the verifier over them as shown above.

Fresh reviewer campaign `r2q1full` (Windows NT 10.0.26200.0, runtime 10.0.12,
`raw-complete`, 0 unobserved exceptions): L1–L7 all qualified; L5 wire **32768 exactly** /
plaintext 1424 / `cipher-cap`; L6 wire 20590 ≤ cap / plaintext **8192 exact** /
`plain-cap`; L7 169 bytes, `5cdf80e6…c7c444`; NC-1 131072 exact; NC-2 elapsed 810.763 >
500 with `prerequisite_confirmed` and `deadline_tamper_detected` true; dispatch audit all
7 implementations invoked. **Each L7 attempt journal contains exactly one typed
`peer-receipt` record out of four journal records** — the real harness complies with the
new requirement and legitimate diagnostic records coexist with it.

Evidence package integrity: 54 files on disk, 54 paths added by the commit, **zero
difference** in either direction. 23 `.jsonl` journals, 8 `.summary.json`, 5 top-level
documents.

## 4. Frozen scope and change-set verification

`git diff 7429c6b..HEAD -- src\|tests` is **EMPTY**; `src` and `tests` are each empty
individually. The pin is an ancestor of HEAD. The R4-R2 commit changes exactly two files
outside its additive evidence package — `verify_r1.py` and `verifier_selftest.py`. No
harness, production, peer, or frozen-test change. No push, tag, or publication.

## 5. F1–F4 preservation

F1 (L7 journal↔receipt binding), F2 (L5 plaintext > 0), F3 (fixture count erratum) and
F4 (provenance documentation) are all still enforced in the reviewed `verify_r1.py` and
still pass on preserved evidence. **No regression was demonstrated in any of them.**

## 6. F5 entry-readiness assessment — OBLIGATIONS NOT YET SUFFICIENTLY PRECISE

`F5-NATIVE-LINUX-READINESS.txt` records O1–O4. I verified its technical claims
independently: E1's candidate harness does shrink `SO_SNDBUF`
(`TlsE1MechanismTests.cs.txt:108`); R4's `Production.cs` never sets the sender's
`SendBufferSize`; `Candidate.cs:283` does shrink; and the native-Linux oracle
`pendingNow = messages == 3 && available > 0 && (queue > 0 || !IsNativeLinux())`
(`Production.cs:243-244`) with `IsNativeLinux() => PlatformID.Unix && !IsWsl()`
(`Support.cs:703-704`) is bypassed under WSL2. The profile set does max out at 262144
preceding bytes, and the campaign plan already carries a profile-4 note naming
native-Linux, so **O4 is substantially already satisfied** and should be restated as
closed-pending-verification rather than open.

**However, the record's causal model does not match the evidence.** Comparing Windows
and WSL2 L3 attempt 1 on the *same* profile (ordinal 1, 36864 preceding bytes):

| Field | Windows (qualified) | WSL2 (precursor-not-observed) |
|---|---|---|
| candidate send buffer req/actual | 2048 / **2048** | 2048 / **4608** |
| peer rcvbuf requested | 2048 | 2048 |
| fixture bytes | **5120** | **2048** |
| fixture_path_saturated | true | **true** |
| shutdown_pending / ms | **true** / 21.6 | **false** / 0 |
| elapsed_ms | 320.4 | **0.207** |

The failure is therefore **not** "262144 preceding bytes against a 4 MB buffer". The
attempts used profile ordinal 1, the fixture was only 2048 bytes, the send path *was*
saturated, the sender's `SO_SNDBUF` *was* already shrunk (to 4608 against Linux's floor)
— and the close_notify still completed immediately (`shutdown_completed_after_hold:
true`), in 0.207 ms. The binding constraint is buffer-capacity arithmetic between the
**effective** candidate send buffer (2048 vs 4608), the peer's effective receive buffer
(2048 vs 2304/4096) and the **fixture byte count** (5120 vs 2048).

**Consequence for O1:** its dichotomy — (a) shrink sender `SO_SNDBUF`, or (b) extend the
registered profile set — does not address the observed mechanism. The sender is already
shrunk; extending the profile set addresses a ceiling that is not binding in the observed
failures. A further diagnostic discrepancy is visible in the peer journals: on WSL2 the
L3 zero-window peer **never reached a `parked` record at all** (journal ends after `tls`),
while L4's peer did park.

**Recommendation:** the smallest defensible F5 implementation commission is a
**diagnostic/repro commission**, not a buffer-change commission. It must first establish,
on native Linux, why close_notify completes immediately when the path is saturated and
both buffers are small, and why the zero-window peer does not park for the L3 shape —
and only then choose between the O1 options. Required native-Linux environment and
prerequisites are as O2/O3 state; a native Linux host with .NET 10 and python3, with
`/proc/net/tcp` readable. Positive control for the `queue > 0` oracle: a terminal write
that is observably stuck exhibits non-zero TxQueue. Negative control: a terminal write
that completes exhibits TxQueue zero **and** the attempt must then be recorded as
non-qualifying, never as passed.

I do **not** authorize F5 implementation here.

## 7. Repository consolidation readiness (non-mutating assessment)

| Item | Finding |
|---|---|
| Local branches | `main` (ahead of `origin/main` by 43) and `qualification/q-b5b-09-tls-linux-qual-01` |
| Worktrees | exactly **one**: `D:/projects/bolt-sharp` on `main`. No additional worktrees exist. |
| Dirty state | clean apart from transient `__pycache__` from my own runs |
| Stashes | **one**: `stash@{0}` containing 2 files — `.github/workflows/q-b5b-09-tls-linux-qual-01.yml` and `docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/run-campaign.py` |
| Local vs remote `main` | local **43 ahead**, remote **0 ahead** → strictly ahead, **no divergence, no rebase needed** |
| Divergences | `main` and the qualification branch **have genuinely diverged**: merge base `3e55368`, 10 unique commits on `main`, **7 unique commits on the qualification branch** |
| Qualification branch content | includes `feat(reviews): implement Q-B5B-09 native-Linux campaign driver` and two native-Linux campaign sync fixes — **directly relevant to F5**. It touches **no** `src`/`tests` content. |
| Remote state | qualification branch tracks `origin/qualification/...`, i.e. already published |

**Accepted work eligible for consolidation:** the 43 unpublished `main` commits are all
`docs/reviews` evidence and `harness`/`verifier`/`peer` additions with an empty
`src`/`tests` diff against the pin, so they carry no production-behaviour risk and are
eligible to publish as one authoritative `main`.

**Recommended activity-aware integration sequence:**

1. **Keep and publish `main`** (43 commits, strictly ahead of origin — no rebase risk).
2. **Do not merge the qualification branch into `main` unilaterally.** Its 7 unique
   commits are native-Linux campaign-driver work that the ongoing TLS qualification
   depends on and that F5 will build on. Reconcile it deliberately, not as consolidation
   fallout. Suggested path: an explicit F5/native-Linux commission that re-bases the
   branch onto current `main`, since its driver work is a *prerequisite* for F5, not a
   candidate for consolidation.
3. **Preserve the stash.** It holds a GitHub Actions workflow and a campaign runner; it is
   the only copy of that pairing and its loss would be unrecoverable. Classify as
   protected; surface it to the owner rather than resolving it unasked.
4. **One checkout already satisfies the "one primary development checkout" objective.**
   No worktree action is required, and none is permitted here.
5. Consolidation of accepted plaintext work is **not** gated on completing the TLS
   roadmap; the two tracks are separable.

## 8. Dispositions

| Subject | Disposition |
|---|---|
| **F6 remediation** | **B — SUPPORTED WITH QUALIFICATIONS** |
| **F5 status** | **OPEN** — obligations recorded, causal model requires correction before implementation |
| **Native-Linux qualification** | **OUTSTANDING** — not executed, not claimed, not advanced |
| **Windows-scope qualification** | stands, on fresh evidence I produced and verified |
| **Consolidation readiness** | `main` ready to publish; qualification branch must be reconciled deliberately; stash protected |
| **Release readiness** | **NOT ESTABLISHED** — gated on F5 and native-Linux qualification |

## 9. Residual limitations

1. The F5 causal model is contradicted by the evidence (§6) — material to the next
   commission.
2. O4's profile-4 note already exists and should be restated rather than reopened.
3. My adversarial control exercised the preserved R4-R2 evidence and one fresh campaign;
   it did not re-execute the R4 and R4-R1 campaigns.
4. Three of my 16 adversarial cases reject via paths other than the one I first expected;
   each was re-verified directly and all are correct rejections.

## 10. Recommended next commission

An **F5 diagnostic/repro commission on native Linux**, with these entry conditions:
establish why close_notify completes immediately under a saturated path with small
effective buffers; establish why the zero-window peer does not park for the L3 shape;
then, and only then, choose between shrinking the sender's `SO_SNDBUF` and extending the
registered profile set — registering either in `campaign-plan.json` before execution —
and validate the native-Linux `queue > 0` oracle with both positive and negative controls.

I stop at the owner decision gate. I have not granted owner acceptance, pushed,
published, tagged, or modified any worktree, branch, or stash.