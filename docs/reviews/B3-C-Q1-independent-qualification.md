# B3-C-Q1 — Independent qualification of the deterministic read-only reference graph backend

**Task:** B3-C-Q1 — independent qualification of B3-C-IMPL-01
**Reviewed `main` HEAD:** `79ddd04da40d5c6eb7fb8dc0177ef7188b613e75` / tree `bdc50fb27f86bc6f9e7fbbf50157ec5f3d320adf`
**B3-C implementation commit:** `37fe40633624a62bc4c6bd6162de2c884e1f0188` / tree `4fc8d822d36352d002252421543fb60db89cc64e`
**Protocol:** Bolt 5.8 / PackStream 1.0
**Reviewer executor:** DeepSeek V4.1 Flash (`nvidia`, OpenCode) — **same executor session as the
B3-C producer**; see §1.

## Disposition

> **B3-C-Q1 — SUPPORTED WITH QUALIFICATIONS**
>
> The B3-C reference backend is independently confirmed: the graph oracle, all supported query
> forms, the exact-form allowlist, read-only capability enforcement, typed parameter preservation,
> cursor lifecycle, fail-closed retirement and B3-A byte-level golden compatibility were verified
> by a reviewer-authored probe (125 checks / 125 passing / 180 traces / exit 0, byte-identical
> across fresh processes). Zero material defects; three documentation-level findings
> (B3-C-Q1-DOC-01/02/03, §18). All fourteen B3-B owner qualifications remain binding; six
> B3-C-specific qualifications are added (§17).

---

## 1. Reviewer independence and provenance

| Item | Verified fact |
| --- | --- |
| Producer | B3-C-IMPL-01 was executed by DeepSeek V4.1 Flash (`nvidia`, OpenCode), commits authored as `BoltSharp Research Agent <agent@bolt-sharp.local>` |
| Reviewer | This Q1 was executed by the same agent session/model (DeepSeek V4.1 Flash) |
| Separate execution established? | **No.** The B3-B owner decision's bounded waiver (§10 of B3-B-OWNER-01) anticipated this and its discretionary future requirement — a different executor for release-grade qualification — is hereby **reiterated for B3-C acceptance** |
| Independently constructed | The probe (`docs/reviews/probes/B3-C-Q1/IndependentProbe/`, 5 source files) shares no code with the producer tests; its expected values are derived from `docs/boltsharp-spec.md` §7 and the retained B0-B-Q1 standalone decode, never from production code or producer fixtures |
| Producer evidence used | Historical manifests, the B3-C report's claimed counts (re-executed), and the authentic t03/t04 byte provenance chain |
| Limitation | Technical verification is thorough; strict personnel/organizational independence is not established. Owner acceptance of B3-C should weigh this exactly as the B3-B waiver did |

## 2. Preflight and reviewed revision

Branch `main`; HEAD/tree exactly as commissioned; one worktree; clean; no prior B3-C-Q1 work
(report/probes/evidence directories did not exist); nothing pushed; `37fe406` and `aae3d0e` both
ancestors. Change isolation since the B3-B owner acceptance (`aae3d0e`): exactly the twelve B3-C
files plus the B3-C report/evidence — zero changes to Sessions, Messages, PackStream, Framing,
Handshake, accepted tests, decisions or historical evidence.

## 3. Evidence-manifest verification

B3-C-IMPL-01 **26/26**; B3-B-Q3 33/33; B3-B-R2 29/29; B3-B-Q2 38/38; B3-B-Q1 12/12; B3-A-Q1
14/14; B3-A-R1 13/13; B3-A-Q2 15/15; B0-B producer 24/24; B0-B-Q1 119/119. Informational
evolution: B3-B-IMPL-01 19/22, B3-B-R1 12/14, B3-A-IMPL-01 16/21 — each superseded entry
re-verified at blob level against its recorded historical commit (`9f224cf`, `454ae00`).

## 4. Implementation change isolation

`git diff aae3d0e..37fe406` adds exactly four production files (`src/BoltSharp/Backend/`) and
eight test files (`tests/BoltSharp.Tests/Backend/`); the B3-C evidence commit adds only the report
and evidence. No accepted B3-A schema, B3-B session, PackStream, Framing, Handshake, dependency,
solution or historical file changed.

## 5. Reproduced test results

| Suite | Reviewer run | Expected |
| --- | --- | --- |
| Core | **1,137 / 0 / 0** | 1,137 |
| B3-C backend filter | **94 / 0** | 94 |
| B3-B sessions | **119 / 0** | 119 |
| B3-A messages | **98 / 0** | 98 |
| B0-B integration | **19 / 0** | 19 |
| Build | 0 errors; 2 pre-existing `xUnit2012` | same |
| Scoped format check (`src/BoltSharp/Backend`, `tests/BoltSharp.Tests/Backend`) | exit 0 | 0 |

**Producer-test audit.** The 94 tests were inspected: assertions cover state, response sequence,
open/attempt/read counters, ownership flags and post-failure permission; fault wrappers integrate
through the real session; no swallowed exceptions or signature-only checks were found. Two
producer-test observations (not defects): the byte-path test's terminal-summary expectation was
correctly adapted to the accepted `SUCCESS {}` behavior (§16), and the whitespace-collapsing
normalization accepts multi-line formatting of supported forms (§9).

## 6. Independent reference graph oracle

The reviewer oracle was constructed from spec §7 and the B0-B-Q1 standalone decode (23/23 checks,
recorded in `docs/reviews/evidence/B0-B-Q1/graph-independent-decode.json` from authentic driver
captures). Probe section `go-*` verified: exactly 2 nodes + 1 relationship; Alice
`1/n:1/[Person]/Alice`; Bob `2/n:2/[Person]/Bob`; KNOWS `1/r:1`, start 1 end 2, `n:1→n:2`,
`since=2024` integer; direction Alice→Bob with no reverse or duplicate relationship; the
deliberate numeric-ID collision (node 1 / relationship 1) involves distinct typed entities.

**Immutability probes:** mutation attempts through every exposed access path — node labels
(`IList<string>` cast + Add), node property map (`ICollection<KVP>` cast + Add), graph node and
relationship collections — were all **rejected**; repeated access returns the same instances;
query execution does not mutate the graph (values, instances and counts re-verified after runs).

**Alternate-fixture falsification:** a different immutable graph (Carol/Dave, `5/n:5`, `6/n:6`,
rel `3/r:3`, `since=1999`) built through the public constructor produced correspondingly different
evaluator results *and* different session-integrated records (scalar 42, property Carol/Dave,
graph node `5/n:5`) — demonstrating the pipeline is graph-driven, not hard-coded.

## 7. Supported-query matrix (independent oracle, through the real session)

| Form | Fields | Records |
| --- | --- | --- |
| `RETURN 42 AS answer` | `["answer"]` | `[42]` |
| `MATCH (n:Person) RETURN n.name` | `["n.name"]` | Alice, Bob (fixture order) |
| `… RETURN n.name AS name ORDER BY name` | `["name"]` | Alice, Bob ascending |
| `MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b` | `["a","r","b"]` | typed node/rel/node |
| `RETURN $value AS answer` | `["answer"]` | typed parameter |

Exhaustion is stable (repeated `ReadRow`/PULL after completion never resurrects records); repeated
execution across sessions produces identical values.

## 8. Read-only and allowlist adversarial results

Normalization matrix (documented contract: any `char.IsWhiteSpace` run collapses to one space;
case significant; nothing else stripped): leading/trailing/multiple internal spaces, tabs,
newlines, NBSP (`U+00A0`, whitespace → collapsed) and multi-line formatting of a supported form →
**accepted**; lowercase/uppercase keyword variants, trailing semicolon, empty, whitespace-only,
zero-width space (`U+200B`, not whitespace) → **refused**.

Mutation and refusal battery (30 reviewer-constructed texts: DETACH DELETE, CREATE, MERGE, SET,
REMOVE variants, `DROP INDEX`, `CALL db.labels()`/`dbms.components()`, `SHOW DATABASES`,
multi-statement, comments before/after, `answer2`, bare `RETURN 42 answer`, `RETURN 41`, wrong
projection/labels/relationship type/direction, `UNWIND`, `$other` parameter, doubled RETURN):
**every one refused** with `50N42` FAILURE/`Failed`, zero owned operations, graph state unchanged,
safe RESET recovery. Bounded input: 100k-space-padded supported query accepted; 1M-char
unsupported query refused without exception leakage.

## 9. RUN capability-refusal matrix

12 refusal cases + accepted cases executed (write/rw modes, wrong-typed and null `mode`, non-null
`db`/`imp_user`, timeout, bookmarks, unknown key, multiple unsupported options, supported query
combined with an unsafe option): all refused `50N42`/`Failed` with zero operations; explicit-null
`db`/`imp_user` accepted as "not requested"; default and `mode:"r"` accepted. The refusal of a
supported query under an unsafe option proves options are inspected, not silently dropped.

## 10. Parameter-type results

Integer (positive/negative/`long.MinValue`/`long.MaxValue`), string (query-like payload returned
verbatim, never interpolated or executed), boolean true/false, explicit null (null record value),
empty list, mixed list `[1,"x",null,true]` and nested map — all preserved with type and structure.
Missing `value` → `50N42`/`Failed` (distinct from explicit null, which succeeds with a null
record); unreferenced parameters are ignored; repeated execution is stable.

## 11. Graph identity and value mapping

Records reference the graph's own immutable instances; the encoded RECORD is byte-identical to
the authentic t04 golden (§15); decoded values match the independent oracle element-by-element
(including the node/relationship numeric-ID collision and all four relationship identity fields).

## 12. Cursor lifecycle and resource limits

Partial PULL (`PULL(1)` → has_more/Streaming), repeated PULL(1) (Alice then Bob terminal),
complete `PULL(-1)` (2 records + 1 exhaustion read), PULL→DISCARD (advancement + retirement),
DISCARD completion followed by PULL (`08N06`/`Defunct`), RESET/GOODBYE/Disconnect with an active
cursor (exactly-once retirement), zero-remaining stability. Resource limits: maximum materialized
result for the fixture is 2 records; graph values are shared immutable instances (not duplicated
per operation); the cursor is an index over a defensive copy; disposed operations are collectable
and never resurrected. Bounded in-memory behavior is documented as sufficient for this fixture
and a scaling limitation for future backends.

## 13. Fail-closed fault-injection results

All ten fault modes through the real session (reviewer fault wrappers): prepare throw (recoverable,
no ownership); null-fields/null-element/throwing metadata (one acquisition read, one retirement
attempt); read failures before/after a record (prefix preserved); column-count violation (no bad
record, `50N42` row-shape); disposal failure (RECORD/FAILURE + `Defunct` + close, no SUCCESS);
read+disposal and metadata+disposal combinations (primary preserved, diagnostic recorded,
terminal); use-after-disposal rejected. **Mandatory overlap control** (`RUN → PULL → retirement
fails → RESET → RUN → Disconnect → Dispose`): `opens=1`, one attempt, `DEFUNCT`, no RESET
recovery, no second RUN, no retry. Positive control: clean-retirement FAILED → RESET → READY
recovery preserved.

## 14. Integrated B3-B session traces

Every probe scenario records per-action traces (state, responses, counters, escapes) into
`q1-probe-results.json` (180 traces). All five commissioned flows (A–E) pass exactly as
specified, with the authentication verifier configured for tests and production's fail-closed
default untouched.

## 15. Independent byte-level t03/t04 comparisons

Provenance: the t03/t04 golden hex vectors were introduced in the B3-A implementation commit
`d99096a`, are byte-identical in the current tree (verified), and were independently decoded from
authentic driver captures by B0-B-Q1's standalone PackStream-subset decoder
(`graph-independent-decode.json`, 23/23 checks). The reviewer probe hard-codes those bytes as its
oracle (independent of production fixtures): authentic t03 RUN/PULL bytes decode, traverse
B3-B/B3-C, and the responses encode **exactly** to `RunSuccessFieldsAnswer` / `Record42`; the
graph RUN/PULL responses encode **exactly** to `RunSuccessFieldsGraph` / `RecordGraph`; decoding
the t04 bytes yields values matching the independent oracle element-by-element.

## 16. Terminal SUCCESS metadata assessment

B3-B emits terminal `SUCCESS {}` (absent `has_more`); the real-server t03 golden carries
`has_more:false`. Per the Bolt specification an absent `has_more` defaults to false, so the
client-visible semantics are equivalent; no other completion metadata is expected for this
bounded backend. Wire-level equality does not hold and is **not claimed**; live-driver
confirmation of the absent-field form remains a B5-B qualification (Q-B3B-12/Q-B3B-09).

## 17. Remaining qualifications

All fourteen B3-B owner-decision qualifications remain binding and are not silently closed.
B3-C-specific binding qualifications for owner acceptance:

- **Q-B3C-01 — Exact-form allowlist.** Query recognition is exact-form with whitespace collapsing
  and case sensitivity; keyword-case variants are refused. Any future loosening requires
  requalification.
- **Q-B3C-02 — Refusal granularity.** All backend refusals surface as the single bounded
  `50N42`/`Request.Invalid` FAILURE (the seam cannot carry reasons); richer reasons are
  diagnostic-only.
- **Q-B3C-03 — Parameter surface.** Only the `value` parameter of Q4 is recognized; the name is
  fixed; explicit null and structured values are preserved; no coercion or general typing exists.
- **Q-B3C-04 — Bounded materialization.** Results are materialized at preparation; adequate for
  the fixture, a scaling boundary for future backends.
- **Q-B3C-05 — Multi-line acceptance.** Because whitespace runs collapse, multi-line formatting of
  a supported form is accepted; within the documented normalization rule, but must be retested by
  any future query-surface change.
- **Q-B3C-06 — `has_more` wire nuance.** B3-B terminal summaries omit `has_more` (semantically
  false); live-driver equivalence remains a B5-B verification.

## 18. Material findings

No protocol or implementation defect was established. Documentation-level findings:

- **B3-C-Q1-DOC-01 (minor, documentation):** the B3-C report §4 does not state that whitespace
  collapsing also accepts multi-line formatting of supported forms; the behavior is consistent
  with the documented normalization rule and the implementation, but the report should name it.
  No runtime impact.
- **B3-C-Q1-DOC-02 (minor, documentation):** the B3-C report's supported-form table presents the
  ordered variant without repeating that keyword case is significant (documented in its §18.2).
  No runtime impact.
- **B3-C-Q1-DOC-03 (observation):** B3-B's terminal `SUCCESS {}` vs the real-server
  `has_more:false` golden is a wire-level distinction worth surfacing in the B5-B test plan.

None of these blocks acceptance; none is a runtime defect.

## 19. Independent negative controls and blind spots

Negative controls (probe `nc-*`, all passing): reversed-direction oracle (2→1 rejected), numeric-ID
confusion distinctness, alternate-fixture derivation (hard-coded-response detection), exact
field-list/golden-byte assertions, NC-05b column-count violation, allowlist battery, write-attempt
graph-mutation control, RUN-option refusal, query-like parameter payload, missing-vs-null
distinctness, cursor use-after-disposal, fail-closed overlap, single terminal summary, and
one-byte corruption of the t04 golden (detected by the decode oracle).

Blind spots recorded: (1) the bounded `PULL(n=-1)` cap cannot be exercised by the two-record
fixture — covered only by B3-B tests with larger synthetic results; (2) true concurrent-session
isolation is outside the single-owner model; (3) byte-path coverage for unsupported queries uses
typed requests (no authentic unsupported-query capture exists).

## 20. Producer-claim reconciliation

No historical probe carries superseded expectations for B3-C (this is its first qualification).
The producer's 94 tests and this review's 125 checks were both executed against the same revision
with zero failures; the review's probe independently re-derives every material producer claim
(graph values, query forms, allowlist, parameters, cursor lifecycle, byte compatibility).

## 21. Formal disposition

**B3-C-Q1 — SUPPORTED WITH QUALIFICATIONS.** The B3-C backend is independently confirmed within
its bounded scope; no material defect was found; the three documentation findings above are
non-blocking.

## 22. Recommendation to the owner

B3-C owner acceptance **with the qualifications in §17** (including the reiterated
different-executor requirement for future release-grade qualification) is supportable on this
record, subject to the owner's weighing of the reviewer-executor overlap disclosed in §1. This is
independent review, not acceptance. The next gate after acceptance would be B4 (transport), which
requires its own commission.

Evidence: `docs/reviews/evidence/B3-C-Q1/` — `Run-B3CQ1.ps1`, raw logs/TRX,
`q1-probe-results.json` (+ byte-identical repeat run), manifest verification, reviewed-input
hashes, `manifest.json`. Probe: `docs/reviews/probes/B3-C-Q1/IndependentProbe/`.
