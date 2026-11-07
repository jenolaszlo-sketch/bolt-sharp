# B3-A-Q2 — Focused independent requalification of the HELLO `bolt_agent` remediation

**Task:** B3-A-Q2 — focused independent requalification of B3-A-R1-IMPL-01 (finding B3-A-Q1-F01)
**Remediation under review:** `B3-A-R1-IMPL-01` (`docs/reviews/B3-A-R1-IMPL-01.md`)
**Reviewed `main` HEAD:** `83126a99be56fa141eec3342a21b863ca4ef45d2` / tree `4c697e8dab5a30f8749c7eb9246827bb6ddcd9ff`
**R1 remediation commit:** `d6686d14f3eaee42f83971d517d9077170a30fe2` / tree `f615af7feff41c2f0cc2e0debd06d4d928daa32d`
**Q1 baseline (isolation reference):** `6974a56a6c4fc9b706a29ac65fe029840834893c`
**Target revision:** Bolt 5.8 / PackStream 1.0
**Reviewer:** GLM-5.3-Flash (`nvidia`, OpenCode). Executor-level independence from the R1 producer
session is recorded; organizational attestation is not available (author-independence caveat
retained). This record is **not** owner acceptance.

## Disposition

> **B3-A-Q2 — SUPPORTED WITH QUALIFICATIONS; F-01 CLOSED.**
>
> The R1 remediation correctly and deterministically rejects a HELLO that omits the
> specification-mandatory `bolt_agent`, preserves every valid HELLO form, changes nothing else in
> the message contracts, introduces no public API change, and leaves all historical evidence,
> fixtures and manifests intact. One new bounded categorization finding (**F-Q2-01**: an
> explicit-null nested `product` reports `MissingRequiredField` rather than the
> `InvalidFieldType` anticipated by the Q2 commission table, because required-string fields are
> treated as null-as-absent throughout B3-A — a pre-existing systemic convention, one level
> narrower than the R1 agent-level handling) is recorded. It does not accept any invalid HELLO and
> is not a material defect. Recommendation: **bounded B3-A owner acceptance** with F-02, F-Q2-01
> and the existing documented limitations retained.

---

## 1. Baseline and reviewed state

```text
git branch --show-current  -> main
git rev-parse HEAD         -> 83126a99be56fa141eec3342a21b863ca4ef45d2 (matches the commission)
git rev-parse HEAD^{tree}  -> 4c697e8dab5a30f8749c7eb9246827bb6ddcd9ff
git worktree list          -> one worktree (D:/projects/bolt-sharp)
git status --porcelain     -> clean at entry
Ancestry                   -> 6974a56 (Q1) -> d6686d1 (R1 fix) -> 83126a9 (R1 evidence)
Prior Q2 work              -> none existed
Nothing pushed             -> origin/main (9abf5e3) remains an ancestor of HEAD
```

## 2. Evidence integrity and change isolation

| Manifest / check | Result |
| --- | --- |
| R1 evidence manifest | **13/13** entries match |
| B3-A-Q1 evidence manifest | **14/14** entries match |
| B0-B producer manifest | **24/24** (`files` + `reports`) |
| B0-B Q1 reviewer manifest | **119/119** |
| `git diff 6974a56..83126a9` | exactly the R1-authorized paths: 2 production files, 3 test files, R1 report + 8 R1 evidence files — nothing else |
| PackStream / Framing / Handshake | **unchanged** (no path in the diff) |
| New public API | **none** (diff adds no `public` member; the only production edits are inside `ValidateHelloExtra` plus one doc comment) |
| Other message contracts | **unchanged** (`BoltMessageCodec` functional diff is confined to the HELLO agent block; RUN/LOGON/PULL/DISCARD/SUCCESS/RECORD/FAILURE/IGNORED paths untouched) |
| Historical tests weakened | **no** — one obsolete assertion (`Assert.Null(hello.BoltAgent)`, which asserted the pre-fix invalid behavior) was replaced by two stronger assertions; two new exact-category tests added |
| Historical Q1 probe / evidence | **unchanged** (the unmodified Q1 probe was re-run into fresh Q2 directories; its committed source and evidence are byte-intact per the 14/14 manifest) |
| B0-B evidence + owner decisions | **unchanged** |

## 3. Independent Bolt 5.8 mandatory-field interpretation

Source: the preserved official specification copy
`docs/reviews/evidence/B3-A-IMPL-01/spec/message.adoc` (fetched from the upstream
`neo4j-drivers/docs-bolt` `message.adoc`):

- HELLO fields block (line 464): `bolt_agent::Dictionary(product::String, platform::String, language::String, language_details::String)`, "(added in *v5.3*)".
- Line 508: **"This field is mandatory."** (the `bolt_agent` bullet).
- Line 511: **"This field is mandatory."** (the nested `product::String` bullet).
- `platform`/`language`/`language_details` are documented as null-able optional entries.

Independent interpretation: `bolt_agent` must be present as a Dictionary and `product` must be
present as a String for a Bolt 5.8 HELLO to be schema-valid; the remaining agent entries are
optional. This is exactly the contract R1 was asked to enforce, and no evidence was found that the
specification imposes any **additional** mandatory HELLO metadata beyond `user_agent` (B3-A
enforces it) and `bolt_agent`/`product`.

## 4. Fresh F-01 closure probes

Fresh, independently generated fixtures (Q2 probe, own encoder/decoder):
`docs/reviews/probes/B3-A-Q2/ClosureProbe/`, results in
`evidence/B3-A-Q2/closure-probe/closure-results.json`. The probe asserts the **exact** outcome
(accepted, or a specific `BoltMessageErrorCategory`) and that only `BoltMessageException` is
raised (no arbitrary exceptions). **17 cases, 16 met, 1 finding.**

| Case | Fixture | Expected | Observed | Met |
| --- | --- | --- | --- | --- |
| Q2-A | HELLO without `bolt_agent` | MissingRequiredField | MissingRequiredField | ✓ |
| Q2-B | `bolt_agent: null` | InvalidFieldType | InvalidFieldType | ✓ |
| Q2-C | `bolt_agent` not a map | InvalidFieldType | InvalidFieldType | ✓ |
| Q2-D | empty `bolt_agent` map | MissingRequiredField | MissingRequiredField | ✓ |
| Q2-E | agent without `product` | MissingRequiredField | MissingRequiredField | ✓ |
| **Q2-F** | **`product: null`** | **InvalidFieldType** | **MissingRequiredField** | **✗ (F-Q2-01)** |
| Q2-G | `product` not a string (int) | InvalidFieldType | InvalidFieldType | ✓ |
| Q2-H | minimal valid agent | accepted | accepted | ✓ |
| Q2-I | valid optional agent entries | accepted | accepted | ✓ |
| Q2-J | valid routing metadata | accepted | accepted | ✓ |
| Q2-K | notification metadata + null optionals | accepted | accepted | ✓ |
| Q2-L | complete field set + unknown metadata | accepted | accepted | ✓ |
| Q2-S1 | sentinel values, missing product | rejected, no value leak | MissingRequiredField, no sentinel in diagnostics | ✓ |
| Q2-S2 | sentinel `product`, wrong-typed `user_agent` | rejected, no value leak | InvalidFieldType, no sentinel | ✓ |

**F-Q2-01 (bounded categorization finding).** An explicit-null nested `product` is reported as
`MissingRequiredField`, whereas the commission's expectation table (and a natural reading — null is
not a String) anticipates `InvalidFieldType`. Cause: `RequireStringField` (and the `user_agent` /
LOGON `scheme` checks) use `BoltMessageFields.GetValue`, which by design collapses a present-but-null
PackStream value to a `null` reference (the accepted B1-R1 contract), so null is treated as absent.
R1 *did* distinguish the agent top level (`bolt_agent: null` → InvalidFieldType via an added
presence scan) but did not extend that distinction to the nested `product`. Verified by direct code
inspection: `user_agent: null` and LOGON `scheme: null` behave the same way (MissingRequiredField),
so this is a systemic, pre-existing convention that predates R1 for all fields except the R1 agent
top level. Materiality: the HELLO is still **rejected deterministically** with a precise,
non-leaking category; no invalid message is accepted; the distinction is internal to BoltSharp's
error taxonomy and invisible to any client (B3-B would emit a FAILURE either way). It is therefore
recorded as a bounded qualification, not a blocker, with an optional one-file remediation offered
below (§10).

## 5. C3 / C13 / C14 historical-fixture analysis

Independently reconstructed (Q2 probe's own encoder) and decoded (Q2 probe's own generic decoder):

| Q1 probe case | Fixture content | `bolt_agent` present (independently decoded)? | Post-R1 observed | Classification |
| --- | --- | --- | --- | --- |
| C3 | `HELLO {user_agent: "X"}` | **No** (10 bytes consumed of 16 → map has only `user_agent`) | `MissingRequiredField` | **EXPECTED REMEDIATION DIFFERENCE** |
| C13 | spec-example field set (`user_agent`, `routing`, `notifications_minimum_severity`, `notifications_disabled_classifications`) | **No** (159/159 consumed, no agent key) | `MissingRequiredField` | **EXPECTED REMEDIATION DIFFERENCE** |
| C14 | `{user_agent, routing:null, notifications_minimum_severity:null, notifications_disabled_classifications:null}` | **No** (99/99 consumed, no agent key) | `MissingRequiredField` | **EXPECTED REMEDIATION DIFFERENCE** |

Each fixture omits the now-mandatory field, so per §3 its correct post-R1 disposition is
`MissingRequiredField`. The C3/C13/C14 differences from Q1 are independently established as the
intended correction, **not** regressions.

## 6. Historical probe comparison

The **unmodified** B3-A-Q1 falsification probe was rebuilt against the remediated assembly and
re-run by the reviewer into `evidence/B3-A-Q2/q1-probe-rerun/`:

- **Byte-identical** to the producer's R1 after-fix evidence (`q1-probe-after-fix/falsification-results.json`) — the producer's R1 probe evidence is fully reproducible.
- 85 probes, **3 mismatches** (C3, C13, C14), each `MissingRequiredField` — the producer's reported figures are exact.
- Comparing every non-C3/C13/C14 case against the before-fix run: **0 differences** in outcome and category (75 CONFORMANT + 7 CONFIRMED unchanged). The producer's claim that the remaining 82 outcomes are byte-identical is confirmed.
- The historical probe source and its committed Q1 evidence were not modified (14/14 manifest; Q2 wrote only to fresh directories).

## 7. Regression results

| Set | Command | Result |
| --- | --- | --- |
| Core solution | `dotnet test BoltSharp.slnx -c Debug` | **924 passed / 0 failed / 0 skipped** |
| B3-A message tests | `dotnet test … --filter FullyQualifiedName~BoltSharp.Tests.Messages` | **98 passed / 0 failed / 0 skipped** |
| B0-B integration | `dotnet test tests/BoltSharp.IntegrationTests/… -c Debug` | **19 passed / 0 failed / 0 skipped** |
| Build | `dotnet build BoltSharp.slnx -c Debug` | 0 warnings / 0 errors |

All producer-reported counts reproduce exactly; no unexpected skips. The two R1-updated positive
tests (`HelloMinimal`, `HelloNullOptional`) were inspected: their fixtures now include the mandatory
`bolt_agent`, their assertions were **strengthened** (agent presence and `product` value asserted),
and no assertion that carried independent coverage was removed.

**Formatting / analyzers (informational, not an R1 regression).** `dotnet format --verify-no-changes`
reports pre-existing whitespace items in `BoltMessageFixtures.cs` (an over-indented fixture block
that exists unchanged at the Q1 baseline) and pre-existing xUnit2012 advisories in
`BoltRequestDecodeTests.cs` (also present at the Q1 baseline). There is **no** `.editorconfig` and
**no** CI/format gate in the repository, so this is not an established acceptance gate; the normal
build is clean. It is out of R1's authorized scope and is not attributed to the remediation.

## 8. Byte-fixture and evidence preservation

- The six authentic `Neo4j.Driver` 6.4.0 request captures still decode unchanged (Q1 probe rerun: F-group 0 differences; producer `B0BFixtureComparisonTests` pass).
- Response encodings remain byte-identical: SUCCESS empty (`B170A0`), HELLO SUCCESS, RUN SUCCESS fields, scalar RECORD (`B171912A`), graph RECORD (B0-B fixture), `has_more` variants, GQL FAILURE — Q1 probes H13–H21 show 0 differences.
- **IGNORED still encodes exactly `B0 7E`** (Q1 probe H13 unchanged); the historical one-field B0-B fixture (`B1 7E A0`) remains untouched and is not reproduced in production logic.
- No unrelated request-decoding behavior changed (0 non-C3/C13/C14 differences across all 85 probes).

## 9. Error consistency and confidentiality

- Every rejection observed in this requalification is a `BoltMessageException` with a documented
  existing `BoltMessageErrorCategory`; no arbitrary exception surfaced in any of the 17 Q2 cases or
  85 Q1 cases.
- Error messages reference field names and expected types only. Sentinel-bearing probes (Q2-S1,
  Q2-S2) confirm no field values appear in exception text even when a sentinel sits in `user_agent`,
  `platform` or `product`. No credential material was created or stored; no authentication semantics
  changed (the codec makes no auth decision).
- The remediation introduces no mutable references or allocations (a local presence check over the
  already-owned map entries).

## 10. Remaining qualifications

| Qualification | Status after Q2 |
| --- | --- |
| **F-01** (HELLO `bolt_agent` mandatory) | **CLOSED** — independently confirmed |
| **F-Q2-01** (new, bounded): explicit-null nested `product` → `MissingRequiredField` instead of `InvalidFieldType`; systemic null-as-absent convention for required strings (`user_agent`, `scheme`, `principal`, `credentials`), one level narrower than R1's agent-level handling | **Recorded** — optional one-file remediation available (extend the presence-scan distinction to nested required strings and pin with tests), or owner accepts the convention as-is |
| **F-02** (live-driver verification of zero-field IGNORED) | **Unchanged** — remains a B5-B gate; R1 did not touch it |
| PULL/DISCARD `n/qid < −1` rejection policy | **Unchanged** documented implementation policy |
| TELEMETRY value-domain validation | **Unchanged** — B3-B processing responsibility |
| Authentic HELLO/LOGON capture limitation / redaction boundary | **Unchanged** |
| Author-independence caveat | **Unchanged** |
| NC-03b, NC-03c, NC-05b, NC-08, zero-arity sequence boundary | **Unchanged** |

No new remediation requirement is raised beyond the optional F-Q2-01 clarification.

## 11. Formal Q2 disposition

**B3-A-Q2 — SUPPORTED WITH QUALIFICATIONS; F-01 CLOSED.**

F-01 is independently confirmed closed: HELLO without `bolt_agent` is rejected
(`MissingRequiredField`), present-but-null and non-map agents are rejected (`InvalidFieldType`), the
nested `product` mandatory/type rules are enforced, and all valid HELLO forms still decode. The
C3/C13/C14 historical differences are demonstrated to be correct expected remediation differences;
the unmodified Q1 probe reproduces the producer's evidence byte-for-byte; and no other message
contract, fixture, evidence file or manifest changed.

## 12. Owner recommendation

Recommend **bounded B3-A owner acceptance** (a B3-A-OWNER-01 decision) adopting the B3-A
implementation report, the Q1 qualification, the R1 remediation and this Q2 requalification, with:

1. **F-01 closed**;
2. **F-02 retained** (B5-B end-to-end IGNORED verification);
3. **F-Q2-01 retained** — owner to either accept the null-as-absent categorization convention in
   writing or authorize a minimal B3-A-R2 that extends the R1 presence-scan distinction to nested
   required strings and pins it with tests (owner's choice; not required for correctness);
4. the existing documented limitations (PULL/DISCARD policy, TELEMETRY boundary, capture/redaction
   limitation, author-independence caveat) retained;
5. the B0-B and B3-A isolation conventions (core solution free of `Neo4j.Driver`) preserved.

The reviewer does **not** record owner acceptance.

---

## Evidence inventory

`docs/reviews/evidence/B3-A-Q2/` (SHA-256 manifest: `manifest.json`): `core-solution-tests.log`,
`message-tests.log`, `integration-tests.log`, `core-build.log`, `environment.txt`,
`closure-probe/closure-results.json`, `closure-probe-run.log`, `q1-probe-rerun/` and its log.
Probe sources: `docs/reviews/probes/B3-A-Q2/ClosureProbe/` (also hashed). Q1 and R1 artifacts were
read, not modified.
