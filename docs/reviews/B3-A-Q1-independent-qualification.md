# B3-A-Q1 — Independent qualification of the Bolt 5.8 message contracts

**Task:** B3-A-Q1 — Independent qualification of B3-A-IMPL-01
**Producer record:** `docs/reviews/B3-A-IMPL-01.md`
**Reviewed `main` HEAD:** `fb81f8b386d46db2347f7b890847885d9815dcf5` / tree `88aba63c3e35e89dc030a1fa54967d941cd06031`
**Implementation commit under review:** `d99096a9689c4a7ce313c569cff2b165cfbda11a` (parent `d5189af…` = B0-B owner acceptance)
**Target revision:** Bolt 5.8 / PackStream 1.0
**Reviewer:** GLM-5.3-Flash (`nvidia`, OpenCode) — same executor family as the producer executor
and as the B0-B-Q1 reviewer; executor-level independence from the B3-A producer session is
recorded, organizational attestation is not available.
**Environment:** Windows 10.0.26200, win-x64, .NET SDK 10.0.401, MSBuild 18.9.11, net8.0
(`evidence/B3-A-Q1/environment.txt`).

## Disposition

> **B3-A-IMPL-01: SUPPORTED WITH QUALIFICATIONS.**
>
> The message codec conforms to the published Bolt 5.8 specification for every required request
> and response contract on signature, structure arity, field ordering, field types, metadata
> optionality, prohibited-field rejection and exact message consumption, as established by 85
> independent falsification probes over reviewer-owned byte vectors and by reproduced producer
> tests whose quality was audited. One conformance deviation is recorded and bounded: **the codec
> accepts a HELLO without the specification-mandatory `bolt_agent` field (F-01)**. The IGNORED
> fixture discrepancy is correctly isolated (F-02). No material blocker was found; one bounded,
> one-line-class remediation is recommended before or at B3-B integration (F-01), with an explicit
> owner-decision alternative. This is **not** owner acceptance.

---

## 1. Baseline and reviewed state

```text
git branch --show-current  -> main
git rev-parse HEAD         -> fb81f8b386d46db2347f7b890847885d9815dcf5 (matches the commission)
git rev-parse HEAD^{tree}  -> 88aba63c3e35e89dc030a1fa54967d941cd06031
git worktree list          -> one worktree (D:/projects/bolt-sharp)
git status --porcelain     -> clean at entry
Ancestry                   -> d5189af (B0-B acceptance) -> d99096a (B3-A implementation)
                              -> 2771052 (implementation report) -> fb81f8b (evidence normalization)
Prior B3-A-Q1 work         -> none existed (no reports, probes or evidence)
Nothing pushed             -> origin/main (9abf5e3) remains an ancestor of HEAD
```

## 2. Integrity verification

| Check | Result |
| --- | --- |
| B3-A evidence manifest | **21/21 entries match** (8 evidence files + 13 report-and-source files; SHA-256 + sizes) |
| B0-B producer manifest | **24/24** — original fixtures, reports and decisions unchanged |
| B0-B Q1 reviewer manifest | **119/119** — reviewer evidence and probes unchanged |
| B0-A-TRACE evidence and owner acceptance | 0 paths changed since `a4429ef` |
| Production changes since `d5189af` | restricted to the six new files under `src/BoltSharp/Messages/` |
| `src/BoltSharp/PackStream/`, `Framing/`, `Handshake/` | **unchanged** (empty diff); no PackStream behavioral change |
| Package/dependency changes | none (`.csproj`, `BoltSharp.slnx`, `Directory.Build.props` diff is empty); no `Neo4j.Driver` reference in production (only the `neo4j_code` field-name string and doc comments) |
| Evidence corruption | none found |

## 3. Reproduced test results

| Set | Command | Result |
| --- | --- | --- |
| Core solution | `dotnet test BoltSharp.slnx -c Debug` | **922 passed / 0 failed / 0 skipped** |
| B0-B integration | `dotnet test tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj -c Debug` | **19 passed / 0 failed / 0 skipped** |
| B3-A message tests | `dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj --filter FullyQualifiedName~BoltSharp.Tests.Messages` | **96 passed / 0 failed / 0 skipped** |
| Build | `dotnet build BoltSharp.slnx -c Debug` | 0 warnings / 0 errors |

**Producer claims: all reproduced.** (6 production files, 6 test files, 96/922/19 counts, 0-warning
build, no historical modifications.)

**Test-quality audit** (§3 of the commission):

- No broad exception assertions: every negative test targets `BoltMessageException` and asserts the
  exact `BoltMessageErrorCategory` (grep-verified; zero `Assert.Throws<Exception>`-style matches).
- No self-round-trip testing: no encode→decode or decode→encode pairs exist in the test suite
  (grep-verified). Expected bytes come from the B0-B-qualified fixtures, hand-written byte vectors,
  and a producer-test-local independent encoder.
- The 96 tests genuinely exercise the production codec: every test calls
  `BoltMessageCodec.DecodeRequest/DecodeResponse/EncodeResponse` directly; fixtures are data only.
- Malformed input is never silently accepted (documented boundaries only: the NC-05b two-value
  RECORD and the telemetry value domain — both deliberate, tested, and documented).
- Shared-defect risk between the producer's test encoder and the production writer is mitigated by
  the third oracle: B0-B-qualified bytes that the real driver accepted, and by this review's own
  freshly written encoder/decoder (§8).
- Expected-error tests cannot conceal different failures because the category is asserted exactly.

## 4. Supported-message conformance matrix

Verified against the preserved official specification copy
(`evidence/B3-A-IMPL-01/spec/message.adoc`), the reviewer's independent encoder/decoder, and the
B0-B-qualified bytes. "Probe" references are entries in
`evidence/B3-A-Q1/falsification-probe/falsification-results.json`.

| Message | Signature | Arity | Field order/types | Mandatory vs optional metadata | Verdict |
| --- | --- | --- | --- | --- | --- |
| HELLO | 0x01 ✓ | 1 ✓ | `extra` map ✓; `user_agent` required string ✓; routing/notification types validated ✓; nested `bolt_agent` shape validated when present ✓ | `user_agent` enforced ✓; **`bolt_agent` NOT enforced (F-01)**; null optionals accepted ✓; prohibited `scheme`/`patch_bolt`/legacy auth rejected ✓ (C10, C11) | **Conforms with F-01 deviation** |
| LOGON | 0x6A ✓ | 1 ✓ | `auth` map ✓; `scheme` required string ✓; basic `principal`+`credentials` ✓; bearer/kerberos `credentials` ✓ | unknown schemes preserved as `Unknown`, support deferred to B3-B ✓ (D6) | **Conforms** |
| LOGOFF | 0x6B ✓ | 0 ✓ | zero fields; extra field rejected ✓ (B-group arity, producer tests) | n/a | **Conforms** |
| TELEMETRY | 0x54 ✓ | 1 ✓ | one integer field ✓; non-integer rejected ✓ (G1) | value domain 0–3 deliberately **not** codec-enforced (G2) — spec's own example answers 9001 with FAILURE; intentional parse/processing boundary | **Conforms (documented boundary)** |
| RUN | 0x10 ✓ | 3 ✓ | query string ✓, parameters map ✓, extra map ✓; 5.8 known-field types validated ✓; nested parameters preserved ✓ (E6); legacy `notifications_disabled_categories` rejected ✓ (E5) | none of the extra fields mandatory (spec defaults) ✓ | **Conforms** |
| PULL | 0x3F ✓ | 1 ✓ | `n` required integer ✓, −1 supported ✓, type violations rejected ✓; optional `qid` ✓ | `n`/`qid` < −1 rejected — **documented implementation policy**, not a quoted spec requirement (F3/F4, §7) | **Conforms (policy recorded)** |
| DISCARD | 0x2F ✓ | 1 ✓ | same shape as PULL ✓; authentic `DISCARD {n:-1}` capture decodes ✓ (F8) | same policy as PULL | **Conforms (policy recorded)** |
| RESET | 0x0F ✓ | 0 ✓ | zero fields; extra field rejected ✓; no side effects ✓ | n/a | **Conforms** |
| GOODBYE | 0x02 ✓ | 0 ✓ | zero fields ✓; no side effects ✓ | n/a | **Conforms** |
| SUCCESS | 0x70 ✓ | 1 ✓ | metadata map ✓; known 5.8 keys type-validated ✓ (H5, H6); `patch_bolt` prohibited ✓ (H7) | **no field mandatory** — generic valid metadata (incl. empty) encodable ✓ (H15), preserving B0-B NC-03c (connection_id optional) and the schema/driver-compat separation | **Conforms** |
| RECORD | 0x71 ✓ | 1 ✓ | one list ✓; typed graph structures preserved ✓ (H19); outer arity enforced ✓ (H2); non-list field rejected ✓ (H1) | column-count matching deliberately deferred to B3-B/B3-C (NC-05b) ✓ (H3) | **Conforms** |
| FAILURE | 0x7F ✓ | 1 ✓ | 5.7+ GQL fields type-validated ✓; recursive bounded `cause` ✓ (H10, H11); pre-5.7 `code` prohibited ✓ (H8); non-map cause rejected ✓ (H9) | optional fields correctly optional; missing optional field never treated as success ✓ | **Conforms** |
| IGNORED | 0x7E ✓ | 0 ✓ | encoded exactly `B0 7E` ✓ (H13); one-field form rejected ✓; see §6 | none permitted ✓ | **Conforms** |
| BEGIN / COMMIT / ROLLBACK / ROUTE | 0x11/0x12/0x13/0x66 | — | known for 5.8, unsupported | always classified `UnsupportedMessage`, never silently accepted ✓ (A4-BEGIN/COMMIT/ROLLBACK/ROUTE) | **Conforms (intentionally unsupported)** |
| Unknown tags / graph tags / non-structures / empty / truncated / trailing | — | — | — | `UnknownSignature` / `NotAStructure` (typed graph value nuance, A6) / `EmptyPayload` / `Truncated` / `TrailingBytes` ✓ (A5–A8, B2–B4) | **Conforms** |

Exact message consumption: the codec rejects trailing bytes for request and response paths
(B4a/B4b), and the independent decoder consumes the codec's complete encoded output for every
encode probe (H13/H19 confirm `consumed == length`).

## 5. HELLO `bolt_agent` findings (F-01)

The preserved specification states, for HELLO (5.3+): *"`bolt_agent::Dictionary` … **This field is
mandatory.**"* and *"***`product::String`** … **This field is mandatory.**"* (evidence
`B3-A-IMPL-01/spec/message.adoc`). Independently probed behavior:

| Case | Probe | Spec requirement | Observed | Verdict |
| --- | --- | --- | --- | --- |
| HELLO without `bolt_agent` | C3 | mandatory → reject | **accepted** | **DEVIATION (F-01)** |
| `bolt_agent` present as non-map | C-group (producer test) + probe suite | Dictionary | rejected `InvalidFieldType` | conforms |
| empty `bolt_agent` map | C4 | `product` mandatory → reject | rejected `MissingRequiredField` | conforms |
| `bolt_agent` without `product` | C5 | reject | rejected `MissingRequiredField` | conforms |
| `product` wrong type | C6 | string | rejected `InvalidFieldType` | conforms |
| optional nested field wrong type (`platform: 2`) | C7 | string or null | rejected `InvalidFieldType` | conforms |
| Test-suite detection | — | — | the nested-shape violations are detected by producer tests (`HelloBoltAgent*`); the *absence* of `bolt_agent` is detected by **no** test — it is documented behavior, not an oversight | — |

**Classification.** The implementation does not enforce the published mandatory `bolt_agent`
requirement: a HELLO that the 5.8 schema requires to carry `bolt_agent` is accepted as
schema-valid. The producer disclosed this as a deliberate less-strict reading of the commission
text ("bolt_agent, where present") and flagged it for owner clarification — the behavior is
therefore **not contrary to the producer's claimed contract**, but it **is** a deviation from the
published specification, recorded here as conformance finding **F-01**.

**Bounding.** The qualified driver 6.4.0 always sends `bolt_agent` (B0-B evidence records HELLO map
keys `[user_agent, routing, bolt_agent]`), so no executed B0-B observation is affected; the codec
is server-side, and no B3-B behavior depends on accepting bolt_agent-less HELLOs. No
version-specific exception applies (the field exists from 5.3; the codec targets 5.8 only). The
reviewer did **not** reinterpret the mandatory requirement as optional: the finding stands as a
conformance gap.

**Recommendation.** Remediation is a bounded, low-risk change (require `bolt_agent` + `product`
during HELLO validation, plus two negative tests). Recommended paths, owner's choice:

1. **Preferred:** a bounded **B3-A-R1** remediation cycle enforcing `bolt_agent` (+`product`)
   before B3-B begins, restoring exact schema conformance; or
2. an explicit owner-accepted deviation record keeping the less-strict codec, with B3-B
   documentation that BoltSharp's HELLO handling deviates from the published schema on this field.

## 6. IGNORED discrepancy resolution (F-02) — verified

1. **Encoding is exactly `B0 7E`:** probe H13 encodes `IgnoredResponse.Instance` and independently
   decodes the output — tag `0x7E`, zero fields, 2 bytes, fully consumed. Byte-equal to
   `B0 7E`.
2. **Zero-field decode** works (`Ignored_SpecZeroFieldForm_Decodes`).
3. **Incorrect arity rejected:** the one-field form `B1 7E A0` is rejected with
   `StructureArity` (producer test, pinned by this review's H14 independent decode of the
   historical fixture showing tag 0x7E with 1 field).
4. **The historical B0-B fixture is unaltered:** B0-B producer manifest still verifies 24/24; the
   byte string exists in B3-A tests only as `B0B.ProducerIgnoredOneField`, documented as *not* a
   golden fixture.
5. **The new tests explicitly distinguish the representations** (zero-field positive test vs
   one-field rejection test with the discrepancy documented in code comments and the report).
6. **No production logic reproduces the invalid fixture:** `grep` of `src/BoltSharp/Messages/`
   shows no special case for the one-field form; the rejection is the generic arity check.
7. **Discrepancy isolation:** the driver-tolerance mechanism (deserializer reads no IGNORED fields;
   pipelined reader discards leftovers — upstream `IgnoredMessageSerializer.cs` /
   `PipelinedMessageReader.cs` preserved in B3-A evidence) is consistent with the reviewer's
   independent reading. Live-driver verification of the corrected `B0 7E` response remains B5-B's
   gate, as the producer recorded; mechanistically expected to pass (the driver reads nothing for
   IGNORED).

## 7. Metadata and field-validation findings

- **PULL/DISCARD `n`/`qid` < −1:** the specification defines `n = −1` ("fetch all") and `qid = −1`
  ("last executed statement") but does **not** state behavior for smaller values. The codec's
  rejection is therefore classified as a **documented implementation policy** (reasonable and
  consistent with the value domains' meaning), not a protocol requirement and not an unverified
  assumption — the policy is asserted by tests and recorded in the producer report §10 and here
  (probes F3–F5, CONFIRMED).
- **TELEMETRY:** the schema expects one integer (verified); the official API identifiers are 0–3;
  the codec accepts any integer at the parsing layer, which the specification's own example
  (`TELEMETRY 9001` → FAILURE) supports as the correct parse/processing boundary (G2). No session
  behavior is required from B3-A — confirmed absent.
- **FAILURE:** 5.7+ fields verified (H8–H11); malformed nested causes and 150-level cause chains
  are rejected (`InvalidFieldType` / `InvalidPackStreamValue`, bounded by the configured PackStream
  depth); obsolete `code` rejected; valid-but-optional missing fields never treated as success.
- **HELLO prohibited metadata:** legacy `scheme`, `credentials`, `principal` and retired
  `patch_bolt` rejected (`ProhibitedField`); retired notification key
  `notifications_disabled_categories` rejected on RUN (E5).
- **Unknown metadata** is preserved verbatim (no invented semantics; no capability grants), and
  unknown signatures are always rejected — never treated as no-ops.
- **No canonical map ordering** is claimed; the caller's order is preserved (producer test verified
  against hand-written bytes).

## 8. Independent falsification results

85 probes (`docs/reviews/probes/B3-A-Q1/FalsificationProbe/`, results in
`evidence/B3-A-Q1/falsification-probe/falsification-results.json`): **77 CONFORMANT, 7 CONFIRMED
(documented boundaries/policies), 1 DEVIATION (F-01), 0 probe mismatches**. The probe carries its
own independent PackStream encoder **and** generic decoder written from the specification
(destroying any shared-defect channel between the production codec and the producer's test
fixtures); fixed B0-B byte vectors provide the third oracle. All 15 commission-required probe
classes are covered (A1–A8, B1–B6, C1–C14, D1–D7, E1–E6, F1–F8, G1–G2, H1–H21, I1–I5, J1–J3),
each recording exact input, specification expectation, observed behavior and the exact error
category. Two initial probe mismatches were traced to transcription errors in the reviewer's own
fixtures (extra byte → malformed UTF-8; miscounted truncation) — the codec's rejections were
correct for the actual inputs; the probes were corrected and re-run clean, and both corrected
cases now assert the intended categories.

## 9. Authentication confidentiality results

Using synthetic sentinel credentials (`SENTINEL-PRINCIPAL-7Q4X`,
`SENTINEL-SECRET-9K2-DO-NOT-LEAK`):

| Probe | Scenario | Result |
| --- | --- | --- |
| I1 | valid basic LOGON → `LogonRequest.ToString()` | scheme only; **no sentinel** |
| I2 | malformed LOGON (wrong-typed credentials) → exception `Message` + full `ToString()` (stack included) | **no sentinel** |
| I3 | 100,000-character sentinel credential failing on `principal` type | **no sentinel**; message stays name/type-only |
| I4 | mutable-cast attempt on `LogonRequest.Auth.Entries` | `InvalidCastException` — read-only wrapper holds; B1-R1 F1 immutability intact |
| I5 | secret-bearing LOGON + trailing byte → `TrailingBytes` rejection | **no sentinel** in diagnostics |

The sentinel values do not appear anywhere in the recorded evidence file (grep-verified: only the
words "sentinel"/"sentinels" and the recorded booleans appear). Producer tests use synthetic
`user`/`password` values only. No credential material was stored in reviewer evidence.

## 10. Graph-value validation

Probe H19 encodes the typed Alice/KNOWS/Bob RECORD through the production codec and verifies the
output with the reviewer's independent decoder: RECORD tag `0x71` with one list of three; Node
`0x4E` four fields in specification order (id, labels, properties, element_id);
Relationship `0x52` eight fields in specification order (id, start, end, type, properties,
element_id, start_node_element_id, end_node_element_id); exact values (`n:1`, `Person`, Alice,
`r:1`, `KNOWS`, since=2024, `n:1`→`n:2`, Bob, `n:2`); numeric id 1 on both node and relationship
remaining distinct typed structures; full consumption. The encoded bytes are byte-identical to the
B0-B fixture that the real driver reconstructed and that B0-B-Q1 independently decoded (23/23).
Outer RECORD arity (B3-A), column-count matching (B3-B/B3-C) and response sequencing (B3-B) remain
correctly separated (H2 vs H3).

## 11. Architectural-boundary assessment

- No transport ownership, session state, authentication decisions, query execution, cursor
  lifecycle or response sequencing exists in `src/BoltSharp/Messages/` (grep-audited; the only
  "Stream"/"Session" matches are doc comments and the `PackStream` namespace).
- No `Neo4j.Driver` dependency in production; no new package references.
- Public surface is minimal and B3-B-ready: codec + 13 typed messages + error model + signature
  table; helpers are `internal`. Message representations are immutable and rest on the qualified
  B1 defensive-ownership contract (probe I4).
- Error categories are deterministic and exhaustive for the exercised space; the
  `NotAStructure`-vs-`UnknownSignature` nuance for typed graph values is documented and pinned
  (A6).
- B3-B can consume the contracts without representation changes; the only coupling is the
  intentional one on PackStream value types.
- Zero-arity signature swaps (RESET/GOODBYE/LOGOFF) are indistinguishable to a stateless codec —
  inherent, documented (A3), and correctly a B3-B state-machine concern.

## 12. Remaining qualifications

1. **F-01 (binding):** HELLO `bolt_agent` mandatory-field enforcement is absent (§5). Bounded;
   remediation recommended (B3-A-R1) or owner-accepted deviation required.
2. **F-02 (retained):** driver acceptance of the spec-conformant zero-field IGNORED is established
   mechanistically from preserved upstream sources but not yet executed end-to-end — **B5-B gate**.
3. PULL/DISCARD `n`/`qid` < −1 rejection is an implementation policy, recorded (§7).
4. TELEMETRY value domain and all stateful behaviors are B3-B responsibilities.
5. No authentic HELLO/LOGON request captures exist in committed evidence (redacted by design in
   B0-B); HELLO/LOGON contracts rest on the specification, the documented driver key set, and the
   spec-derived fixtures.
6. Reviewer/producer author independence: different sessions, same executor family; no
   organizational attestation. This must not be described as verified independent signoff in
   external statements.

## 13–15. Formal disposition, remediation and owner recommendation

**Disposition: B3-A-IMPL-01 — SUPPORTED WITH QUALIFICATIONS.**

The claimed contracts conform to the published Bolt 5.8 specification on every required message
except the single bounded deviation F-01; independent byte-level evidence supports the
implementation from three mutually independent oracles; historical evidence, tests and protocol
components are untouched; and the documented boundaries (NC-03b/c, NC-05b, NC-08, Q1-F03, F-02)
are all preserved exactly as accepted.

**Recommended remediation (optional path 1, preferred):** authorize a bounded B3-A-R1 to enforce
the spec-mandatory HELLO `bolt_agent` (+ nested `product`) and add the two missing negative tests.
**Alternative (path 2):** the owner explicitly accepts the deviation in writing and directs B3-B to
document it. Either way, **B5-B remains the end-to-end interoperability gate** for the corrected
IGNORED form and all response bytes against the live driver.

**Owner recommendation:** accept this qualification, decide path 1 vs path 2 for F-01, and issue
the B3-B commission (state machine) referencing this report, the producer report and the binding
qualifications above. No owner acceptance is recorded by this review.

---

## Evidence inventory

`docs/reviews/evidence/B3-A-Q1/` (SHA-256 manifest: `manifest.json`): `core-solution-tests.log`,
`integration-tests.log`, `message-tests.log`, `core-build.log`, `environment.txt`,
`falsification-probe/falsification-results.json`, `falsification-probe-run.log`. Probe sources:
`docs/reviews/probes/B3-A-Q1/FalsificationProbe/` (also hashed). The producer's report and evidence
are preserved unaltered.
