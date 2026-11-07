# B3-A-OWNER-01 — owner acceptance of the Bolt 5.8 message contracts

**Status:** ACCEPTED WITH QUALIFICATIONS (owner decision recorded 2026-11-07)
**Date:** 2026-11-07 (Asia/Manila; execution timestamps are UTC)
**Task:** B3-A-OWNER-01 — formal owner acceptance and milestone closure
**Canonical branch:** `main` (one worktree)
**Protocol target:** Bolt 5.8 / PackStream 1.0
**Pinned acceptance entry:** `684a13d1785985fbbd9ecaaca1402b5ff9ac730a` / tree `6fd0789d3e6147e67313ee6880ad5781337c4dae`

This record is an owner decision. It contains **no implementation**, modifies no production or test
code and no historical evidence, and makes **no** Neo4j / `Neo4j.Driver` compatibility claim. It
records acceptance only for the bounded scope in §4, and it references — rather than replaces — the
B3-A implementation report, the two independent qualifications and the remediation record.

---

## 1. Owner decision

**Owner decision: B3-A — ACCEPTED WITH QUALIFICATIONS — CLOSED.**

The owner reviewed the qualification chain — the
[implementation record](../reviews/B3-A-IMPL-01.md) (**IMPLEMENTED, READY FOR INDEPENDENT
QUALIFICATION**), the [Q1 independent qualification](../reviews/B3-A-Q1-independent-qualification.md)
(**SUPPORTED WITH QUALIFICATIONS**), the [R1 remediation record](../reviews/B3-A-R1-IMPL-01.md)
(**IMPLEMENTED, READY FOR INDEPENDENT B3-A-Q2 REQUALIFICATION**) and the
[Q2 independent requalification](../reviews/B3-A-Q2-independent-requalification.md)
(**SUPPORTED WITH QUALIFICATIONS; F-01 CLOSED**) — against the committed evidence under
`docs/reviews/evidence/B3-A-{IMPL-01,Q1,R1-IMPL-01,Q2}/`, and records:

1. **B3-A is accepted with qualifications**, within the scope of §4, **bounded to Bolt 5.8 /
   PackStream 1.0**.
2. **Q1-F01 is CLOSED** (§6.1). The R1 remediation and independent Q2 establish that the
   specification-mandatory HELLO `bolt_agent` field and its nested `product` are now enforced.
3. **F-Q2-01 is accepted as a documented error-classification convention** (§6.2). No B3-A-R2
   remediation is authorized or required.
4. **Q1-F02 is an accepted qualification deferred to B5-B** (§6.3): live official-driver
   verification of the specification-correct zero-field `IGNORED` response.
5. **All remaining binding qualifications are preserved** without broadening or silent
   resolution (§6.4).
6. **Owner acceptance vs. reviewer dispositions.** The Q1 and Q2 reviewer dispositions are reviewer
   conclusions, not the owner decision. Recording acceptance does not modify, supersede or rewrite
   any producer report, independent qualification, remediation record, probe or evidence file.
7. **Reviewer identity is recorded but author independence remains UNVERIFIED.** The B3-A
   implementation, the R1 remediation and the Q1/Q2 reviews were produced by sessions running
   `GLM-5.3-Flash` (`nvidia`, OpenCode) with the B0-B producer on a different executor; the
   executor-level separation is recorded, but no organizational attestation is available. This
   result must **not** be described as verified independent signoff in external statements.
8. Accepting B3-A does **not** establish the non-claims in §5.

---

## 2. Verified entry state

Independently read from the canonical checkout before this decision:

```text
git branch --show-current  -> main
git rev-parse HEAD         -> 684a13d1785985fbbd9ecaaca1402b5ff9ac730a (matches the commission)
git rev-parse HEAD^{tree}  -> 6fd0789d3e6147e67313ee6880ad5781337c4dae
git worktree list          -> one worktree (D:/projects/bolt-sharp)
git status --porcelain     -> clean
prior B3-A-OWNER-01        -> none existed
push state                 -> origin/main (9abf5e3) is an ancestor of HEAD; nothing newer pushed
```

## 3. Commit and tree identities

| Stage | Commit | Tree |
| --- | --- | --- |
| B0-B owner acceptance (B3-A baseline) | `d5189af56c79bbdb86bbaaa156bfb2a0f4046c37` | `ed7873eece039559916f29df5e406c54e4c2c3fa` |
| B3-A implementation | `d99096a9689c4a7ce313c569cff2b165cfbda11a` | `6cb9cace72d403d496ca92a067c06510be64220c` |
| B3-A implementation report + evidence | `2771052f11fb1aae134d3340f28a5d3affe914f2` | `584745f46376d7f904b8d07d00b11aaeb2d54270` |
| B3-A evidence log normalization | `fb81f8b386d46db2347f7b890847885d9815dcf5` | `88aba63c3e35e89dc030a1fa54967d941cd06031` |
| B3-A-Q1 independent qualification | `26f57d07fcf8e79a3cc84d3c1d6936da60ee9d1a` | `2d5c4c8d1677c6e28762d72fd3ff2f85a8e24ec7` |
| B3-A-Q1 log normalization | `6974a56a6c4fc9b706a29ac65fe029840834893c` | `a45a8f1ec8d241ee18752c3d1ff94a9117ecb211` |
| B3-A-R1 remediation (F-01) | `d6686d14f3eaee42f83971d517d9077170a30fe2` | `f615af7feff41c2f0cc2e0debd06d4d928daa32d` |
| B3-A-R1 report + evidence | `83126a99be56fa141eec3342a21b863ca4ef45d2` | `4c697e8dab5a30f8749c7eb9246827bb6ddcd9ff` |
| B3-A-Q2 independent requalification | `684a13d1785985fbbd9ecaaca1402b5ff9ac730a` | `6fd0789d3e6147e67313ee6880ad5781337c4dae` |
| Owner-acceptance commit (this file) | recorded in the final handoff (this file cannot contain its own SHA) | — |

Ancestry is linear and additive: `d5189af` → `d99096a` → `2771052` → `fb81f8b` → `26f57d0` →
`6974a56` → `d6686d1` → `83126a9` → `684a13d`. The `…86db…` normalization commits converted
UTF-16 PowerShell-captured logs to UTF-8 text (refreshing the affected manifests) within the
respective milestones; they are part of the recorded evidence history.

## 4. Evidence integrity

Verified from actual manifests and working-tree bytes:

| Evidence | Result |
| --- | --- |
| B3-A implementation (`evidence/B3-A-IMPL-01`) | **21/21 entries match for the pre-R1 snapshot.** The 5 non-matching entries are exactly the five files the *authorized* R1 remediation changed (`BoltMessageCodec.cs`, `BoltRequests.cs`, `BoltMessageFixtures.cs`, `BoltMessageNegativeTests.cs`, `BoltRequestDecodeTests.cs`); all 8 evidence files and the implementation report remain byte-identical, and the R1 manifest records the post-remediation hashes (13/13). |
| B3-A-Q1 (`evidence/B3-A-Q1`) | **14/14** |
| B3-A-R1 (`evidence/B3-A-R1-IMPL-01`) | **13/13** |
| B3-A-Q2 (`evidence/B3-A-Q2`) | **15/15** |
| B0-B producer (`evidence/B0-B-DRV-INT-01`) | **24/24** |
| B0-B Q1 (`evidence/B0-B-Q1`) | **119/119** |

Additional integrity results:

- **B0-B decisions, evidence and probes: 0 paths changed** since the B0-B acceptance
  (`d5189af`); the B0-B owner acceptance and its reconciliation record are unchanged.
- **B0-A-TRACE evidence and owner acceptance: 0 paths changed** since `a4429ef`.
- **B3-A-Q1 report, evidence and probes: 0 paths changed** since the Q1 commit; **B3-A-Q1 and R1
  artifacts: 0 paths changed** since their respective commits; **B3-A-IMPL-01 report and evidence:
  0 paths changed** since the evidence normalization commit.
- **R1 production change scope:** the functional diff is confined to `ValidateHelloExtra` plus one
  doc comment; no new public member; no other message contract touched.
- **Q2 is additive only:** its commit added only the Q2 report, probe and evidence paths — no
  production, test, decision or historical path.
- **PackStream, Framing and Handshake implementation: 0 paths changed** across the entire B3-A
  line (implementation + remediation).
- **No accepted test or independent fixture was weakened:** the only removed assertion in R1
  (`Assert.Null(hello.BoltAgent)`) asserted the pre-fix invalid behavior and was replaced by
  strictly stronger assertions; two new exact-category negative tests were added.

## 5. Accepted implementation scope and non-claims

### 5.1 Accepted scope

The owner accepts the B3-A message-contract codec for the following bounded capabilities, each
established by the reproduced evidence cited in the referenced reports:

1. Bolt 5.8 request signature recognition (HELLO, LOGON, LOGOFF, TELEMETRY, RUN, PULL, DISCARD,
   RESET, GOODBYE).
2. Typed, immutable request and response representations.
3. Exact structure-arity validation for every supported message.
4. Required-field and field-type validation, including mandatory HELLO `user_agent` and
   `bolt_agent`/`product`.
5. HELLO and LOGON message contracts (including the four predefined LOGON scheme shapes,
   structurally validated only).
6. RUN, PULL and DISCARD message contracts (RUN three-field shape; PULL/DISCARD `n`/`qid`).
7. RESET, GOODBYE, LOGOFF and TELEMETRY message contracts (TELEMETRY structural only).
8. SUCCESS, RECORD, FAILURE and IGNORED response encoding, including the specification-correct
   zero-field `IGNORED` (`B0 7E`).
9. Bolt 5.7+ GQL-aware FAILURE metadata handling with bounded recursive `cause` and rejection of
   the obsolete pre-5.7 `code` field.
10. Deterministic message error classification (`BoltMessageErrorCategory`, 14 categories).
11. Credential-safe diagnostic behavior (no field values in `ToString()` or exceptions).
12. Preservation of supported nested PackStream and graph values (Node 4-field / Relationship
    8-field layouts).
13. Explicit classification of the known-but-unsupported BEGIN/COMMIT/ROLLBACK/ROUTE messages as
    `UnsupportedMessage` (never silently accepted).
14. Integration with the existing PackStream contracts without transport or session ownership, and
    without a `Neo4j.Driver` production dependency.
15. Independent golden-byte conformance and authentic `Neo4j.Driver` 6.4.0 request-fixture
    compatibility within the exercised scope.

**Acceptance is explicitly version-bounded to Bolt 5.8 / PackStream 1.0.** No other Bolt or
PackStream revision is accepted or implied.

### 5.2 Explicit non-claims

B3-A acceptance does **not** establish:

- legal protocol state transitions;
- authentication correctness or credential verification;
- query execution;
- cursor management;
- response sequencing;
- pipelined request handling;
- failed-state recovery;
- `RESET` interruption;
- transaction semantics;
- routing or cluster compatibility;
- complete Neo4j driver interoperability;
- Browser interoperability;
- TCP/TLS production readiness;
- complete Bolt protocol conformance;
- NuGet release readiness.

These belong to subsequent milestones and must not be inferred from message-codec acceptance.

## 6. Finding dispositions

### 6.1 Q1-F01 — mandatory HELLO `bolt_agent` — **CLOSED**

The R1 remediation added enforcement of the specification-mandatory HELLO `bolt_agent` field
(absent → `MissingRequiredField`; null or non-map → `InvalidFieldType`) while preserving the
existing mandatory nested `product` validation, and the independent Q2 requalification confirmed
the closure with fresh, independently generated fixtures, a byte-identical reproduction of the
producer's probe evidence, and intact regressions. The original missing-field acceptance defect is
closed. No further remediation is required for F-01.

### 6.2 F-Q2-01 — explicit-null nested `product` — **ACCEPTED LIMITATION**

Observed behavior (independently verified in Q2): missing nested `product` →
`MissingRequiredField`; explicit-null nested `product` → `MissingRequiredField`; wrong-type
non-null nested `product` → `InvalidFieldType`. The existing validation convention treats explicit
null as absence for required string fields, and the same convention applies to `user_agent`, LOGON
`scheme`, `principal` and `credentials`. The owner accepts this behavior because:

1. invalid HELLO messages are deterministically rejected;
2. no invalid input is accepted as conforming;
3. no credential or metadata value is leaked;
4. the distinction concerns BoltSharp-internal error categorization, not wire-protocol acceptance
   (a client observes a FAILURE either way);
5. a localized change solely for `product` would make required-string handling inconsistent across
   fields (the R1 agent-level presence scan already distinguishes the agent top level, but the
   nested-field convention is systemic and predates R1).

**No B3-A-R2 remediation is authorized or required.** Any future change that distinguishes missing
fields from explicit null should be treated as a separate, general error-model refinement, not an
outstanding B3-A defect. Explicit-null `product` is **not** valid input and must not be described as
valid.

### 6.3 Q1-F02 — zero-field `IGNORED` verification — **ACCEPTED QUALIFICATION; DEFERRED TO B5-B**

B3-A emits the specification-correct structure `B0 7E`. The historical B0-B fake-server fixture
emitted the nonconforming one-field form `B1 7E A0`; the independently qualified B3-A codec rejects
that representation, and the historical fixture is preserved unchanged. Live end-to-end
verification of the zero-field response with the official Neo4j driver remains a **B5-B**
responsibility. This deferred interoperability verification is **not** a B3-A schema defect.

### 6.4 Additional binding qualifications (retained)

- PULL/DISCARD `n/qid < −1` rejection is a **documented implementation policy**, not a separately
  established universal protocol requirement.
- TELEMETRY message decoding is separate from the later session-layer validation of its API
  identifier domain (0–3).
- Authentic HELLO/LOGON captures remain limited by the accepted evidence-redaction approach.
- The author-independence caveat remains explicit.
- NC-03b and NC-03c retain their accepted B0-B boundaries (server-value fidelity not a milestone
  claim; `connection_id` optional for generic SUCCESS).
- NC-05b: outer RECORD structure arity is validated; cross-message column-count consistency remains
  deferred to B3-B/B3-C.
- NC-08: extra terminal SUCCESS and response-sequence validation remain deferred.
- Zero-arity signature swaps (RESET/GOODBYE/LOGOFF) cannot be classified by stateless message
  parsing alone; legal message sequencing belongs to B3-B.
- Pre-existing formatting/analyzer observations (`dotnet format` whitespace and xUnit2012
  advisories in B3-A test files, present at the Q1 baseline, with no repository format gate) are
  recorded as pre-existing observations and are **not** a B3-A acceptance blocker.

These are **accepted limitations, not outstanding remediation requirements**, and must remain
visible to B3-B.

## 7. Test and build evidence

The most recent independently reproduced baseline at the pinned entry commit:

| Set | Command | Result |
| --- | --- | --- |
| Core solution | `dotnet test BoltSharp.slnx -c Debug` | **924 passed / 0 failed / 0 skipped** |
| Message-contract tests | `dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj --filter FullyQualifiedName~BoltSharp.Tests.Messages` | **98 passed / 0 failed / 0 skipped** |
| B0-B integration | `dotnet test tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj -c Debug` | **19 passed / 0 failed / 0 skipped** |
| Build | `dotnet build BoltSharp.slnx -c Debug` | **0 warnings / 0 errors** |

The owner decision records a fresh read-only rerun of all four sets at `684a13d` (executed
2026-11-07) reproducing these exact counts; the commands and outputs are the same as those recorded
and verified by Q1, R1 and Q2. No production or test file was changed to obtain this result.
Formatting observations remain as recorded in §6.4 and are not described as clean.

## 8. Next-milestone dependency and handoff

**B3-B — Bolt 5.8 Connection State Machine** may be commissioned separately. B3-B must consume the
accepted B3-A message contracts **without changing their schema semantics**, and its
responsibilities include: negotiated protocol session lifecycle; HELLO and LOGON ordering;
authentication-state enforcement; legal and illegal message transitions; RUN/PULL/DISCARD
sequencing; FAILED/IGNORED recovery; RESET behavior and limitations; GOODBYE semantics; pipelined
request ordering; response-sequence validation; and resource/cursor ownership boundaries.

The binding B3-A qualifications (§6.4) must remain visible to B3-B. In particular, **B3-B must not
compensate for invalid message structures by silently interpreting them differently**, and it owns
the deferred contextual checks (NC-05b column-count matching, NC-08 sequence validation), the
TELEMETRY value domain, and the failed-state `IGNORED` logic. B5-B owns the live-driver
verification of the zero-field `IGNORED` response (F-02). **B3-B is not started by this decision.**

## 9. Formal owner disposition

**B3-A — ACCEPTED WITH QUALIFICATIONS — CLOSED.**

No further B3-A remediation is required within the accepted scope. The milestone delivered a
reusable, independently qualified Bolt 5.8 message-contract codec, with one defect found and closed
through a bounded remediation and a focused independent requalification, and with all remaining
limitations explicitly accepted and carried forward.
