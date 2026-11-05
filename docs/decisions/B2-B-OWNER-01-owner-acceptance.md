# B2-B-OWNER-01 — B2-B qualified owner acceptance

**Status:** ACCEPTED WITH QUALIFICATIONS (owner decision recorded 2026-11-05)
**Date:** 2026-11-05 (Asia/Manila)
**Task:** B2-B-OWNER-01 — qualified owner acceptance of B2-B (Bolt handshake) and of the
B2BQ1-F01 remediation
**Canonical branch:** `main` (no additional worktree)
**Pinned qualification commit:** `91ea211e201c4d936ec1aca06a9f53e727d2a7da`
**Pinned qualification tree:** `aeb635847c40561beaed49f19ab06361b6f73cda`

This record is an owner decision. It contains **no implementation**, modifies no production or test
code and no historical evidence, and makes **no claim** of Neo4j, Neo4j Browser, or official
`Neo4j.Driver` compatibility. It records acceptance only for the bounded scope in §4.

---

## 1. Owner decision

**Owner decision: B2-B ACCEPTED WITH QUALIFICATIONS.**

The owner reviewed the B2-B deliverable against the [B2 task](../tasks/B2-bolt-integration.md) §3
contract and §4 acceptance criteria, the [B2-B implementation record](../reviews/B2-B-bolt-handshake.md),
the [B2-B-Q1 independent qualification](../reviews/B2-B-Q1-independent-qualification.md), the
[B2-B-R1 remediation record](../reviews/B2-B-R1-zero-version-remediation.md), and the
[B2-B-Q2 independent requalification](../reviews/B2-B-Q2-independent-requalification.md), and
records:

1. **B2-B is accepted with qualifications**, within the handshake-only scope of §4.
2. **B2BQ1-F01 is CLOSED.** The remediation and the independent requalification support closure;
   the four preserved original failing assertions now pass without modification.
3. The B2-B-R1 regression evidence and the B2-B-Q2 independent qualification are accepted as
   technical evidence.
4. **Q2-OBS1 and Q2-OBS2 are accepted as nonblocking observations** (§6).
5. **Reviewer identity is recorded, but author independence remains UNVERIFIED.** The owner accepts
   the technical qualification evidence with this documented exception, and does **not** describe
   the result as verified independent signoff.
6. Accepting B2-B does **not** qualify B2-C, B3, or any other protocol feature (§7).

**Owner acceptance vs. reviewer disposition.** The B2-B-Q2 reviewer disposition was **SUPPORTED
WITH QUALIFICATIONS**; that is a reviewer conclusion, not the owner decision. The owner decision is
separate and is recorded here. Recording acceptance does not modify, supersede, or rewrite the Q1,
R1, or Q2 reports, which remain the historical evidence.

---

## 2. Verified entry state

Independently read from the canonical checkout before this decision:

```text
git rev-parse HEAD                    -> 91ea211e201c4d936ec1aca06a9f53e727d2a7da
git rev-parse HEAD^{tree}             -> aeb635847c40561beaed49f19ab06361b6f73cda
git branch --show-current             -> main
git status --porcelain=v1             -> (empty, clean working tree)
```

The pinned qualification commit and tree match the task entry exactly. Ancestry is linear with the
preceding stage as sole parent at each step (verified parent links, §3).

---

## 3. Commit and tree identities

| Stage | Commit | Tree |
| --- | --- | --- |
| B2-B implementation (production) | `ca36d36ee1b840b444180432e355832d56f5384f` | `61485c1c37864a80c00692330958b04483e1204d` |
| B2-B-Q1 independent qualification (baseline) | `c99ae17d5a446989ea63098d7e368fabefd70166` | `89c2b178225fcd62e2ee28590973354af56956e1` |
| B2-B-R1 zero-version remediation | `b0bcadbf344ea0eef95cdc60702fd1bf8da620c8` | `fe4159ac5e68b891fdbbd34f489c38fa84ce04af` |
| B2-B-R1 evidence | `70607f0e89fa0554f9731b1b7dcc49e97798372d` | `f319b359f375ea9f1fab57e33d130948d3eb0d5f` |
| B2-B-Q2 independent requalification (pinned) | `91ea211e201c4d936ec1aca06a9f53e727d2a7da` | `aeb635847c40561beaed49f19ab06361b6f73cda` |

Ancestry (verified as sole-parent links): `ca36d36` → `c99ae17` → `b0bcadb` → `70607f0` → `91ea211`.
The B2-B-R1 production change-set is exactly three modified handshake sources plus one added test
file; no project, dependency, solution or historical file changed. The owner acceptance commit is
recorded in the final handoff message of this task (this file is part of it, so it cannot contain
its own SHA).

---

## 4. Accepted scope

Within the B2-B handshake-only boundary (over an abstract ordered byte stream; no sockets, TLS,
pipelining framework or server):

- **B2-B handshake implementation** — `src/BoltSharp/Handshake/*`: magic `60 60 B0 17`, exactly four
  big-endian 32-bit proposals, legacy selection (a version, or `00 00 00 00` then close), version
  ranges (Bolt 4.3 style), bounded VarInts, Manifest v1 offer/selection with capability subset
  enforcement, deterministic typed failures, caller-owned stream with exact-read/pipelining
  preservation. Supported by the 155 producer handshake tests within the green solution.
- **B2BQ1-F01 remediation and closure** — protocol `0.0` is never a configured, offered, selected
  or accepted version; zero-filled client proposal slots remain valid placeholders; legacy
  no-match writes exactly four zero bytes and requests closure; no failed negotiation publishes
  `Accepted=true` or `ShouldClose=false`; Manifest never advertises/selects/accepts `0.0`.
- **B2-B-R1 regression evidence** — 27 focused cases (configuration, legacy sync/async, manifest
  sync/async, placeholders, no-match bytes/closure, legitimate versions, repeated state).
- **B2-B-Q2 technical qualification** — 46 independent reviewer cases including negative controls
  that implement the pre-remediation algorithm.
- **Q2-OBS1 and Q2-OBS2** — accepted as nonblocking observations (§6).

### Explicitly excluded (not established by this acceptance)

- **No B2-C qualification.** The integration harness and the pinned, gated `Neo4j.Driver`
  integration-test scaffold are **not** implemented, qualified or accepted here (§7).
- **No B3 or later protocol feature** — no message schemas, `HELLO`/`LOGON`/`RUN`/`PULL`/`RESET`/
  `GOODBYE`, session/connection state, authentication, backend contract or cursors.
- **No driver compatibility.** Neo4j, Neo4j Browser and official `Neo4j.Driver` compatibility are
  **not established**; that claim is admissible only with an executed integration test against a
  pinned driver version at the later executable milestone.
- **No transport** — no sockets, `SslStream`, Kestrel, WebSocket or real-server behaviour.
- **No finalized protocol revision** — the B0-A compatibility target remains **PROVISIONAL** and
  the live client probe remains `NOT RUN`.
- **No NuGet package publication** or any other published artifact.
- **No cross-platform, deployment or production-security acceptance** — in-memory Windows/net8.0
  evidence only.

---

## 5. Finding closure

| Finding | Severity | Status | Evidence |
| --- | --- | --- | --- |
| **B2BQ1-F01** — explicit `0.0` configured, offered, selected and published as accepted | MAJOR | **CLOSED** | [B2-B-R1](../reviews/B2-B-R1-zero-version-remediation.md) §4–§8; [B2-B-Q2](../reviews/B2-B-Q2-independent-requalification.md) §11; four preserved assertions pass 4/0/0 |
| Q2-OBS1 — configured `0.0` silently excluded rather than reported | LOW (observation) | ACCEPTED (nonblocking) | [B2-B-Q2](../reviews/B2-B-Q2-independent-requalification.md) §6, §10 |
| Q2-OBS2 — public codec types can represent a `0.0` range as inert data | INFO | ACCEPTED (nonblocking) | [B2-B-Q2](../reviews/B2-B-Q2-independent-requalification.md) §10 |

No CRITICAL, MAJOR or HIGH finding remains open. The B2-B-Q1 verdict was REMEDIATION REQUIRED on
F01 only; that single finding is now independently closed. All other B2-B-Q1 gates (provenance,
resource/state safety, regression, evidence integrity) were PASS and are re-exercised by the
B2-B-Q2 requalification.

---

## 6. Accepted qualifications

1. **Author independence is UNVERIFIED.** The reviewer identity is recorded
   (`DeepSeek V4.1 Flash`, `nvidia`, distinct qualification session), but the original
   implementer's model/session identity is unknown and the shared Git author identity
   (`BoltSharp Research Agent <agent@bolt-sharp.local>`) is not evidence of a distinct reviewer.
   The owner accepts the technical qualification evidence with this documented exception. This
   result must **not** be described as verified independent signoff.
2. **Q2-OBS1** — configured `0.0` is dropped silently rather than reported. Accepted as an
   appropriate fail-closed configuration policy; no documented contract is violated and the
   outcome is deterministic and safe.
3. **Q2-OBS2** — the public codec types can still represent a `0.0` range as inert data. Accepted
   as a layer boundary; the negotiator never emits or accepts such data.
4. Accepted B2-B-Q1 limitations are carried forward unchanged: no rollback/resynchronisation after
   a handshake failure; no intrinsic timeout/flood policy; non-minimal bounded VarInts accepted;
   in-memory Windows/net8.0 evidence only.

These qualifications do not invalidate the claim that `B2BQ1-F01` is closed and that existing B2-B
handshake guarantees are preserved on the exercised surface.

---

## 7. Scope boundary and B2-C eligibility

**Accepting B2-B does not qualify B2-C, B3, or any other protocol feature.** This decision
authorizes no implementation work.

**B2-C eligibility.** From a dependency standpoint B2-C's prerequisite is now satisfied: both B2-A
and B2-B are accepted. B2-C is therefore **eligible to start locally**, but it is **not authorized
by this decision**. Before B2-C begins, the owner should confirm the B2 task §8 open item on
integration-harness and driver-test-project timing and issue a separate B2-C assignment. The
unreconciled remote tip (§8) is an integration/hosting blocker and does not prevent a local start.

B3 and later messaging/state/transport work are not eligible to start and must not be begun.

---

## 8. Repository state and integration blocker

- Canonical `main` only; a single worktree; the working tree was clean at entry.
- Local `main` is **ahead of the stale tracking reference** `refs/remotes/origin/main`
  (`a46c916e7f6e72e315504e2d284921d406ee6c74`) by the additive local commits through this decision;
  the local commits are preserved.
- The reported **live remote tip `357df2d6836c4995a741b72bd442f09efa91756f` remains
  UNRECONCILED**, with unresolved ancestry relative to local history. No fetch, push, force-push,
  reset, rebase or history rewrite was performed by this task.
- **This is a separate integration blocker to be resolved outside this qualification/acceptance
  work**; it does not affect the reviewed local identity. Do not interpret local ahead-of-tracking
  status as a verified relationship to the live remote.

---

## 9. Verification results

Environment: Windows `10.0.26200`, `win-x64`; .NET SDK `10.0.401`; MSBuild `18.9.11+e34a38d2a`;
target `net8.0`; .NET 8 runtime `8.0.31`; CLI host `10.0.12`. Commands were executed from the
canonical repository root against the pinned qualification state.

| Set | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Full solution (`BoltSharp.slnx`) | 786 | 0 | 0 |
| B2-B-R1 regression (subset of solution) | 27 | 0 | 0 |
| B2-B-Q1 preserved reviewer probes (separate project) | 67 | 0 | 0 |
| — the four original F01 assertions (subset of 67) | 4 | 0 | 0 |
| B2-A-Q2 reviewer probes (separate project) | 18 | 0 | 0 |
| B2-B-Q2 reviewer probes (separate project) | 46 | 0 | 0 |

Solution build: **0 warnings, 0 errors**. Overlapping subsets are not summed into a single total.
Counts were independently derived from executed TRX results; the pinned B2-B-R1 evidence manifest
re-verified 18/18 hashes with no mismatch. The verdicts of the reviewed reports support the accepted
scope: B2-B-Q1 = REMEDIATION REQUIRED (F01 only); B2-B-R1 = remediation complete; B2-B-Q2 =
SUPPORTED WITH QUALIFICATIONS.

---

## 10. Preservation

This decision changes exactly one path: this document. It does not modify `src/`, `tests/`, any
`.csproj`/`.props`/`.slnx`, or any historical report, reviewer probe or evidence file. The B2-B-R1
and B2-B-Q2 reports, their probes and their evidence remain the authoritative historical record.
No owner acceptance modifies the reviewed content.

---

## 11. Acceptance receipt

```text
Task: B2-B-OWNER-01 (qualified owner acceptance of B2-B)
Decision: ACCEPTED WITH QUALIFICATIONS (2026-11-05)
Baseline (B2-B-Q1): c99ae17d5a446989ea63098d7e368fabefd70166
B2-B implementation: ca36d36ee1b840b444180432e355832d56f5384f
B2-B-R1 remediation: b0bcadbf344ea0eef95cdc60702fd1bf8da620c8
B2-B-R1 evidence: 70607f0e89fa0554f9731b1b7dcc49e97798372d
Pinned qualification entry: 91ea211e201c4d936ec1aca06a9f53e727d2a7da / tree aeb635847c40561beaed49f19ab06361b6f73cda
Owner acceptance commit: recorded in the final handoff message (this file cannot contain its own SHA)
Changed files: docs/decisions/B2-B-OWNER-01-owner-acceptance.md (this record only)
Accepted scope: B2-B handshake; B2BQ1-F01 closure; B2-B-R1 regression; B2-B-Q2 qualification;
                Q2-OBS1 and Q2-OBS2 as nonblocking observations
Finding B2BQ1-F01: CLOSED
Outstanding qualification: author independence UNVERIFIED (accepted with documented exception)
Explicitly not qualified: B2-C, B3, driver compatibility, transport, NuGet publication
Protocol revision: B0-A remains PROVISIONAL (live probe NOT RUN)
Tests: 786 solution / 0 failed / 0 skipped; 67 + 18 + 46 separate reviewer cases, 0 failed;
       build 0 warnings / 0 errors
B2-C: eligible to start locally; NOT authorized by this decision
Integration blocker: live remote tip 357df2d6836c4995a741b72bd442f09efa91756f UNRECONCILED
Stop: owner decision only; no B2-C/B3 implementation begun
```
