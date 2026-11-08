# B3-B-OWNER-01 — owner acceptance of the Bolt 5.8 connection state machine

**Status:** ACCEPTED WITH QUALIFICATIONS (owner decision recorded 2026-11-08)
**Date:** 2026-11-08 (Asia/Manila; execution timestamps are UTC)
**Task:** B3-B-OWNER-01 — formal owner acceptance and milestone closure
**Canonical branch:** `main` (one worktree)
**Protocol target:** Bolt 5.8 / PackStream 1.0
**Starting HEAD / tree:** `fe5b4810bcb30d183f6928c00764bae579374d41` / `adfd1e899a9589cd63f62c6ea693263c1bb910a1`

This record is an owner decision. It contains **no implementation**, modifies no production, test,
historical or evidence file, and makes **no** Neo4j / `Neo4j.Driver` compatibility claim. It records
acceptance only for the bounded scope in §7, and it references — rather than replaces — the B3-B
implementation, remediation and qualification history.

---

## 1. Owner decision

**Owner decision: B3-B — ACCEPTED WITH QUALIFICATIONS — CLOSED.**

The owner reviewed the complete B3-B qualification chain — implementation
([B3-B-IMPL-01](../reviews/B3-B-IMPL-01.md): *IMPLEMENTED, READY FOR INDEPENDENT QUALIFICATION*),
initial qualification ([B3-B-Q1](../reviews/B3-B-Q1-independent-qualification.md):
*REMEDIATION REQUIRED (bounded)*), first remediation
([B3-B-R1-IMPL-01](../reviews/B3-B-R1-IMPL-01.md): *IMPLEMENTED, READY FOR INDEPENDENT B3-B-Q2
REQUALIFICATION*), second qualification ([B3-B-Q2](../reviews/B3-B-Q2.md): *REMEDIATION REQUIRED*),
second remediation ([B3-B-R2-IMPL-01](../reviews/B3-B-R2-IMPL-01.md): *IMPLEMENTED, READY FOR
INDEPENDENT B3-B-Q3 REQUALIFICATION*) and final requalification
([B3-B-Q3](../reviews/B3-B-Q3-independent-requalification.md): *SUPPORTED WITH QUALIFICATIONS;
Q2-F01 CLOSED; Q2-F02 CLOSED*) — against the committed evidence under `docs/reviews/evidence/`
and the reproductions recorded in §16, and records:

1. **B3-B is accepted with qualifications**, within the scope of §7, bounded to Bolt 5.8 /
   PackStream 1.0.
2. **Q1-F01 is CLOSED** (§8.1): cleanup-exception containment, completed across R1 and R2 and
   verified by Q3 under adversarial failures.
3. **Q1-F02 is CLOSED** (§8.2): invalid-integer TELEMETRY metadata, corrected by R1, supported by
   Q2 and preserved through R2/Q3.
4. **Q2-F01 is CLOSED** (§8.3): unsafe RESET recovery after failed retirement, corrected by R2 and
   independently verified by Q3 (`opens` stays 1; RESET cannot restore READY).
5. **Q2-F02 is CLOSED** (§8.4): metadata acquisition outside the ownership boundary, corrected by
   R2 and independently verified by Q3 (single acquisition, controlled retirement, no abandoned
   handle).
6. **The fail-closed retirement contract is accepted as normative** (§9): an unsuccessful
   retirement attempt permanently invalidates the connection.
7. **Historical probe expectations are adjudicated, not erased** (§9.2, §10): Q1's X04
   difference is an expected contract correction; all 181 checks of the unchanged Q2 probe are
   individually classified (152 pass-unchanged, 11 defect-closed, 15 expectation-superseded,
   3 unreachable-after-correction, zero unresolved), its 18 currently failing checks are
   exactly the expectation-superseded and unreachable groups, and passing Q3 replacement
   assertions cover each. The historical Q2 probe **does not pass** and is not claimed to pass.
8. **The reviewer-executor overlap is explicitly acknowledged** (§11): Q3 was executed by the same
   agent session as R2. This bounded waiver is recorded with rationale and a discretionary future
   verification requirement; no claim of organizational independence is made.
9. **All binding qualifications are preserved** (§12) and the **non-claims in §13** stand.
10. **B3-C is not authorized by this decision** (§14); it requires a separate commission.
11. Reviewer dispositions are reviewer conclusions, not owner decisions. Recording acceptance does
    not modify, supersede or rewrite any report, probe, manifest or evidence file.

---

## 2. Verified entry state

Independently read from the canonical checkout before this decision:

```text
git branch --show-current  -> main
git rev-parse HEAD         -> fe5b4810bcb30d183f6928c00764bae579374d41 (matches the commission)
git rev-parse HEAD^{tree}  -> adfd1e899a9589cd63f62c6ea693263c1bb910a1 (matches the commission)
git worktree list          -> one worktree (D:/projects/bolt-sharp)
git status --porcelain     -> clean; no concurrent writer
prior B3-B-OWNER-01        -> none existed (docs/decisions/ contained only the B0-A/B0-B/B2-B/B2-C/B3-A decisions)
push state                 -> origin/main (9abf5e3724a31aff085b3810ebb9bd3a7783ea2a) unchanged; nothing pushed
```

## 3. Qualification chain

| Stage | Revision / tree | Disposition |
| --- | --- | --- |
| B3-A accepted baseline | `c2881ea` / `4285faa8579033d981fa50919925583aea2e19c5` | ACCEPTED WITH QUALIFICATIONS — CLOSED (B3-A-OWNER-01) |
| B3-B implementation | `9f224cf` / `bae5940dfd255a80eba6a67ce44208cd398d4b5e` | IMPLEMENTED, READY FOR INDEPENDENT QUALIFICATION |
| B3-B implementation evidence | `81c0af9` / `27097120fc5f64d5c9cece16ba0091e9aa5aed82` | evidence recorded |
| B3-B-Q1 | `3caab5b` / `e1399dcb0d2080323c529cf5986e14c60721cb71` | REMEDIATION REQUIRED (bounded) |
| B3-B-R1 implementation | `454ae00` / `f93388ca7a23bcf727b6c76b8435d0b1f609f5ad` | IMPLEMENTED, READY FOR INDEPENDENT B3-B-Q2 REQUALIFICATION |
| B3-B-R1 evidence | `801badc` / `243998eb21d01aa918f4b83143cb8a0c6bb27dd1` | evidence recorded |
| B3-B-Q2 | `4ae87b4` / `f4d2ebb606ac2c34f2b5ffe6e39eb700d71e7852` | REMEDIATION REQUIRED |
| B3-B-R2 implementation | `7eb9a8f` / `c62e9d21ea578c4186bf34ca0b9eb280055eae4d` | IMPLEMENTED, READY FOR INDEPENDENT B3-B-Q3 REQUALIFICATION |
| B3-B-R2 evidence | `b9356c3` / `f592acde2571e2084601b85a147eb48098b987e7` | evidence recorded |
| B3-B-Q3 | `fe5b481` / `adfd1e899a9589cd63f62c6ea693263c1bb910a1` | SUPPORTED WITH QUALIFICATIONS (Q2-F01/Q2-F02 CLOSED) |
| Owner-acceptance commit (this file) | recorded in the final handoff (this file cannot contain its own SHA) | — |

Ancestry is linear and additive: `c2881ea → 9f224cf → 81c0af9 → 3caab5b → 454ae00 → 801badc →
4ae87b4 → 7eb9a8f → b9356c3 → fe5b481`. Every revision and tree above was verified in the
canonical checkout (`git cat-file`/`git rev-parse`); no discrepancy was found.

## 4. Evidence reviewed

Implementation: `docs/reviews/B3-B-IMPL-01.md`. Qualifications and remediations:
`B3-B-Q1-independent-qualification.md`, `B3-B-R1-IMPL-01.md`, `B3-B-Q2.md`, `B3-B-R2-IMPL-01.md`,
`B3-B-Q3-independent-requalification.md`. Evidence directories: `B3-B-IMPL-01`, `B3-B-Q1`,
`B3-B-R1-IMPL-01`, `B3-B-Q2` (including the 181-check adversarial probe, its retained pre-R2
results and the ~38-file manifest), `B3-B-R2-IMPL-01` (before/after Q2 reconciliation and the
regression record) and `B3-B-Q3` (independent probe, 129/129 checks, negative controls, full
181-check reclassification). Also reviewed: the current production session sources and tests under
`src/BoltSharp/Sessions/` and `tests/BoltSharp.Tests/Sessions/`, the B3-A owner acceptance
(`docs/decisions/B3-A-OWNER-01-owner-acceptance.md`) as the accepted message-contract boundary,
the B0-B owner acceptance and integration evidence, and the preserved Bolt 5.8 normative snapshots
under `docs/reviews/evidence/B3-B-IMPL-01/spec/`.

## 5. Evidence integrity

Verified from the manifests and the working-tree bytes at `fe5b481`:

| Evidence package | Result |
| --- | ---: |
| B3-B-Q3 | **33/33** |
| B3-B-R2 | **29/29** |
| B3-B-Q2 | **38/38** (the retained 181-check pre-R2 results unchanged, SHA-256 `1ef748c7…`) |
| B3-B-Q1 | **12/12** |
| B3-A-Q1 | **14/14** |
| B3-A-R1 | **13/13** |
| B3-A-Q2 | **15/15** |
| B0-B producer | **24/24** |
| B0-B-Q1 | **119/119** |

Expected historical source evolution, confirmed rather than treated as corruption:

- **B3-B-IMPL-01 currently matches 19/22 entries.** The three non-matching entries are the session
  source, session options and the authentication test file changed by the authorized R1 and R2
  remediations. Each manifest hash was verified by Q3 at blob level against the recorded historical
  commit (`9f224cf`); the original files and all evidence files remain unchanged.
- **B3-B-R1 currently matches 12/14 entries.** The two non-matching entries were changed by the
  authorized R2 remediation (verified at `454ae00` blob level).
- **B3-A-IMPL-01 remains 16/21** because of its authorized R1 changes (five message sources/tests),
  as recorded in the B3-A owner acceptance; the difference is unchanged and explained.

**Historical manifests describe their original revisions, not the latest production tree.** No
manifest, report, probe or evidence file was edited to obtain these results. Historical artifacts
are byte-unchanged since their commits: Q1 (since `3caab5b`), Q2 (since `4ae87b4`), R2 (since
`b9356c3`) and B3-B-IMPL-01 (since `81c0af9`) all verify with zero changed paths.

## 6. Current regression status (freshly executed for this decision)

Executed read-only at `fe5b481` on 2026-11-08 (Windows 10.0.26200, .NET SDK 10.0.401, MSBuild
18.9.11; build via `BoltSharp.slnx` with `--no-incremental`, artifacts directed outside the
repository; no repository file changed):

| Suite | Result |
| --- | ---: |
| Core test project | **1,043 passed / 0 failed / 0 skipped** |
| B3-B session filter | **119 passed / 0 failed** |
| R1 remediation class (`CleanupAndTelemetryRemediationTests`) | **21 passed / 0 failed** |
| R2 remediation class (`ResourceLifecycleRemediationTests`) | **18 passed / 0 failed** |
| B3-A message filter | **98 passed / 0 failed** |
| B0-B official-driver integration project | **19 passed / 0 failed** |
| Build | **0 errors; 2 warnings** — both pre-existing `xUnit2012` at `BoltRequestDecodeTests.cs:71-72`; no new warning |

Historical reviewer probes (freshly re-executed at the same revision): the unchanged Q1 probe
reports 118 checks / 117 passing / exit 4 (the single historical X04 expectation, §10); the
unchanged Q2 probe reports 181 checks / 163 passing / 18 failing / 217 trace steps / exit 4
(adjudicated in §10); the independent Q3 probe reports 129 checks / 129 passing / exit 0. These
counts reproduce the Q3 evidence exactly.

## 7. Accepted B3-B capabilities

Subject to the qualifications in §12, the owner accepts the following bounded capabilities, each
established by the reproduced evidence cited in the referenced reports.

### 7.1 Connection state management

Explicit representation and enforcement of NEGOTIATION, AUTHENTICATION, READY, STREAMING, FAILED,
INTERRUPTED (within its documented synchronous limits) and DEFUNCT, with legal transitions
enforced within the implemented auto-commit subset. No explicit transaction-state support is
claimed.

### 7.2 Authentication gating

HELLO/LOGON sequencing; injected credential verification; fail-closed behavior with no verifier;
deterministic refusal of failed and throwing verification; reauthentication after LOGOFF; no query
execution before authentication; no credential disclosure in the verified protocol-response paths
(sentinel scans in Q1 and Q3 found no leakage).

### 7.3 Scripted operation lifecycle

RUN preparation through the provisional `IBoltOperationProvider` seam; result-field metadata; PULL
and DISCARD sequencing; finite result pagination; bounded incremental streaming; a single active
auto-commit result; cursor ownership and retirement. This covers a scripted operation provider,
not a production graph backend.

### 7.4 Failure handling

Recoverable FAILURE; specification-correct zero-field IGNORED (`B0 7E`); RESET recovery when
retirement succeeded; terminal handling of unrecoverable failures; GOODBYE without fabricated
SUCCESS; idempotent connection termination; primary/cleanup failure separation.

### 7.5 Contextual validation

NC-05b RECORD column-count consistency against the RUN-time metadata snapshot; NC-08
response-terminal sequencing; no execution of invalid-state requests; ordered processing of
supported request batches.

### 7.6 TELEMETRY

Accepted identifier domain 0–3; out-of-domain integer identifiers answered with the Bolt 5.8
FAILURE metadata verified in §8.2; the wrong-typed structural boundary retained at B3-A (§12,
Q-B3B-07).

### 7.7 Resource safety

Immediate session ownership after acquisition; controlled metadata validation; a single metadata
snapshot used for publication and column checks; exactly one disposal attempt per acquired
operation; persistent fail-closed behavior after unsuccessful retirement; no new operation after
failed disposal; no RESET recovery from irrecoverable cleanup.

---

## 8. Formal finding closure

### 8.1 Q1-F01 — cleanup exception containment — **CLOSED**

Original defect (Q1): a provider `Dispose()` that threw escaped `GOODBYE`, `Disconnect()`, session
`Dispose()` and the RUN-null-`Fields` path, leaving the session non-terminal. Closure chain: R1
(`454ae00`) introduced guarded disposal (`DetachAndDisposeOperation`/`DisposeGuarded`), terminal
state independent of cleanup outcome, idempotent termination and the `LastCleanupFailure`
diagnostic, and updated the null-`Fields` path; R2 (`7eb9a8f`) corrected the remaining
resource-retirement and recovery weaknesses (Q2-F01 and Q2-F02 below). Q3 verified containment
under adversarial providers: GOODBYE/Disconnect/Dispose with throwing disposal reach DEFUNCT with
exactly one attempt and no escape; the R1 remediation class passes 21/21; Q1's own probe now fails
only the superseded X04 expectation. R1 alone is **not** claimed as sufficient closure; the Q3
verification covers the combined chain.

### 8.2 Q1-F02 — TELEMETRY status metadata — **CLOSED**

Original defect (Q1): out-of-domain integer TELEMETRY identifiers returned `22G03` /
`Neo.ClientError.Request.Invalid`. R1 corrected the mapping to `gql_status = 50N00` and
`neo4j_code = Neo.DatabaseError.General.UnknownError` with the four-field accepted FAILURE shape;
Q2 independently supported the correction against the retained Bolt 5.8 snapshots (Example 3,
v5.7+); Q3 preserved it (message suite 98/98; the Q2 probe's TELEMETRY checks remain passing;
invalid-identifier metadata unchanged). No further remediation is required.

### 8.3 Q2-F01 — unsafe RESET recovery after failed retirement — **CLOSED**

Original defect (Q2): after a failed disposal on PULL/DISCARD completion, reading or RECORD-shape
validation, the session entered FAILED and RESET returned SUCCESS/READY while the resource
remained live, allowing RUN to open a second operation (`DisposeAttempts=1; ResourceLive=true;
RESET→SUCCESS/Ready; opens=2`). R2 made unsuccessful retirement authoritative: `CompleteResult`
and `FailRecoverable` now establish terminal DEFUNCT with the primary FAILURE still reported once.
Q3 independently verified: failed disposal results in DEFUNCT; RESET cannot restore READY; no
second operation opens (`opens` stays 1 on all five paths); disposal is attempted exactly once and
never retried; no new provider execution occurs; safe retirement still permits the documented
FAILED/RESET recovery. All five Q2 `*-no-overlap` checks were re-established by Q3 (DEFECT_CLOSED).

### 8.4 Q2-F02 — metadata acquisition outside the ownership boundary — **CLOSED**

Original defect (Q2): a throwing first `Fields` getter escaped with zero disposal attempts and the
session still READY; later metadata failures escaped after installation, leaving STREAMING without
a RUN response; column checks re-read provider metadata. R2 established ownership immediately on
a non-null `Prepare` return, introduced the single-acquisition `TryCaptureFields` snapshot,
contained getter/`Count`/indexer/null-element failures, published STREAMING only from the
validated snapshot, and made metadata failure plus unsuccessful retirement terminal. Q3
independently verified: metadata is captured once (`FieldsReads==1` across full lifecycles,
including withdrawn and changing providers); getter, `Count` and indexer exceptions are contained;
invalid metadata causes controlled retirement; metadata failure followed by disposal failure
becomes DEFUNCT; no STREAMING publication without valid metadata; no operation is abandoned
outside session ownership (teardown retires exactly once). The six Q2 F02 checks were
re-established by Q3 (DEFECT_CLOSED).

---

## 9. Normative fail-closed retirement contract

The owner explicitly accepts the following B3-B invariant as normative:

> **An unsuccessful retirement attempt permanently invalidates the connection.**

Required consequences, each verified by Q3 and accepted:

1. Once an operation's disposal fails, the session becomes DEFUNCT.
2. The session must not return to READY.
3. RESET cannot recover the connection.
4. No subsequent RUN may open another operation.
5. The failed operation is not disposed a second time.
6. The external resource is not falsely reported as released (the diagnostic and evidence state
   the resource remains live; no physical-release claim is made).
7. Primary and cleanup failures remain distinguishable (primary FAILURE metadata on the wire;
   the exact disposal exception in `LastCleanupFailure`).
8. The terminal state remains irreversible (DEFUNCT has no outgoing transition; every later
   message is refused without provider activity).

This contract **supersedes earlier expectations** that allowed a recoverable FAILED state after
unsuccessful disposal. The superseding tests are recorded in R2 §13 and re-proven by Q3; the
historical reports and probes keep their original expectations and are not rewritten.

### 9.1 Historical X04

Q1's historical X04 difference (`no-throw;Failed` expected vs `no-throw;Defunct;FAILURE`
observed) is accepted as an **expected contract correction**, not a defect: RUN with invalid
metadata (null `Fields`) and unsuccessful cleanup must not leave a recoverable session. Q3
re-verified the current contract (`x04-contained`, `x04-no-recovery` pass).

### 9.2 Historical Q2 probe

The unchanged Q2 probe continues reporting 181 checks / 163 passing / 18 failing / exit code 4.
**The historical Q2 probe is not claimed to pass.** Q3 classified all 181 checks:
152 PASS_UNCHANGED; 11 DEFECT_CLOSED; 15 EXPECTATION_SUPERSEDED; 3
UNREACHABLE_AFTER_CORRECTION; zero unresolved or inconclusive. The replacement Q3 assertions for
every superseded or unreachable expectation pass. The correct statement is that the remaining
failures have been individually adjudicated against the accepted, stricter contract — not that
the probe succeeds. The original evidence remains byte-unchanged (manifest 38/38).

## 10. Reviewer-independence governance decision

The project working contract (`docs/boltsharp-spec.md` §11) requires that "a separate
reviewer/model checks against the official protocol docs and golden fixtures" and that the
implementer remediate and re-run before the owner accepts. The B3-B history substantially met
this: the two blocking defect families were found by separately executed Q1 and Q2 reviewer
sessions (Q1/Q2 ran different sessions from their producers, and Q1's F-01/F-02 were later
completed by Q2's independent residual analysis). However, **Q3 was executed by the same agent
session as the R2 producer** ("DeepSeek V4.1 Flash", this session). Strict different-executor
independence for the final R2→Q3 cycle is therefore **not** established, and this record must not
be described as an organizationally independent signoff.

**Bounded waiver (explicit, not implicit).** The owner accepts Q3 as sufficient evidence for this
bounded milestone with the following rationale:

1. The failure scenarios were originally found and reproduced by separately executed Q1/Q2
   reviewers, not by the producer alone.
2. R2 addresses two narrowly identified root causes with a bounded, reviewed diff.
3. Q3's probes are separately constructed verification artifacts (129 checks, 165 deterministic
   traces) that share no code with the producer tests or earlier probes.
4. All historical evidence is immutable and re-verifiable (manifest results in §5).
5. Q3's negative controls demonstrate sensitivity to each targeted defect mode.
6. The corrected behavior is verified through observable counters (`opens`, disposal attempts,
   metadata access counts, state transitions) rather than internal flags alone.
7. Full external driver/Browser interoperability remains outside the accepted claim (§13) and is
   reserved for later milestones where independent qualification can be re-established.

**Discretionary future verification requirement.** For any release-grade or interoperability
qualification (B5-B onward, and any future package/publication review), the owner requires a final
requalification executed by a **different executor session/model** than the corresponding
implementer. If a project governance rule is later adopted that makes different-executor
independence mandatory for acceptance, this bounded waiver does not set a precedent; the rule
governs prospectively.

The historical Q2 reviewer byline and provenance are **not** silently changed; the byline
discrepancy note recorded in R2 §20 remains as-is.

## 11. Historical evidence retained unchanged

- Q1 report/probe/evidence: unchanged since `3caab5b` (zero paths).
- Q2 report/evidence (including the pre-R2 181-check results, SHA-256 `1ef748c7…`): unchanged
  since `4ae87b4` (zero paths).
- R2 report/evidence: unchanged since `b9356c3` (zero paths).
- B3-B-IMPL-01 report/evidence: unchanged since `81c0af9` (zero paths).
- No manifest, report, probe or raw result was edited to produce this decision.

---

## 12. Binding remaining qualifications

All material unresolved limitations are preserved. Cross-reference against the full Q1 §19 list
(ten items) and the Q3 report shows every original qualification is represented below.

- **Q-B3B-01 — Bounded PULL behavior.** `PULL(n=-1)` uses bounded incremental delivery through the
  accepted `has_more` mechanism; a client may need subsequent PULL requests. No general claim of
  unbounded streaming or complete interoperability is authorized. (Q1 §19.1)
- **Q-B3B-02 — RESET interruption.** B3-B supports synchronous recovery but not full mid-flight
  jump-ahead interruption; a blocked provider cannot currently be preempted. Full interruption
  requires further design and qualification during B3-C/B5 integration. (Q1 §19.2)
- **Q-B3B-03 — Batch ownership.** `ReceiveBatch` preserves unprocessed requests with the caller
  but does not expose an ideal consumed/remaining-count contract; the transport adapter must not
  silently discard unprocessed messages. (Q1 §19.3)
- **Q-B3B-04 — Session concurrency.** Single-owner execution model; not qualified as thread-safe;
  concurrent callers must be serialized externally. The type documentation gap (Q3-DOC-02)
  should be closed before broader public consumption. (Q1 §19.5)
- **Q-B3B-05 — Provisional public API.** The session/provider interfaces remain provisional
  (visibility chosen to avoid the observed `InternalsVisibleTo` overload-resolution regression);
  no stable public compatibility guarantee is implied. (Q1 §19.6)
- **Q-B3B-06 — Failure metadata.** Some non-TELEMETRY error codes are bounded placeholders; B5-B
  must verify appropriate error semantics during real-driver interoperability. (Q1 §19.7)
- **Q-B3B-07 — TELEMETRY structural boundary.** Wrong-typed TELEMETRY is rejected by B3-A; B3-B
  owns domain validation for valid integer message structures. (Q1 §19.4)
- **Q-B3B-08 — Cancellation.** No general asynchronous cancellation-token or provider-interruption
  API; do not claim full interruptible operation execution. (Q1 §19.9)
- **Q-B3B-09 — Driver interoperability.** B0-B integration tests remain passing, but they do not
  prove that the production B3-B engine is fully interoperable with `Neo4j.Driver`; actual
  production-path interoperability remains B5 work.
- **Q-B3B-10 — Existing build advisories.** Two pre-existing `xUnit2012` advisories remain outside
  B3-B scope. (Q1 §19.8)
- **Q-B3B-11 — Reviewer independence.** Q3 was executed by the same agent session as R2; its
  evidence is accepted under the bounded waiver in §10, subject to applicable project rules and
  the discretionary future verification requirement. (Q1 §19.10)
- **Q-B3B-12 — B3-A F-02.** Live-driver verification of the zero-field IGNORED representation
  remains deferred to B5-B; it must not be marked closed merely because B3-B uses the correct
  encoding. (B3-A §6.3)
- **Q-B3B-13 — Fail-closed supersession.** The normative retirement contract (§9) supersedes
  historical expectations; the unchanged Q2 probe continues to report exit 4 by design and must
  not be "fixed" historically. (Q3 §15, Q3-DOC-01)
- **Q-B3B-14 — Diagnostic semantics.** `LastCleanupFailure` exposes the raw provider exception to
  callers (potentially sensitive text); diagnostic-replacement across successive failed
  retirements is unreachable by design. The single-owner documentation gap (Q-B3B-04) and the
  absence of a production host/backend remain carried forward. (Q3 §12/§15)

## 13. Explicit non-claims

B3-B acceptance does **not** claim or establish:

- full Bolt 5.8 protocol conformance;
- complete `Neo4j.Driver` interoperability;
- Neo4j Browser interoperability;
- production TCP/TLS readiness;
- production WebSocket readiness;
- general Cypher execution;
- a working reference graph backend;
- explicit transaction execution;
- routing or clustering;
- full asynchronous RESET interruption;
- thread-safe concurrent session execution;
- stable published public APIs;
- security certification;
- production deployment readiness;
- NuGet publication readiness.

B3-B accepts the bounded connection-state engine and its scripted-provider integration only.

## 14. B3-C entry relationship

**B3-C implementation is not authorized by accepting B3-B.** A separate B3-C specification and
implementation commission are required. If commissioned, B3-C may consume the accepted B3-A
message schemas, the B3-B session engine, the existing provisional `IBoltOperationProvider` /
`IBoltOperation` seam, the verified field-metadata and record-count rules, and the normative
fail-closed retirement contract. Candidate scope: a deterministic read-only reference graph
(Alice, Bob, KNOWS) with an explicit supported-query contract, bounded result enumeration, correct
record metadata, resource ownership and disposal, and defined provider failure/cancellation
behavior, integrating with B3-B **without modifying its accepted semantics**.

B3-C must account for: failed-retirement terminal behavior; no resource reuse after failed
cleanup; single-owner session execution; bounded streaming semantics; cancellation limitations;
unprocessed batch ownership; and provisional backend-interface stability. The B3-B engine must
not be silently redesigned by B3-C.

## 15. Formal owner disposition

**B3-B — ACCEPTED WITH QUALIFICATIONS — CLOSED.**

- Q1-F01 CLOSED; Q1-F02 CLOSED; Q2-F01 CLOSED; Q2-F02 CLOSED.
- R2 fail-closed retirement semantics accepted as normative.
- Q3 supported the bounded lifecycle behavior.
- All current regression suites pass (freshly executed, §6).
- Historical evidence integrity is preserved; expected source evolution is accounted for (§5).
- Remaining qualifications (§12) are binding.
- Reviewer-executor overlap is acknowledged and bounded (§10).
- No additional B3-B remediation is required within the accepted scope.
- B3-C requires separate authorization (§14).
- Deferred capabilities are **not** implied to be implemented.

## 16. Final verification record

- **Decision document:** `docs/decisions/B3-B-OWNER-01-owner-acceptance.md` (this file; precisely
  one file in the decision commit).
- **Verification executed at:** `fe5b481` / `adfd1e899a9589cd63f62c6ea693263c1bb910a1` (fresh
  build and tests recorded in §6; manifests in §5; ancestry in §3).
- **Repository state after commit:** recorded in the final handoff (branch, HEAD/tree, one
  worktree, clean index; nothing pushed).
- **Prohibited changes verified absent:** no `src/`, `tests/`, solution/project, dependency,
  historical report/decision/evidence/probe, manifest or build-configuration modification; no new
  source or test code; no git history rewrite; no push.
- **Material condition check:** every acceptance gate in §15 is supported by the evidence cited;
  no blocker was found.
