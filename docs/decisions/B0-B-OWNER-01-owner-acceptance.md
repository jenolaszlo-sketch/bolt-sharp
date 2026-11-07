# B0-B-OWNER-01 — owner acceptance of B0-B

**Status:** ACCEPTED WITH QUALIFICATIONS (owner decision recorded 2026-11-07)
**Date:** 2026-11-07 (execution timestamps are UTC)
**Task:** B0-B-OWNER-01 — documentation reconciliation and bounded owner acceptance
**Canonical branch:** `main` (one worktree, clean at decision time)
**Entry HEAD:** `37cc989bbae2273a639cd04dc8cf7a95799472cd` / tree `3702fce8b6f126ff4d6cd49891a0747fd87ae415`
**Reconciliation commit:** `26d4fa82f0c70aab530e089d009ff28997833368` / tree `ff951fdeefa698fb7947bad02d96948e4c56db95`

This record is an owner decision. It contains **no implementation**, modifies no production or test
code and no historical evidence, and makes **no** Neo4j compatibility or certification claim. It
records acceptance only for the bounded scope in §3, and its qualifications in §4 are **binding on
all downstream use of B0-B results**.

---

## 1. Owner decision

**Owner decision: B0-B — ACCEPTED WITH QUALIFICATIONS — CLOSED.**

The owner reviewed the producer implementation record
([`B0-B-DRV-INT-01-implementation.md`](../reviews/B0-B-DRV-INT-01-implementation.md)), the gap
matrix ([`B0-B-DRV-INT-01-gap-matrix.md`](../reviews/B0-B-DRV-INT-01-gap-matrix.md)), the
independent qualification
([`B0-B-Q1-independent-qualification.md`](../reviews/B0-B-Q1-independent-qualification.md) —
**SUPPORTED WITH QUALIFICATIONS**), the
[qualification reconciliation](B0-B-OWNER-01-qualification-reconciliation.md), and the committed
evidence under `docs/reviews/evidence/B0-B-DRV-INT-01/` and `docs/reviews/evidence/B0-B-Q1/`, and
records:

1. **B0-B is accepted with qualifications**, within the scope of §3.
2. **The accepted claims are established by the verified producer implementation and the
   independent Q1 qualification**, subject to the corrections in the reconciliation document
   (D01, D04, T07 fidelity), which the owner adopts as authoritative.
3. **The four Q1 negative-control blind spots are accepted with the dispositions assessed in the
   reconciliation document §6** (two ACCEPTED LIMITATION, two DEFERRED COVERAGE, zero ACCEPTANCE
   BLOCKER). No producer test or assertion was or will be modified under this decision.
4. **Q1-F01 … Q1-F05 are retained as binding qualifications** (§4). They are accepted limitations,
   not outstanding remediation requirements within B0-B's authorized scope.
5. **Reviewer identity is recorded but author independence remains UNVERIFIED.** The Q1 report
   records a session running `GLM-5.3-Flash` (`nvidia`, OpenCode) while the producer executor was
   `DeepSeek V4.1 Flash`; the models/executors differ, but no organizational attestation beyond
   that is available. This result must **not** be described as verified independent signoff in any
   external statement.
6. Accepting B0-B does **not** establish the non-claims in §5.

**Owner acceptance vs. reviewer disposition.** The Q1 reviewer disposition was **SUPPORTED WITH
QUALIFICATIONS**; that is a reviewer conclusion, not the owner decision. The owner decision is
separate and is recorded here. Recording acceptance does not modify, supersede, or rewrite the
producer record, the gap matrix (as corrected via the reconciliation document), or the Q1 report,
whose content, findings and qualifications remain unchanged.

---

## 2. Verified entry state and evidence integrity

Independently verified from the canonical checkout before this decision:

```text
git rev-parse --abbrev-ref HEAD -> main
git rev-parse HEAD              -> 37cc989bbae2273a639cd04dc8cf7a95799472cd  (Q1 qualification commit)
git rev-parse HEAD^{tree}       -> 3702fce8b6f126ff4d6cd49891a0747fd87ae415
git status --porcelain=v1       -> clean
git worktree list               -> one worktree (D:/projects/bolt-sharp)
git remote / push state         -> origin/main (9abf5e3) is an ancestor of HEAD; nothing newer pushed
prior owner decision            -> none for B0-B (only B2-B, B2-C, B0-A-TRACE exist)
```

Evidence integrity results (verified against actual manifest contents, not assumed counts):

| Check | Result |
| --- | --- |
| Producer evidence manifest (`docs/reviews/evidence/B0-B-DRV-INT-01/manifest.json`) | **24/24 entries match** (22 evidence files + 2 report files; SHA-256 and sizes over working-tree bytes, `* -text`) |
| Q1 reviewer evidence manifest (`docs/reviews/evidence/B0-B-Q1/manifest.json`) | **119/119 entries match** (110 evidence files + 9 probe sources) |
| Original 19 producer scenario captures | **byte-identical** to the Q1 `producer-reproduction/` copies (19 compared, 0 mismatches) — confirming the originals are unchanged since Q1 |
| Historical B0-A-TRACE evidence and owner acceptance | unchanged — `git diff a4429ef..HEAD` over `docs/reviews/evidence/B0-A-TRACE` and `docs/decisions/B0-A-TRACE-OWNER-01-owner-acceptance.md` is empty |
| Production code, existing tests, solution/project files | unchanged — diff `a4429ef..HEAD` over `src/`, `tests/BoltSharp.Tests/`, `BoltSharp.slnx`, `Directory.Build.props`, `README.md` is empty; the only test-side change since `a4429ef` is the new, self-contained `tests/BoltSharp.IntegrationTests/` added by the milestone itself |
| Committed producer oracle (`tests/BoltSharp.IntegrationTests/Infrastructure/*`) | unchanged since the implementation commit `46b0e82` (empty diff) |
| Existing Q1 reviewer evidence and probes | unchanged — Q1 manifest hash verification above; working tree clean |

No evidence corruption, no conflicting owner decision, and no concurrent modification was found.

---

## 3. Accepted scope

The owner accepts the B0-B integration-harness and discovery milestone for the following bounded
capabilities, each established by the reproduced evidence cited:

1. **Executable integration tests using the authentic `Neo4j.Driver` 6.4.0 package** (exact pin;
   assembly 6.4.12.0; 19/19 tests passing, reproduced twice, evidence byte-identical).
2. **A programmable loopback-only fake Bolt server** (`ScriptedBoltServer`: OS-assigned port,
   bounded lifecycle, full ordered transcript, unscripted-request detection).
3. **Independently constructed and reviewed Bolt 5.8/PackStream 1.0 response fixtures**
   (`ProtocolFixtures`, written from the published specifications, no BoltSharp writer; Q1 §4 found
   no discrepancy; independently decoded for the graph scenario).
4. **Authentic driver handshake and initialization observations** (magic + four proposals decoded;
   legacy 5.8 selection; Manifest v1; reject-all fails without HELLO; HELLO→LOGON ordering with
   credential redaction).
5. **Scripted scalar-result interoperability** (`RUN`/`PULL`/`RECORD`/`SUCCESS` against the real
   driver; `RETURN 42 AS answer` reconstructed).
6. **Typed graph reconstruction for the accepted Alice/Bob/KNOWS scenario** (distinct typed
   identities despite reused numeric id 1; direction, element ids, labels and properties verified
   against independently decoded bytes).
7. **Exercised pagination, DISCARD and failure-handling behavior** (finite `PULL(n=1)` with
   `has_more`; DISCARD on consumption and early disposal; FAILURE → `Neo4jException`, pipelined
   `PULL` answered `IGNORED`, RESET recovery; termination and truncation never clean).
8. **Post-handshake framing and fragmentation observations** (per-byte/chunked/coalesced writes
   tolerated; fragmented 4-byte handshake response rejected by driver 6.4.0 — see Q1-F01).
9. **Reproducible real BoltSharp listener boundary testing** (T08: genuine B2-B negotiation +
   B2-A framing; 5.8 negotiated in both modes; first message `HELLO` observed; messaging unreached).
10. **An independently reviewed gap matrix identifying remaining B3/B5 implementation
    requirements**, as corrected by the reconciliation document.

**Critical distinction:** items 1–10 establish **scripted fake-server interoperability** — what the
real driver sends and requires, observed against controlled responders. They do **not** establish
**complete production BoltSharp server interoperability**: the genuine BoltSharp listener currently
implements negotiation and framing only, and every messaging step (HELLO response onward) is
unreached (T08 boundary). The gap matrix's B3-A/B3-B/B3-C/B5-A/B5-B mapping records what remains.

---

## 4. Binding qualifications

The following qualifications are **binding** on every downstream use of B0-B results. They are
recorded in and retained by the [qualification reconciliation
document](B0-B-OWNER-01-qualification-reconciliation.md), which is the authoritative statement of
each; this list is the owner's adoption of them, not a replacement.

| Qualification | Content (summary) |
| --- | --- |
| **Q1-F01** | Driver **6.4.0** rejects a fragmented 4-byte handshake response (single `ReadAsync`; fails ~25 ms regardless of delays; `{N}` = first-segment size). Mechanism confirmed in upstream tag `6.4.0` (`ParseProtocolVersionResponse`). A single server-side write does **not** guarantee a single client-side read — this is a driver robustness limitation, not a TCP or Bolt rule. BoltSharp servers must write handshake responses in one write for this driver version. Version-specific; post-handshake framing was tolerant in all exercised legal segmentations. |
| **Q1-F02** | Corrected HELLO SUCCESS metadata: **`server` required** by observed driver 6.4.0 behavior; **`connection_id` and `hints` optional**. NRE (not a clean protocol error) when `server` is absent. Driver-specific, not established as a Bolt protocol requirement. |
| **Q1-F03** | Version-specific authentication classification: `neo4j_code` **drives** `AuthenticationException` vs `DatabaseException`; without it the driver still raises a `Neo4jException` (`DatabaseException`, `Neo.DatabaseError.General.UnknownError`). Under new-style metadata the description appears in `GqlStatusDescription`, not necessarily `Message`. Legacy `{code, message}` yields `AuthenticationException` with the message text. |
| **Q1-F04** | `DISCARD` observed on **both** partial-result consumption (`ConsumeAsync`) and early session disposal paths. |
| **Q1-F05** | `t07-handshake-fragmentation.json` evidence-fidelity limitation: empty `requests`/`frames`/`notes` are **not affirmative protocol evidence**; that artifact must not be cited as a successful wire capture. The Q1 fragmentation probes are the applicable reproducible evidence. Original artifact preserved unaltered. |
| **Blind spots** | NC-03b (**ACCEPTED LIMITATION**), NC-03c (**ACCEPTED LIMITATION** — its finding is the F02 correction), NC-05b (**DEFERRED COVERAGE**), NC-08 (**DEFERRED COVERAGE**). Full dispositions: reconciliation §6. |
| **Reviewer limitations** | Windows 10.0.26200 / net8.0 / plaintext loopback only; synthetic controlled traffic; driver 6.4.0 pinned; fake-server request decoding uses BoltSharp's reader (response correctness anchored by the real driver and independent decoders); reviewer/producer author independence UNVERIFIED (different executors, no organizational attestation). |

---

## 5. Explicit non-claims

B0-B acceptance does **not** establish:

- a complete Bolt server implementation;
- fully functioning production session handling;
- end-to-end production BoltSharp query execution;
- general Cypher compatibility;
- browser interoperability;
- TLS (`bolt+s://`) interoperability;
- cross-version driver compatibility (all driver observations are pinned to 6.4.0);
- production security readiness;
- NuGet publication readiness;
- complete protocol conformance.

Each of these requires separate implementation and qualification under the mapped work packages
(B3-A, B3-B, B3-C, B5-A, B5-B).

---

## 6. Commit and tree identities

| Stage | Commit | Tree | Parent |
| --- | --- | --- | --- |
| Q1 qualification (entry HEAD) | `37cc989bbae2273a639cd04dc8cf7a95799472cd` | `3702fce8b6f126ff4d6cd49891a0747fd87ae415` | `9d121ae…` |
| Reconciliation commit (Commit 1) | `26d4fa82f0c70aab530e089d009ff28997833368` | `ff951fdeefa698fb7947bad02d96948e4c56db95` | `37cc989…` |
| Owner-acceptance commit (Commit 2) | recorded in the final handoff (this file is part of it, so it cannot contain its own SHA) | — | `26d4fa8…` |

The reconciliation commit changes exactly one path
(`docs/decisions/B0-B-OWNER-01-qualification-reconciliation.md`, 330 insertions). The
owner-acceptance commit changes exactly one path (`this file`) and does not alter the
reconciliation document. Both commits are documentation-only; no `src/`, `tests/`, solution,
project, evidence, manifest, or report file is touched by either.

---

## 7. Next-milestone recommendation

**Recommended next work package: B3-A — Message Schemas for the Negotiated Bolt Revision.**

B0-B findings that constrain B3-A design and acceptance:

- **Version-specific message signatures and arity:** the exercised Bolt 5.8 signatures are HELLO
  `0x01`, GOODBYE `0x02`, RESET `0x0F`, RUN `0x10`, BEGIN `0x11`, COMMIT `0x12`, ROLLBACK `0x13`,
  DISCARD `0x2F`, PULL `0x3F`, TELEMETRY `0x54`, ROUTE `0x66`, LOGON `0x6A`, LOGOFF `0x6B`;
  responses SUCCESS `0x70`, RECORD `0x71`, IGNORED `0x7E`, FAILURE `0x7F`. Arity is a known blind
  spot of the B0-B suite (reconciliation §6.3): B3-A must pin request and response arity in its
  schemas and tests. BoltSharp already decodes these signatures (producer `ProtocolFixtures` /
  `WireCapture` naming agrees with the accepted B1/B2 suites).
- **HELLO/LOGON metadata:** send `server`, `connection_id` **and** `hints` in HELLO SUCCESS for
  driver 6.4.0 compatibility (F02 — do not rely on `connection_id` being optional); LOGON SUCCESS
  may be empty; FAILURE classification is metadata-driven (F03) — a BoltSharp server should emit
  `gql_status` + `neo4j_code` (+ `description`) and must not assume the driver will place its
  description in the surfaced `Message`.
- **RUN/PULL and summary structures:** `RUN(query, parameters, extra)` → `SUCCESS {fields}`
  required before `PULL`; `PULL {n}` (driver default 1000, fetch-size configurable) → `RECORD`+
  `SUCCESS {has_more}`; terminal framing strictness is a blind spot (reconciliation §6.4) — B3-A
  schemas should make the terminal SUCCESS the declared end of a response stream.
- **FAILURE/IGNORED semantics:** after a `FAILURE` the driver may pipeline `PULL`; the server must
  answer `IGNORED` until `RESET` returns to READY (fact 3, NC-04). B3-B (state machine) owns this,
  but B3-A must define the `IGNORED` message shape.
- **Graph-record structure handling:** Node 4-field (`0x4E`: id, labels, properties, elementId) and
  Relationship 8-field (`0x52`: id, start, end, type, properties, elementId, startElementId,
  endElementId) layouts are independently verified against driver 6.4.0 reconstruction — reuse the
  accepted B5 field layout as specified in the gap matrix.
- **Independent protocol fixtures as correctness oracles:** `ProtocolFixtures` (and the Q1
  reviewer's standalone decoders/probes under `docs/reviews/probes/B0-B-Q1/`) are established
  spec-derived oracles; B3-A should encode message schemas against them rather than against
  BoltSharp's own writer, preserving the independent-oracle discipline that made B0-B qualifiable.

This is a recommendation only. No B3-A work is authorized or started by this decision; a separate
B3-A commission will be issued.

---

## 8. Owner conclusion

**B0-B is accepted with qualifications. No further remediation is required within its authorized
discovery and test-harness scope.**

The milestone provides a reusable, independently qualified foundation for subsequent server
implementation. Its evidence is reproducible, its protocol discoveries survived independent
challenge, its one factual overclaim and its evidence-fidelity limitation are reconciled, and its
four assertion blind spots are dispositioned without an additional test cycle. Final disposition:

**B0-B — ACCEPTED WITH QUALIFICATIONS — CLOSED**
