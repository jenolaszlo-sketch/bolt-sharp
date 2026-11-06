# B0-B-Q1 — Independent qualification of B0-B-DRV-INT-01

**Task:** B0-B — Official Neo4j Driver Integration Harness and Protocol Discovery
**Producer record:** `docs/reviews/B0-B-DRV-INT-01-implementation.md` (IMPLEMENTED, READY FOR
INDEPENDENT QUALIFICATION)
**Reviewed `main` HEAD:** `9d121aedb3e733930b903d2986fcb74aa0d7f39c`
**Implementation commit:** `46b0e8226331117b6b5d6da10ec40a24c46862d9`
**Parent (B0-A-TRACE baseline):** `a4429ef969a3f150c4ab1b4908a193ffaddcce2a`
**Reviewer:** GLM-5.3-Flash (provider `nvidia`) in OpenCode — independent of the producer executor
(DeepSeek V4.1 Flash).
**Driver under test:** `Neo4j.Driver` **6.4.0** exact pin (assembly `6.4.12.0`, informational
`6.4.0+4ed413b01906c5d1682c62a3ee3c982cb28f99cd` — the commit suffix matches upstream tag `6.4.0`).
**Environment:** Windows `10.0.26200`, win-x64, .NET SDK `10.0.401`, MSBuild `18.9.11`, net8.0,
runtime `8.0.31`. Plaintext loopback only.

## Disposition

> **B0-B-DRV-INT-01: SUPPORTED WITH QUALIFICATIONS.**
>
> The harness, its evidence and its protocol discoveries are reproducible and independently
> verified. One producer claim is **partially incorrect** (which HELLO SUCCESS metadata the driver
> requires — F-02) and several claims require precision qualifiers (F-03, F-05). None of the
> qualifications invalidates a test result, the gap matrix's work-package mapping, or the
> harness's suitability as the B3/B5 proving ground. This is **not** owner acceptance and
> establishes **no** Neo4j compatibility claim.

Recommended before B3 planning begins (documentation-only; performed by the producer or owner, not
by this review):

1. Correct gap-matrix fact 1 and the HELLO row's "required next step" note: driver 6.4.0 requires
   `server` in HELLO SUCCESS; `connection_id` and `hints` are **optional** (F-02).
2. Qualify gap-matrix fact 4 (auth failure classification is driven by `neo4j_code`; see F-03).
3. Note the t07-handshake-fragmentation evidence-fidelity limitation (F-05) in the evidence README
   or matrix, so future readers do not read the empty `requests`/`frames` arrays as a protocol
   observation.

---

## 1. Scope, independence and method

This review independently qualifies the B0-B official-driver integration harness. It is bounded by
the same stop conditions as the producer milestone: no production code change, no producer
test/oracle change, no historical evidence change, no remediation, no B3/B5 work, no new public
APIs, no branches or linked worktrees, no reset/rebase/force/push. All reviewer artifacts are
additive and live under:

- `docs/reviews/probes/B0-B-Q1/` — probe sources (`FragmentationProbe`, `NegativeControls`,
  `GraphDecode`, READMEs);
- `docs/reviews/evidence/B0-B-Q1/` — reviewer evidence and `manifest.json` (SHA-256 inventory);
- this report.

Method: nothing was accepted from the producer record on trust. Every load-bearing claim was
re-executed, and the producer's 19 evidence files were required to reproduce **byte-identically**.
Passing test counts were treated as necessary but not sufficient: assertion sensitivity was
measured with negative controls (Section 5), and response bytes were decoded independently of both
BoltSharp and the producer fixtures (Section 6).

Reviewer probes are deliberately independent:

- `FragmentationProbe` is a standalone minimal TCP responder referencing only `Neo4j.Driver`;
  its wire helpers were written from the published Bolt 5.8 / PackStream 1.0 specification and
  contain no BoltSharp code and no reuse of `ProtocolFixtures`.
- `NegativeControls` links the producer infrastructure source **read-only** and runs copies of
  producer assertions against mutated server scripts; no producer file is touched.
- `GraphDecode/decode-graph.ps1` is a standalone PowerShell PackStream decoder for the exact RECORD
  hex captured in producer evidence.

---

## 2. Baseline and isolation verification

| Check | Result |
| --- | --- |
| `main`, HEAD `9d121ae`, clean tree, single worktree | Confirmed |
| `git diff a4429ef..HEAD` touches only `tests/BoltSharp.IntegrationTests/**` and `docs/reviews/B0-B-DRV-INT-01*` | Confirmed |
| `src/`, `tests/BoltSharp.Tests/`, `tests/BoltSharp.DriverIntegrationTests/`, `BoltSharp.slnx`, `Directory.Build.props`, `README.md` unchanged | Confirmed (empty diff) |
| Producer evidence `manifest.json` (24 entries) — sizes and SHA-256 | 24/24 match |
| Producer evidence reproduction (19 JSONs re-generated into `evidence/B0-B-Q1/producer-reproduction/`) | 19/19 **byte-identical** |
| Producer test execution receipts (`commands.json`) | Reproduced: same commands, same counts |

---

## 3. Reproduction of the producer's executable claims

| Set | Command | Result |
| --- | --- | --- |
| Integration build | `dotnet build tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj -c Debug` | 0 warnings / 0 errors |
| Integration tests (run 1) | `dotnet test … -c Debug` with `BOLTSHARP_B0B_EVIDENCE_DIR` set | **19 passed / 0 failed / 0 skipped** (~22 s) |
| Integration tests (run 2, this review) | same | **19 passed / 0 failed / 0 skipped** (21 s) |
| Core solution tests | `dotnet test BoltSharp.slnx -c Debug` | **826 passed / 0 failed / 0 skipped** (unchanged baseline) |
| Core solution restore + build (this review) | `dotnet restore BoltSharp.slnx`, `dotnet build BoltSharp.slnx -c Debug` | 0 warnings / 0 errors; core `project.assets.json` files contain **no Neo4j reference** |

Timing note: `T07_HandshakeFragmentation_DriverRejectsIt` alone accounts for ~20 s of the
integration-suite duration because it swallows a 20 s `WaitForCompletionAsync` timeout (see F-05).

The one-command reproduction claim is verified: a single `dotnet test` invocation of the
integration project with the evidence environment variable set reproduces the full 19-test set and
its evidence. There is **no CI configuration in the repository** (no `.github/`, no pipeline
files), so CI isolation is not applicable; solution membership is the only automation surface, and
it is correctly maintained (Section 8).

---

## 4. Spec conformance of the producer's byte oracle

The producer's `ProtocolFixtures` encoders were checked against the published Bolt 5.8 /
PackStream 1.0 specification (fetched during this review):

- handshake response bytes `00 00 08 05` (legacy) and the Manifest v1 layout
  `00 00 01 FF | VarInt N=1 | range(5.8,0) | VarInt capabilities=0`;
- tiny-struct markers (`0xB0|n`), message signatures (HELLO `0x01`, GOODBYE `0x02`, RESET `0x0F`,
  RUN `0x10`, PULL `0x3F`, DISCARD `0x2F`, LOGON `0x6A`, SUCCESS `0x70`, RECORD `0x71`,
  IGNORED `0x7E`, FAILURE `0x7F`);
- chunk framing (big-endian 16-bit length header + `00 00` terminator);
- Node 4-field (`0x4E`: id, labels, properties, elementId) and Relationship 8-field (`0x52`: id,
  start, end, type, properties, elementId, startElementId, endElementId) structure layouts;
- FAILURE metadata fields for 5.7+ (`gql_status`, `neo4j_code`, `description`,
  `diagnostic_record`);
- FAILED/IGNORED/RESET semantics.

No discrepancy was found. The driver's independent reconstruction of the scripted responses
(scalar 42, typed Alice/Bob/KNOWS graph, IGNORED recovery) is itself additional evidence that the
fixtures are spec-correct: a shared serializer mistake cannot make the real driver agree with both
the fake responder and this review's independent decoder.

---

## 5. Negative controls — assertion sensitivity

Copies of producer assertions (linked producer infrastructure, read-only) were executed against
server scripts mutated in exactly one place per control
(`docs/reviews/evidence/B0-B-Q1/negative-controls/summary.json`):

| Control | Mutation | Detection | Mechanism |
| --- | --- | --- | --- |
| NC-01 | RECORD returns 43 instead of 42 | **DETECTED** | exact-value assertion |
| NC-02 | relationship start/end reversed | **DETECTED** | `StartNodeId`/`StartNodeElementId` assertions |
| NC-03a | HELLO SUCCESS omits `server` | **DETECTED** | driver NRE fails the uncaught T02 call |
| NC-03b | HELLO SUCCESS carries wrong `server` value | not detected | T02 only requires the connection to succeed (documented blind spot) |
| NC-03c | HELLO SUCCESS omits `connection_id` | not detected | driver 6.4.0 accepts it — the producer matrix claims both fields are required (F-02) |
| NC-04 | post-FAILURE PULL answered with SUCCESS instead of IGNORED | **DETECTED** | IGNORED-frame assertion |
| NC-05a | PULL answered with FAILURE instead of RECORD/SUCCESS | **DETECTED** | surfaces as `ClientException` from the uncaught T03 call |
| NC-05b | RECORD carries two values for one RUN field | not detected | T03 never asserts record arity (documented blind spot) |
| NC-06 | extra RECORD beyond the scripted three | **DETECTED** | exact values + exact RECORD-count assertions in T05 |
| NC-07 | truncated PULL frame replaced by a complete clean reply | **DETECTED** | `Assert.NotNull(failure)` in T06 |
| NC-08 | extra empty SUCCESS after `has_more=false` | not detected | driver stops at `has_more=false`; T03 asserts no frame count (documented blind spot) |
| NC-09 | node numeric ids swapped (Alice=2, Bob=1) | **DETECTED** | numeric node-id assertions |
| NC-10 | relationship element id changed `r:1`→`r:99` | **DETECTED** | relationship element-id assertion |

9/13 controls detected their mutation, as required. The four non-detections are **assertion
blind spots**, not false successes: the producer tests never claimed to verify those properties.
They matter for future work: B3-A/B3-C test suites should assert record arity, terminal-SUCCESS
semantics and (if it becomes a contract point) HELLO metadata values.

---

## 6. Independent protocol probes (FragmentationProbe, 35 cases + repeats)

All runs use fresh processes (one case per process) and fresh loopback listeners. Full evidence:
`docs/reviews/evidence/B0-B-Q1/fragmentation/` (per-case JSON with request transcripts, write
segments and timings), `fragmentation-summary.json` (aggregate of all 35 cases),
`fragmentation-repeat/` (8 critical cases × 5 fresh-process repeats = 40 runs).

### F-01 — Handshake-response fragmentation is rejected by driver 6.4.0 (confirms and strengthens producer D02)

- Every split of the identical 4-byte response (`00 00 08 05`) fails:
  `hs-1-3`, `hs-2-2`, `hs-3-1`, `hs-1-1-1-1`, with 1/10/50/1000/3000 ms inter-segment delays.
- Failure mode is exact and stable: `ServiceUnavailableException` wrapping
  `IOException: Unexpected end of stream when performing handshake, read only returned {1|2|3}
  bytes but expected 4 bytes.` The `{N}` always equals the size of the **first** segment.
- Delays do not help: the failure occurs in ~25 ms, before the remaining bytes are written. The
  driver performs a **single** read for the 4-byte response and does not loop until 4 bytes arrive.
- Upstream source confirms it: `BoltHandshaker.ParseProtocolVersionResponse` in tag `6.4.0`
  (commit `4ed413b…`, matching the package informational version) performs one `ReadAsync` of 4
  bytes and throws exactly this `IOException` when `read < 4`
  (`docs/reviews/evidence/B0-B-Q1/driver-source/BoltHandshaker.cs`, lines 45–64).
- Whole-response controls succeed for both legacy and Manifest v1 handshakes and complete the full
  HELLO→LOGON→RESET→RUN→PULL→GOODBYE flow with query value 42.
- Post-handshake framing is tolerant: per-byte writes, chunk header/body/terminator splits, a
  100 ms header delay, and a message split into two legal chunks all succeed (query 42).

Producer D02 ("handshake response must be a single write") is confirmed with mechanism.

### F-02 — Correction to producer D01: `server` is the required HELLO SUCCESS field

| HELLO SUCCESS metadata | Outcome |
| --- | --- |
| `{server, connection_id, hints}` (producer fixture) | success, query 42 |
| `{server, connection_id}` (no hints) | success, query 42 |
| `{server}` only | **success, query 42** |
| `{connection_id}` only | failure: `System.NullReferenceException` |
| `{hints}` only | failure: `System.NullReferenceException` |
| `{}` (empty) | failure: `System.NullReferenceException` |

Driver 6.4.0 requires **`server`**; `connection_id` and `hints` are optional. The producer matrix
states "`server` and `connection_id` are mandatory" and fact 1 says both are required — this is
**partially incorrect**. The observed failure mode (NRE instead of a clean protocol error) is
correctly reported and confirmed. Corrected by negative control NC-03c and the `hello-*` probe
family; consistent across 5 fresh-process repeats per critical case.

### F-03 — Qualification to producer D04: classification is driven by `neo4j_code`

| LOGON FAILURE metadata | Surfaced exception | `Code` | `Message` |
| --- | --- | --- | --- |
| new-style with `neo4j_code=Neo.ClientError.Security.Unauthorized` (+ gql_status/description/diagnostic_record) | `Neo4j.Driver.AuthenticationException` | `Neo.ClientError.Security.Unauthorized` | generic `"An unknown error occurred"`; the description surfaces in `GqlStatusDescription` |
| new-style without `neo4j_code` | `Neo4j.Driver.DatabaseException` | `Neo.DatabaseError.General.UnknownError` | generic; description in `GqlStatusDescription` |
| legacy `{code, message}` | `Neo4j.Driver.AuthenticationException` | `Neo.ClientError.Security.Unauthorized` | the actual message text |

The producer claim ("auth failure requires `neo4j_code`… surfaced as an authentication
`Neo4jException`") needs this precision: a FAILURE without `neo4j_code` still surfaces as a
`Neo4jException` (a `DatabaseException`), so authentication failure is never silently swallowed;
`neo4j_code` (or legacy `code`) is what selects the **authentication** classification. The
producer test asserts only `Neo4jException`, which holds in every variant — the test is correct,
the matrix wording was imprecise. Also relevant for B3-B: with new-style metadata the driver does
not put the server's description into `Message`.

### F-04 — DISCARD trigger (confirms and extends producer D05)

Producer evidence `t05-discard.json` and `t05-early-disposal.json` are identical request sequences
(RUN, PULL, DISCARD, RESET, GOODBYE) and reproduce byte-identically. Both observed paths —
`ConsumeAsync` after a partial fetch and early session disposal — make the driver `DISCARD` the
remaining cursor. The matrix statement is correct but covers only the first path.

### F-05 — t07-handshake-fragmentation evidence fidelity (producer-side limitation, not a defect)

`t07-handshake-fragmentation.json` records the client handshake, server handshake and decoded
proposals, but its `requests`, `frames` and `notes` arrays are empty: the test writes evidence only
after swallowing a 20 s `WaitForCompletionAsync` timeout (the scripted server never receives HELLO
because the driver fails first), and the server sets `negotiated_version: "5.8"` before the client
has accepted it. The test's assertions (failure occurred, message mentions "handshake") are
nonetheless verified independently and much more deeply by this review's probe (F-01). Noted so
future readers do not interpret the empty arrays as a protocol observation.

---

## 7. Graph value verification (T04)

- The exact RECORD hex from producer evidence
  (`B17193B44E01…B852010102…B44E02…`) was decoded by a standalone PackStream 1.0 decoder written
  for this review (`docs/reviews/probes/B0-B-Q1/GraphDecode/decode-graph.ps1`):
  **23/23 checks pass** (`docs/reviews/evidence/B0-B-Q1/graph-independent-decode.json`) —
  Node(id=1, `n:1`, [Person], name=Alice), Relationship(id=1, start=1, end=2, KNOWS, `r:1`,
  `n:1`→`n:2`, since=2024), Node(id=2, `n:2`, [Person], name=Bob), with the documented struct tags
  (4-field Node, 8-field Relationship).
- Negative identity/direction controls against the copied T04 assertions:
  reversed relationship endpoints (NC-02), swapped node numeric ids (NC-09) and a wrong
  relationship element id (NC-10) are all **DETECTED**.
- The producer's "distinct typed identity despite reused numeric id 1" claim is confirmed: the
  decoded relationship and node A both carry numeric id 1 and the driver surfaces them as distinct
  typed objects (`INode` vs `IRelationship`).

---

## 8. Harness, T08 and isolation audit

### ScriptedBoltServer / fixtures (Group A)

- Loopback-only (`IPAddress.Loopback`, port 0), bounded lifecycle, no sleeps, OS-assigned port;
  every unscripted request is recorded and fails strict scenarios (`Assert.DoesNotContain(…,
  r => r.Unscripted)` appears in T01–T06).
- Request decoding uses BoltSharp's `BoltMessageReader`/`PackStreamReader`; responses come only
  from the independent `ProtocolFixtures` oracle. This asymmetry is documented by the producer and
  is sound: request-side decoding is already covered by the accepted B1/B2 suites, and response
  correctness is anchored by the real driver plus this review's independent decode.
- Credentials: HELLO/LOGON frames are redacted (`<redacted>`) in all evidence; T02 asserts
  redaction explicitly.

### T08 real-listener boundary (Group B)

- `BoltSharpListenerTests` reuses the genuine accepted test-only listener by **source link**;
  `tests/BoltSharp.DriverIntegrationTests/` is unchanged in `46b0e82` (empty diff), so the
  "unmodified reuse" claim holds.
- The listener delegates to production `BoltHandshakeNegotiator` (B2-B) and observes the first
  post-handshake message with production `BoltMessageReader` (B2-A framing) — real negotiation,
  real framing, no reimplementation.
- Reproduced evidence (`t08-real-listener-*.json`, byte-identical): both legacy and Manifest v1
  modes negotiate **5.8**, `Outcome=ACCEPTED`, first message `MESSAGE_READ`/`HELLO`, driver then
  fails with `ServiceUnavailableException` (the listener sends no response). The stopping point is
  exactly the documented boundary: handshake compatible, messaging unreached.

### Solution and dependency isolation

- `BoltSharp.slnx` lists only `src/BoltSharp` and `tests/BoltSharp.Tests`; both driver-dependent
  projects are excluded, so core restore/build cannot pull `Neo4j.Driver` (verified: core assets
  contain no Neo4j reference; core build 0/0).
- The integration project resolves exactly `Neo4j.Driver 6.4.0` with no floating ranges
  (`package-list.txt`).
- No CI configuration exists in the repository; the documented exclusion convention is therefore
  the only enforcement point and it is correctly applied.

---

## 9. Gap-matrix audit (entry by entry)

`docs/reviews/B0-B-DRV-INT-01-gap-matrix.md` against this review's evidence:

| Matrix entry | Verdict | Notes |
| --- | --- | --- |
| Handshake row (fake server) | **Supported** | Proposals, magic, legacy 5.8 selection, reject-all-without-HELLO all reproduced. |
| Handshake row (real listener) | **Supported (partial, as stated)** | T08 verified; the "partial" scoping is accurate. |
| HELLO row | **Supported with correction** | "server and connection_id are mandatory" → only `server` is mandatory (F-02). NRE failure mode confirmed. |
| LOGON row | **Supported with qualification** | Classification detail per F-03; `Neo4jException` claim holds in all variants. |
| RUN row | **Supported** | `RUN(query, parameters, extra)`, `SUCCESS {fields}` before PULL; query string decoded from the real request (T03). |
| PULL row | **Supported** | Finite `PULL(n=1)` pagination and DISCARD reproduced (T05). |
| Graph values row | **Supported** | Independent decode 23/23 (Section 7); identity/direction negatives detected. |
| RESET / failure row | **Supported** | FAILURE → `Neo4jException`, pipelined PULL → IGNORED, RESET recovery, termination/truncation never clean (T06 + NC-04/NC-07). |
| Observed fact 1 (HELLO metadata) | **Correction required** | F-02. |
| Observed fact 2 (handshake single write) | **Supported, strengthened** | F-01 with mechanism and upstream source. |
| Observed fact 3 (FAILURE → IGNORED) | **Supported** | T06 + NC-04. |
| Observed fact 4 (auth requires neo4j_code) | **Qualified** | F-03. |
| Observed fact 5 (DISCARD trigger) | **Supported, extended** | F-04. |
| Smallest work-package mapping (B3-A/B3-B/B3-C/B5-A/B5-B) | **Supported** | Every remaining gap maps to an existing package; nothing pre-implemented; no compatibility claim made. |

The matrix's own disclaimer ("populated only from executed results… not owner acceptance") is
accurate.

---

## 10. Evidence inventory

Reviewer evidence (SHA-256 manifest: `docs/reviews/evidence/B0-B-Q1/manifest.json`):

- `producer-reproduction/` — 19 byte-identical re-runs of the producer evidence;
- `fragmentation/` (35 catalog cases plus one early smoke run) + `fragmentation-summary.json`
  (aggregate of the 35 catalog cases) + `fragmentation-repeat/` (40 runs);
- `negative-controls/summary.json`, `negative-controls-run.log`, `negative-controls-build.log`;
- `graph-independent-decode.json`, `graph-independent-decode.log`;
- `driver-source/BoltHandshaker.cs` (upstream tag `6.4.0` excerpt);
- `integration-tests.log`, `integration-tests-rerun.log`, `integration-build.log`,
  `core-solution-tests.log`, `core-solution-build-rerun.log`, `dotnet-info.txt`,
  `package-list.txt`, `fragmentation-probe-build.log`.

Probe sources: `docs/reviews/probes/B0-B-Q1/{README.md, FragmentationProbe/, NegativeControls/,
GraphDecode/}` (also hashed in the manifest).

---

## 11. Limitations of this review

- Windows 10.0.26200 / net8.0 / plaintext loopback only; TLS, clustering and real-server behavior
  are out of scope (as they are for the milestone itself).
- Driver findings are pinned to `Neo4j.Driver` 6.4.0 (assembly 6.4.12.0); other driver versions are
  not tested.
- The negative controls copy producer assertions rather than executing the producer test assembly
  itself (the producer infrastructure is linked read-only; assertion sequences are copied
  verbatim). This measures assertion sensitivity of the same checks the producer tests perform.
- The upstream driver source excerpt is evidence of the mechanism in tag `6.4.0`; the binary
  behavior was established by execution, not by source inspection alone.
- Author independence: this review was produced by a different model/executor than the producer,
  but no organizational attestation beyond that is available.

---

## 12. Handoff

**B0-B-DRV-INT-01: SUPPORTED WITH QUALIFICATIONS.**

The harness is reproducible, strictly asserted where it claims to be, isolated from the core
solution, and its protocol discoveries survive independent challenge — with the D01 correction and
D04/D05 qualifications recorded in Section 5 and the gap-matrix audit. The four documented
assertion blind spots are inputs to B3-A/B3-C test design, not defects in B0-B. No remediation was
performed by this review, and none is authorized by it.
