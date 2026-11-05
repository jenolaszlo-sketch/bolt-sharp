# B2-C-OWNER-01 — B2-C qualified owner acceptance

**Status:** ACCEPTED WITH QUALIFICATIONS (owner decision recorded 2026-11-05)
**Date:** 2026-11-05 (Asia/Manila)
**Task:** B2-C-OWNER-01 — qualified owner acceptance of B2-C and synchronization of canonical `main`
**Canonical branch:** `main` (one worktree)
**Pinned qualification entry (B2-C-Q2):** `06af745ec6b9bab2dba771150f9c6fb255a2620b` / tree `809d5ac0524c1bbbafcce317db949c1a8a820c2b`

This record is an owner decision. It contains **no implementation**, modifies no production or test
code and no historical evidence, and makes **no** Neo4j / `Neo4j.Driver` compatibility claim. It
records acceptance only for the bounded scope in §4.

---

## 1. Owner decision

**Owner decision: B2-C ACCEPTED WITH QUALIFICATIONS.**

The owner reviewed the B2-C deliverable against the [B2 task](../tasks/B2-bolt-integration.md) §2–§4,
the [B2-C-01 implementation record](../reviews/B2-C-01-harness-implementation.md), the
[B2-C-Q1 independent qualification](../reviews/B2-C-Q1-independent-qualification.md) (**SUPPORTED
WITH QUALIFICATIONS**), the [B2-C-R1 targeted remediation](../reviews/B2-C-R1-targeted-remediation.md),
and the [B2-C-Q2 independent requalification](../reviews/B2-C-Q2-independent-requalification.md)
(**SUPPORTED WITH QUALIFICATIONS**), and records:

1. **B2-C is accepted with qualifications**, within the scope of §4.
2. **Q1-F01 and Q1-F02 are independently CLOSED** (B2-C-Q2 §4–§5).
3. The B2-C-R1 remediation and the B2-C-Q2 independent requalification are accepted as technical
   evidence.
4. The outstanding qualifications in §6 are accepted as documented, nonblocking exceptions.
5. **Reviewer identity is recorded but author independence remains UNVERIFIED**, accepted as a
   documented exception; this result is **not** described as verified independent signoff.
6. Accepting B2-C does **not** establish driver connectivity, transport compatibility,
   authentication, query handling, graph decoding, or Neo4j certification (§7).

**Owner acceptance vs. reviewer disposition.** The B2-C-Q2 reviewer disposition was **SUPPORTED
WITH QUALIFICATIONS**; that is a reviewer conclusion, not the owner decision. The owner decision is
separate and is recorded here. Recording acceptance does not modify, supersede, or rewrite the Q1,
R1, or Q2 reports, which remain the historical evidence.

## 2. Verified entry state

Independently read from the canonical checkout before this decision:

```text
git rev-parse HEAD          -> 06af745ec6b9bab2dba771150f9c6fb255a2620b
git rev-parse HEAD^{tree}   -> 809d5ac0524c1bbbafcce317db949c1a8a820c2b
git branch --show-current   -> main
git status --porcelain=v1   -> (empty, clean)
git worktree list           -> one worktree (D:/projects/bolt-sharp)
```

`HEAD` and tree match the pinned B2-C-Q2 identity. The four unpublished B2-C commits were present
and every ref was reachable from `main` (no unique work outside `main`; see §9).

## 3. Commit and tree identities

| Stage | Commit | Tree |
| --- | --- | --- |
| B2-B accepted baseline | `8f8b7e43b7d7fb3a9576db6e9465c13b74264d87` | `f7b09159be81d6855d0c7e4a4e632c242cd63187` |
| B2-C-01 implementation | `5295137bd688d38a2c1e3c67e6cc2daa51c050fb` | `5351dacd16475c75a2b44054a033ba34bd610e51` |
| B2-C-Q1 independent qualification | `2710bc73741ecd1f144cf4d60d7e2387d8f125f9` | `1c5dcc38a1138f662fb0b00fbbf5708b4d171f25` |
| B2-C-R1 targeted remediation | `87e029f490fc1cd965be2ce656c4e22ccfa26cf5` | `7983a0643f9f97031ecb03561e0cc2d429fe7272` |
| B2-C-Q2 independent requalification (pinned) | `06af745ec6b9bab2dba771150f9c6fb255a2620b` | `809d5ac0524c1bbbafcce317db949c1a8a820c2b` |

Ancestry is linear and additive: `8f8b7e4` → `5295137` → `2710bc7` → `87e029f` → `06af745`. The
owner acceptance commit is recorded in the final handoff message of this task (this file is part of
it, so it cannot contain its own SHA).

## 4. Accepted scope

- **Deterministic in-memory fragmenting transport harness** (`tests/BoltSharp.Tests/Harness/`):
  bounded, signal-backed ordering; fixed/seeded-random read fragmentation; shaped writes; explicit
  EOF/half-close; deterministic cancellation; disposal that releases pending reads; injected faults;
  no timers/sleeps.
- **Integration of existing B2-A framing and B2-B handshake** through the harness without duplicating
  their logic.
- **Official `Neo4j.Driver` 6.4.0 test-only scaffold** (`tests/BoltSharp.DriverIntegrationTests/`):
  exact pin, outside the core solution, loopback direct default, environment-driven credentials,
  enforced direct-URI validation, single validated driver-construction path, and D01–D10 gated.
- **B2-C-R1 remediation** of Q1-F01 and Q1-F02.
- **Q1-F01 (driver URI validation) and Q1-F02 (dispose wakes blocked reads) independently CLOSED**
  (B2-C-Q2 §4–§5), with the reviewer-owned replacement probe for the superseded C6 assertion.
- **B2-C-Q2 independent technical qualification** (40 reviewer probes, all pass).

### Explicitly not established by this acceptance

- **No driver connectivity.** D01–D10 remain gated; no live connection, TLS handshake,
  `HELLO`/`LOGON`, `RUN`/`PULL`, authentication, query handling, graph decoding or Neo4j
  certification is established.
- **No transport/server** — no TCP, TLS, WebSocket, Kestrel or real-server behaviour.
- **No B3/B4/B5** — no message schemas, session/connection state, backend or reference host.
- **No protocol-revision finalization** — B0-A remains **PROVISIONAL** (live probe `NOT RUN`).
- **No NuGet package publication** or other published artifact.
- **No cross-platform/deployment/production-security acceptance** — in-memory Windows/net8.0
  evidence only.

## 5. Finding closure

| Finding | Severity | Status | Evidence |
| --- | --- | --- | --- |
| Q1-F01 — routing URI documented rejected but not enforced | MINOR | **CLOSED** | B2-C-R1 §3; B2-C-Q2 §4 (30 independent probes) |
| Q1-F02 — dispose did not wake a blocked read | MINOR | **CLOSED** | B2-C-R1 §4; B2-C-Q2 §5 (9 probes + 100-iteration interleavings) |
| C6 historical assertion of pre-remediation behavior | n/a | **SUPERSEDED HISTORICAL ASSERTION — NOT A CURRENT CONTRACT TEST** | B2-C-Q2 §6 |

No BLOCKING or MAJOR finding remains open.

## 6. Accepted qualifications

1. **Historical C6 assertion intentionally superseded and left unchanged.** The original
   `C6_Dispose_alone_does_not_wake_a_blocked_read_documented` asserts the pre-remediation behavior
   and now fails by design; it is preserved unmodified and reported as the historical result
   **29 passed, 1 superseded failure** (never 30/30). It is not a current contract test and is
   replaced by `C6R_Dispose_releases_blocked_read_with_disposal_result`.
2. **OBS1** — synchronous reads on an empty harness channel are non-blocking/EOF-like; asynchronous
   reads block via signal. Harness-only, documented.
3. **OBS2** — `HarnessDuplex.Create` sizes both channels at the minimum of the two endpoints'
   configured caps. Harness-only, documented.
4. **OBS3** — the harness provides no concurrent multi-writer guarantee (standard `Stream`
   semantics). Documented.
5. **Reviewer independence UNVERIFIED.** Reviewer identity is recorded, but the implementer's
   identity is unknown and the shared Git author is not evidence of a distinct reviewer. Accepted as
   a documented exception; not verified independent signoff.
6. **B0-A protocol revision PROVISIONAL**; **official-driver interoperability UNVERIFIED**;
   **D01–D10 remain gated** with the documented prerequisite.

These qualifications are nonblocking: B2-C delivers test infrastructure and a gated scaffold only,
and neither finding's closure depends on them.

## 7. Non-claims

Acceptance of B2-C does **not** establish, and must not be represented as, driver connectivity,
transport compatibility, TLS or authentication behaviour, query execution, graph decoding, Neo4j
compatibility or certification. Interoperability can only be claimed after an executed integration
test against a pinned driver with environment, OS, TLS mode, command and output recorded (a later
milestone), and only under separate owner authorization.

## 8. Verification results

Environment: Windows `10.0.26200`, `win-x64`; .NET SDK `10.0.401`; MSBuild `18.9.11+e34a38d2a`;
target `net8.0`; .NET 8 runtime `8.0.31`. Commands were executed from the canonical root against the
pinned qualification state; evidence is under `docs/reviews/evidence/B2-C-R1/` and
`docs/reviews/evidence/B2-C-Q2/`.

| Executed set | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Full solution (`BoltSharp.slnx`) | 826 | 0 | 0 |
| Harness (`~Harness`) | 40 | 0 | 0 |
| Driver scaffold | 24 | 0 | 10 |
| B2-C-Q2 reviewer probes (separate project) | 40 | 0 | 0 |
| B2-B-Q1 reviewer probes (separate project) | 67 | 0 | 0 |
| B2-B-Q2 reviewer probes (separate project) | 46 | 0 | 0 |
| B2-A-Q2 reviewer probes (separate project) | 18 | 0 | 0 |
| Historical B2-C-Q1 reviewer probes | 29 | 1 (superseded C6) | 0 |

Builds: **0 warnings, 0 errors**. Overlapping subsets are not double-counted. The one historical
failure is the explicitly documented superseded assertion, not a current regression.

## 9. Repository state and synchronization

- Canonical `main` only; one worktree; working tree clean at entry.
- Before the push, local `main` held four unpublished B2-C commits (`5295137`, `2710bc7`, `87e029f`,
  `06af745`) ahead of `origin/main` (`8f8b7e4`). The remote tip was an ancestor of local `main`, so a
  normal fast-forward push was safe.
- Branch/ref inventory: local `main` only; remote `origin/main` (`8f8b7e4`) and
  `origin/feature/protocol-spec` (`e8475a47`). **No ref contained commits outside `main`**; the
  obsolete `feature/protocol-spec` branch is fully integrated (an ancestor of `main`). No branch or
  worktree was deleted.
- Synchronization: `git push origin main` (normal, non-force) updated `origin/main` to the accepted
  `main`. Post-push `origin/main` equals local `main`; ahead/behind 0/0.

## 10. Preservation

This decision changes exactly one path: this document. It does not modify `src/`, `tests/`, any
project/solution file, or any historical report, reviewer probe or evidence file. The B2-C-01/Q1/R1/Q2
records, their probes and their evidence remain the authoritative historical record. The historical
C6 failure is preserved and documented, not hidden or made green.

## 11. Acceptance receipt

```text
Task: B2-C-OWNER-01 (qualified owner acceptance of B2-C and canonical main synchronization)
Decision: ACCEPTED WITH QUALIFICATIONS (2026-11-05)
B2-B accepted baseline: 8f8b7e43b7d7fb3a9576db6e9465c13b74264d87
B2-C-01 implementation: 5295137bd688d38a2c1e3c67e6cc2daa51c050fb
B2-C-Q1 qualification:  2710bc73741ecd1f144cf4d60d7e2387d8f125f9
B2-C-R1 remediation:    87e029f490fc1cd965be2ce656c4e22ccfa26cf5
B2-C-Q2 requalification (pinned entry): 06af745ec6b9bab2dba771150f9c6fb255a2620b / tree 809d5ac0524c1bbbafcce317db949c1a8a820c2b
Owner acceptance commit: recorded in the final handoff message (this file cannot contain its own SHA)
Changed files: docs/decisions/B2-C-OWNER-01-owner-acceptance.md (this record only)
Accepted scope: in-memory harness; B2-A/B2-B integration; Neo4j.Driver 6.4.0 test-only scaffold;
                B2-C-R1 remediation; Q1-F01 and Q1-F02 CLOSED; B2-C-Q2 qualification
Outstanding qualifications: C6 superseded (29/1 historical); OBS1/OBS2/OBS3; reviewer independence
                UNVERIFIED; B0-A PROVISIONAL; driver interoperability UNVERIFIED; D01-D10 gated
Explicitly not established: driver connectivity, transport, authentication, queries, graph decode,
                Neo4j certification
Tests: solution 826/0/0; driver 24 passed/10 skipped; B2-C-Q2 40; B2-B-Q1 67; B2-B-Q2 46;
       B2-A-Q2 18; historical B2-C-Q1 29 passed/1 superseded; build 0 warnings/0 errors
Push: normal fast-forward push of canonical main (no force/rewrite)
Stop: acceptance recorded and main synchronized; B3/B4/B5 and the live driver probe not begun
```
