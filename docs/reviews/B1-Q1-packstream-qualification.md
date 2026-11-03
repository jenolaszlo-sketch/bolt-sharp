# B1-Q1 — Independent PackStream qualification

**Status:** COMPLETE (review delivered; owner acceptance PENDING)
**Date:** 2026-11-03
**Implementation under review:** `7230f1289c7f48605b244d85439ac0d99f4420d4` (B1-A → B1-D2)
**Reviewer:** independent qualification pass (see §1.3 independence limitation)
**Final disposition:** **SUPPORTED WITH QUALIFICATIONS**

---

## 1. Review baseline and scope

### 1.1 Environment

| Item | Value |
| --- | --- |
| Repository | `jenolaszlo-sketch/bolt-sharp` |
| Branch | `feature/protocol-spec` |
| Implementation commit | `7230f1289c7f48605b244d85439ac0d99f4420d4` |
| Working tree at review start | clean |
| .NET SDK | 10.0.401 |
| .NET runtime (target) | Microsoft.NETCore.App 8.0.31 |
| Producer tests reproduced | 382 passed / 0 failed / 0 skipped |
| Reviewer probes added | 29 (all passing; 4 document defects) |
| Combined suite after review | **411 passed / 0 failed / 0 skipped, 0 warnings, 0 errors** |
| Production source changed during review | **NO** (`git diff 7230f1… -- src/` is empty) |

Commands executed: `dotnet restore BoltSharp.slnx`, `dotnet build BoltSharp.slnx --no-restore`,
`dotnet test BoltSharp.slnx --no-build`, plus a clean build/test cycle.

### 1.2 Scope

All B1 production code under `src/BoltSharp/PackStream/` and all B1 tests:
B1-A (scalars), B1-B (strings/bytes), B1-C (lists/maps/structures), B1-D1 (Node,
Relationship, UnboundRelationship), B1-D2 (Path). Sources of truth: the official
PackStream 1.0 specification (https://neo4j.com/docs/bolt/current/packstream/) and the
Bolt 5.8 structure semantics (https://neo4j.com/docs/bolt/current/bolt/structure-semantics/).

### 1.3 Independence limitation

The review was performed by the same agent/model that produced the B1 implementation.
This is a structural limitation: the reviewer shares the implementer's biases and the
golden-byte fixtures were re-derived rather than produced by a second party. All findings
below are nonetheless backed by executable probes and by specification text. A further
independent human or model review remains necessary before owner acceptance.

---

## 2. Specification coverage matrix

Legend: ✅ verified by probe and/or independent byte derivation · 🟡 verified with a
qualification noted · ❌ not applicable to B1.

| Specification area | Requirement | Result | Evidence |
| --- | --- | --- | --- |
| Integers | Marker selection matches the optimal-representation table at every boundary | ✅ | Producer fixtures re-derived from spec table; sweep covers all tiny values |
| Integers | Big-endian multi-byte layout | ✅ | Golden bytes `C9 01 00`, `CA 00 00 80 00`, `CB 00 00 00 00 80 00 00 00` |
| Integers | Wider-than-necessary representations accepted | ✅ | Spec's `42` example in all five encodings; Probe10 |
| Floats | IEEE 754 bit preservation, `1.23` official example, ±Inf, `-0.0` vs `0.0`, NaN payload | ✅ | Producer tests; bit-level assertions |
| Floats | Reserved/unknown markers rejected | ✅ | Probe01 full 256-byte sweep |
| Strings | Marker chosen by UTF-8 byte length, not character count | ✅ | `Größenmaßstäbe` (18 bytes) → `D0 12`; 15/16/255/256/65535/65536 boundaries |
| Strings | Strict UTF-8 decode (overlong, surrogate-encoded, invalid continuation/start) | ✅ | Producer tests + Probe01/02 sweeps |
| Strings | UTF-16 validation (unpaired surrogates) | ✅ | Producer tests (lone high/low/embedded surrogates) |
| Strings/bytes | Defensive ownership of decoded byte arrays | ✅ | Producer test (input mutation does not affect result) — but see F1 |
| Strings/bytes | Configured limits, checked before allocation | ✅ | Probe12, Probe13; producer limit tests |
| Strings/bytes | Declared length within limit but beyond input → truncation (not limit failure) | ✅ | Probe12 |
| Lists/maps | Markers 90–9F/A0–AF, D4–D6/D8–DA; length = element count | ✅ | Golden bytes at 15/16/255/256/65535/65536 |
| Lists/maps | Wider representations accepted | ✅ | Producer tests; Probe10 |
| Maps | String-only keys; non-string keys rejected | ✅ | Producer test + Probe04 |
| Maps | Duplicate keys: last value wins, first-seen position kept | ✅ | Spec example; Probe04 (complex nested values) |
| Structures | Tag range 0–127; ≤15 fields; arity validation | ✅ | Producer tests; Probe01 (B0 alone truncated) |
| Containers | Depth limits | ✅ | Producer tests; Probe13b |
| Containers | **Aggregate element budget bounds nested graphs** | ✅ | Probe03 (2101-value tree: rejected at budget 1000, accepted at 3000) |
| Containers | Large declared counts with tiny input → deterministic failure, no bulk allocation | ✅ | Producer tests; Probe16 |
| Containers | Ownership/immutability of decoded values | 🟡 | Guarantee holds by convention only — **F1** |
| Node | `B4 4E`, fields id/labels/properties/element_id in order | ✅ | Independently derived golden bytes |
| Relationship | `B8 52`, 8 fields, direction and both endpoint element IDs preserved | ✅ | Independently derived golden bytes |
| UnboundRelationship | `B4 72`, 4 fields, no endpoints invented | ✅ | Golden bytes |
| Path | `B3 50`, fields nodes/rels/indices in order | ✅ | Golden bytes; Probe02 truncation sweep |
| Path | 1-based signed relationship indices; 0 refused; 0-based nonnegative node indices | ✅ | Producer tests; Probe11 |
| Path | `long.MinValue` handled without overflow (`Math.Abs` not used) | ✅ | Comparison-only validation; producer test + Probe11 |
| Path | Official repeated-relationship fixture `(42)-[1000]→(69)-[1000]→(42)←[1001]-(1)` | ✅ | Independently re-derived; traversal reconstructed in Probe/producer tests |
| Path | Empty node list refused; odd index count refused | ✅ | Producer tests |
| Reserved tags | Graph tags are reserved; wrong arity fails instead of degrading | ✅ | Producer tests; Probe08 (writer side — see F4) |
| Reader position | Position reflects consumed bytes; reader must be discarded after failure | 🟡 | Producer tests; Probe09b documents the reuse hazard — **F5** |
| Unknown value types | Temporal/spatial tags (e.g. `0x49`) pass through as generic structures | 🟡 | Probe15 — documented limitation |

---

## 3. Producer-test reproduction

The producer's full suite was executed unmodified before any reviewer test was added:
**382 passed, 0 failed, 0 skipped**. No producer test was altered during the review.
After adding the reviewer probes the combined suite reports **411 passed / 0 failed /
0 skipped** with 0 build warnings and 0 errors, including a clean checkout cycle.

Producer claims that were independently re-verified rather than trusted:

- Golden bytes for Node/Relationship/UnboundRelationship/Path were re-derived from the
  specification text and byte-compared against the producer's fixtures. They match,
  including the official Path example with a repeated relationship.
- The claimed `long.MinValue` index safety was re-checked by reading the validation code
  (comparison-only, no negation of the index) and by probe.
- The claimed "budget applies to nested graphs" claim was initially **not** demonstrable
  from the producer tests, so Probe03 was written; it confirms the aggregate behaviour.

---

## 4. Independent probe evidence

Reviewer probes live in `tests/BoltSharp.Tests/Qualification/B1Q1ReviewerProbes.cs`
(29 tests). Probes marked *KNOWN DEFECT* pass while demonstrating a defect; they are
evidence for §5, not endorsements.

| Probe | Result | Purpose |
| --- | --- | --- |
| Probe01 | pass | All 256 byte values as single-byte inputs: no non-PackStream exception ever escapes; each byte either decodes as the specification requires or fails as truncated/format |
| Probe02 | pass | Truncation at **every** byte boundary of six representative values (Node, Path, Map, List, Bytes, tiny string): every proper prefix fails with a PackStream exception |
| Probe03 | pass | Aggregate element budget bounds a 2,101-value nested list graph (reject at 1000, accept at 3000) |
| Probe04 | pass | Duplicate map keys with complex nested values: last value wins |
| Probe05, 05b | pass (defect) | Public collections and decoded byte arrays are mutable through array casts / `MemoryMarshal.TryGetArray` — **F1** |
| Probe06 | pass (defect) | Writer emits bytes before a nested validation failure — **F3** |
| Probe07 | pass (defect) | `WritePath` ignores `MaxListItems`; emits bytes its own reader rejects — **F2** |
| Probe08 | pass (defect) | Generic structure with reserved graph tag and wrong arity is writable but undecodable — **F4** |
| Probe09, 09b | pass | Malformed trailing value fails after prior success; reader reuse after failure produces misaligned values (documented contract) |
| Probe10, 10b | pass | Wider empty string/bytes representations accepted |
| Probe11, 11b | pass | `long.MaxValue` relationship and node indices rejected |
| Probe12 | pass | Declared length within limit but beyond input → truncation, not limit failure |
| Probe13, 13b | pass | Zero string/depth limits behave deterministically on both sides |
| Probe14 | pass | `null` inside a path node list rejected |
| Probe15 | pass | Non-graph tags (e.g. temporal `0x49`) preserved as generic structures |
| Probe16 | pass | MAP_32 declaring 100,000 entries with no payload → truncation |

Four initial probe **authoring** errors (wrong fixture bytes, not implementation
behaviour) were corrected before the probes were accepted; none masked a defect.

---

## 5. Findings

### F1 — MODERATE: immutability is not enforced on decoded values

**Claim under review:** "callers cannot mutate decoded paths through public collection
references" (B1-D2) and "immutable or defensively owned collections" (B1-C/B1-D1).

**Actual:** `PackStreamList.Items`, `PackStreamMap.Entries`, `PackStreamStructure.Fields`,
`PackStreamNode.Labels`, `PackStreamPath.Nodes`/`UnboundRelationships` are arrays stored
behind `IReadOnlyList<T>`/`IReadOnlyList<KeyValuePair<…>>` properties. A cast back to the
array type succeeds and allows mutation; `PackStreamBytes.Value` exposes the private array
through `MemoryMarshal.TryGetArray`.

**Probe:** `Probe05_PublicCollections_CanBeMutatedThroughArrayCasts_KnownDefect`,
`Probe05b_DecodedByteArrays_CanBeMutatedThroughMemoryMarshal_KnownDefect`.

**Expected:** mutation impossible. **Actual:** `node.Labels[0]` becomes `"MUTATED"` after a
cast, and re-encoding the mutated node emits the mutated label.

**Impact:** bounded. A hostile consumer can corrupt a decoded value, but no protocol state
or native resource is at risk, and honest callers are unaffected.

**Remediation (not applied):** wrap exposed collections in `ReadOnlyCollection<T>` (or use
`ImmutableArray<T>`), return `ReadOnlyMemory<byte>` without `TryGetArray` exposure, or
document the cast caveat explicitly.

### F2 — MODERATE: `WritePath` bypasses `MaxListItems` (writer/reader limit asymmetry)

**Actual:** `PackStreamPath`'s constructor enforces traversal validity but not size limits,
and `WritePathCore` writes the three field lists through `WriteCollectionHeader` without
checking `MaxListItems`. Every other writer entry point (`WriteList`, `WriteMap`,
`WriteStringList`) enforces the limit.

**Probe:** `Probe07_WritePath_BypassesMaxListItems_KnownDefect`. With
`new PackStreamLimits(maxListItems: 4)`: `WriteList(5 items)` throws
`PackStreamFormatException`, while `WritePath(5 nodes)` succeeds and emits `B3 50 95 …`;
reading those bytes with the same limits throws.

**Expected:** symmetric limits — the writer must not produce wire data that its own reader
rejects under identical configuration.

**Impact:** bounded (requires an unusually large in-memory path), but it breaks the
limit invariant that B2 message budgets will rely on.

**Remediation (not applied):** enforce `MaxListItems`/`MaxMapEntries` in `WritePathCore`
(or validate list sizes in the `PackStreamPath` constructor against injected limits).

### F3 — MODERATE: writer emits partial output when a nested validation fails

**Actual:** container writes stream into the `IBufferWriter<byte>` as they go. When a
nested element fails validation (e.g. an unpaired surrogate deep inside a list), the outer
marker, count and earlier elements have already been emitted, and the exception leaves that
partial output in place. No XML documentation states this contract.

**Probe:** `Probe06_WriterValidationFailureAfterPartialOutput_LeavesBytesWritten_KnownDefect`
— after `WriteList` throws, the buffer contains exactly `92 01`.

**Expected:** either atomic writes or a documented rule that callers must discard buffered
output when any write throws.

**Impact:** Bolt messages must be atomic; B2 framing will decide message boundaries, so an
undocumented partial-write contract is a real interoperability hazard if ignored.

**Remediation (not applied):** document "discard the writer's output on exception" on
`PackStreamWriter`, or buffer/roll back per top-level value.

### F4 — MINOR: generic structures may carry reserved graph tags with unusable arity

**Actual:** `WriteStructure(0x4E, 1 field)` emits `B1 4E 01`, which no reader accepts
(a Node requires four fields). The writer validates tag range and field count but not the
reserved-tag/arity relationship.

**Probe:** `Probe08_GenericStructureWithReservedGraphTag_IsWritableButUndecodable_KnownDefect`.

**Impact:** produces dead bytes only; the reader fails predictably.

**Remediation (not applied):** reject reserved graph tags in `WriteStructure` /
`PackStreamStructure`, or document that graph tags must only be used through the typed APIs.

### F5 — MINOR: reader-reuse hazard is under-documented

**Actual:** after any read failure the reader is not realigned; a subsequent read silently
interprets following bytes as a new value (`Probe09b`: after `B0 4E` fails, the trailing
`2A` reads as integer 42). The contract is recorded only in test comments, not in the
public API documentation.

**Remediation (not applied):** add the contract to `PackStreamReader`'s XML documentation.

### F6 — QUALIFICATION: claims that cannot be made yet

- **No driver interoperability claim.** No official `Neo4j.Driver` integration test exists.
  B1 supports the wire types; it does not demonstrate driver compatibility.
- **Value-type coverage is partial by design.** Temporal, spatial, duration and UUID values
  are not implemented. Non-graph unknown tags (e.g. `0x49`) pass through as generic
  structures (Probe15) rather than failing — acceptable pass-through, but BoltSharp cannot
  validate such values.
- **The element budget applies only to `ReadValue` trees.** Direct scalar reads
  (`ReadInteger`, …) are unbudgeted by design; B2 must wrap message decoding in `ReadValue`
  to inherit message-level budgets.
- **Reviewer independence is limited** (§1.3). Findings are evidence-backed, but a second,
  structurally independent review is still required before owner acceptance.

No CRITICAL and no MAJOR findings were reproduced. Every adversarial probe aimed at
memory safety (full marker sweep, exhaustive truncation, oversized declarations, deep
nesting, extreme integers) behaved deterministically with PackStream exception types only.

---

## 6. Unsupported and unqualified claims

The following are **not** claimed by this review:

- Neo4j Browser or `Neo4j.Driver` compatibility of any kind.
- Bolt protocol-level compatibility (no handshake, framing or message codec exists yet).
- Support for temporal, spatial, duration or UUID values.
- Suitability of the writer for untrusted, concurrently shared output buffers (see F3).
- That the review is a substitute for owner acceptance or a second independent review.

---

## 7. B2 readiness recommendation

**Recommendation: proceed to B2 (handshake and chunk framing) with the codec as-is,
carrying F1–F5 as tracked remediation items.**

Rationale:

- The wire-format core (integers, floats, strings, bytes, containers, graph structures,
  paths) matches the specification byte-for-byte in every independently derived fixture.
- Failure behaviour is deterministic and typed everywhere it was probed, including
  exhaustive truncation and a full marker sweep; no probe produced a foreign exception,
  a hang, or unbounded allocation.
- The aggregate element budget, depth limits and per-collection limits behaved as claimed
  under adversarial probes, which is the foundation B2 needs for message-level budgets.

Conditions to carry into B2/B4:

1. Resolve **F2** and **F3** before any client-facing message encoding: Bolt messages are
   atomic, and B2's framing layer must define (and B4 must honour) what happens when a
   message write fails mid-stream.
2. Resolve **F1** before exposing decoded values to application code or the mock backend.
3. Record **F4/F5** as documentation/remediation items.
4. Keep "PackStream value coverage" explicitly partial in any compatibility matrix.

Concrete API observations for B2 (no changes requested now): `ReadValue` over a complete
message buffer already provides per-message budgets; `PackStreamStructure` preserves
unknown tags and ordered fields, which is sufficient to represent Bolt request messages
such as `HELLO` (`01`), `RUN` (`10`) and `PULL` (`3F`) generically.

---

## 8. Final disposition

**SUPPORTED WITH QUALIFICATIONS.**

The B1 PackStream implementation is a correct, resource-safe and specification-faithful
foundation for B2. Qualifications: four bounded defects (F1–F4) and one documentation gap
(F5) were found and left for remediation; value-type coverage is intentionally partial; no
driver interoperability is claimed; and the review's independence is limited by having been
performed by the implementing agent.

**Evidence:** 411 passing tests (382 producer + 29 reviewer probes), 0 warnings, 0 errors,
production source unchanged (`git diff 7230f1289c7f48605b244d85439ac0d99f4420d4 -- src/`
is empty). Review artifacts: this report and
`tests/BoltSharp.Tests/Qualification/B1Q1ReviewerProbes.cs`.

Independent review of this review: PENDING. Owner acceptance: PENDING.
