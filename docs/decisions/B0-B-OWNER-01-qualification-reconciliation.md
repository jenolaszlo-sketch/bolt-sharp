# B0-B-OWNER-01 — reconciliation of B0-B-Q1 qualification findings

**Status:** OWNER-RECORDED DOCUMENTATION RECONCILIATION (2026-11-07; execution timestamps are UTC)
**Task:** B0-B-OWNER-01 — documentation reconciliation and bounded owner acceptance
**Entry HEAD:** `37cc989bbae2273a639cd04dc8cf7a95799472cd` / tree `3702fce8b6f126ff4d6cd49891a0747fd87ae415` (`main`, one worktree, clean)
**Reconciled milestone:** B0-B — Official Neo4j Driver Integration Harness and Protocol Discovery
**Driver pin:** `Neo4j.Driver` **6.4.0** exact (assembly `6.4.12.0`, informational
`6.4.0+4ed413b01906c5d1682c62a3ee3c982cb28f99cd`)

This document is the **authoritative correction** to the B0-B gap-matrix conclusions
(`docs/reviews/B0-B-DRV-INT-01-gap-matrix.md`) for the findings below. It is additive: the producer
implementation record, the gap matrix, the [B0-B-Q1 independent qualification
report](../reviews/B0-B-Q1-independent-qualification.md), all producer evidence, all Q1 reviewer
evidence, and all Q1 reviewer probes are **preserved unchanged**. Readers must interpret the gap
matrix **through** this reconciliation; the original combined mandatory-field claim in gap-matrix
fact 1 is not retained as an accepted fact.

Every statement here is grounded in committed, hash-verified evidence. Nothing was regenerated,
rewritten, or retroactively strengthened.

---

## 1. D01 — HELLO SUCCESS metadata (correction)

**Original producer claim** (gap-matrix fact 1 and the HELLO row's "required next step"):
"`SUCCESS` for `HELLO` must carry `server` **and** `connection_id`; without them the driver throws a
`NullReferenceException`."

**Corrected conclusion (accepted):**

| HELLO SUCCESS metadata | Observed outcome (driver 6.4.0) |
| --- | --- |
| `{server, connection_id, hints}` (producer fixture) | connection succeeds; query returns 42 |
| `{server, connection_id}` (no hints) | connection succeeds |
| `{server}` only | **connection succeeds** |
| `{connection_id}` only | `System.NullReferenceException` |
| `{hints}` only | `System.NullReferenceException` |
| `{}` (empty) | `System.NullReferenceException` |

- **`server` is required** by the observed Neo4j.Driver 6.4.0 behavior; omitting it produces a
  `NullReferenceException` rather than a clean protocol error.
- **`connection_id` is optional** in the qualified observations.
- **`hints` is optional** in the qualified observations.

**Driver-specific vs protocol:** this requirement is a property of the pinned **driver version**
(6.4.0), established by execution against that driver. It is **not** established as a Bolt
protocol requirement: the Bolt 5.8 specification does not make these fields mandatory, other driver
versions may differ, and real Neo4j servers always send them. B3-A/B3-B implementations should
treat `server`, `connection_id` and `hints` as *send-them-all* fields for driver compatibility while
not asserting a protocol-level requirement.

**Evidence:**

- `docs/reviews/evidence/B0-B-Q1/fragmentation/hello-{full,empty,server-only,connection-id-only,no-hints,hints-only}.json`
  (independent minimal-TCP-responder probe, no BoltSharp code);
- `docs/reviews/evidence/B0-B-Q1/fragmentation-repeat/hello-{server-only,connection-id-only,empty}-run{1..5}.json`
  (5 fresh-process repeats per critical case, all consistent);
- negative control `NC-03c-missing-hello-connection-id` in
  `docs/reviews/evidence/B0-B-Q1/negative-controls/summary.json` (the mutation passes the copied
  producer assertions, demonstrating the producer claim was over-broad).

---

## 2. D04 — authentication failure classification (correction)

**Original producer claim** (gap-matrix fact 4): "Auth failure **requires** `neo4j_code`. A
`FAILURE` on `LOGON` carrying `neo4j_code = Neo.ClientError.Security.Unauthorized` is surfaced as an
authentication `Neo4jException`."

**Corrected interpretation (accepted):** classification is **driven by** `neo4j_code`; it is not a
precondition for the driver to recognize a protocol failure.

| LOGON FAILURE metadata | Surfaced exception | `Code` | `Message` |
| --- | --- | --- | --- |
| new-style with `neo4j_code=Neo.ClientError.Security.Unauthorized` (+ `gql_status`/`description`/`diagnostic_record`) | `Neo4j.Driver.AuthenticationException` | `Neo.ClientError.Security.Unauthorized` | generic `"An unknown error occurred"`; the description surfaces in `GqlStatusDescription` |
| new-style **without** `neo4j_code` | `Neo4j.Driver.DatabaseException` | `Neo.DatabaseError.General.UnknownError` | generic; description in `GqlStatusDescription` |
| legacy `{code, message}` (v1–5.6 style) | `Neo4j.Driver.AuthenticationException` | `Neo.ClientError.Security.Unauthorized` | the actual message text |

- `neo4j_code` **influences** whether the driver produces `AuthenticationException` or
  `DatabaseException`.
- **Without** `neo4j_code` the driver still produces a `Neo4jException` — the failure is never
  silently swallowed. The producer's underlying test assertion (`Neo4jException`) holds in every
  exercised variant; only the matrix's "requires" wording was imprecise.
- Under the exercised **new-style** metadata, the description appears in `GqlStatusDescription`,
  **not necessarily** in `Message`. Legacy `{code, message}` metadata does place the message text
  in `Message`.

**Version boundary:** all rows are observed on `Neo4j.Driver` 6.4.0 only. Classification behavior
may differ in other driver versions, and these observations do not characterize real Neo4j server
failure payloads beyond the eight scripted variants exercised.

**Evidence:** `docs/reviews/evidence/B0-B-Q1/fragmentation/logon-failure-{full,no-code,no-gql,empty,old-code,message-only,code-gql-no-description,neo4j-code-only}.json`.

---

## 3. T07 — `t07-handshake-fragmentation.json` evidence fidelity (limitation)

`docs/reviews/evidence/B0-B-DRV-INT-01/t07-handshake-fragmentation.json` (and its byte-identical Q1
reproduction) records the client handshake bytes, the server handshake bytes and the decoded
proposals, but its `requests`, `frames` and `notes` arrays are **empty**.

Q1 established the mechanism: the test writes this evidence only **after swallowing a 20-second
`WaitForCompletionAsync` timeout** (the scripted server never receives `HELLO` because the driver
fails the fragmented handshake first), and the server sets `negotiated_version: "5.8"` before the
client has accepted it. It is recorded explicitly that:

- The empty arrays are **not affirmative protocol evidence**. They are the absence of a capture,
  caused by the test's evidence-writing order, not an observation that nothing was exchanged.
- This producer artifact **must not be cited as a successful wire capture** of a fragmented
  handshake exchange, and must not be cited as characterizing the driver's behavior on its own.
- The applicable reproducible evidence for handshake fragmentation is the independent Q1 probe set:
  `docs/reviews/evidence/B0-B-Q1/fragmentation/hs-*.json` (35-case catalog),
  `fragmentation-repeat/` (8 critical cases × 5 fresh-process repeats = 40 runs), and the upstream
  source excerpt `docs/reviews/evidence/B0-B-Q1/driver-source/BoltHandshaker.cs` (§5).
- The original artifact **remains preserved without alteration**, byte-identical to its
  committed state, including its misleading `negotiated_version` field.

No missing captures were fabricated and no producer evidence was retroactively strengthened.

---

## 4. Remaining supported Q1 findings (retained)

The following Q1 findings are supported by committed independent evidence and are retained as
accepted observations, each with its client-version and test-scope limits:

1. **Pipelined `PULL` after `FAILURE` requires correct failed-state handling** — including
   answering with `IGNORED` until a legal `RESET` recovery — reproduced from the producer T06
   evidence and confirmed by negative control `NC-04-missing-ignored` (answering `PULL` with
   `SUCCESS` after a `FAILURE` is detected by the copied producer assertions). Scope: driver 6.4.0
   against scripted Bolt 5.8 responses; `IGNORED` is a Bolt server-state requirement, not a
   driver-specific one, but only this driver version was exercised.
2. **`DISCARD` was observed during both partial-result consumption (`ConsumeAsync`) and early
   session disposal** — producer `t05-discard.json` and `t05-early-disposal.json` carry identical
   request sequences (RUN, PULL, DISCARD, RESET, GOODBYE) and reproduce byte-identically. Scope:
   driver 6.4.0, fetch size 1, scripted `has_more` pagination.
3. **The Bolt 5.8 / PackStream 1.0 response fixtures were independently checked** against the
   published specifications (handshake response bytes, tiny-struct markers, message signatures,
   chunk framing, Node 4-field / Relationship 8-field layouts, 5.7+ FAILURE metadata fields,
   FAILED/IGNORED/RESET semantics). No discrepancy was found (Q1 report §4).
4. **Typed graph reconstruction was verified against independently decoded bytes** — the exact
   RECORD hex from the producer's T04 evidence decodes (standalone PackStream decoder, 23/23
   checks) to Node(id=1, `n:1`, [Person], Alice), Relationship(id=1, start=1, end=2, KNOWS, `r:1`,
   `n:1`→`n:2`, since=2024), Node(id=2, `n:2`, [Person], Bob), matching the fixtures the real
   driver reconstructed (`docs/reviews/evidence/B0-B-Q1/graph-independent-decode.json`).
5. **The genuine BoltSharp listener's current interoperability boundary was reproduced** — real
   B2-B negotiation (production `BoltHandshakeNegotiator`) and B2-A framing (production
   `BoltMessageReader`) accept the driver's handshake in both legacy and Manifest v1 modes,
   negotiate 5.8, observe the first post-handshake message as `HELLO`, and the driver then fails
   because the listener implements no messaging protocol (`t08-real-listener-{legacy,manifest}.json`).
   The boundary is: **handshake compatible; messaging unreached.**

**Client-version and test-scope limitations (apply to all items):** Windows 10.0.26200, win-x64,
.NET SDK 10.0.401, net8.0, runtime 8.0.31; plaintext loopback only (no TLS); synthetic controlled
traffic (no real Neo4j server, no routing, no clustering); `Neo4j.Driver` 6.4.0 pinned; fake-server
request decoding uses BoltSharp's own reader (request-side decoding is therefore not an independent
check of BoltSharp's reader — response correctness is anchored by the real driver and the
independent decoders).

---

## 5. Handshake fragmentation qualification (Q1-F01 carried forward)

**Observed driver behavior (execution):** when the server writes the 4-byte legacy handshake
response (`00 00 08 05`) in more than one TCP segment — `1+3`, `2+2`, `3+1`, `1+1+1+1`, with 1/10/50/
1000/3000 ms inter-segment delays — driver 6.4.0 consistently fails within ~25 ms with
`ServiceUnavailableException` wrapping
`IOException: Unexpected end of stream when performing handshake, read only returned {1|2|3} bytes
but expected 4 bytes.`, where `{N}` equals the size of the first segment. Whole-response controls
(legacy and Manifest v1) succeed and complete the full HELLO→LOGON→RESET→RUN→PULL→GOODBYE flow.
Post-handshake framing tolerates per-byte writes, chunk header/body/terminator splits, a 100 ms
header delay, and a message split into two legal chunks.

**Source-level mechanism (upstream tag `6.4.0`, commit `4ed413b01906c5d1682c62a3ee3c982cb28f99cd`
— the commit suffix matches the package informational version):**
`BoltHandshaker.ParseProtocolVersionResponse` (`Neo4j.Driver/Neo4j.Driver/Internal/Connector/
BoltHandshaker.cs`, lines 45–64; preserved at
`docs/reviews/evidence/B0-B-Q1/driver-source/BoltHandshaker.cs`) performs a **single**
`ReadAsync` of 4 bytes and throws exactly this `IOException` when `read < 4`. It does not loop
until four bytes arrive.

**General TCP transport guarantees — what this is *not*:** a stream socket delivers a byte stream
with **no message boundaries**; a single server-side `send()` **never guarantees** a single
client-side read of that size, and any conforming Bolt implementation may legitimately fragment
writes. The driver's single-read expectation is a **client-side robustness limitation**, not a
TCP property and not a Bolt protocol rule.

**Actual interoperability implications:** a BoltSharp server (B2-B negotiator, future B5-A
transport) must write the complete 4-byte handshake response — and the complete Manifest v1
response — in **one TCP write** to interoperate with driver 6.4.0 as observed. Post-handshake
framing enjoys no such constraint in the exercised scenarios. BoltSharp's production write strategy
is **not** to be modified to conceal this observation; the constraint is recorded for B5-A/B5-B
design. This qualification is **driver-version-specific**: it does not establish how other
Neo4j drivers (other languages or versions) behave when the handshake response is fragmented.

---

## 6. Negative-control blind-spot dispositions

Q1 executed 13 mutated-server negative controls (9 detected, 4 not detected). The four blind spots
below are identified from `docs/reviews/evidence/B0-B-Q1/negative-controls/summary.json` (the
controls with `Detected: false`), not inferred from counts. For each: probe identifier, mutation,
expected observable behavior, actual assertion behavior, the claim the existing test does not
establish, independent coverage, impact on B0-B acceptance, and future-coverage recommendation.

No producer test or assertion was modified in producing or dispositioning these findings.

### 6.1 NC-03b — wrong HELLO `server` value passes T02 — **ACCEPTED LIMITATION**

1. **Probe identifier:** `NC-03b-wrong-hello-server-value` (mirrors producer
   `T02_Initialization_ObservesHelloThenLogonAndRequiredMetadata`).
2. **Mutation:** HELLO SUCCESS carries `server = "Neo4j/9.9.9"` instead of the producer fixture
   value `Neo4j/5.26.0`.
3. **Expected observable behavior (if sensitive):** the copied T02 assertion sequence fails.
4. **Actual assertion behavior:** all copied assertions pass — the connection succeeds, HELLO→LOGON
   ordering and metadata keys are observed, redaction holds, no unscripted requests.
5. **Claim not established:** T02 does not verify the *value* of the server-reported identity; it
   establishes connection readiness, message ordering, metadata-key presence and redaction — not
   that the server identity matches the fixture.
6. **Independent coverage:** none asserts the server value; the Q1 FragmentationProbe records the
   fixture value only as an input. However, the accepted B0-B claims (§4 of the owner-acceptance
   record) do not depend on server-value validation: the fixture itself is the independently
   verified oracle input, and the driver demonstrably consumes it.
7. **Impact on B0-B acceptance:** none. The blind spot is bounded — no required milestone claim
   involves asserting the server's reported identity value.
8. **Future coverage:** when BoltSharp implements HELLO (B3-A/B3-B), its own suite should assert
   the server-version metadata it returns and, at B5-B, cross-check it against a real driver's
   expectations.

### 6.2 NC-03c — missing HELLO `connection_id` passes T02 — **ACCEPTED LIMITATION**

1. **Probe identifier:** `NC-03c-missing-hello-connection-id`.
2. **Mutation:** HELLO SUCCESS omits `connection_id` (producer matrix claimed it is mandatory).
3. **Expected observable behavior (if the producer claim were correct):** the copied T02 sequence
   fails.
4. **Actual assertion behavior:** all copied assertions pass — driver 6.4.0 accepts HELLO SUCCESS
   without `connection_id` and completes initialization.
5. **Claim not established:** that `connection_id` is a required HELLO SUCCESS field. This is
   precisely the producer overclaim corrected in §1; the control's *finding is the correction*.
6. **Independent coverage:** yes — the Q1 FragmentationProbe `hello-*` family (6 metadata variants;
   5 fresh-process repeats per critical case) establishes that `server` is required and
   `connection_id`/`hints` are optional for driver 6.4.0.
7. **Impact on B0-B acceptance:** none — the affected matrix wording is superseded by §1 of this
   reconciliation rather than by new tests.
8. **Future coverage:** none required for this finding; B3-A should send all three fields for
   driver compatibility (§1 guidance).

### 6.3 NC-05b — wrong RECORD arity passes T03 — **DEFERRED COVERAGE**

1. **Probe identifier:** `NC-05b-wrong-record-arity` (mirrors producer
   `T03_ReturnScalar_RealDriverReconstructsValue`).
2. **Mutation:** RECORD carries two values (`42`, `43`) for the single RUN field (`answer`).
3. **Expected observable behavior (if sensitive):** the copied T03 assertion sequence fails (driver
   rejects the arity mismatch, or the value assertions fail).
4. **Actual assertion behavior:** all copied assertions pass — the driver maps the first value to
   `answer` and ignores the extra value; `observed == 42` holds.
5. **Claim not established:** that RECORD arity conforms to the RUN `SUCCESS {fields}` count. T03
   never asserts record arity.
6. **Independent coverage:** partial — the Q1 independent graph decode verifies the *structure the
   server sends* (three values for three declared fields in T04), but no test establishes that the
   driver *rejects* an arity mismatch, and none needs to for B0-B: the accepted scalar
   interoperability claim is that the driver reconstructs the scripted value from a
   reviewer-verified fixture, which it does.
7. **Impact on B0-B acceptance:** none. The blind spot concerns driver-internal conformance
   behavior outside B0-B's discovery scope, not the authenticity of the executed observations.
8. **Future coverage:** B3-A should pin message arity in the schema work; B3-C/B5-B suites should
   exercise arity-conformant and arity-violating server responses once BoltSharp is the server.

### 6.4 NC-08 — extra SUCCESS after `has_more=false` passes T03 — **DEFERRED COVERAGE**

1. **Probe identifier:** `NC-08-extra-success-after-has-more-false` (mirrors producer T03).
2. **Mutation:** the PULL reply carries an additional empty `SUCCESS` after the terminal
   `has_more=false` SUCCESS.
3. **Expected observable behavior (if sensitive):** the copied T03 assertion sequence fails.
4. **Actual assertion behavior:** all copied assertions pass — the driver stops at
   `has_more=false` and ignores the trailing message.
5. **Claim not established:** that a PULL exchange terminates exactly at the terminal SUCCESS with
   no trailing responses (terminal-framing strictness). T03 asserts no frame counts.
6. **Independent coverage:** partial — T05 asserts exact RECORD counts and negative control
   NC-06 demonstrates that extra *RECORD* frames are detected; trailing non-RECORD responses after
   terminal SUCCESS are counted nowhere. The accepted B0-B claims do not include rejecting trailing
   server messages.
7. **Impact on B0-B acceptance:** none. The blind spot concerns server-side framing strictness,
   which becomes relevant only when BoltSharp is the server.
8. **Future coverage:** B3-C/B5-B should pin terminal framing (a BoltSharp server must not emit
   trailing messages, and B5-B acceptance should detect any that slip through).

### 6.5 Disposition summary and blocker assessment

| Blind spot | Classification | Rationale |
| --- | --- | --- |
| NC-03b | **ACCEPTED LIMITATION** | Server-value fidelity is not a B0-B claim; fixtures are the verified oracle. |
| NC-03c | **ACCEPTED LIMITATION** | Its finding *is* the §1 correction; independently covered by the Q1 probe family. |
| NC-05b | **DEFERRED COVERAGE** | Arity conformance is a B3-A/B5-B requirement, not a B0-B claim. |
| NC-08 | **DEFERRED COVERAGE** | Terminal framing strictness is a B3-C/B5-B requirement, not a B0-B claim. |

**Blocker assessment:** none of the four blind spots materially undermines (a) the authenticity of
real-driver execution — the driver genuinely ran in every control and the detected controls prove
the assertion suite is sensitive where it claims to be; (b) protocol correctness — the fixtures
were independently checked against the specifications and independently decoded; (c) typed graph
reconstruction — identity and direction mutations (NC-02, NC-09, NC-10) are detected; or (d) the
accuracy of the interoperability boundary — T08 was reproduced byte-identically in both handshake
modes. No acceptance blocker exists.

---

## 7. Effect on the gap matrix (entry-by-entry)

The gap matrix remains in force except as corrected/qualified below. Original producer and reviewer
reports are unchanged.

| Matrix element | Effect of this reconciliation |
| --- | --- |
| Fact 1 ("server **and** connection_id" mandatory) | **Superseded by §1:** only `server` is required by driver 6.4.0; `connection_id` and `hints` optional. NRE failure mode confirmed. |
| Fact 2 (handshake response must be a single write) | **Retained, qualified by §5:** observed driver-version-specific behavior with mechanism; not a TCP or protocol guarantee. |
| Fact 3 (FAILURE → IGNORED until RESET) | **Retained** (T06 + NC-04). |
| Fact 4 ("auth failure requires neo4j_code") | **Qualified by §2:** `neo4j_code` drives classification; failures without it still surface as `Neo4jException`. |
| Fact 5 (DISCARD trigger) | **Retained, extended by §4.2:** observed on both consumption and early-disposal paths. |
| HELLO row "required next step" note | **Corrected per §1.** |
| T07 row / `t07-handshake-fragmentation.json` | **Fidelity limitation per §3:** empty arrays are not protocol evidence; cite the Q1 fragmentation probes instead. |
| All other rows (Handshake, LOGON, RUN, PULL, Graph values, RESET/failure, T08, work-package mapping) | **Unchanged; supported** by the Q1 audit. |

---

## 8. Scope of this record

This document makes **no implementation change**, modifies no production or test code, no producer
or reviewer evidence, and no manifest. It records corrections and dispositions only. It is **not**
the owner acceptance decision; the acceptance decision is recorded separately in
`docs/decisions/B0-B-OWNER-01-owner-acceptance.md`.
