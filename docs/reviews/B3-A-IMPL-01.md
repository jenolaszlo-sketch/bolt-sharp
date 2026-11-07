# B3-A-IMPL-01 — Bolt 5.8 message schemas and protocol contracts

**Task:** B3-A — Message schemas for the negotiated Bolt revision (per
`docs/implementation-tasks.md`, governed by `docs/boltsharp-spec.md`)
**Baseline:** `d5189af56c79bbdb86bbaaa156bfb2a0f4046c37` (B0-B ACCEPTED WITH QUALIFICATIONS, CLOSED)
**Implementation commit:** `d99096a9689c4a7ce313c569cff2b165cfbda11a`
**Implementation tree:** `6cb9cace72d403d496ca92a067c06510be64220c`
**Selected revision:** **Bolt 5.8, PackStream 1.0** (as pinned by `docs/compatibility-target.md`,
exercised by B0-B, and confirmed by the B0-B owner decision — no incompatible-revision evidence was
found, so no alternative version was introduced)
**Producer executor:** GLM-5.3-Flash (`nvidia`, OpenCode). The producer does **not** record
independent qualification or owner acceptance (§18).

---

## 1. Baseline verification

Verified before implementation:

```text
git branch --show-current  -> main
git rev-parse HEAD         -> d5189af56c79bbdb86bbaaa156bfb2a0f4046c37 (matches the commission baseline)
git rev-parse HEAD^{tree}  -> ed7873eece039559916f29df5e406c54e4c2c3fa
git worktree list          -> one worktree (D:/projects/bolt-sharp)
git status --porcelain     -> clean
B0-B owner decision        -> present (docs/decisions/B0-B-OWNER-01-owner-acceptance.md, ACCEPTED
                              WITH QUALIFICATIONS) plus the reconciliation record; nothing pushed
Core solution baseline     -> 826 passed / 0 failed / 0 skipped (re-run before implementation)
B0-B integration baseline  -> 19 passed / 0 failed / 0 skipped (re-run before implementation,
                              evidence/baseline/integration-tests-baseline.log)
```

No prior B3-A work existed; `src/BoltSharp/` contained only `PackStream/`, `Framing/` and
`Handshake/`. Nothing was duplicated.

## 2. Scope and source-file changes

All changes are additive; no existing production file, test, or evidence file was modified.

**Production (6 files, new `src/BoltSharp/Messages/`):**

| File | Content |
| --- | --- |
| `BoltMessageSignatures.cs` | The 16 Bolt 5.8 message signature constants (9 requests, 4 responses, BEGIN/COMMIT/ROLLBACK/ROUTE) and the known-name lookup. |
| `BoltMessageError.cs` | `BoltMessageErrorCategory` (13 deterministic categories) and `BoltMessageException` (category + optional signature; messages never carry field values). |
| `BoltRequests.cs` | Typed immutable request records: `HelloRequest`, `LogonRequest`, `RunRequest`, `PullRequest`, `DiscardRequest`, `ResetRequest`, `GoodbyeRequest`, `LogoffRequest`, `TelemetryRequest` + `BoltAuthScheme`. Redacting `ToString()` implementations. |
| `BoltResponses.cs` | Typed immutable response records: `SuccessResponse`, `RecordResponse`, `FailureResponse`, `IgnoredResponse`, with typed metadata accessors. |
| `BoltMessageFields.cs` | Internal map accessors (last-wins duplicate-key semantics, matching the B1 reader) and ordered-map construction helpers. |
| `BoltMessageCodec.cs` | `DecodeRequest`, `DecodeResponse`, `EncodeResponse` and the shared schema validation (HELLO/LOGON/RUN/PULL/DISCARD extra, SUCCESS/FAILURE metadata incl. recursive bounded cause). |

**Tests (6 files, new `tests/BoltSharp.Tests/Messages/`):** `BoltMessageFixtures.cs` (independent
oracle + fixtures), `BoltRequestDecodeTests.cs`, `BoltMessageNegativeTests.cs`,
`BoltResponseEncodeTests.cs`, `BoltResponseDecodeTests.cs`, `B0BFixtureComparisonTests.cs`.

## 3. Actual message catalog implemented

### 3.1 Requests (decoded)

| Message | Signature | Arity | Contract | Classification |
| --- | --- | --- | --- | --- |
| `HELLO` | `0x01` | 1 | `extra` map; `user_agent` required string; `routing`/`notifications_minimum_severity`/`notifications_disabled_classifications` optional (string/list/map **or null**); `bolt_agent` map when present with required `product` string; prohibited legacy fields rejected | **Supported and decoded** |
| `LOGON` | `0x6A` | 1 | `auth` map; required string `scheme`; `none`/`basic`/`bearer`/`kerberos` recognized with structural shape validation (basic: `principal`+`credentials` strings; bearer/kerberos: `credentials` string); unknown schemes preserved as `Unknown` | **Supported and decoded** (recognition ≠ support) |
| `RUN` | `0x10` | 3 | `query` string, `parameters` map, `extra` map; 5.8 extra fields type-validated when non-null; legacy `notifications_disabled_categories` rejected | **Supported and decoded** |
| `PULL` | `0x3F` | 1 | `extra.n` required integer ≥ −1 (−1 = all remaining); optional `qid` ≥ −1 | **Supported and decoded** |
| `DISCARD` | `0x2F` | 1 | same shape as PULL | **Supported and decoded** |
| `RESET` | `0x0F` | 0 | zero fields; no side effects | **Supported and decoded** |
| `GOODBYE` | `0x02` | 0 | zero fields; no side effects | **Supported and decoded** |
| `LOGOFF` | `0x6B` | 0 | zero fields; no side effects | **Supported and decoded** (basic contract per §4.1) |
| `TELEMETRY` | `0x54` | 1 | one integer field; only the structural contract is validated — the specification's own example answers an out-of-domain `api` with FAILURE, so the 0–3 value domain is a server/B3-B decision, recorded as such | **Supported and decoded** (basic contract per §4.1) |
| `BEGIN` | `0x11` | — | known Bolt 5.8 signature | **Known, not implemented** → `UnsupportedMessage` |
| `COMMIT` | `0x12` | — | known Bolt 5.8 signature | **Known, not implemented** → `UnsupportedMessage` |
| `ROLLBACK` | `0x13` | — | known Bolt 5.8 signature | **Known, not implemented** → `UnsupportedMessage` |
| `ROUTE` | `0x66` | — | known Bolt 5.8 signature | **Known, not implemented** → `UnsupportedMessage` |
| any other tag (incl. graph tags) | — | — | not a message signature | **Unknown** (`UnknownSignature`, or `NotAStructure` when the PackStream layer resolves the tag to a typed graph value) |

Valid-but-unsupported requests are never silently accepted: BEGIN/COMMIT/ROLLBACK/ROUTE decode
attempts always raise `BoltMessageException(UnsupportedMessage)`.

### 3.2 Responses (encoded, and independently decodable for diagnostics)

| Message | Signature | Arity | Contract |
| --- | --- | --- | --- |
| `SUCCESS` | `0x70` | 1 | metadata map; known 5.8 keys type-validated when non-null (`server`, `connection_id`, `hints`, `protocol_version`, `fields` list-of-strings, `t_first`, `qid`, `db`, `has_more`, `advertised_address`, `credentials_expired`); `patch_bolt` prohibited; empty metadata valid |
| `RECORD` | `0x71` | 1 | one list field; arbitrary supported PackStream values incl. typed graph structures (Node 4-field, Relationship 8-field, UnboundRelationship, Path — accepted B1 contracts) |
| `IGNORED` | `0x7E` | 0 | zero fields, no metadata or payload — encoded exactly as the Bolt 5.8 specification defines (see §8 for the recorded B0-B fixture discrepancy) |
| `FAILURE` | `0x7F` | 1 | 5.7+ GQL metadata: `gql_status`, `neo4j_code`, `description`, `message`, `diagnostic_record` type-validated when non-null; `cause` validated as a recursively nested failure-metadata map bounded by the configured depth; the pre-5.7 `code` field is prohibited |

Response decoding exists for diagnostics and test verification and is **not** used as a substitute
for the independent golden-byte fixtures (§7/§8).

## 4. Public/internal API boundaries

Public (assembly `BoltSharp`, namespace `BoltSharp.Messages`): `BoltMessageCodec` (with
`PackStreamLimits`-taking constructor), the nine request types and four response types, the
`BoltRequest`/`BoltResponse` bases, `BoltAuthScheme`, `BoltMessageErrorCategory`,
`BoltMessageException`, `BoltMessageSignatures`.

Internal: `BoltMessageFields`/`BoltMessageMaps` (map access/construction helpers) and all
validation helpers. The initial public surface is exactly what B3-B needs; no session, transport or
backend types are exposed. Messages are immutable: maps/lists arrive as the already-defensively-
owned PackStream values (B1-R1 F1 contract) and are never mutated or re-exposed mutably; no
connection state exists on any message type or on the codec.

`ToString()` redaction: `LogonRequest` renders the scheme only; `HelloRequest` renders the user
agent only; `RunRequest` renders the query and entry counts (never parameter values);
`FailureResponse` renders status codes only; `SuccessResponse` renders known key names only.
Codec exception messages reference field names, expected types and counts — never values.

## 5. Version-specific contracts (Bolt 5.8)

- **HELLO extra (5.8):** `user_agent`, `routing` (map of string values, or null = no routing),
  `notifications_minimum_severity` (string or null), `notifications_disabled_classifications`
  (list of strings or null), `bolt_agent` (map; `product` mandatory within it; `platform`/
  `language`/`language_details` optional or null). Prohibited: `scheme` (removed after 5.0),
  `patch_bolt` (removed after 4.4), and with them any interpretation of legacy authentication
  entries (`credentials`, `principal`) — authentication moved to `LOGON` in 5.1.
- **LOGON (5.1+):** `scheme` required; the four predefined schemes' structural shapes validated;
  unknown schemes preserved. `LOGON` SUCCESS metadata: `advertised_address` (5.8),
  `credentials_expired` — represented, never fabricated.
- **RUN extra (5.8):** `bookmarks`, `tx_timeout`, `tx_metadata`, `mode`, `db`, `imp_user`,
  `notifications_minimum_severity`, `notifications_disabled_classifications` — type-validated when
  non-null; nulls are valid defaults. `notifications_disabled_categories` (removed after 5.4) is
  prohibited. **RUN SUCCESS:** `fields`, `t_first`, optional `qid` (explicit transactions only —
  never required for auto-commit), optional `db` (5.8).
- **PULL/DISCARD extra:** `n` required, ≥ −1; `qid` optional, ≥ −1. `n = −1` supported (the
  authentic driver capture `DISCARD {"n": -1}` decodes to it).
- **FAILURE (5.7+):** `gql_status`/`neo4j_code`/`description`/`message`/`diagnostic_record`/
  `cause`. The obsolete `code` field is rejected. Q1-F03 carried forward as client observations:
  `neo4j_code` influences the qualified driver's exception subclass, its absence still surfaces a
  `Neo4jException`, and new-style descriptions may surface via `GqlStatusDescription` — no Neo4j
  driver classes are hardcoded into BoltSharp.
- **TELEMETRY:** one integer; the 0–3 value domain is deliberately not codec-enforced (the
  specification's own example returns FAILURE for `TELEMETRY 9001` rather than treating it as
  malformed) — recorded for B3-B.
- **IGNORED:** zero fields, per the 5.8 specification (see §8).

## 6. Independent oracle provenance

1. **Official specification** — the Bolt message specification (upstream `neo4j-drivers/docs-bolt`
   `message.adoc`, fetched 2026-11-07) preserved at
   `evidence/B3-A-IMPL-01/spec/message.adoc`; PackStream 1.0 markers per the published PackStream
   specification (as qualified in B1).
2. **Independent test-local encoder** — `tests/BoltSharp.Tests/Messages/BoltMessageFixtures.cs`
   carries `IndependentPackStream`, a ~100-line PackStream 1.0 encoder written from the
   specification, deliberately independent of the production `PackStreamWriter` (the same
   independent-oracle pattern the accepted B0-B harness established with its `ProtocolFixtures`).
   All spec-derived request/response fixtures and negative fixtures are built with it.
3. **B0-B qualified fixtures and authentic captures** — byte-copied from the hash-verified B0-B
   evidence (`docs/reviews/evidence/B0-B-DRV-INT-01/*.json`): authentic driver request captures
   (RUN ×2, PULL ×2, DISCARD, RESET, GOODBYE) and qualified response fixtures (HELLO SUCCESS,
   empty SUCCESS, RUN SUCCESS ×2, RECORD scalar, RECORD graph, has_more false, FAILURE GQL).
4. **Upstream driver source** — `Neo4j.Driver` 6.4.0 (tag `6.4.0`, commit `4ed413b…`, matching the
   package informational version) excerpts preserved under `evidence/B3-A-IMPL-01/driver-source/`
   (`IgnoredMessageSerializer.cs`, `PipelinedMessageReader.cs`) supporting the IGNORED discrepancy
   record (§8).

No fixture is produced by the production encoder; round trips are never the sole evidence.

## 7. Positive and negative test matrix

**96 new tests, all passing** (`dotnet test --filter FullyQualifiedName~BoltSharp.Tests.Messages`).

Positive coverage (summary — the code is the complete record):

- every supported request signature decodes to its typed record (HELLO minimal/full/null-optional/
  agent; LOGON none/basic/bearer/kerberos/unknown; RUN with parameter and full 5.8 extra; PULL
  `n`, `n=-1`, `n+qid`; DISCARD `n+qid`; RESET; GOODBYE; LOGOFF; TELEMETRY);
- every supported response signature decodes (SUCCESS empty/hello/run/logon metadata, RECORD
  scalar and typed graph, FAILURE with and without nested cause, IGNORED);
- encoding reproduces independent golden bytes for every response type and the documented variants
  (§8), preserves metadata order deterministically, and uses the PackStream minimal integer
  representation;
- optional-metadata omission and explicit-null forms; nested query parameters preserved;
- `RECORD` with two values for one RUN field decodes (NC-05b boundary: outer arity only);
- codec statelessness: interleaved decodes are independent; repeated decodes are deterministic.

Negative coverage — every case asserts the **exact** `BoltMessageErrorCategory`:

| Category | Cases |
| --- | --- |
| `EmptyPayload` | empty payload |
| `NotAStructure` | top-level integer; graph Node as top-level message |
| `UnknownSignature` | non-message tag (0x5A); unknown signature on the response path |
| `UnsupportedMessage` | BEGIN, COMMIT, ROLLBACK, ROUTE |
| `StructureArity` | HELLO 2-field and 0-field; RUN 2-field; RESET/GOODBYE/LOGOFF with extra field; TELEMETRY 0-field; RECORD 2-field outer arity |
| `MissingRequiredField` | HELLO without `user_agent`; `bolt_agent` without `product`; LOGON without `scheme`; basic without `credentials`; bearer without `credentials`; PULL/DISCARD without `n` |
| `InvalidFieldType` | non-map `extra`/`auth`/metadata; wrong-typed `user_agent`, `scheme`, `principal`, `query`, `parameters`, `tx_timeout`, `n`, `qid`, `api`, SUCCESS `fields`/`has_more`, non-list RECORD field, routing non-string values, non-string classification items, FAILURE non-map cause |
| `InvalidFieldValue` | `n = −2` (PULL and DISCARD); `qid = −2` |
| `ProhibitedField` | HELLO `scheme`/`patch_bolt`; RUN `notifications_disabled_categories`; SUCCESS `patch_bolt`; FAILURE `code` |
| `TrailingBytes` | bytes after the top-level structure |
| `Truncated` | truncated RUN payload |
| `InvalidPackStreamValue` | invalid nested marker (0x7F); list exceeding the configured limit; encode-path cause chain deeper than the configured depth |

Encoding-side schema validation is tested symmetrically (wrong-typed SUCCESS metadata →
`InvalidFieldType`; prohibited FAILURE `code` → `ProhibitedField`; unbounded cause chain →
`InvalidPackStreamValue`), and unknown response types are rejected with `ArgumentException`.

## 8. B0-B fixture comparison results

**Authentic request captures** (`evidence/B0-B-DRV-INT-01/*.json` frame hex, byte-copied): all six
distinct authentic request byte strings decode to exactly the expected typed values —
`RUN("RETURN 42 AS answer", {}, {mode:"r"})`, `RUN("RETURN n", {}, {})`, `PULL{n:1000}`,
`PULL{n:1}`, `DISCARD{n:-1}`, `RESET`, `GOODBYE`. Authentic HELLO/LOGON captures do not exist in
the B0-B evidence (the capture layer redacts sensitive messages by design); the codec's HELLO/LOGON
contracts are therefore established against the specification and the documented driver behavior
(map keys observed in the B0-B evidence: `user_agent`, `routing`, `bolt_agent` for HELLO;
`scheme`, `principal`, `credentials` for LOGON), and this limitation is recorded in §10.

**Qualified response fixtures:** the codec's encoding reproduces the B0-B-qualified server bytes
exactly for: empty SUCCESS (`B170A0`), HELLO SUCCESS (`{server, connection_id, hints}`), RUN SUCCESS
`{fields}` ×2, RECORD `[42]`, RECORD graph (Alice/KNOWS/Bob — the fixture independently decoded in
B0-B-Q1 with 23/23 checks), `has_more:false` SUCCESS, and the t06 GQL FAILURE. Additional variants
(`has_more:true`, `t_first`, `advertised_address`, `credentials_expired`, nested FAILURE cause)
are pinned against the independent spec encoder.

**Recorded fixture discrepancy — IGNORED.** The official Bolt 5.8 specification defines `IGNORED`
as a summary message with **no fields**. The B0-B producer fixture (`ProtocolFixtures.Ignored`)
emitted a **one-field** structure `B1 7E A0`, and the qualified driver 6.4.0 accepted it.
Upstream source explains the tolerance without making the form conformant: the driver's
`IgnoredMessageSerializer` reads **zero** fields for tag `0x7E` regardless of the declared arity,
and the pipelined reader advances past the entire chunked-message buffer after deserialization
(`pipeReader.AdvanceTo(buffer.End)`), discarding unconsumed declared bytes. Consequences recorded:

- B3-A **encodes the specification form** `B0 7E` (the commission mandates a zero-field IGNORED);
- the codec's diagnostic decoder rejects the one-field form with `StructureArity`;
- the producer byte string is preserved unaltered in the B0-B evidence and is explicitly **not** a
  golden fixture for B3-A (`B0B.ProducerIgnoredOneField` exists only to pin the discrepancy);
- **B5-B must verify end-to-end** that the qualified driver accepts the specification form
  (mechanistically expected: the driver reads no IGNORED fields, so `B0 7E` leaves nothing to
  discard — but this has not been executed against the driver and is recorded as an open
  verification for B5-B, not a claim).

No historical evidence, producer test, or fixture was modified in recording this discrepancy.

## 9. Regression test counts and commands

| Set | Command | Result |
| --- | --- | --- |
| Core solution (before implementation) | `dotnet test BoltSharp.slnx -c Debug` | 826 passed / 0 failed / 0 skipped |
| Core solution (after implementation) | `dotnet test BoltSharp.slnx -c Debug` | **922 passed / 0 failed / 0 skipped** (826 historical + 96 new) |
| New message tests only | `dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj --filter FullyQualifiedName~BoltSharp.Tests.Messages` | 96 passed / 0 failed |
| B0-B integration project (after implementation) | `dotnet test tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj -c Debug` | **19 passed / 0 failed / 0 skipped** |
| Core build | `dotnet build BoltSharp.slnx -c Debug` | 0 warnings / 0 errors |
| B0-B reviewer probes (build check) | `dotnet build docs/reviews/probes/B0-B-Q1/{FragmentationProbe,NegativeControls} -c Release` | both succeed (no API breakage) |

Logs: `evidence/B3-A-IMPL-01/baseline/` (integration baseline, core build, final core + integration
runs). No external Neo4j server is required by any test; no test-only driver dependency was added
to production or to the core solution; PackStream behavior is unchanged (zero diffs in
`src/BoltSharp/PackStream/`, `Framing/`, `Handshake/`).

## 10. Known limitations

1. **No authentic HELLO/LOGON request captures** exist in the committed evidence (redacted by
   design). The HELLO/LOGON contracts rest on the specification plus the documented driver
   behavior; the reviewer may add captures if a capture mechanism is authorized separately.
2. **`bolt_agent` presence:** the specification marks `bolt_agent` mandatory in 5.8 HELLO, while
   the commission text reads it as "where present". The codec validates its shape **when present**
   but does not require it (the less-strict reading). Driver 6.4.0 always sends it, so requiring it
   would not break the qualified client — recorded as an owner-clarification item for B3-B.
3. **Routing-context value validation** enforces the specification's `Dictionary(address::String)`
   typing (string values). No conformant counterexample is known; flagged for reviewer challenge.
4. **`n < −1` and `qid < −1` rejection** is a recorded implementation policy (the specification
   defines `−1` but does not spell out the rejection of smaller values); consistent with the value
   domains' meaning. Flagged for reviewer challenge.
5. **TELEMETRY `api` value domain (0–3)** is deliberately not codec-enforced (see §5); B3-B owns it.
6. **Unknown metadata keys are preserved**, not forwarded to any backend (B3-B decides forwarding
   policy); unknown request signatures are always rejected, never treated as no-ops.
7. **No canonical map ordering** is claimed: encoding preserves caller order; decoding preserves
   wire order with the B1 last-wins duplicate-key rule.
8. **IGNORED form** — see §8; B5-B verification of the specification form against the real driver
   is an open action.
9. Windows 10.0.26200 / net8.0 / .NET SDK 10.0.401 only; no other platform was exercised.

## 11. Remaining B3-B/B3-C responsibilities

- Legal state transitions (HELLO→LOGON→READY; FAILED/IGNORED/RESET recovery; GOODBYE shutdown) and
  response sequencing — including NC-08 (terminal SUCCESS sequence validation) and the observed
  pipelined `PULL` after `FAILURE` answered with `IGNORED`.
- Which authentication schemes are supported and whether credentials are accepted (B3-A only
  validates structure); credential verification and failure semantics.
- Contextual RECORD column-count matching against the RUN `fields` list (NC-05b boundary: the
  stateless codec enforces outer arity only — both halves are tested and documented).
- When to emit `IGNORED`; RESET interruption semantics; DISCARD lifecycle handling across the two
  observed driver paths (partial-fetch consumption and early session disposal — Q1-F04).
- Driver-compatibility metadata enforcement for HELLO SUCCESS (`server` required for the qualified
  driver per B0-B-Q1 D01; `connection_id`/`hints` optional) — the codec deliberately accepts any
  valid generic SUCCESS metadata; `bolt_agent` mandatory-ness (§10 item 2).
- PULL scheduling, cursor management, query execution and read-only enforcement (B3-C).

## 12. Implementation commit and tree

```text
Implementation commit: d99096a9689c4a7ce313c569cff2b165cfbda11a
Implementation tree:   6cb9cace72d403d496ca92a067c06510be64220c
Parent:                d5189af56c79bbdb86bbaaa156bfb2a0f4046c37 (B0-B owner acceptance)
Changed files:         12 (6 production under src/BoltSharp/Messages/, 6 test files under
                       tests/BoltSharp.Tests/Messages/); 3,264 insertions; no other path touched.
```

The report and evidence package are finalized in a follow-up documentation commit; the evidence
manifest records the hashes of both.

## 13. Independent reviewer handoff

The B3-A-Q1 reviewer is asked to falsify, with the specification and the evidence open:

1. **Signature dispatch** — that every 5.8 signature routes correctly, graph tags and unknown tags
   are never mis-decoded as messages, and BEGIN/COMMIT/ROLLBACK/ROUTE are classified
   `UnsupportedMessage` (never silently accepted, never decoded as no-ops).
2. **Arity and field validation** — the negative matrix (§7), especially the exact category
   assertions, `n`/`qid` domain policy (§10 item 4), and the HELLO `bolt_agent`/`user_agent`
   requiredness decisions (§5, §10 item 2).
3. **FAILURE schema** — 5.7+ field semantics, recursive bounded cause handling, prohibition of the
   pre-5.7 `code` field, and consistency with the Q1-F03 client observations.
4. **RECORD graph preservation** — typed identity (node id 1 vs relationship id 1), element IDs,
   direction; the NC-05b outer-arity vs contextual column-count boundary.
5. **Byte-level fixture conformance** — that the golden bytes come from the independent oracles
   (§6), that the encode tests reproduce the B0-B-qualified fixtures, and that the IGNORED
   discrepancy record (§8) is technically accurate against the preserved driver sources.
6. **Malformed-payload handling** — truncation, trailing bytes, invalid nested values, configured
   limits; that no parse failure can produce a response and that error messages cannot leak field
   values (inspect `ToString()` implementations and exception construction).
7. **Isolation** — no session state, no transport knowledge, no side effects; the codec is
   stateless and reusable (interleaved-decode tests).

Evidence: `docs/reviews/evidence/B3-A-IMPL-01/` (spec copy, driver-source excerpts, baseline and
final test logs, build log, `manifest.json` with SHA-256 over report, sources, tests and evidence).
