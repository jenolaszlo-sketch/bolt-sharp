# B3-C-IMPL-01 — Deterministic read-only reference graph backend

**Task:** B3-C-IMPL-01 — bounded B3-C implementation per `docs/implementation-tasks.md`
**Baseline (entry) HEAD:** `aae3d0e986c3cef1dd4e0e49356cffafd06094d4` / tree `ef144bfd289e9b2b3599ed5c2b3f46111f5bee47`
**Implementation commit:** `37fe40633624a62bc4c6bd6162de2c884e1f0188` / tree `4fc8d822d36352d002252421543fb60db89cc64e`
**Protocol:** Bolt 5.8 / PackStream 1.0
**Producer executor:** DeepSeek V4.1 Flash (provider `nvidia`, OpenCode). This record is **not**
independent qualification and **not** owner acceptance.

---

## Disposition

**B3-C-IMPL-01 — IMPLEMENTED, READY FOR INDEPENDENT B3-C-Q1 QUALIFICATION.**

The reference backend integrates with the accepted B3-A message contracts and the accepted B3-B
session engine **without changing either**: it implements the existing provisional
`IBoltOperationProvider`/`IBoltOperation` seam, executes an explicitly allowlisted query surface
against a deterministic immutable Alice/Bob/KNOWS graph, refuses everything else (writes,
multi-statement text, unsafe RUN options, missing parameters) deterministically, and preserves
the normative fail-closed retirement contract. 94 new tests (Level A unit, Level B real-session
integration, Level C protocol-byte golden comparisons) pass, together with the full historical
regression (core 1,137; sessions 119; messages 98; B0-B integration 19) and a scoped format check.
Historical evidence and both owner decisions verify byte-identical.

## 1. Preflight and baseline

| Item | Verified |
| --- | --- |
| Branch | `main` |
| Starting HEAD / tree | `aae3d0e…` / `ef144bfd…` (exact match with the commission) |
| Worktrees | Exactly one, canonical |
| Working tree / index | Clean at entry; no concurrent writer |
| Prior B3-C work | None (`src/BoltSharp/Backend/` and `tests/BoltSharp.Tests/Backend/` did not exist) |
| Push | None; `origin/main` unchanged |

Read before implementation: the governing spec (module boundaries including the planned
`Backend/` module and the spec §7 reference-backend fixture), `implementation-tasks.md` B3-C
acceptance, the B3-A and B3-B owner decisions (including all binding qualifications), the B3-B
implementation/R2/Q3 reports, the B1 graph-value types, the B3-A request/response contracts, the
B0-B t04 Alice/Bob/KNOWS fixture and its retained golden bytes.

## 2. Provider interface and integration design

Pipeline (unchanged layers; B3-C added at the provider position):

```text
B3-A codec -> B3-B session -> ReferenceOperationProvider -> ReferenceQueryEvaluator
           -> ReferenceGraph (immutable) -> ReferenceResultOperation -> B3-B PULL/DISCARD -> B3-A encode
```

- `ReferenceOperationProvider : IBoltOperationProvider` (no seam shape change). `Prepare` checks
  the RUN extra map against `ReferenceCapabilityPolicy`, evaluates the query through
  `ReferenceQueryEvaluator`, and returns a fresh `ReferenceResultOperation` or `null`.
- `null` maps to the session's deterministic recoverable `50N42`/`Request.Invalid` FAILURE — the
  same signal the seam already defines for "not supported". The seam cannot carry a ROLLBACK
  reason on the wire; the evaluator exposes `RefusalReason` (`unsupported-query`,
  `missing-parameter`) for tests and diagnostics only (documented limitation, §18).
- B3-C owns graph data, query recognition/evaluation, field metadata, record production,
  provider-side lifecycle and capability refusal. B3-B continues to own states, RUN/PULL/DISCARD
  orchestration, FAILED/IGNORED/RESET, ordering, cursor ownership, cleanup failure handling and
  contextual validation. B3-A continues to own message validation and encoding. Nothing was
  duplicated or re-implemented in the backend.

## 3. Reference graph fixture and identities

`ReferenceGraph` (immutable; read-only wrappers over defensive copies; no mutation API):

| Entity | Numeric ID | Element ID | Details |
| --- | ---: | --- | --- |
| Alice | 1 | `n:1` | labels `[Person]`, `name = "Alice"` |
| Bob | 2 | `n:2` | labels `[Person]`, `name = "Bob"` |
| Alice-KNOWS->Bob | 1 | `r:1` | type `KNOWS`, start `n:1` (id 1), end `n:2` (id 2), `since = 2024` |

Identities and values follow `docs/boltsharp-spec.md` §7 and the retained B0-B t04 fixture
(independently decoded 23/23 in B0-B-Q1); the relationship deliberately reuses numeric ID 1 to
catch node/relationship aliasing bugs. The graph is deterministic, in-memory, service-free and
fresh-process safe; the single `ReferenceGraph.Default` instance is shared immutably.

## 4. Supported-query matrix

Matching is exact against the whitespace-normalized text (all whitespace runs collapse to single
spaces, surrounding whitespace removed). Keyword case **is significant** in this reference
backend; lowercase variants are refused as unsupported (documented qualification, §18).

| Form | Query (exact) | Fields | Records |
| --- | --- | --- | --- |
| Q1 scalar | `RETURN 42 AS answer` | `["answer"]` | one record `[42]` |
| Q2 property | `MATCH (n:Person) RETURN n.name` | `["n.name"]` | `["Alice"]`, `["Bob"]` (deterministic fixture order) |
| Q2 ordered variant | `MATCH (n:Person) RETURN n.name AS name ORDER BY name` | `["name"]` | ascending `["Alice"]`, `["Bob"]` (ordinal order) |
| Q3 graph | `MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b` | `["a","r","b"]` | one record `[AliceNode, KnowsRelationship, BobNode]` |
| Q4 parameterized | `RETURN $value AS answer` | `["answer"]` | one record with the typed `value` parameter |

Q3 records reference the graph's own node/relationship instances (test-asserted with
`Assert.Same`), proving results derive from the graph model rather than a detached hard-coded
record. Fixture enumeration order is documented as a backend property, not a Cypher row-order
guarantee.

## 5. Unsupported-query and capability-refusal matrix

Everything not in §4 is refused with deterministic `null` → `50N42` recoverable FAILURE; nothing
is executed, nothing is answered with a fabricated empty result, and no operation is ever
acquired (tests assert `provider.Operations` stays empty):

| Category | Examples verified refused |
| --- | --- |
| Writes | `CREATE (n:Person)`, `MERGE (n:Person)`, `MATCH (n:Person) SET n.name = 'x'`, `MATCH (n) DETACH DELETE n`, `MATCH (n:Person) REMOVE n.name`, `DROP INDEX x` |
| Multi-statement | `RETURN 42 AS answer; MATCH (n) DELETE n` |
| Procedure/administration | `CALL db.labels()` |
| Substring tricks / comments | `/* supported phrase */ RETURN 42 AS answer`, `RETURN 42 AS answer // trailing comment` |
| Unsupported read shapes | `MATCH (n:Person) RETURN n`, `MATCH (n:Person) RETURN n.name ORDER BY name` (without the aliased form), `UNWIND [1,2] AS x RETURN x`, `RETURN 1 AS answer`, `RETURN 41 AS answer` |
| Case variants | `return 42 as answer` |
| RUN options | `mode` ∈ {`w`, `rw`, `write`}; non-null `db`; non-null `imp_user`; `tx_timeout`; `bookmarks`; `tx_metadata`; `notifications_*`; any unknown key |
| Accepted options | empty extra map; `mode = "r"`; explicit-null `db`; explicit-null `imp_user` |

Read-only enforcement is allowlist-based: an unsupported clause is refused even without a write
keyword. No blacklist is relied upon.

## 6. Query parameter handling

- Q4 reads the `value` key from the RUN parameter map (last-wins, consistent with accepted map
  read semantics). The typed `PackStreamValue` is passed through unchanged — never stringified,
  interpolated or re-parsed. Tests cover integer, string (including a query-like
  `"MATCH (n) DETACH DELETE n"` value that is returned verbatim and never executed), boolean, an
  explicit null (preserved as a null record value), and a two-element list (structure preserved).
- Missing `value` for Q4 is refused deterministically (`missing-parameter`). Unreferenced
  parameters are ignored; they cannot change execution semantics because no expression
  evaluation exists.
- No general parameter typing system exists: the accepted B3-A request model delivers typed
  PackStream values, and that is the entire boundary.

## 7. Field metadata and Bolt value mapping

- Field metadata is a fixed, stable `IReadOnlyList<string>` per supported form (§4); the session
  reads it once (B3-B-R2 single acquisition) and B3-B uses the snapshot for NC-05b column checks.
- Records use the repository's existing B1 graph values: `PackStreamNode` (4-field layout),
  `PackStreamRelationship` (8-field layout); scalars use `PackStreamInteger`/`PackStreamString`/
  `PackStreamBoolean`/`PackStreamNull` (C# null). No alternative serialization exists in the
  backend. The byte-path tests prove the encoded bytes equal the retained B0-B golden fixtures.
- Mapping is total for the fixture: every value round-trips through the accepted B3-A codec.

## 8. Result cursor lifecycle

`ReferenceResultOperation` implements the accepted seam with an index cursor over an immutable,
defensively copied record list:

- `ReadRow` is incremental (no queue, no background work) and returns records in order; after
  exhaustion it keeps returning `null` (never resurrects records).
- `Dispose` is idempotent and never throws; use after disposal throws
  `ObjectDisposedException` deterministically.
- Instances are created per RUN, never shared across operations/sessions, never reused after
  disposal. The session owns retirement; the registered tests assert exactly-once disposal
  (`DisposeCount == 1`) across completion, RESET, GOODBYE, Disconnect and session Dispose, and
  zero attempts while a cursor is merely active.

## 9. Resource limits

The reference graph and its results are finite and tiny; bounded in-memory record
materialization at preparation time is the documented behavior. The cursor abstraction itself is
pull-based and does not assume in-memory backends (no unbounded queues, no detached tasks, no
lazy work after preparation). No external service, filesystem database, network or time/random
dependency exists anywhere in the backend.

## 10. Failure and disposal behavior

The ten commissioned fault modes are covered by `ReferenceFailureTests` and the integration
file, always through the real session:

| # | Fault | Observed (asserted) |
| --- | --- | --- |
| 1 | Prepare throws before return | `50N00` FAILURE/`Failed`; no operation acquired; RESET recovers |
| 2 | Invalid metadata (null fields / null element) | contained FAILURE; one retirement attempt; `Failed` (clean disposal) |
| 3 | Metadata getter throws | contained; `FieldsReads == 1`; one attempt |
| 4 | First record read fails | `50N00` FAILURE/`Failed`; one attempt |
| 5 | Failure after one record | `RECORD, FAILURE` prefix preserved; `Failed` |
| 6 | Invalid record column count | no record emitted; `50N42` row-shape FAILURE; one attempt |
| 7 | Disposal throws (completion path) | `RECORD, FAILURE` + `Defunct`/close; **no SUCCESS**; one attempt; diagnostic retained |
| 8 | Read failure + disposal failure | primary `50N00` preserved; `Defunct`; diagnostic recorded separately |
| 9 | Metadata failure + disposal failure | `Defunct`; one attempt; RUN refused afterwards |
| 10 | Use after disposal | deterministic `ObjectDisposedException`; idempotent Dispose |

The critical accepted contract is re-established with the real backend: `RUN → PULL → disposal
failure → RESET → RUN` never opens operation #2 (`PrepareCount` stays 1, disposal attempts stay
1), and Disconnect/Dispose do not retry the failed disposal.

## 11. B3-B integrated conversation results (Level B, mandatory)

All conversations run through `BoltConnectionSession` with a counting provider; the tests
establish actual provider calls, response values, state transitions and disposal counts.

- **Scalar**: `HELLO → SUCCESS`, `LOGON → SUCCESS`, `RUN "RETURN 42 AS answer" → SUCCESS(fields=["answer"])`, `PULL → RECORD([42]), SUCCESS` → `Ready`; one prepare; `FieldsReads == 1`; one disposal.
- **Property**: `RUN "MATCH (n:Person) RETURN n.name" → SUCCESS(fields=["n.name"])`; `PULL → RECORD(["Alice"]), RECORD(["Bob"]), SUCCESS` → `Ready`. The task-acceptance shape `PULL(1)` twice returns Alice (has_more) then Bob (terminal).
- **Ordered variant**: fields `["name"]`, ascending.
- **Graph**: fields `["a","r","b"]`; the record carries the graph's own instances with verified labels, `name` properties, relationship type/direction, numeric IDs and all four element IDs; terminal sequence `RECORD, SUCCESS`; one disposal.
- **Parameterized**: typed values (integer, string, boolean, null, list) returned verbatim; missing parameter → recoverable `50N42` FAILURE with no operation acquired and RESET recovery.
- **Negative flows** (all asserted): RUN before LOGON → `08N06`/`Defunct`, zero provider calls; unsupported/write/multi-statement queries after auth → `50N42`/`Failed`, zero operations acquired, RESET recovers, a later supported RUN works; unsupported RUN capabilities (six cases) → `50N42`, zero operations; PULL before RUN → `08N06`/`Defunct`; second RUN while STREAMING → `08N06`/`Defunct` with cursor exactly-once retirement; partial PULL then DISCARD → advancement, no RECORD in DISCARD, cursor retired; recoverable read failure → `Failed` → RESET recovers → new RUN reacquires; cleanup failure → `Defunct`, RESET refused, no second operation; GOODBYE/Disconnect/session Dispose with an active cursor → exactly one disposal, no fabricated response, idempotent.

## 12. B3-A byte-level integration results (Level C)

`ReferenceBytePathTests` uses authentic request bytes (B3-A has no request encoder) and compares
responses byte-for-byte with retained B0-B golden fixtures:

| Check | Result |
| --- | --- |
| Authentic RUN bytes (`t03` `RETURN 42 AS answer`, extra `{"mode":"r"}`) | decoded and executed; `SUCCESS` encodes **exactly** `BoltMessageFixtures.B0B.RunSuccessFieldsAnswer` |
| Authentic PULL bytes (`{n:1000}`) | `RECORD [42]` encodes **exactly** `Record42`; decoded round-trip equals 42 |
| Graph RUN (typed request; no authentic bytes exist) | `SUCCESS` encodes **exactly** `RunSuccessFieldsGraph`; the graph RECORD encodes **exactly** `RecordGraph` (the authentic t04 golden: `Node(1,"n:1",[Person],name=Alice)`, `Relationship(1,1→2,KNOWS,"r:1","n:1","n:2",since=2024)`, `Node(2,"n:2",[Person],name=Bob)`) |
| Property query responses | every response encodes and decodes back to the same type; first record decodes to `"Alice"` |

This is byte-level proof that the reference backend emits the accepted B3-A graph
representation, without any transport listener.

## 13. New tests

94 tests in `tests/BoltSharp.Tests/Backend/` (Level A unit, Level B integration, Level C bytes):

| Class | Count | Focus |
| --- | ---: | --- |
| `ReferenceGraphTests` | 4 | identities, determinism, immutability, value equality |
| `SupportedQueryTests` | 21 | allowlist forms, normalization, parameters, refusal matrix |
| `ReadOnlyBoundaryTests` | 24 | provider-level refusal, RUN-option policy, capability reasons |
| `ReferenceResultTests` | 4 | cursor increments, exhaustion stability, disposal, defensive copies |
| `ReferenceSessionIntegrationTests` | 27 | real-session conversations and negative flows |
| `ReferenceFailureTests` | 11 | the ten fault modes plus use-after-disposal |
| `ReferenceBytePathTests` | 3 | authentic-byte and golden-fixture comparisons |

No historical test was modified or weakened; the new tests only add coverage.

## 14. Full regression results (executed at `37fe406`)

| Suite | Result | Exit |
| --- | --- | ---: |
| Core solution | **1,137 / 0 / 0** (1,043 historical + 94 new) | 0 |
| B3-C backend filter | **94 / 0** | 0 |
| B3-B session filter | **119 / 0** | 0 |
| R1 remediation class | **21 / 0** | 0 |
| R2 remediation class | **18 / 0** | 0 |
| B3-A message filter | **98 / 0** | 0 |
| B0-B official-driver integration | **19 / 0** | 0 |
| Formatting (`dotnet format BoltSharp.slnx --verify-no-changes --include src/BoltSharp/Backend tests/BoltSharp.Tests/Backend`) | no changes required | 0 |

Historical floors are all met or exceeded. No CI-equivalent pipeline exists in the repository
(no workflow files); the commands above are the available gates.

## 15. Build results and warnings

Full rebuild: **0 errors, 2 warnings** — both pre-existing `xUnit2012` at
`tests/BoltSharp.Tests/Messages/BoltRequestDecodeTests.cs:71-72`. No warning is attributable to
B3-C; the new code is format-clean (§14).

## 16. Historical evidence verification

| Evidence package | Result |
| --- | ---: |
| B3-B-Q3 | **33/33** |
| B3-B-R2 | **29/29** |
| B3-B-Q2 | **38/38** |
| B3-B-Q1 | **12/12** |
| B3-A-Q1 / R1 / Q2 | 14/14, 13/13, 15/15 |
| B0-B producer / Q1 | 24/24, 119/119 |
| B3-B-IMPL-01 (informational) | 19/22 — the three entries evolved by the authorized R1/R2 remediations |
| B3-A-IMPL-01 (informational) | 16/21 — the documented B3-A-R1 delta |

Both owner decisions remain **byte-identical** (`git diff aae3d0e… HEAD` empty). No historical
report, probe, manifest or evidence file was modified; no manifest was rewritten to reflect the
new tree. Full details in `historical-integrity.json` / `historical-integrity-summary.txt`.

## 17. Changed source/test paths

Implementation commit `37fe406` adds exactly twelve files (no modifications):

- `src/BoltSharp/Backend/ReferenceGraph.cs`, `ReferenceQueryEvaluator.cs`,
  `ReferenceOperationProvider.cs`, `ReferenceResultOperation.cs`
- `tests/BoltSharp.Tests/Backend/`: `BackendTestSupport.cs`, `ReadOnlyBoundaryTests.cs`,
  `ReferenceBytePathTests.cs`, `ReferenceFailureTests.cs`, `ReferenceGraphTests.cs`,
  `ReferenceResultTests.cs`, `ReferenceSessionIntegrationTests.cs`, `SupportedQueryTests.cs`

No PackStream, Framing, Handshake, Messages, Sessions, solution, build-configuration or
dependency change. No new runtime dependencies (no `Neo4j.Driver` anywhere in production code).

## 18. Known limitations (B3-C-specific qualifications)

1. **Refusal granularity on the wire.** The accepted seam returns only `null`/operation, so every
   refusal (unsupported query, missing parameter, unsafe RUN option) surfaces as one bounded
   `50N42`/`Request.Invalid` FAILURE. The evaluator's richer `RefusalReason` is diagnostic-only.
2. **Exact-form matching.** Only whitespace normalization is accepted; keyword case is
   significant; aliases must match the documented forms exactly. This is deliberate (allowlist,
   never substring matching) but narrower than Cypher's case-insensitive keywords.
3. **Parameter surface.** Only the `value` parameter of the Q4 form is recognized; the parameter
   name is fixed; no other parameter shapes or expressions are accepted. Explicit null and list
   values are preserved; no general type coercion exists.
4. **Bounded materialization.** Results are materialized at preparation time. Adequate for the
   reference graph; future backends with streaming sources must not copy this assumption.
5. **`has_more` absence on final summaries (B3-B observation).** The accepted B3-B engine emits
   `SUCCESS {}` at PULL/DISCARD exhaustion (no `has_more` field), while the retained real-server
   t03 golden carries `has_more:false`. Existing B3-B acceptance tests treat absence as false.
   B3-C does not change B3-B emissions; this distinction is flagged for B5-B driver
   interoperability qualification.
6. **No request encoder.** Level C uses authentic request bytes where available and typed
   requests otherwise; B3-A currently exposes no request-encoding API.
7. **No transport/authentication integration.** No TCP/WebSocket listener, no TLS, no
   authentication changes; those remain B4/B5 work.
8. **No general Cypher.** By design: no lexer, parser, planner, expression evaluation or
   ordering beyond the single documented `ORDER BY name` variant.

All fourteen B3-B owner-decision qualifications remain binding and untouched; none is silently
closed by this milestone.

## 19. B5 integration dependencies

- Official `Neo4j.Driver` interoperability remains unverified end-to-end (no production path yet);
  the driver may exercise the `has_more`-absence nuance (§18.5) and bounded placeholder error
  codes (Q-B3B-06).
- The zero-field `IGNORED` live-driver verification (B3-A F-02 / Q-B3B-12) remains deferred to
  B5-B.
- Transport (TCP/TLS) and Browser-facing behavior remain B4/B5; kill/teardown and cancellation
  semantics stay as qualified by B3-B (Q-B3B-02/Q-B3B-08).
- The provisional provider seam is intended to be consumed, not redesigned, by B4/B5; any
  interface change requires separate authorization.

## 20. Implementation commits and trees

| Stage | Commit | Tree |
| --- | --- | --- |
| Entry baseline (B3-B owner acceptance) | `aae3d0e986c3cef1dd4e0e49356cffafd06094d4` | `ef144bfd289e9b2b3599ed5c2b3f46111f5bee47` |
| B3-C implementation + tests | `37fe40633624a62bc4c6bd6162de2c884e1f0188` | `4fc8d822d36352d002252421543fb60db89cc64e` |
| Report + evidence | recorded in the final handoff (derivable from the commit adding this report) | — |

## 21. Independent B3-C-Q1 reviewer handoff

Q1 should independently challenge, with its own probes and without reusing producer tests:
query correctness (scalar/parameter/node/relationship values, labels, direction, endpoints,
numeric and element IDs, field names and column counts, exhaustion, deterministic fixture
behavior); capability safety (unsupported queries, mutation attempts, multi-statement text,
unsupported RUN extras, incorrect/missing parameter types, allowlist-bypass attempts, and the
documented case/whitespace boundaries of §18.2); resource safety (partial PULL, DISCARD, RESET,
GOODBYE, Disconnect, provider exceptions, throwing Dispose, exactly-once retirement, no recovery
after failed disposal, no second operation after terminal cleanup); and integration correctness
(actual B3-B state-machine use, B3-A value compatibility, response sequencing, no fabricated
success, no duplicate terminal summary, no weakening of prior acceptance tests). The evidence
directory contains the reproducible runner, raw logs and TRX results, and byte-level golden
comparisons; the report's §18 limitations should be tested as boundaries, not treated as bugs.

Evidence: `docs/reviews/evidence/B3-C-IMPL-01/` (`Run-B3C.ps1`, logs, TRX results,
`historical-integrity.json`, `reviewed-inputs.json`, `manifest.json`).

