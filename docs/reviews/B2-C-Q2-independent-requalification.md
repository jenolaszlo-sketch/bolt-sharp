# B2-C-Q2 — Independent remediation requalification

**Status:** COMPLETE — independent requalification delivered; owner acceptance PENDING
**Date:** 2026-11-05 (Asia/Manila)
**Task:** B2-C-Q2 (targeted requalification of B2-C-R1, findings Q1-F01 and Q1-F02)
**Canonical branch:** `main` (one worktree)
**Disposition:** **SUPPORTED WITH QUALIFICATIONS**

This is an independent reviewer record. It does not modify production code, producer tests or
historical reviewer evidence, records no owner acceptance, and makes no `Neo4j.Driver`
compatibility claim.

---

## 1. Commit and tree integrity

| Object | Exact identity | Verified |
| --- | --- | --- |
| Original implementation | `5295137bd688d38a2c1e3c67e6cc2daa51c050fb` / tree `5351dacd16475c75a2b44054a033ba34bd610e51` | yes |
| B2-C-Q1 qualification | `2710bc73741ecd1f144cf4d60d7e2387d8f125f9` / tree `1c5dcc38a1138f662fb0b00fbbf5708b4d171f25` | yes |
| B2-C-R1 remediation | `87e029f490fc1cd965be2ce656c4e22ccfa26cf5` / tree `7983a0643f9f97031ecb03561e0cc2d429fe7272` | yes |

Entry `HEAD` was exactly `87e029f` on `main`, clean, with one worktree. Ancestry is linear:
`87e029f^ == 2710bc7`, and both `2710bc7` and `5295137` are ancestors of `87e029f`.

**Remediation change-set (`git diff --name-status 2710bc7 87e029f`):** 3 modified producer files
(`HarnessStream.cs`, `DriverTestSettings.cs`, `DriverInteropScaffoldTests.cs`), 2 new test files
(`HarnessDisposalTests.cs`, `DriverUriValidationTests.cs`), the B2-C-R1 record, and 27 evidence
files — all additive/confinable. Verified unchanged with empty diffs:

```text
git diff --exit-code 2710bc7 87e029f -- src                                        -> exit 0
git diff --exit-code 2710bc7 87e029f -- docs/reviews/B2-C-Q1-independent-qualification.md \
    docs/reviews/probes/B2-C-Q1 docs/reviews/evidence/B2-C-Q1 \
    docs/reviews/B2-C-01-harness-implementation.md docs/reviews/evidence/B2-C-01      -> exit 0
git diff --exit-code 2710bc7 87e029f -- docs/reviews/probes/B2-B-Q1 docs/reviews/probes/B2-B-Q2 \
    docs/reviews/probes/B2-A-Q2 docs/reviews/evidence/B2-B-Q1 docs/reviews/evidence/B2-B-Q2 \
    docs/reviews/evidence/B2-A-Q2                                                      -> exit 0
git diff --exit-code 2710bc7 87e029f -- BoltSharp.slnx Directory.Build.props \
    tests/BoltSharp.Tests/BoltSharp.Tests.csproj                                       -> exit 0
```

`src/BoltSharp` is byte-identical, so the production `BoltSharp` assembly and accepted protocol
behaviour are unchanged. No unauthorized dependency or milestone expansion was found: the B2-C-R1
change-set adds no `PackageReference`, no solution membership, and no B3/B4/B5 code.

## 2. Reviewer identity and independence

- **Reviewer:** the current session (`DeepSeek V4.1 Flash`, provider `nvidia`), distinct from the
  B2-C-R1 remediation session.
- **Participation:** none. This session did not author or edit the remediation; output is isolated
  to `docs/reviews/probes/B2-C-Q2/`, `docs/reviews/evidence/B2-C-Q2/` and this report.
- **Independence status: INDEPENDENCE UNVERIFIED.** The Git author identity
  (`BoltSharp Research Agent <agent@bolt-sharp.local>`) is shared across producer and reviewer
  commits and does not identify a distinct model or person; no implementer identity is established.
  Model diversity alone is not treated as proof. Results are executable technical evidence, not
  established author-independent signoff.

## 3. Evidence-manifest verification

The B2-C-R1 SHA-256 manifest (`docs/reviews/evidence/B2-C-R1/sha256.json`) was independently
re-computed: **27 entries, 0 missing, 0 mismatched, no unlisted files**. All nine B2-C-R1 TRX files
were independently parsed and match the producer `summary.json`: solution 826, harness 40, f02 9,
driver 24/10, f01 19, reviewer 67, q2 46, aq2 18, b2cq1 29 passed/1 failed. No inconsistency was
found; proceeding was authorized.

## 4. F01 closure assessment — CLOSED

**Original finding (reproduced at B2-C-Q1):** `DriverTestSettings` documented routing rejection but
accepted `neo4j://127.0.0.1:7687` (`UsesRoutingScheme == true`; `Scaffold_Default_Endpoint_Is_Direct_Loopback`
failed). Before relying on the evidence this reviewer re-confirmed the pre-fix behaviour from the
qualification record.

**Remediation mechanism:** `DriverTestSettings.ValidateUri` accepts only `bolt`, `bolt+s`, `bolt+ssc`
and rejects routing/unsupported/malformed URIs, empty host, embedded credentials, path, query and
fragment; the constructor validates every configured value; `CreateDriver` is the single
construction path. Source review confirms `GraphDatabase.Driver` appears **exactly once** in the
project (inside `CreateDriver`), so no construction path bypasses validation; environment values
cannot bypass it because the constructor validates them.

**Independent closure evidence (reviewer probes, via the built producer assembly by reflection):**

- 7 accepted direct URIs (`bolt`, `BOLT` case variant, `bolt+s`, `Bolt+Ssc`, IPv6, host without
  port, trailing slash) all return a `bolt*` scheme and never a `neo4j*` scheme.
- 22 rejected inputs: all three `neo4j` schemes (including `NEO4J://` and `Neo4j+S://`), `http`/
  `https`/`ws`/`wss`/`tcp`, `bolt://` (no host), `bolt://:7687`, opaque `bolt:127.0.0.1:7687`,
  path, `../` traversal, `%2e%2e` escaping, query, fragment, `user@host`, `user:secret@host`,
  a non-URI string and the empty string.
- A structural probe asserts construction is confined to the validated factory and that the
  constructor calls `ValidateUri` on the environment value.

Environment-driven configuration cannot bypass validation; the constructor and factory enforce the
same policy; the default endpoint remains `bolt://127.0.0.1:7687` (loopback, direct). Accepting
direct schemes is a configuration policy and does not assert TLS, routing or driver compatibility.

**Final status: CLOSED.**

## 5. F02 closure assessment — CLOSED

**Original finding (reproduced at B2-C-Q1):** a blocked `ReadAsync` remained pending after
`HarnessStream.Dispose()` (`wokeOnDispose=False`), released only by cancellation.

**Remediation mechanism:** `HarnessChannel` gains a disposed flag and a `Dispose()` that sets it and
completes the pending-read signal; `Read`/`ReadAsync` throw `ObjectDisposedException` when disposed
before returning any buffered data; `Write` throws when disposed; `HarnessStream.Dispose` marks the
endpoint disposed and disposes its read channel idempotently.

**Independent closure evidence (reviewer probes on the linked producer harness source):**

1. Dispose before read → `ObjectDisposedException`.
2. Dispose during a blocked read → released promptly with `ObjectDisposedException` (bounded wait).
3. Dispose after buffering → disposal result, never stale data or a false successful read.
4. Repeated dispose (20×) → safe and idempotent.
5. Disposal vs cancellation → each observed deterministically when it occurs first.
6. Pending writer during disposal → terminates (bounded), no deadlock.
7. Bounded-buffer signalling → preserved (over-cap write refused wholly).
8. Half-close (`CompleteWrites`) → clean EOF, distinct from disposal.
9. 100 iterations across four schedules (read→dispose, cancel→dispose, dispose→read,
   write→read→dispose) → every operation terminated with a valid result; none hung; no indefinite
   pending operation remained.

The 100-iteration interleaving matrix plus the concurrent-writer probe exercise multiple schedules
rather than relying on repetition alone.

**Final status: CLOSED.**

## 6. Historical C6 supersession record

`BoltSharp.Qualification.B2CQ1.HarnessReviewProbes.C6_Dispose_alone_does_not_wake_a_blocked_read_documented`
asserted the **pre-remediation** behaviour: `Assert.False(wokeOnDispose)` and that only cancellation
raises. After remediation disposal intentionally wakes the read, so C6 fails with:

```text
Assert.False() Failure  Expected: False  Actual: True
```

This reviewer confirmed by execution that the failure is **exactly** the disposal-wake assertion —
no unrelated behaviour contributes (the read completes, so `wokeOnDispose` is `True`). The C6
source is **preserved unchanged** (byte-identical from `2710bc7`) and is not deleted, suppressed or
excluded.

Classification:

> **C6 — SUPERSEDED HISTORICAL ASSERTION — NOT A CURRENT CONTRACT TEST.**

The corrected contract is asserted by the reviewer-owned replacement
`C6R_Dispose_releases_blocked_read_with_disposal_result` (in `docs/reviews/probes/B2-C-Q2/`), which
requires disposal alone to wake the read and to produce `ObjectDisposedException`. The new Q2 suite
passes with **no expected failures**.

The historical result is reported separately as **B2-C-Q1: 29 passed / 1 superseded assertion
failed**, never as 30/30. The superseded failure is not a newly introduced production defect: it
reflects an intentional contract change and demonstrates no contract violation of the remediated
behaviour.

## 7. Independent probes and negative controls

New project `docs/reviews/probes/B2-C-Q2/` (`B2CQ2.Probes.csproj`; 40 discovered cases, all pass).
It links the exact producer `HarnessStream.cs`, references the built producer driver assembly for
reflection-based URI checks, and adds the C6 replacement. Negative controls for F01 cover routing,
unsupported, malformed, canonicalized-traversal and escaped inputs; F02 controls cover disposal
ordering, cancellation races, buffering, half-close and bounded-buffer signalling.

## 8. Regression results

Environment: Windows `10.0.26200`, `win-x64`; .NET SDK `10.0.401`; target `net8.0`; .NET 8 runtime
`8.0.31`. Commands and raw results: `docs/reviews/evidence/B2-C-Q2/` (`Run-Evidence.ps1`,
`commands.json`, `summary.json`; independent TRX parse below). Builds: **0 warnings, 0 errors**.

| Executed set | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Full solution | 826 | 0 | 0 |
| Harness (`~Harness`) | 40 | 0 | 0 |
| — disposal regression subset | 9 | 0 | 0 |
| Driver scaffold | 24 | 0 | 10 |
| — URI validation subset | 19 | 0 | 0 |
| B2-B-Q1 reviewer probes | 67 | 0 | 0 |
| B2-B-Q2 reviewer probes | 46 | 0 | 0 |
| B2-A-Q2 reviewer probes | 18 | 0 | 0 |
| **Historical B2-C-Q1 reviewer probes** | **29** | **1 (superseded C6)** | 0 |
| **New B2-C-Q2 reviewer probes** | **40** | **0** | 0 |

Command exit codes: all `0` except the historical B2-C-Q1 project (`1`), which is the expected
superseded-assertion failure. Overlapping subsets (harness/f02, driver/f01) are not double-counted.
A historical expected failure is not treated as a successful execution; it is recorded explicitly
and does not affect acceptance because it is an intentional, superseded assertion rather than a
production contract violation.

## 9. Outstanding observations and limitations

**B2-C-Q1 observations reassessed — all remain nonblocking, accurately documented and unchanged:**

- **OBS1:** synchronous `Read` on an empty open harness channel returns 0 (EOF-like); asynchronous
  reads block via signal. Harness-only; test callers supply bytes or use the async path.
- **OBS2:** `HarnessDuplex.Create` sizes both channels at the minimum of the two endpoints'
  configured caps. Conservative, harness-only.
- **OBS3:** the harness is not designed for concurrent multi-writer use (standard `Stream`
  semantics). Reviewer probes use one writer and one reader; no multi-writer guarantee is claimed.

**Confirmed boundaries:** official-driver interoperability remains **unverified**; D01–D10 remain
**gated (10 skips)** with the documented prerequisite; the B0-A revision remains **PROVISIONAL**
(live probe `NOT RUN`); no B3/B4/B5 functionality, no TCP/TLS/WebSocket, no fabricated
`HELLO`/`LOGON`/`SUCCESS`, and no live networking or official-driver connectivity experiment was
performed. Evidence is `net8.0` in-memory only.

**Data-quality note (not a finding):** the B2-C-R1 record's §7 table labels the driver baseline as
"24 passed / 10 skipped" (post-remediation) versus "4 passed / 10 skipped" (pre-remediation); both
are correctly stated in context. No corrective action is required.

No new finding was reproduced during requalification.

## 10. Final qualification disposition

| Finding | Original severity | Status |
| --- | --- | --- |
| Q1-F01 — routing URI documented rejected but not enforced | MINOR | **CLOSED** |
| Q1-F02 — dispose does not wake a blocked read | MINOR | **CLOSED** |
| C6 — asserts pre-remediation non-wake | n/a | **SUPERSEDED HISTORICAL ASSERTION — NOT A CURRENT CONTRACT TEST** |

**SUPPORTED WITH QUALIFICATIONS.** Both findings are independently closed with no regression: the
826/40/24/67/46/18 suites and the 40 new Q2 probes pass, the production assembly is unchanged, and
the only failure is the explicitly superseded historical C6 assertion. The qualifications are the
unverified reviewer independence and the three nonblocking harness observations (OBS1–OBS3),
together with the provisional revision and unverified driver interoperability.

## 11. Recommendation for owner acceptance

Present **SUPPORTED WITH QUALIFICATIONS** to the owner for the B2-C acceptance decision. B2-C may be
accepted on this basis with the documented qualifications; alternatively the owner may direct that
the harness observations (OBS1–OBS3) receive a small documentation-only clarification. This record
does not self-approve B2-C, does not authorize driver compatibility, and does not begin B3/B4/B5.

## 12. Evidence binding

Reviewer probes: `docs/reviews/probes/B2-C-Q2/`. Evidence: `docs/reviews/evidence/B2-C-Q2/` with a
SHA-256 manifest. This report and its evidence are committed additively on top of `87e029f`; the
reviewer commit/tree is recorded in the final handoff message (this file cannot contain its own
SHA). At entry, local `main` was 3 commits ahead of `origin/main` (`8f8b7e4`); no push or remote
change was performed by this review.
