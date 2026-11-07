# B3-A-R1-IMPL-01 — Mandatory HELLO `bolt_agent` enforcement (F-01 remediation)

**Task:** B3-A-R1-IMPL-01 — bounded remediation of B3-A-Q1 finding F-01 only
**Baseline (entry) HEAD:** `6974a56a6c4fc9b706a29ac65fe029840834893c` / tree `a45a8f1ec8d241ee18752c3d1ff94a9117ecb211`
**Remediation commit:** `d6686d14f3eaee42f83971d517d9077170a30fe2` / tree `f615af7feff41c2f0cc2e0debd06d4d928daa32d`
**Target revision:** Bolt 5.8 / PackStream 1.0
**Producer executor:** GLM-5.3-Flash (`nvidia`, OpenCode). This record is **not** independent
qualification and **not** owner acceptance; B3-A-Q2 must independently verify.

---

## 1. Original finding (B3-A-Q1 F-01)

The published Bolt 5.8 specification marks the HELLO `extra.bolt_agent` field mandatory from
Bolt 5.3 (*"`bolt_agent::Dictionary` … **This field is mandatory.**"*) with a mandatory nested
`product` string. The B3-A codec validated the agent's shape **only when present** and therefore
accepted a HELLO that omitted `bolt_agent` — a schema deviation recorded by the independent
qualification as finding F-01 (probe C3, verdict DEVIATION), bounded because the qualified
`Neo4j.Driver` 6.4.0 always sends the field (B0-B evidence: HELLO map keys
`[user_agent, routing, bolt_agent]`).

## 2. Baseline verification

```text
git branch --show-current  -> main
git rev-parse HEAD         -> 6974a56a6c4fc9b706a29ac65fe029840834893c (matches the commission)
git rev-parse HEAD^{tree}  -> a45a8f1ec8d241ee18752c3d1ff94a9117ecb211
git worktree list          -> one worktree (D:/projects/bolt-sharp)
git status --porcelain     -> clean at entry
Prior B3-A-R1 work         -> none existed
B0-B owner acceptance      -> present and intact (docs/decisions/B0-B-OWNER-01-owner-acceptance.md)
Q1 artifacts               -> present (report, evidence manifest 14/14, probe sources)
```

## 3. Root cause

`BoltMessageCodec.ValidateHelloExtra` guarded the entire agent validation block behind
`if (BoltMessageFields.GetValue(extra, "bolt_agent") is { } agent)` — an optional-presence guard.
The nested rules (map type, mandatory `product`, optional string entries) were correct; only the
presence requirement was missing. The empty-map and missing-`product` cases were already rejected
because `RequireStringField(agentMap, "product", …)` ran inside the guard.

## 4. Exact production correction

`src/BoltSharp/Messages/BoltMessageCodec.cs`, `ValidateHelloExtra` — the agent block now:

1. determines key presence directly from the map entries (`bolt_agent` absent →
   `MissingRequiredField`, message references the field name only);
2. rejects a present-but-null or non-map value (`InvalidFieldType`, "must be a map");
3. retains the existing nested validation unchanged (`product` required string →
   `MissingRequiredField`/`InvalidFieldType`; `platform`/`language`/`language_details` optional
   string-or-null).

No new error category was needed; the existing `BoltMessageErrorCategory` values represent the
failures exactly. No other validation, message, PackStream, framing or handshake code was touched.
`HelloRequest`'s `BoltAgent` XML documentation was updated to state the mandatory requirement — a
comment-only change.

**Not authorized and not done:** general metadata handling, other message schemas, session
behavior, authentication, transports, public API changes, historical reports/evidence, the Q1
review or its probe.

## 5. Changed source and test files

| File | Change |
| --- | --- |
| `src/BoltSharp/Messages/BoltMessageCodec.cs` | the F-01 correction (§4) |
| `src/BoltSharp/Messages/BoltRequests.cs` | `HelloRequest.BoltAgent` doc comment updated |
| `tests/BoltSharp.Tests/Messages/BoltMessageFixtures.cs` | `Spec5_8.HelloMinimal` and `Spec5_8.HelloNullOptional` updated to carry the now-mandatory `bolt_agent` (their previous shape encoded the schema-invalid form); new negative fixtures `Negative.HelloMissingBoltAgent` and `Negative.HelloNullBoltAgent` |
| `tests/BoltSharp.Tests/Messages/BoltMessageNegativeTests.cs` | two new exact-category tests: `Hello_MissingBoltAgent_IsRejected` (MissingRequiredField) and `Hello_NullBoltAgent_IsRejected` (InvalidFieldType) |
| `tests/BoltSharp.Tests/Messages/BoltRequestDecodeTests.cs` | the two positive tests whose fixtures encoded the pre-fix invalid form updated (assertions strengthened: `bolt_agent`/`product` now asserted); no assertion weakened |

**Reuse over duplication:** the empty-map, missing-`product`, wrong-typed-`product`,
wrong-typed-nested-field, valid-agent, full-metadata and unrelated-malformed-metadata cases were
already covered by qualified tests (`Hello_BoltAgentShapeViolations_AreRejected`,
`HelloAgent`, `HelloFull`, `Hello_ClassificationsWrongItemType`, `Hello_RoutingWrongValueType`)
and were reused unchanged; only the absent-`bolt_agent` and explicit-null cases required new
coverage, plus the two positive-fixture corrections. Historical B0-B tests and evidence were not
touched.

## 6. Original defect reproduction and corrected outcome

The **unmodified** B3-A-Q1 falsification probe was executed before and after the correction
(rebuilt against the changed production assembly), writing to fresh R1 evidence directories so the
original Q1 evidence remains untouched:

| Run | Command | Result |
| --- | --- | --- |
| Before fix | `dotnet run --project docs/reviews/probes/B3-A-Q1/FalsificationProbe -c Release --no-build -- docs/reviews/evidence/B3-A-R1-IMPL-01/q1-probe-before-fix` | 85 probes, **0 mismatches**, C3 = `decoded to HelloRequest` (DEVIATION) — the defect reproduced exactly as Q1 recorded it |
| After fix | same probe, rebuilt, output to `…/q1-probe-after-fix` | 85 probes, **3 mismatches** — C3, C13 and C14 now **rejected with `MissingRequiredField`**; all other 82 outcomes unchanged (75 CONFORMANT + 7 CONFIRMED) |

The three post-fix mismatches are stale *pre-fix predictions* inside the historical probe, not
regressions: each of those probe inputs omits the now-mandatory `bolt_agent`
(C3: `{user_agent}` only; C13: the spec-example field set without the agent; C14: null optionals
without the agent), and each is now rejected with exactly the expected deterministic category.
The probe exits 4, which is the intended closure signal for a historical probe reused across a
behavior change. The before/after pair establishes that the previously accepted invalid HELLO is
now rejected, with no other behavioral change.

## 7. Negative and positive regression results

| Commission scenario | Coverage | Result |
| --- | --- | --- |
| HELLO missing `bolt_agent` | **new** `Hello_MissingBoltAgent_IsRejected` + post-fix probe C3 | rejected, `MissingRequiredField` |
| HELLO with empty `bolt_agent` map | existing `Hello_BoltAgentShapeViolations_AreRejected` (reused) | rejected, `MissingRequiredField` |
| HELLO with `bolt_agent` but missing `product` | existing (reused) | rejected, `MissingRequiredField` |
| HELLO with non-string `product` | existing (reused) | rejected, `InvalidFieldType` |
| HELLO with valid `bolt_agent.product` | existing `HelloAgent` (reused, assertions unchanged) | accepted |
| HELLO with valid additional optional metadata | existing `Hello_Full_…` (reused) + updated `Hello_Minimal_…`/`Hello_ExplicitNullOptionals_…` | accepted |
| HELLO with malformed unrelated metadata | existing routing/classifications/user-agent tests (reused, unchanged outcomes) | rejected with unchanged categories |
| explicit-null `bolt_agent` | **new** `Hello_NullBoltAgent_IsRejected` | rejected, `InvalidFieldType` |

Message tests: **98 passed / 0 failed** (96 previous + 2 new). All exact-category assertions.

## 8. Independent fixture comparison results

Re-verified after the correction (via the post-fix probe run and the test suite):

- Valid HELLO requests (full metadata, null optionals, agent variants) continue to decode.
- The six authentic `Neo4j.Driver` 6.4.0 request captures remain valid and unchanged:
  `RUN("RETURN 42 AS answer", {}, {mode:"r"})`, `RUN("RETURN n", {}, {})`, `PULL{n:1000}`,
  `PULL{n:1}`, `DISCARD{n:-1}`, `RESET`, `GOODBYE` (probe F7/F8 CONFORMANT; producer
  `B0BFixtureComparisonTests` pass).
- **No response encoding changed:** SUCCESS empty (`B170A0`), HELLO SUCCESS, RUN SUCCESS fields,
  scalar RECORD (`B171912A`), graph RECORD (byte-identical to the B0-B fixture with independent
  layout decode), `has_more:false`, GQL FAILURE and IGNORED all remain CONFORMANT (probes
  H13–H21).
- The accepted zero-field IGNORED encoding remains exactly **`B0 7E`** (probe H13; the historical
  one-field B0-B fixture remains unaltered and un-reproduced in production logic).
- F-02 is untouched: no live-driver verification was attempted, per scope.

## 9. Complete regression test counts

| Set | Command | Result |
| --- | --- | --- |
| Core solution | `dotnet test BoltSharp.slnx -c Debug` | **924 passed / 0 failed / 0 skipped** (922 + 2 new) |
| B3-A message tests | `dotnet test … --filter FullyQualifiedName~BoltSharp.Tests.Messages` | **98 passed / 0 failed** |
| B0-B integration | `dotnet test tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj -c Debug` | **19 passed / 0 failed / 0 skipped** |
| Build | `dotnet build BoltSharp.slnx -c Debug` | 0 warnings / 0 errors |

Logs under `evidence/B3-A-R1-IMPL-01/baseline/`. The core count increased only by the two genuine
new tests; no historical test was weakened (the two updated positive tests assert *more* than
before, and their fixture corrections are documented in §5).

## 10. Historical evidence preservation

- B0-B producer manifest: 24/24 (unchanged). B0-B Q1 manifest: 119/119 (unchanged).
- B3-A-Q1 evidence manifest: 14/14 (unchanged); the Q1 probe source and its original evidence were
  not modified — both post-fix runs wrote to fresh R1 directories.
- `git diff 6974a56..d6686d1` touches exactly the five files in §5; no reports, decisions,
  evidence, manifests or other message contracts were modified.

## 11. Remaining qualifications

All B3-A-Q1 qualifications other than F-01 are preserved unchanged: **F-02** (live-driver
verification of the spec-conformant zero-field IGNORED remains deferred to B5-B), the PULL/DISCARD
`n/qid < −1` documented policy, the TELEMETRY parse/processing boundary, the missing authentic
HELLO/LOGON captures limitation, and the author-independence caveat. The zero-arity
signature-swap boundary (A3) and the NC-03b/c, NC-05b, NC-08 boundaries are likewise unchanged.

## 12. Implementation commit and tree

```text
Remediation commit: d6686d14f3eaee42f83971d517d9077170a30fe2
Remediation tree:   f615af7feff41c2f0cc2e0debd06d4d928daa32d
Parent:             6974a56a6c4fc9b706a29ac65fe029840834893c
Changed paths:      5 (2 production, 3 test files); +69/−20.
```

The remediation report and evidence are recorded in a follow-up documentation commit (hashes in
the R1 handoff and evidence manifest).

## 13. Handoff instructions for independent B3-A-Q2

Q2 should be a **focused** requalification:

1. Confirm F-01 closure: HELLO without `bolt_agent` is rejected (`MissingRequiredField`), present-
   but-null rejected (`InvalidFieldType`), and the empty-map / missing-`product` / wrong-typed
   cases remain rejected — e.g. by re-running the unmodified Q1 probe (expect exactly the C3/C13/
   C14 stale-prediction mismatches with `MissingRequiredField`, all else unchanged) or equivalent
   independent vectors.
2. Confirm valid HELLO forms (agent present, full metadata, null optionals) still decode, and that
   the six authentic driver captures and all response encodings (including `B0 7E` IGNORED) are
   byte-unchanged.
3. Confirm test counts (924/98/19), the 0-warning build, and that `git diff 6974a56..d6686d1`
   touches only the five authorized files.
4. Confirm F-02 and the remaining qualifications are preserved and that no historical evidence,
   report or accepted fixture was modified.

Q2 must not perform owner acceptance; the producer of this remediation must not perform Q2.
