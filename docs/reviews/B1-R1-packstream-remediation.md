# B1-R1 — PackStream qualification remediation

**Status:** COMPLETE (B1-R1 remediation delivered; independent requalification PENDING)
**Date:** 2026-11-03
**Baseline:** `f40a2da0b3ba36eac0777a63576a75439a313e89` (branch `feature/protocol-spec`)
**Remediates:** findings F1–F5 of [`B1-Q1-packstream-qualification.md`](B1-Q1-packstream-qualification.md)

This document is additive remediation evidence. It does not alter the original review,
its findings, or its historical disposition. See the review's §4/§5 for the original
probe and finding evidence; the original defect-demonstrating probe versions are preserved
in git history.

---

## 1. Summary

| Finding | Severity | Correction | Evidence |
| --- | --- | --- | --- |
| F1 — immutable value ownership | MODERATE | All public value representations establish defensively owned state; collections are exposed through `ReadOnlyCollection<T>`; byte payloads are copied on every public read | `B1R1F1ImmutabilityTests` (14), remediated probes 05/05b |
| F2 — `WritePath` bypassed `MaxListItems` | MODERATE | All three Path field lists enforce `MaxListItems` before emission | `B1R1F2PathWriterLimitTests` (9), remediated probe 07 |
| F3 — partial output on validation failure | MODERATE | Single-code-path validate-then-emit: every public composite write runs its own recursive validation pass (no writing) before committing bytes | `B1R1F3ExceptionSafetyTests` (21), remediated probe 06 |
| F4 — reserved graph tags with invalid arity | MINOR | Writer rejects reserved tags 4E/52/72/50 unless the field count is exactly 4/8/4/3; valid-arity and unknown tags unchanged | `B1R1F4ReservedGraphTagTests` (26), remediated probe 08 |
| F5 — reader-reuse hazard undocumented | MINOR | Failure contract documented on `PackStreamReader` (XML docs): discard the reader after any decoding exception | `B1R1F5ReaderFailureContractTests` (4) |

## 2. F1 — immutable value ownership

**Root cause:** `PackStreamList.Items`, `PackStreamMap.Entries`, `PackStreamStructure.Fields`,
`PackStreamNode.Labels` and `PackStreamPath.Nodes`/`UnboundRelationships`/`Indices` stored
arrays behind `IReadOnlyList<T>` properties, so a cast back to the array type reached the
backing storage; `PackStreamBytes.Value` exposed the private payload array through
`MemoryMarshal.TryGetArray`.

**Correction (no external dependencies, no hierarchy redesign):**

- Public constructors copy caller-supplied collections into privately owned arrays before
  wrapping them; mutating a constructor input after construction cannot change a value or
  its encoded bytes.
- Public collection properties return `System.Collections.ObjectModel.ReadOnlyCollection<T>`
  wrappers: array casts fail with `InvalidCastException`, and mutation through the
  `IList<T>`/`ICollection<T>` views throws `NotSupportedException`.
- `PackStreamBytes` keeps an exclusively owned private array; `Value` returns memory over a
  fresh copy on each access, so mutation via `MemoryMarshal.TryGetArray` cannot change the
  stored payload. Internal encoding reads the owned array directly (no repeated copies).
- The reader hands internally built state to internal ownership constructors
  (`PackStreamList(List<>)`, `PackStreamMap(List<>)`, `PackStreamStructure(List<>)`,
  `PackStreamNode(string[] labels)`, and the pre-existing `PackStreamPath` array
  constructor), so decoded values have the same guarantees without redundant copies.
- The `ReadBytes()` convenience API intentionally remains caller-owned (returns a fresh
  `byte[]`); it is not part of the immutable-value contract and is pinned by a test.

## 3. F2 — Path writer limits

**Root cause:** `WritePathCore` wrote the three field lists through `WriteCollectionHeader`
without checking `MaxListItems`, while every other list-writing entry point enforced it.

**Correction:** `WritePathCore` compares each of `path.Nodes.Count`,
`path.UnboundRelationships.Count` and `path.Indices.Count` against `MaxListItems` (plain
integer comparison; no arithmetic, no overflow, no temporary allocations) before any byte
is emitted. The reader's limits were not changed. Valid Path encodings are byte-identical.

Note: Path indices must be even in count (traversal pairs), so the nearest constructible
count above a boundary is boundary+2 for that field; the tests document this.

## 4. F3 — exception safety of writes

**Root cause:** container writes streamed into the `IBufferWriter<byte>` while recursing,
so a nested validation failure left the outer marker, count and earlier elements committed.

**Correction:** every recursive writer core takes a `validateOnly` flag. Each public
composite entry point (`WriteList`, `WriteMap`, `WriteStructure`, `WriteNode`,
`WriteRelationship`, `WriteUnboundRelationship`, `WritePath`, `WriteValue`) runs its own
core once in validation mode and once in emission mode. Because validation and emission
share a single code path, they cannot diverge: any input the validation pass accepts is
emitted byte-for-byte as before, and any rejection happens before the first byte is
committed. The pass is bounded (it walks the value tree once, allocates nothing, and is
depth-bounded by `MaxDepth`); no full-size staging buffer is used. Scalar writes
(`WriteString`, `WriteBytes`) already validated before writing and remain atomic.

**Contract (documented on `PackStreamWriter`):** a `PackStreamException` raised by value
validation leaves the destination exactly as it was before the call. Failures raised by the
destination writer itself (a throwing or contract-violating `IBufferWriter<byte>`) are
outside the contract; their effect on the destination is undefined. `IBufferWriter<byte>`
has no general rollback mechanism, and no transactional behaviour is claimed for
destination-originated failures.

## 5. F4 — reserved graph-tag arity

**Root cause:** `WriteStructure` validated tag range and field count independently of the
Bolt graph semantics, so `B1 4E 01` (a "Node" with one field) could be emitted although no
reader accepts it.

**Correction:** `ValidateReservedGraphTagArity` requires — writer-side only — the exact
field counts: `4E` → 4, `52` → 8, `72` → 4, `50` → 3. Generic structures using these tags
with the correct arity remain writable (e.g. a `PackStreamStructure` with tag `4E` and four
valid fields still encodes and decodes as a Node), and unknown tags (including future
protocol tags such as temporal `0x49`) accept any field count from 0 to 15.

## 6. F5 — reader behaviour after failure

**Contract (documented on `PackStreamReader`, `Position` and `ReadValue`):** when any read
operation throws, bytes consumed up to the failure point are reflected in `Position`, but
the reader is not realigned and provides no rollback or recovery. After a decoding
exception the caller must discard the reader; further reads on the same instance are
unspecified. No rollback or recovery was implemented.

## 7. Reviewer probes (B1-Q1 §4)

The 29 probes remain in `tests/BoltSharp.Tests/Qualification/B1Q1ReviewerProbes.cs`. Five
of them (05, 05b, 06, 07, 08) originally demonstrated defects F1–F4 by passing while the
defect existed. Because the remediation removed those defects, their defect-asserting
expectations became impossible to satisfy; each probe now retains the reviewer's original
attack vector and asserts the corrected behaviour, is annotated with the finding it
verifies, and was renamed from `..._KnownDefect` to `..._Remediated`. No probe was weakened,
skipped or deleted, and the review report was not edited. All 29 probes pass.

## 8. Verification

- Baseline at `f40a2da0…`: 411 passed / 0 failed / 0 skipped (382 producer + 29 probes).
- After remediation: **485 passed / 0 failed / 0 skipped** (382 producer + 29 probes +
  74 remediation regressions), 0 build warnings, 0 errors.
- No new external packages; no changes outside B1 scope (`src/BoltSharp/PackStream/`,
  tests, and this document).
