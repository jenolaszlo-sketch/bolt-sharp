# B1-Q2 — Independent PackStream remediation requalification

**Status:** COMPLETE (requalification delivered; owner acceptance PENDING)
**Date:** 2026-11-03
**Baseline under requalification:** `f40a2da0b3ba36eac0777a63576a75439a313e89` (branch `feature/protocol-spec`)
**Remediation under requalification:** `93759a2759efeab5cc2ab2ec63d204a9e52e1945` (B1-R1)
**Reviewer:** independent B1-Q2 requalification pass
**Final disposition:** **SUPPORTED WITH QUALIFICATIONS**

This report is additive evidence. It does not modify the B1-Q1 review, the B1-R1
remediation evidence, or any production code.

---

## 1. Verification of commit identity, ancestry, scope and evidence preservation

Commands executed against the canonical checkout:

```text
git rev-parse HEAD                → 93759a2759efeab5cc2ab2ec63d204a9e52e1945
git branch --show-current         → feature/protocol-spec
git status --short                → empty (clean working tree)
git merge-base --is-ancestor f40a2da0… 93759a2… → exit 0 (remediation descends from the baseline)
git log --oneline -2              → 93759a2 fix(packstream): B1-R1 remediation… / f40a2da docs(review): B1-Q1…
git diff --stat f40a2da0..93759a2 → 11 files, +1812/−255
```

Diff scope verified — exactly:

| Area | Files |
| --- | --- |
| Production (B1 scope only) | `src/BoltSharp/PackStream/PackStreamValue.cs`, `GraphValues.cs`, `PackStreamWriter.cs`, `PackStreamReader.cs` |
| Reviewer probes | `tests/BoltSharp.Tests/Qualification/B1Q1ReviewerProbes.cs` (five probes revised, §3) |
| New tests | `tests/BoltSharp.Tests/Remediation/B1R1F1…F5*Tests.cs` (5 files) |
| New evidence | `docs/reviews/B1-R1-packstream-remediation.md` |

Evidence preservation verified by empty diffs over `f40a2da0..93759a2` for:
`docs/reviews/B1-Q1-packstream-qualification.md` (byte-for-byte unchanged),
`PackStreamLimits.cs` (reader limits untouched), `PackStreamException.cs`,
`PackStreamMarkers.cs`, `StrictUtf8.cs`, and all `*.csproj`/`*.slnx` files (no new
external packages). `git diff f40a2da0..93759a2 --name-only` lists no file outside B1
scope.

---

## 2. Independent inspection and falsification attempts

The implementation was inspected directly (not via the producer's report), and each
finding's correction was attacked. New adversarial probes were written in
`tests/BoltSharp.Tests/Qualification/B1Q2RequalificationProbes.cs` (24 probes), kept
separate from both the B1-Q1 probes and the B1-R1 remediation tests.

### F1 — Immutable value ownership: **CLOSED**

**Inspection.** All seven public collection properties (`PackStreamList.Items`,
`PackStreamMap.Entries`, `PackStreamStructure.Fields`, `PackStreamNode.Labels`,
`PackStreamPath.Nodes`/`UnboundRelationships`/`Indices`) are `ReadOnlyCollection<T>`
wrappers over privately owned arrays (`PackStreamValue.cs:87,127,189`,
`GraphValues.cs:19,178-180`). Public constructors copy (`Copy`/`CopyLabels`) before
wrapping. `PackStreamBytes` stores an exclusively owned array; `Value` returns a fresh
copy per access (`PackStreamValue.cs:67`) and `OwnedBytes` is internal-only
(`PackStreamValue.cs:74`). The reader hands internally built state to internal ownership
constructors (`PackStreamValue.cs:95,135,197`, `GraphValues.cs:29`, pre-existing
`PackStreamPath` array constructor), so decoded values have equivalent guarantees without
redundant copies. `ReadBytes()` remains caller-owned (untouched; diff empty on its line).

**Falsification attempts (all failed to falsify).** Array casts on all seven collections
throw `InvalidCastException`; every `IList<T>`/`ICollection<T>` mutation (Add, Insert,
RemoveAt, Clear, indexer set) throws `NotSupportedException` with stored values intact;
`MemoryMarshal.TryGetArray` reaches only a per-access copy; constructor-input mutation
after construction (lists *and* arrays, including `PackStreamBytes` source memory and
Path array inputs) never changes stored values or encoded bytes; a byte payload nested
three levels deep cannot corrupt the root encoding. A search for remaining mutable
aliases found none: no public getter returns an array, span, or writable view of internal
state. The internal ownership constructors' exclusivity assumption was verified against
their only call sites (the reader builds and never re-touches the state).

**Qualification (inherent, not a defect):** immutability is not defended against
reflection or hostile `MemoryManager` tricks; that is outside the .NET object model and
outside the finding's scope.

### F2 — Path writer limits: **CLOSED**

**Inspection.** `WritePathCore` compares all three field counts against `MaxListItems`
(`PackStreamWriter.cs:515,521,527`) unconditionally — i.e. in both the validation and
emission passes — before any marker or header is written. No arithmetic is performed on
counts (no overflow), no temporary allocations exist, and the reader's limits are
untouched (empty diff on `PackStreamLimits.cs`).

**Falsification attempts (all failed to falsify).** Boundary accepted / boundary+1
rejected for nodes and relationships, and boundary / nearest-constructible-above (even
parity) for indices, each leaving the destination empty; a degenerate `maxListItems: 0`
rejects every path; a wrong-limit-key attack (`maxListItems: 4, maxMapEntries: 2` with a
three-node path) proves the correct limit key is used; wire bytes hand-built with a
five-item node list are still rejected by the reader under identical limits (the writer
refuses to produce them); the same valid path writes and reads under identical limits;
default-limit paths remain byte-identical (Path golden tests pass).

### F3 — Exception safety of writes: **CLOSED, with one documented qualification**

**Inspection.** The two-pass design shares a single recursive code path: every core takes
`validateOnly`, and each public composite entry point runs its core twice
(`PackStreamWriter.cs:138-259`). All emission is guarded (`if (!validateOnly)`, or an
early `return` after validation in `WriteStringValue`/`WriteBytesValue`); a re-audit found
no unguarded write to `_output`. `StrictUtf8.ValidateUtf16` and `GetByteCount` are pure
and deterministic, and every value type is immutable, so for library-provided values the
validation pass observes exactly what the emission pass emits: any rejection happens
before the first byte, and accepted values are byte-identical to the pre-remediation
encoder (golden suites pass). No staging buffers, no allocations in the validation pass.

**Falsification attempts (all failed to falsify, except where noted).** Late failures in
the deepest possible positions — Node's fourth field, Relationship's eighth field, a
Path's second node's property, an unsupported value subclass nested in a list, null map
keys, limit violations, invalid strings at depth — all leave `WrittenCount == 0`; a
failed write followed by valid writes leaves the destination byte-exact
(`01 82 68 69`); destination-originated failures (throwing `Advance`, and a destination
returning a span shorter than `sizeHint`) raise non-PackStream exceptions, matching the
documented exclusion.

**Qualification (found by falsification — see probe
`B1Q2_F3_UnstableCallerCollection_CanDefeatTwoPassAtomicity_Documented`).** The two
traversals re-read the caller-supplied `IReadOnlyList<T>`. A caller collection whose
content changes between the validation and emission passes can defeat atomicity
deterministically: validation observes a valid string, emission observes an unpaired
surrogate, and the list header alone (`91`) is committed before the exception. The
`PackStreamWriter` XML contract is literally accurate ("if validation of the supplied
value fails…"), and every library-provided value is immutable and unaffected, but the
B1-R1 evidence document's unconditional phrase "a failed write never commits partial
output" does not record this traversal-stability assumption. Severity: LOW. Recommended
(not applied — production code is out of B1-Q2 scope): document on `PackStreamWriter`
that caller-supplied collections must remain stable for the duration of the call.

### F4 — Reserved graph-tag arity: **CLOSED, with one documented residual**

**Inspection.** `ValidateReservedGraphTagArity` (`PackStreamWriter.cs:621`) requires
`4E`→4, `52`→8, `72`→4, `50`→3 in both passes, after tag-range and field-count checks.
Unknown tags are unaffected.

**Falsification attempts (all failed to falsify, except the documented residual).**
Arity 0, ±1 and 15 for each reserved tag is rejected before emission; the exact arities
remain wire-compatible and decode as the typed graph structures for **all four** tags
(Node, Relationship, UnboundRelationship, Path — end-to-end verified); unknown tags
(`0x01`/0 fields, `0x48`/1, `0x7F`/15) round-trip.

**Residual (documented by probe
`B1Q2_F4_ReservedTagValidArityWrongFieldTypes_WritableButNotTypedDecodable_Documented`).**
The guarantee is arity-only: a generic structure with a reserved graph tag, valid arity,
and field types the graph structure does not accept (e.g. `B4 4E` + four integers) is
emitted and then rejected by the typed reader. This is consistent with the original F4
scope ("invalid field counts"), with the preserved producer behaviour
(`GraphTags_AreReserved_GenericStructureWithNodeTagDecodesAsNode` requires valid-arity
generic structures to stay writable), and produces no malformed wire format — but it
means the writer does not claim "every emitted byte is decodable" for generic graph-tag
structures. Typed graph APIs (`WriteNode`, `WritePath`, …) carry the full guarantee.
Severity: LOW.

### F5 — Reader failure contract: **CLOSED**

**Inspection.** The contract is documented at the class level, on `Position` and on
`ReadValue` (`PackStreamReader.cs:7-21,41-45,123-137`): bytes consumed before the failure
are reflected in `Position`; the reader is not realigned; after any decoding exception
the caller must discard the reader. No rollback was implemented.

**Falsification attempts (all failed to falsify).** Position semantics pinned precisely:
format failures in nested containers (position = bytes consumed at the failure point,
`91 B0 4E` → 3), truncation, element-budget exhaustion (same discard rule, supported
recovery via a fresh reader demonstrated), and malformed UTF-8, where the unconsumed
payload is correctly **excluded** from `Position` (`D0 02 C3 28` → 2). No case was found
where `Position` overshoots the failure point or where the documented rule mis-describes
observed behaviour.

---

## 3. Review of the five revised B1-Q1 probes

The five probes that originally demonstrated defects F1–F4 were compared line-by-line
against their pre-remediation versions (`git diff f40a2da0..93759a2 -- tests/BoltSharp.Tests/Qualification/B1Q1ReviewerProbes.cs`):

| Probe | Original (defect-asserting) | Revised (verification) | Attack vector preserved | Assertions |
| --- | --- | --- | --- | --- |
| 05 | mutation via `((string[])Labels)` / `((PackStreamNode[])Nodes)` succeeds | same casts, `Assert.Throws<InvalidCastException>` + unchanged values + round-trip | yes | strengthened |
| 05b | `TryGetArray` mutation reflected in stored bytes | same vector, asserts stored value unchanged | yes | strengthened |
| 06 | partial `92 01` after failed `WriteList` | asserts `WrittenCount == 0` | yes (same failing value) | strengthened |
| 07 | `WritePath` emits 5-node path under limit 4 | asserts rejection + empty destination | yes (same 5-node path, same limits) | strengthened |
| 08 | `B1 4E 01` writable but undecodable | asserts rejection + empty destination + valid-arity form still writes/decodes | yes | strengthened |

Assessment: **no probe was weakened, skipped or deleted**; every revised probe retains the
reviewer's original attack vector and asserts the corrected outcome. Renames
(`…_KnownDefect` → `…_Remediated`) and per-probe annotations referencing the B1-Q1
findings are accurate. Per the requalification mandate: these revised probes are **not**
unchanged historical evidence — they are remediation-verification tests. The historical
evidence of the original defect behaviour lives in the B1-Q1 report (§4/§5, unchanged)
and in git history (`git show f40a2da0:tests/BoltSharp.Tests/Qualification/B1Q1ReviewerProbes.cs`).

One observation: revised Probe07 no longer asserts the specific oversized wire bytes
(`B3 50 95 B4 …`) the original defect produced; those bytes can no longer be emitted by
design, and the corresponding positive case (write+read at identical limits) is covered
by remediation tests and by B1-Q2 probe `B1Q2_F2_ManualOversizedPathWire…`.

---

## 4. Test execution and accounting

Commands and results (all executed on the canonical checkout, .NET SDK 10.0.401, net8.0):

```text
dotnet restore BoltSharp.slnx                        → ok, no new packages
dotnet build BoltSharp.slnx --no-restore             → 0 warnings, 0 errors
dotnet test BoltSharp.slnx --no-build                → Passed! Failed: 0, Passed: 509, Skipped: 0
dotnet test … --filter "FullyQualifiedName~Qualification.B1Q1ReviewerProbes" → 29 passed
dotnet test … --filter "FullyQualifiedName~Tests.Remediation"               → 74 passed
dotnet test … --filter "FullyQualifiedName!~Qualification&FullyQualifiedName!~Remediation" → 382 passed
dotnet test … --filter "FullyQualifiedName~Golden"                          → 285 passed
dotnet test … --filter "FullyQualifiedName~B1Q2RequalificationProbes"       → 24 passed
```

Accounting verified independently: **382 producer** (381 in `Tests.PackStream` + 1
`SmokeTests`) + **29 B1-Q1 probes** + **74 B1-R1 remediation regressions** + **24 B1-Q2
probes = 509**, 0 failed, 0 skipped. The pre-remediation review's 411-test claim
(382 + 29) is consistent with these filters. Golden-fixture compatibility confirmed: all
285 golden tests (scalars, strings, byte arrays, maps, lists, structures, graph
structures, paths) pass unchanged.

One B1-Q2 probe authoring error was corrected during the review (a Path fixture declared
relationship index 1 against an empty relationship list); the reader was correct to
reject it. No production defect was masked.

---

## 5. New B1-Q2 reviewer probes and outcomes

All 24 pass (two are `_Documented` probes: passing while recording a contract boundary).

| Probe | Outcome | Purpose |
| --- | --- | --- |
| B1Q2_F1_AllPublicCollections_RejectArrayCasts | pass | all seven collection getters reject array casts |
| B1Q2_F1_AllPublicCollections_IListViews_AreReadOnly | pass | every IList/ICollection mutation throws; values intact |
| B1Q2_F1_Bytes_ConstructorInputMutation_CannotChangeStoredValue | pass | coverage gap: bytes ctor-input mutation |
| B1Q2_F1_Path_ConstructorInputArrays_MutationAfterConstruction_CannotChangeStoredPath | pass | array-input defensive copy (reviewer vector shape) |
| B1Q2_F1_Bytes_NestedThreeLevelsDeep_CannotChangeRootEncoding | pass | nested byte payload cannot corrupt root encoding |
| B1Q2_F2_PathLimits_KeyOnMaxListItems_NotMaxMapEntries | pass | wrong-limit-key falsification failed |
| B1Q2_F2_Path_ZeroListLimit_RejectsAnyPath_DestinationUnchanged | pass | degenerate limit edge |
| B1Q2_F2_ManualOversizedPathWire_IsRejectedByReader_UnderIdenticalLimits | pass | reader-side symmetry at boundary+1 |
| B1Q2_F3_WriteNode_InvalidElementId_LastField_DestinationUnchanged | pass | deepest Node failure |
| B1Q2_F3_WriteRelationship_InvalidEighthField_DestinationUnchanged | pass | deepest Relationship failure |
| B1Q2_F3_WritePath_InvalidStringInSecondNode_DestinationUnchanged | pass | late failure after first node's bytes |
| B1Q2_F3_WriteValue_UnsupportedValueSubclass_NestedInList_DestinationUnchanged | pass | unsupported type inside container |
| B1Q2_F3_FailedWrite_CommitsNothing_SubsequentValidWritesAreByteExact | pass | no byte leakage across failures |
| B1Q2_F3_DestinationReturningShortSpan_FailsWithForeignException_OutsideTheContract | pass | destination-contract violation exclusion |
| B1Q2_F3_StableCallerCollection_TwoPassWrite_MatchesGoldenBytes | pass | two-pass equivalence on non-array collections |
| B1Q2_F3_UnstableCallerCollection_CanDefeatTwoPassAtomicity_Documented | pass | **residual**: mutating caller collection commits partial header `91` |
| B1Q2_F4_ValidArities_AllFourReservedTags_EncodeAndDecodeAsTypedGraphStructures | pass | wire compatibility, all four tags end-to-end |
| B1Q2_F4_ReservedTagValidArityWrongFieldTypes_WritableButNotTypedDecodable_Documented | pass | **residual**: arity-only guarantee |
| B1Q2_F4_UnknownTags_AnyArity_RoundTrip (×3) | pass | future-extension safety |
| B1Q2_F5_MalformedUtf8_Position_ExcludesUnconsumedPayload | pass | precise position semantics |
| B1Q2_F5_BudgetExhaustion_IsADecodingException_ReaderMustBeDiscarded | pass | budget exhaustion under the same rule |
| B1Q2_F5_NestedFailure_PositionReflectsBytesConsumedAtTheFailurePoint | pass | nested failure position |

---

## 6. Disposition summary

| Finding | B1-Q1 severity | Disposition |
| --- | --- | --- |
| F1 immutable value ownership | MODERATE | **CLOSED** |
| F2 Path writer limits | MODERATE | **CLOSED** |
| F3 writer exception safety | MODERATE | **CLOSED** — with qualification: traversal-stability assumption for caller-supplied collections is undocumented (LOW) |
| F4 reserved graph-tag arity | MINOR | **CLOSED** — with residual: arity-only guarantee for generic graph-tag structures (LOW, consistent with original scope) |
| F5 reader failure contract | MINOR | **CLOSED** |

No regression was found: producer behaviour, golden bytes, reader limits, public API
signatures, and resource limits are preserved; the only behavioural changes are the
documented corrections. No CRITICAL, MAJOR or additional MODERATE defect was reproduced.

---

## 7. Residual risks and supported scope of claims

**Residual risks (both LOW, neither blocks B2):**

1. **F3 stability assumption.** Atomicity holds for all library-provided (immutable)
   values; it can be defeated only by caller collections that change between the writer's
   two internal traversals (probe evidence: partial `91` header). Recommendation for a
   future production-only change: document the assumption on `PackStreamWriter`.
2. **F4 arity-only scope.** Generic structures with reserved graph tags and valid arity
   but non-conforming field types are emitted and rejected by the typed reader. Typed
   graph APIs carry the full guarantee. Recommendation: document the scope.
3. **General .NET limitations (out of scope):** reflection, hostile `MemoryManager`
   implementations, and callers retaining and mutating inputs they handed to public
   constructors before the call are outside the immutability contract.
4. **Performance notes (not defects):** composite writes traverse the value twice;
   `PackStreamBytes.Value` copies per access. Both are the measured cost of the F1/F3
   guarantees and are bounded by existing limits.

**Supported scope of claims:** the B1 PackStream codec at `93759a2` is
specification-faithful against the golden fixtures, closes B1-Q1 findings F1–F5, and —
for immutable values — writes atomically with symmetric writer/reader limits. **Not**
claimed or supported: Neo4j driver or Browser compatibility, B2/B4 behaviour, temporal,
spatial, duration or UUID value coverage, independence of the B1-R1 remediation author
(same structural limitation recorded in B1-Q1 §1.3 applies to this requalification's
author), and owner acceptance.

---

## 8. Verdict

**SUPPORTED WITH QUALIFICATIONS.**

All five B1-Q1 findings are substantively closed and independently verified; no
regressions were found; the two qualifications above are documentation-level gaps with
probes pinning their exact boundaries, and they do not weaken any wire-format or
resource-safety guarantee.

Evidence: 509 passing tests (382 producer + 29 B1-Q1 probes + 74 B1-R1 remediation
regressions + 24 B1-Q2 probes), 0 warnings, 0 errors; independent probe file
`tests/BoltSharp.Tests/Qualification/B1Q2RequalificationProbes.cs`; this report.

Independent review of this requalification: PENDING. Owner acceptance: PENDING.
