# B1-A1 — PackStream owner acceptance and consolidation preparation

**Status:** ACCEPTED WITH QUALIFICATIONS (owner decision recorded 2026-11-04)
**Date:** 2026-11-03
**Prepared at:** B1-A1 closeout commit (see §10)
**Qualified HEAD under consideration:** `47344b654ef41c0a76002a70be7dd7ec02b1c22f`
**B1-R1 (remediation):** `93759a2759efeab5cc2ab2ec63d204a9e52e1945`
**B1-Q1 baseline:** `f40a2da0b3ba36eac0777a63576a75439a313e89`

This record began as additive closeout preparation, with the owner decision PENDING. The
owner decision is now recorded: see §11 (owner decision) and the updated decision fields in
§10. Sections 1–9 are preserved as the pre-decision qualification and preparation record.

---

## 1. Qualified commit identities and ancestry

Verified on the canonical checkout (`feature/protocol-spec`) before any change:

```text
git rev-parse HEAD                → 47344b654ef41c0a76002a70be7dd7ec02b1c22f (== qualified B1-Q2 HEAD)
git status --short                → empty (clean working tree)
git merge-base --is-ancestor f40a2da0 93759a2 → 0
git merge-base --is-ancestor 93759a2 47344b6  → 0
git merge-base --is-ancestor 47344b6 HEAD     → 0
```

Ancestry is linear: `f40a2da0` (B1-Q1 baseline + probes) → `93759a2` (B1-R1 remediation)
→ `47344b6` (B1-Q2 requalification). No changes existed after the qualified HEAD at
preparation start (`git diff 47344b6 HEAD` empty).

## 2. Scope of B1

B1 delivers the PackStream 1.0 codec only, in `src/BoltSharp/PackStream/`: scalars
(B1-A), strings/bytes (B1-B), recursive lists/maps/structures (B1-C), Bolt graph
structures (B1-D1) and Path (B1-D2), with configured resource limits, strict UTF-8,
defensive value ownership, and documented reader/writer failure contracts. No networking,
handshake, framing, messages, session state, or backend (B2+) is included.

## 3. Findings F1–F5 and closure evidence

| Finding | Disposition (B1-Q2) | Closure evidence |
| --- | --- | --- |
| F1 immutable value ownership (MODERATE) | CLOSED | B1-R1 §2; B1-Q2 §2 F1: array casts and `MemoryMarshal.TryGetArray` vectors neutralized across all seven public collections, bytes, and constructor inputs (arrays and lists); decoded values equivalent |
| F2 Path writer limits (MODERATE) | CLOSED | B1-R1 §3; B1-Q2 §2 F2: all three field lists bounded by `MaxListItems` in both passes, boundary±1 verified per field, wrong-limit-key falsification failed, writer/reader symmetry confirmed |
| F3 writer exception safety (MODERATE) | CLOSED (qualification closed in B1-A1) | B1-R1 §4; B1-Q2 §2 F3: single-code-path validate-then-emit verified; late-failure vectors leave destination empty; **qualification:** traversal-stability assumption for caller-supplied collections — now documented on `PackStreamWriter` (§4 below) |
| F4 reserved graph-tag arity (MINOR) | CLOSED (residual closed in B1-A1) | B1-R1 §5; B1-Q2 §2 F4: exact arities enforced for `4E/52/72/50`; valid-arity generic structures wire-compatible; **residual:** arity-only guarantee — now documented on `WriteStructure`/`PackStreamStructure` (§4 below) |
| F5 reader failure contract (MINOR) | CLOSED | B1-R1 §6; B1-Q2 §2 F5: contract documented on `PackStreamReader`/`Position`/`ReadValue`; precise position semantics pinned (incl. malformed-UTF-8 payload exclusion, budget exhaustion) |

## 4. The two LOW qualifications — closed by documentation (B1-A1)

Documentation-only changes were made in `PackStreamWriter.cs` (class contract and
`WriteStructure` remarks) and `PackStreamValue.cs` (`PackStreamStructure` class doc). No
executable code, public signature, wire format, test, or runtime behaviour changed; the
full diff consists solely of `///` XML documentation lines.

**F3 — two-pass writer atomicity.** The authoritative contract now states: for **stable
input**, validation failures commit no bytes to the destination; library-owned immutable
values (`PackStreamValue` and derived) always provide the required stability;
caller-provided collections passed to `WriteList`/`WriteMap`/`WriteStructure` must remain
unchanged between the validation and emission traversals — mutation between traversals
may produce partial output; exceptions originating from the `IBufferWriter<byte>`
destination are excluded; **no general transactional or rollback guarantee is claimed**.
The B1-R1 report's broader phrase ("a failed write never commits partial output") is
preserved unchanged as history; this section and the XML documentation are the
authoritative current contract. Boundary evidence: B1-Q2 probe
`B1Q2_F3_UnstableCallerCollection_CanDefeatTwoPassAtomicity_Documented` (partial `91`
header under a mutating caller collection).

**F4 — reserved graph-tag validation.** The authoritative documentation now states:
reserved tags `0x4E`, `0x52`, `0x72`, `0x50` are validated for required arity (4, 8, 4,
3); **correct arity does not establish semantic validity of the fields**; a generic
structure with valid arity may still fail typed graph decoding when its field values do
not conform; this is within the accepted B1 contract and no new semantic validation is
required for B1 acceptance; the typed graph writers carry the full decodability
guarantee. Boundary evidence: B1-Q2 probe
`B1Q2_F4_ReservedTagValidArityWrongFieldTypes_WritableButNotTypedDecodable_Documented`.

## 5. Independent B1-Q2 verdict

`docs/reviews/B1-Q2-packstream-requalification.md`: **SUPPORTED WITH QUALIFICATIONS** —
all five findings CLOSED; no regressions; no CRITICAL/MAJOR/MODERATE defect reproduced;
two LOW documentation qualifications (both closed as above). Evidence at the time: 509
passing tests including 24 independent B1-Q2 probes.

## 6. Explicit supported guarantees and exclusions

**Supported (B1 scope):**

- PackStream 1.0 wire format for the implemented types, byte-identical to independently
  derived golden fixtures (285 golden tests).
- Immutable, defensively owned value representations (F1 contract).
- Symmetric writer/reader resource limits, including Path field lists (F2 contract).
- Write atomicity for stable, immutable values under value-validation failures (F3
  contract, with the documented stability requirement and exclusions).
- Reader failure contract: position tracks bytes consumed before a failure; after any
  decoding exception the caller must discard the reader; no rollback (F5 contract).
- Deterministic, typed failure behaviour with bounded allocations under the configured
  limits.

**Exclusions / non-goals (unchanged from B1-Q1 §6):**

- No Neo4j Browser / `Neo4j.Driver` / Bolt protocol-level (handshake, framing, messages)
  compatibility claim — B2 and later tasks.
- Temporal, spatial, duration and UUID value types are not implemented; unknown
  non-graph tags pass through as generic structures.
- Direct scalar reads (`ReadInteger`, …) are unbudgeted by design; message-level budgets
  belong to B2 wrapping.
- No transactional/rollback guarantee over `IBufferWriter<byte>` destinations (F3).
- Immutability is not defended against reflection or hostile memory managers.

## 7. Evidence preservation verification

Verified byte-for-byte unchanged since creation (empty diffs at B1-A1 preparation start):

```text
git diff f40a2da0..47344b6 -- docs/reviews/B1-Q1-packstream-qualification.md → empty
git diff 93759a2..47344b6  -- docs/reviews/B1-R1-packstream-remediation.md  → empty
git diff 47344b6..47344b6  -- docs/reviews/B1-Q2-packstream-requalification.md → empty
```

All reviewer and remediation probes preserved: 29 B1-Q1 probes (five revised in B1-R1 to
verify the corrections while retaining the original attack vectors — see B1-Q2 §3), 24
B1-Q2 probes, 74 B1-R1 remediation regressions. None altered by B1-A1.

## 8. Test results (B1-A1 final verification)

Commands and results (recorded in §10; .NET SDK 10.0.401, target net8.0):

```text
dotnet restore BoltSharp.slnx        → ok, no new packages
dotnet build BoltSharp.slnx --no-restore → 0 warnings, 0 errors
dotnet test BoltSharp.slnx --no-build    → Passed! Failed: 0, Passed: 509, Skipped: 0
```

Accounting: 382 producer + 29 B1-Q1 probes + 74 B1-R1 remediation + 24 B1-Q2 probes =
509 — identical to the qualified baseline, as expected: the only B1-A1 changes are XML
documentation comments and this record.

## 9. Remaining non-goals and deferred work

- B2 handshake/framing (not authorized), B3+ session/transport/backend work.
- Value-type coverage extensions (temporal, spatial, duration, UUID) — later tasks.
- Optional future hardening (not required for B1 acceptance): none identified beyond the
  closed qualifications; the F3 stability note and F4 arity note are documentation
  items now closed.

## 10. Consolidation, authorization and decision record

- **Owner decision:** ACCEPTED WITH QUALIFICATIONS — recorded 2026-11-04 (see §11)
- **Consolidation:** AUTHORIZED (see §11)
- **Push of canonical `main`:** AUTHORIZED after verification (see §11)
- **Package publication:** NOT AUTHORIZED (see §11)
- **B2:** AUTHORIZED after verified consolidation (see §11)
- **Consolidation readiness:** `main` (`49a4c87`) is an ancestor of the qualified HEAD;
  at preparation the branch was 14 commits ahead, 0 behind → clean fast-forward is
  available. Plan and safeguards: see §11 of the B1-A1 handoff (not executed in that task).
- **Commit identities:** see §1; the B1-A1 documentation closeout commit SHA and the owner
  decision commit SHA are recorded in the final handoff message of their respective tasks
  (this file is part of the owner decision commit, so it cannot contain its own SHA).

Before the owner decision, no approval was recorded or inferred; pushing, publishing,
consolidation and B2 each required separate explicit authorization. The authorization is
now recorded in §11.

---

## 11. Owner decision record (2026-11-04)

**Owner decision: ACCEPTED WITH QUALIFICATIONS.**

The owner reviewed the B1 PackStream deliverable against the B1-Q1 independent
qualification (`f40a2da0`), the B1-R1 remediation (`93759a2`), the B1-Q2 independent
requalification (`47344b6`), and this acceptance preparation record, and records the
following authorized work:

1. **B1 is accepted with qualifications.** The two LOW qualifications of §4 — the F3
   traversal-stability assumption for caller-supplied collections and the F4 arity-only
   guarantee for generic reserved-graph-tag structures — are accepted as documented
   contract boundaries. They are established in the authoritative XML documentation on
   `PackStreamWriter` (exception-safety/stability contract) and
   `PackStreamValue.PackStreamStructure` (reserved-graph-tag arity) and are preserved
   unchanged by this decision. No executable behaviour, public signature, wire format,
   limit, golden fixture, or test was changed to record it.
2. **Consolidation to canonical `main` is authorized.** `main` may be fast-forwarded to the
   accepted `feature/protocol-spec` history including this record. No merge commit, squash,
   rebase, amend, or history rewrite is permitted, and no additional worktrees are to be
   used. The consolidation must stop if ancestry, scope, or working-tree state differs from
   expectations.
3. **Push of canonical `main` is authorized after verification.** The push must be a
   normal fast-forward push (no force). The obsolete feature branch is not to be pushed.
4. **Package publication is NOT authorized.** No NuGet package or other published artifact
   may be produced from this decision or from B1.
5. **B2 is authorized to begin after verified consolidation**, on canonical `main`, with its
   own scope, implementation contract, acceptance criteria, and independent qualification
   requirements established first. Driver compatibility must not be claimed until
   independently demonstrated by executable integration tests.

**Qualification preservation.** The F3 and F4 qualifications are those established in the
authoritative XML documentation and summarized verbatim in §4; this record neither restates
nor alters them. The historical B1-Q1, B1-R1, and B1-Q2 reports are not modified by this
decision.
