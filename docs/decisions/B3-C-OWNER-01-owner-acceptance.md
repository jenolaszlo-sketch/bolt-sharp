# B3-C-OWNER-01 — owner acceptance of the deterministic read-only reference graph backend

**Status:** ACCEPTED WITH QUALIFICATIONS (owner decision recorded 2026-11-09)
**Date:** 2026-11-09 (Asia/Manila; execution timestamps are UTC)
**Task:** B3-C-OWNER-01 — formal owner acceptance and B3-C milestone closure
**Canonical branch:** `main` (one worktree)
**Protocol target:** Bolt 5.8 / PackStream 1.0
**Starting HEAD / tree:** `9d97c3bd55f59f864e87adf1a15bf43e4ffbd243` / `bcafccedde7d992497d91756e0399956fd0edf5f`

This record is an owner decision. It contains **no implementation**, modifies no production, test,
historical or evidence file, and makes **no** Neo4j / `Neo4j.Driver` / Browser compatibility claim.
It records acceptance only for the bounded scope in §8–§12, and references — rather than replaces —
the B3-C implementation report and the independent qualification.

---

## 1. Owner decision

**Owner decision: B3-C — ACCEPTED WITH QUALIFICATIONS — CLOSED.**

The owner reviewed the B3-C qualification chain — the implementation
([B3-C-IMPL-01](../reviews/B3-C-IMPL-01.md): *IMPLEMENTED, READY FOR INDEPENDENT B3-C-Q1
QUALIFICATION*) and the independent qualification
([B3-C-Q1](../reviews/B3-C-Q1-independent-qualification.md): *SUPPORTED WITH QUALIFICATIONS*) —
against the committed evidence under `docs/reviews/evidence/B3-C-IMPL-01/` and
`docs/reviews/evidence/B3-C-Q1/`, the actual backend sources and tests, and the accepted B3-A/B3-B
boundaries, and records:

1. **B3-C is accepted with qualifications**, within the scope of §8–§12, bounded to Bolt 5.8 /
   PackStream 1.0 and the deterministic reference backend.
2. The B3-C implementation is complete within its authorized reference-backend scope; no material
   runtime defect was identified by this decision or remains outstanding.
3. The exact-form query allowlist, read-only capability enforcement, typed parameter handling,
   cursor lifecycle and fail-closed resource behavior are accepted as implemented and qualified.
4. **The B3-B normative fail-closed retirement contract remains binding and is preserved by B3-C**
   (§11): an unsuccessful retirement attempt permanently invalidates the connection.
5. **Q-B3C-01 through Q-B3C-06 are binding qualifications** (§17) and are **not** closed by this
   acceptance.
6. **All fourteen B3-B owner qualifications remain binding** and are carried forward (§18); none
   is silently closed because B3-C exercised related behavior.
7. The three Q1 documentation findings receive explicit **ACCEPTED DOCUMENTATION OBLIGATION -
   NON-BLOCKING** dispositions (§16).
8. **The reviewer-executor overlap is explicitly acknowledged** (§15): B3-C-Q1 was executed by the
   same agent session as the B3-C producer. A bounded owner waiver is recorded with rationale and
   a continuing different-executor requirement for future release-grade qualification; no claim of
   organizational independence is made.
9. **B4 and B5 are not authorized** by this decision (§21); each requires its own commission.
10. Reviewer dispositions are reviewer conclusions, not owner decisions. Recording acceptance does
    not modify, supersede or rewrite any report, probe, manifest or evidence file.

## 2. Verified entry state

Independently read from the canonical checkout before this decision:

```text
git branch --show-current  -> main
git rev-parse HEAD         -> 9d97c3bd55f59f864e87adf1a15bf43e4ffbd243 (matches the commission)
git rev-parse HEAD^{tree}  -> bcafccedde7d992497d91756e0399956fd0edf5f (matches the commission)
git worktree list          -> one worktree (D:/projects/bolt-sharp)
git status --porcelain     -> clean; no concurrent writer
prior B3-C-OWNER-01        -> none existed (docs/decisions/ contained only the B0-A/B0-B/B2-B/B2-C/B3-A/B3-B decisions)
push state                 -> origin/main (9abf5e3724a31aff085b3810ebb9bd3a7783ea2a) unchanged; nothing pushed
```

## 3. B3-C scope and prerequisites

B3-C was commissioned as a **deterministic, read-only reference graph backend** executing an
explicitly allowlisted query surface through the accepted B3-B session engine — a reference
backend, not a general-purpose Cypher database. Prerequisites verified: B3-A and B3-B are
**ACCEPTED WITH QUALIFICATIONS — CLOSED** (`docs/decisions/B3-A-OWNER-01-owner-acceptance.md`,
`docs/decisions/B3-B-OWNER-01-owner-acceptance.md`), and the B3-B normative resource-lifecycle
contract is binding on B3-C.

## 4. Evidence reviewed

- `docs/reviews/B3-C-IMPL-01.md` and `docs/reviews/evidence/B3-C-IMPL-01/` (26-entry manifest,
  regression record, format check, byte-golden comparisons).
- `docs/reviews/B3-C-Q1-independent-qualification.md`,
  `docs/reviews/probes/B3-C-Q1/IndependentProbe/` (reviewer probe sources) and
  `docs/reviews/evidence/B3-C-Q1/` (125-check probe results + byte-identical repeat run,
  manifest verification, owner-decision diff, t04 provenance, regression logs/TRX).
- `docs/reviews/B3-B-Q3-independent-requalification.md` and the B3-B R2/Q3 history for the
  lifecycle contract.
- `docs/boltsharp-spec.md` (§5 module boundaries, §7 reference-backend fixture, §11 working
  contract), `docs/implementation-tasks.md` (B3-C task and acceptance), the B3-A owner decision,
  the B0-B t03/t04 authentic fixtures and B0-B-Q1 independent decode.
- The actual implementation (`src/BoltSharp/Backend/`, four files) and producer tests
  (`tests/BoltSharp.Tests/Backend/`, eight files) were inspected directly.

## 5. Qualification chronology

| Stage | Revision / tree | Disposition |
| --- | --- | --- |
| B3-A owner acceptance | (accepted; see B3-A decision) | ACCEPTED WITH QUALIFICATIONS — CLOSED |
| B3-B owner acceptance | `aae3d0e` / `ef144bfd289e9b2b3599ed5c2b3f46111f5bee47` | ACCEPTED WITH QUALIFICATIONS — CLOSED |
| B3-C implementation | `37fe406` / `4fc8d822d36352d002252421543fb60db89cc64e` | IMPLEMENTED, READY FOR INDEPENDENT B3-C-Q1 QUALIFICATION |
| B3-C implementation evidence | `79ddd04` / `bdc50fb27f86bc6f9e7fbbf50157ec5f3d320adf` | evidence recorded |
| B3-C-Q1 | `9d97c3b` / `bcafccedde7d992497d91756e0399956fd0edf5f` | SUPPORTED WITH QUALIFICATIONS |
| Owner-acceptance commit (this file) | recorded in the final handoff (this file cannot contain its own SHA) | — |

Ancestry is linear and additive: `aae3d0e → 37fe406 → 79ddd04 → 9d97c3b`. All four revisions and
trees were verified in the canonical checkout; no discrepancy was found. The producer's
implementation disposition and the reviewer's qualification disposition are recorded as their
conclusions; this document is the owner decision.

## 6. Evidence integrity and change isolation

Manifest verification at `9d97c3b` (working-tree bytes):

| Evidence package | Result |
| --- | ---: |
| B3-C-Q1 | **29/29** |
| B3-C-IMPL-01 | **26/26** |
| B3-B-Q3 | **33/33** |
| B3-B-R2 | **29/29** |
| B3-B-Q2 | **38/38** |
| B3-B-Q1 | **12/12** |
| B3-A-Q1 | **14/14** |
| B3-A-R1 | **13/13** |
| B3-A-Q2 | **15/15** |
| B0-B producer | **24/24** |
| B0-B-Q1 | **119/119** |

Known historical source evolution, confirmed at blob level against the recorded historical
commits (not assumed): B3-B-IMPL-01 **19/22** (three entries changed by authorized R1/R2
remediations, verified at `9f224cf`), B3-B-R1 **12/14** (two entries changed by R2, verified at
`454ae00`), B3-A-IMPL-01 **16/21** (five entries changed by the authorized B3-A-R1 remediation).
Historical manifests describe their original revisions, not the latest production tree; nothing
was rewritten.

**Change isolation** since the B3-B owner acceptance: exactly four new production backend files,
eight new backend test files, and the additive B3-C report/evidence/Q1 artifacts. Core protocol
paths (`src/BoltSharp/Messages`, `PackStream`, `Framing`, `Handshake`) unchanged since `9f224cf`;
both owner decisions byte-identical; no dependency, solution or build-configuration change.

## 7. Regression results (freshly executed for this decision)

Executed read-only at `9d97c3b` on 2026-11-09 (Windows 10.0.26200, .NET SDK 10.0.401, MSBuild
18.9.11; build via `BoltSharp.slnx` with `--no-incremental --nodeReuse:false`, artifacts directed
outside the repository; no repository file changed):

| Suite | Result |
| --- | ---: |
| Core test project | **1,137 passed / 0 failed / 0 skipped** |
| B3-C backend filter | **94 passed / 0 failed** |
| B3-B session filter | **119 passed / 0 failed** |
| B3-A message filter | **98 passed / 0 failed** |
| B0-B official-driver integration project | **19 passed / 0 failed** |
| Build | **0 errors; 2 warnings** — both pre-existing `xUnit2012` at `BoltRequestDecodeTests.cs:71-72`; no new warning |
| Scoped format check (`src/BoltSharp/Backend`, `tests/BoltSharp.Tests/Backend`) | **exit 0** |

The independent Q1 probe was verified from the committed evidence: **125 checks / 125 passing /
180 traces / exit 0**, with the committed repeat run **byte-identical** (SHA-256 match) — fresh-
process determinism. These reproduce the counts recorded in the B3-C-IMPL-01 and B3-C-Q1
evidence exactly; nothing was reinterpreted.

## 8. Accepted reference graph

The owner accepts the deterministic immutable reference graph fixture, as independently verified
by Q1 against the spec (§7) and the B0-B-Q1 standalone decode of the authentic t04 fixture:

| Entity | Identity | Details |
| --- | --- | --- |
| Alice | numeric ID `1`, element ID `n:1` | label `[Person]`, property `name = "Alice"` |
| Bob | numeric ID `2`, element ID `n:2` | label `[Person]`, property `name = "Bob"` |
| Alice-KNOWS→Bob | numeric ID `1` (deliberate collision with Alice's node ID), element ID `r:1` | type `KNOWS`, start `1`/`n:1`, end `2`/`n:2`, property `since = 2024` (integer) |

Verified evidence basis: Q1 independently confirmed correct node and relationship identities,
distinct node/relationship semantics despite the numeric-ID collision, correct direction and
endpoints, correct labels and properties, immutable contents (mutation attempts through every
exposed access path rejected), stable identities across repeated execution and fresh processes,
and the accepted B1 `PackStreamNode`/`PackStreamRelationship` representation — with the encoded
RECORD byte-identical to the authentic t04 golden. Q1's alternate-fixture probe (Carol/Dave,
`5/n:5`, `6/n:6`, rel `3/r:3`, `since=1999`) demonstrated through the real session that results
derive from the graph data rather than hard-coded Alice/Bob records.

**Not claimed:** general graph storage, graph mutation, dynamic data, or production database
behavior. The graph is a fixed two-node fixture; no mutation API exists.

## 9. Accepted query capabilities

The owner accepts the exact-form evaluator for the five supported forms only:

| Form | Fields | Records |
| --- | --- | --- |
| `RETURN 42 AS answer` | `["answer"]` | one record `[42]` |
| `MATCH (n:Person) RETURN n.name` | `["n.name"]` | `["Alice"]`, `["Bob"]` (deterministic fixture order) |
| `MATCH (n:Person) RETURN n.name AS name ORDER BY name` | `["name"]` | ascending `["Alice"]`, `["Bob"]` |
| `MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b` | `["a","r","b"]` | one typed graph record |
| `RETURN $value AS answer` | `["answer"]` | one record with the typed `value` parameter |

Accepted: correct field metadata; correct scalar and graph values; correct result counts and
arity; deterministic fixture enumeration (documented as a backend property, not a Cypher
row-order guarantee); ascending ordering for the single supported ORDER BY variant; typed
parameter propagation with explicit-null/missing distinction; preserved list and nested-map
parameter values within the verified surface. This is an **exact-form query evaluator**: general
Cypher parsing, execution, planning or compatibility is **not** claimed (§13, §17).

## 10. Read-only enforcement

Accepted as independently verified by Q1's adversarial battery: write statements (CREATE/MERGE/
SET/REMOVE/DETACH DELETE/DROP), procedure and administration calls, multi-statement text,
comments and substring tricks, unknown read shapes, wrong labels/relationship types/directions,
and unsupported RUN capabilities are **refused** with a deterministic recoverable `50N42` FAILURE;
refused queries acquire **no** operation, mutate **nothing**, and the session recovers safely via
RESET. The boundary is an explicit allowlist with documented whitespace normalization (§17,
Q-B3C-01/Q-B3C-05) — not a blacklist, and not general query-language protection. Unknown forms
fail closed.

## 11. B3-B integration and resource safety

Accepted as independently demonstrated through the **actual B3-B session engine** (Level B
integration; evaluator-only tests were treated as insufficient): authentication gating; provider
preparation; single metadata acquisition; field metadata; PULL/DISCARD including partial,
repeated, complete and `n=-1` forms; correct record arity; exhaustion stability; correct state
transitions; exactly-once cursor disposal; recoverable failure handling; RESET recovery when
retirement succeeded; GOODBYE and Disconnect lifecycle.

**Normative fail-closed retirement contract (inherited from B3-B, binding):** an unsuccessful
retirement attempt permanently invalidates the connection — DEFUNCT, no RESET recovery, no
subsequent RUN, no second disposal attempt, primary/cleanup failures distinguishable. Q1's
mandatory overlap control (`RUN → PULL → retirement fails → RESET → RUN`) was verified with the
real backend: one operation opened, one disposal attempt, DEFUNCT, no RESET recovery, no second
operation, no retry — and the positive control (clean retirement after a recoverable error)
preserves FAILED/RESET recovery.

## 12. Byte-level compatibility

Accepted bounded claims: authentic t03 RUN/PULL request bytes decode and execute through B3-B and
B3-C, with `RUN SUCCESS` and `RECORD [42]` encoding **byte-for-byte** to the retained B0-B goldens
(`RunSuccessFieldsAnswer`, `Record42`); the graph RUN/RECORD responses encode **byte-for-byte** to
`RunSuccessFieldsGraph` / `RecordGraph` (the authentic t04 fixture: `Node(1,"n:1",[Person],
name=Alice)`, `Relationship(1,1→2,KNOWS,"r:1","n:1","n:2",since=2024)`, `Node(2,"n:2",[Person],
name=Bob)`); decoded values match Q1's independent structured oracle element-by-element; a
one-byte corruption of the t04 golden is detected by that oracle.

**Provenance discipline:** the t03/t04 goldens originate from B0-B authentic driver captures
(introduced in B3-A `d99096a`, byte-stable since, independently decoded 23/23 by B0-B-Q1's
standalone decoder) — they are historical observation fixtures, **not** bytes captured from a
B3-C production network host. No production transport host exists or is accepted by this
milestone.

## 13. Terminal SUCCESS metadata disposition

**ACCEPTED QUALIFICATION.** B3-B emits the PULL/DISCARD terminal summary as `SUCCESS {}` (no
`has_more` field at exhaustion); the retained real-server t03 golden carries `has_more:false`
explicitly. Under the Bolt specification an absent `has_more` defaults to false, so the
client-visible semantics are equivalent: the observed difference does not establish an incorrect
result, and wire-level byte equality for this terminal summary is **not** claimed. Historical
golden bytes are preserved unchanged. Live-driver verification of the terminal-summary form (and
of the zero-field IGNORED form, Q-B3B-12) remains a **B5-B** responsibility.

## 14. Unsupported-query error metadata disposition

**ACCEPTED WITH QUALIFICATION.** Unsupported queries and capabilities fail through the session's
recoverable path with the exact four-field accepted FAILURE shape — `gql_status = 50N42`,
`neo4j_code = Neo.ClientError.Request.Invalid`, description and empty `diagnostic_record` — with
no query-text or parameter echo (verified by Q1's marker test) and no graph mutation. The owner
accepts `50N42` as the current bounded reference-backend classification for unsupported
operations: it is a generic unexpected-request status, and **precise production error-code
compatibility for every unsupported operation is not qualified**. More granular mapping may be
evaluated during B5-B interoperability work or a separately authorized remediation; no immediate
backend change is required absent an established material defect.

## 15. Reviewer-independence governance decision

**Disclosed fact:** B3-C-Q1 was executed by the same agent session (DeepSeek V4.1 Flash,
OpenCode) as the B3-C implementation producer. This does **not** establish strict
reviewer-executor independence, and this acceptance must not be described as an
organizationally independent signoff.

**Governance check.** The applicable repository governance (`docs/boltsharp-spec.md` §11)
requires that "a separate reviewer/model checks against the official protocol docs and golden
fixtures" before owner acceptance; no repository rule makes a different *executor session*
mandatory and unwaivable for milestone acceptance, and the B3-A/B3-B owner decisions established
the precedent of recording executor-level facts and caveats explicitly rather than silently.

**Bounded waiver (explicit, not implicit).** The owner accepts the Q1 evidence for this bounded
reference-backend milestone with the following rationale:

1. The B3-A/B3-B foundations were previously accepted with their own review chains.
2. Q1's expected values were independently constructed from the specification and the B0-B-Q1
   standalone decode, not from production code or producer fixtures.
3. Alternate-fixture testing falsified any hard-coded-response implementation.
4. Query recognition was challenged adversarially (30 unsupported texts, normalization matrix,
   bounded-input probes).
5. Byte comparisons used historical authentic evidence with a one-byte-corruption detection
   control.
6. Resource safety was exercised through the actual B3-B session with observable counters
   (opens, disposal attempts, metadata reads, state transitions).
7. All 125 reported Q1 checks passed; two fresh-process runs were byte-identical.
8. Negative controls demonstrated detection of each targeted defect mode.
9. No material runtime defect was found by Q1 or by this decision's own verification.
10. Release-grade driver/Browser interoperability remains outside B3-C's accepted scope, where
    stricter independence can be re-established.

**Continuing limitation:** future release-grade and external-client interoperability
qualification (B5-B and any package/publication review) must use a **different executor
session/model** than the corresponding implementer, per the B3-B owner decision's standing
requirement. The historical Q1 byline and provenance are preserved unchanged.

## 16. Q1 documentation findings

Q1 recorded three non-blocking documentation findings. Each was inspected against the B3-C
report and the implementation; none affects runtime correctness.

| Finding | Discrepancy | Non-blocking because | Correction needed before a future milestone? | Disposition |
| --- | --- | --- | --- | --- |
| B3-C-Q1-DOC-01 | The B3-C report's supported-form table does not state that whitespace collapsing also accepts multi-line formatting of supported forms (a multi-line `MATCH (n:Person) RETURN n.name` is accepted) | Behavior matches the documented normalization rule and the implementation; verified by Q1's `norm-multiline-supported-form` check | Documentation-only; fold into any future query-surface change | **ACCEPTED DOCUMENTATION OBLIGATION — NON-BLOCKING** |
| B3-C-Q1-DOC-02 | The supported-form table does not repeat that keyword case is significant (documented in B3-C report §18.2); lowercase variants are refused | Behavior matches the documented exact-form rule; verified by Q1's case checks | Documentation-only; repeat next to the forms in future docs | **ACCEPTED DOCUMENTATION OBLIGATION — NON-BLOCKING** |
| B3-C-Q1-DOC-03 | B3-B's terminal `SUCCESS {}` differs at byte level from the real-server `has_more:false` golden | Semantically equivalent under the accepted Bolt interpretation (§13); wire equality not claimed | Surface in the B5-B driver-interop test plan | **ACCEPTED DOCUMENTATION OBLIGATION — NON-BLOCKING** (overlaps Q-B3C-06 / Q-B3B-12) |

No historical report was edited; none of these requires correction before B4 entry, though
DOC-03 must inform the B5-B test plan.

## 17. Six binding B3-C qualifications

Carried in substance from B3-C-Q1 §17; these bind B3-C from acceptance onward:

- **Q-B3C-01 — Exact-form query allowlist.** Only the explicitly supported query forms and the
  documented whitespace-normalization variants are accepted (case-sensitive; multi-line
  whitespace-collapsed forms accepted). General Cypher compatibility is not claimed. Any future
  loosening requires requalification.
- **Q-B3C-02 — Refusal granularity.** All backend refusals surface as the single bounded
  `50N42`/`Request.Invalid` FAILURE (the seam cannot carry reasons; richer reasons are
  diagnostic-only). Precise production error-code compatibility for unsupported operations
  remains unqualified (see §14).
- **Q-B3C-03 — Parameter surface.** Parameterized execution is limited to the supported `$value`
  query form and the verified value types (scalars, explicit null, lists, nested map). General
  parameterized Cypher execution is not supported.
- **Q-B3C-04 — Bounded materialization.** Results are materialized at preparation over the
  finite in-memory graph; large-scale query execution and production backend scalability are not
  claimed; future backends must not copy this assumption.
- **Q-B3C-05 — Query normalization.** Any `char.IsWhiteSpace` run collapses to a single space and
  surrounding whitespace is removed; multi-line and NBSP variants of supported forms are
  therefore accepted. The exact demonstrated behavior is binding; other query transformations
  (case folding, comments, semicolons) remain unsupported.
- **Q-B3C-06 — Terminal SUCCESS representation.** The omitted `has_more` field at exhaustion is
  accepted as semantically terminal (defaults to false). Byte-for-byte terminal-summary identity
  with the real-server fixture is not claimed; live-driver verification remains deferred to
  B5-B.

## 18. Fourteen inherited B3-B qualifications (carry-forward)

All fourteen qualifications from `docs/decisions/B3-B-OWNER-01-owner-acceptance.md` §12 remain
binding; none is closed by B3-C:

- **Q-B3B-01 — Bounded PULL behavior.** `PULL(n=-1)` uses bounded incremental delivery through
  the accepted `has_more` mechanism; a client may need subsequent PULL requests. No general claim
  of unbounded streaming or complete interoperability is authorized. (Q1 §19.1)
- **Q-B3B-02 — RESET interruption.** Synchronous recovery only; no full mid-flight jump-ahead
  interruption; a blocked provider cannot currently be preempted. Full interruption requires
  further design and qualification during B3-C/B5 integration. (Q1 §19.2)
- **Q-B3B-03 — Batch ownership.** `ReceiveBatch` preserves unprocessed requests with the caller
  but does not expose an ideal consumed/remaining-count contract; the transport adapter must not
  silently discard unprocessed messages. (Q1 §19.3)
- **Q-B3B-04 — Session concurrency.** Single-owner execution model; not qualified as
  thread-safe; concurrent callers must be serialized externally. The type-documentation gap
  (Q3-DOC-02) should be closed before broader public consumption. (Q1 §19.5)
- **Q-B3B-05 — Provisional public API.** The session/provider interfaces remain provisional
  (visibility chosen to avoid the observed `InternalsVisibleTo` overload-resolution regression);
  no stable public compatibility guarantee is implied. (Q1 §19.6)
- **Q-B3B-06 — Failure metadata.** Some non-TELEMETRY error codes are bounded placeholders; B5-B
  must verify appropriate error semantics during real-driver interoperability. (Q1 §19.7)
- **Q-B3B-07 — TELEMETRY structural boundary.** Wrong-typed TELEMETRY is rejected by B3-A; B3-B
  owns domain validation for valid integer message structures. (Q1 §19.4)
- **Q-B3B-08 — Cancellation.** No general asynchronous cancellation-token or
  provider-interruption API; do not claim full interruptible operation execution. (Q1 §19.9)
- **Q-B3B-09 — Driver interoperability.** B0-B integration tests remain passing, but they do not
  prove that the production B3-B engine is fully interoperable with `Neo4j.Driver`; actual
  production-path interoperability remains B5 work.
- **Q-B3B-10 — Existing build advisories.** Two pre-existing `xUnit2012` advisories remain
  outside B3-B scope. (Q1 §19.8)
- **Q-B3B-11 — Reviewer independence.** Q3 was executed by the same agent session as R2;
  accepted under the B3-B bounded waiver, subject to applicable project rules and the continuing
  different-executor requirement for release-grade qualification. (Q1 §19.10)
- **Q-B3B-12 — B3-A F-02.** Live-driver verification of the zero-field IGNORED representation
  remains deferred to B5-B; not closed merely because B3-B uses the correct encoding.
  (B3-A §6.3)
- **Q-B3B-13 — Fail-closed supersession.** The normative retirement contract (§11) supersedes
  historical expectations; the unchanged Q2 probe continues to report exit 4 by design and must
  not be "fixed" historically. (Q3 §15)
- **Q-B3B-14 — Diagnostic semantics.** `LastCleanupFailure` exposes the raw provider exception
  to callers (potentially sensitive text); diagnostic replacement across successive failed
  retirements is unreachable by design. (Q3 §12/§15)

## 19. Explicit non-claims

B3-C acceptance does **not** claim or establish:

- general Cypher support or arbitrary MATCH expressions;
- graph mutation or persistent graph storage;
- general transaction execution, query planning or optimization;
- general ORDER BY support beyond the single documented variant;
- arbitrary procedure execution;
- routing or clustering;
- production graph-database scalability;
- complete Bolt 5.8 conformance;
- complete `Neo4j.Driver` interoperability or Browser interoperability;
- production TCP/TLS or WebSocket transport;
- full RESET jump-ahead interruption;
- thread-safe concurrent session execution;
- stable public backend API compatibility;
- security certification, production deployment readiness or NuGet release readiness.

A deterministic reference graph is not a substitute for a production database engine; the
accepted result is an integrated, bounded reference implementation suitable for subsequent
transport and interoperability work.

## 20. B3 milestone closure relationship

B3 comprises three commissioned components, each accepted separately with qualifications:
B3-A (stateless message contracts), B3-B (connection state machine) and now B3-C (reference
graph backend). With this decision, **the three commissioned B3 components are complete within
their individually accepted scopes** — "B3 implementation scope complete, subject to the
inherited and newly accepted qualifications." This does **not** establish full Bolt protocol
compatibility; the authoritative B3-C disposition remains **B3-C — ACCEPTED WITH
QUALIFICATIONS — CLOSED**. No separate umbrella B3 owner-decision file is created.

## 21. Future B4/B5 entry considerations

The next engineering decision concerns transport and client interoperability. The owner records
the following non-authorizing considerations for whichever commission is chosen next:

- **B4 (Browser/WebSocket)** would produce the earliest real external-client demonstration
  (Browser rendering the reference graph) and reuses B3-A/B3-B/B3-C unchanged; it requires the
  Kestrel WebSocket adapter, TLS and loopback defaults, and Browser-specific HELLO metadata
  verification.
- **B5 (TCP/TLS + official driver)** provides typed-graph interop with `Neo4j.Driver`, but
  inherits the deferred live verifications: zero-field IGNORED (Q-B3B-12), terminal-summary
  `has_more` form (Q-B3C-06), placeholder error codes (Q-B3B-06), and TLS requirements.
- Both must respect: single-owner session execution, the fail-closed retirement contract,
  bounded streaming, unprocessed batch ownership, the provisional APIs, and the different-
  executor qualification requirement for release-grade review.

**No B4 or B5 implementation is authorized by this commission**; each requires its own
specification, implementation and independent qualification milestones.

## 22. Formal owner disposition

**B3-C — ACCEPTED WITH QUALIFICATIONS — CLOSED.**

- The B3-C implementation is complete within its authorized reference-backend scope.
- B3-C-Q1 is SUPPORTED WITH QUALIFICATIONS; all 125 independent checks passed.
- No material runtime defect remains identified within the accepted scope.
- Historical evidence integrity is preserved; expected source evolution is accounted for.
- The read-only query boundary is accepted within its explicit allowlist.
- The graph-value and byte-path claims are accepted within their verified coverage.
- The accepted B3-B resource-lifecycle contract remains binding and preserved.
- All six B3-C qualifications are retained (§17); all fourteen B3-B qualifications remain
  binding (§18).
- The three Q1 documentation findings are accepted non-blocking obligations (§16).
- Same-executor review is acknowledged through an explicit bounded owner waiver (§15).
- Complete driver and Browser interoperability are not claimed.
- No further B3-C remediation is required within the accepted scope.
- B4/B5 remain subject to separate authorization.

## 23. Verification and commit record

- **Decision document:** `docs/decisions/B3-C-OWNER-01-owner-acceptance.md` (this file; exactly
  one file in the decision commit).
- **Verification executed at:** `9d97c3b` / `bcafccedde7d992497d91756e0399956fd0edf5f` (fresh
  build, five test suites and the scoped format check recorded in §7; manifests in §6; ancestry
  in §5).
- **Q1 claims verified from committed bytes:** probe results (125/0/180 at the reviewed head),
  determinism record (byte-identical repeat run), and the report's disposition (§7, §15).
- **Prohibited changes verified absent:** no `src/`, `tests/`, solution/project, dependency,
  historical report/decision/evidence/probe, manifest or build-configuration modification; no new
  source or test code; no git history rewrite; no push.
- **Repository state after commit:** recorded in the final handoff (branch, HEAD/tree, one
  worktree, clean index; nothing pushed).
- **Material condition check:** every acceptance gate above is supported by the cited evidence;
  no blocker was found.
