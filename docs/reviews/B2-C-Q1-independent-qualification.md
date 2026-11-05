# B2-C-Q1 — Independent harness and driver scaffold qualification

**Status:** COMPLETE — independent qualification delivered; owner acceptance PENDING
**Date:** 2026-11-05 (Asia/Manila)
**Task:** B2-C-Q1 (independent qualification of B2-C-01)
**Canonical branch:** `main` (one worktree)
**Disposition:** **SUPPORTED WITH QUALIFICATIONS**

This is an independent reviewer record. It does not modify production code, producer tests or
historical evidence, records no owner acceptance, and makes no Neo4j / `Neo4j.Driver`
compatibility claim.

---

## 1. Source and tree identities

| Object | Exact identity | Verified |
| --- | --- | --- |
| Reviewed baseline | `8f8b7e43b7d7fb3a9576db6e9465c13b74264d87` / tree `f7b09159be81d6855d0c7e4a4e632c242cd63187` | yes |
| Reviewed implementation | `5295137bd688d38a2c1e3c67e6cc2daa51c050fb` / tree `5351dacd16475c75a2b44054a033ba34bd610e51` | yes |
| Implementation sole parent | `8f8b7e43b7d7fb3a9576db6e9465c13b74264d87` | yes |
| Producer harness source under test | `tests/BoltSharp.Tests/Harness/HarnessStream.cs` SHA-256 `C061ECD3F2234F4DAE6E620BEB686D757E10EE614B97D0589BF379F38E6F6718` | yes |

Entry `HEAD` was exactly `5295137` on `main` with a clean working tree and one worktree.
Ancestry confirmed: `5295137^ == 8f8b7e4`; `--is-ancestor 8f8b7e4 5295137` exit 0. The
implementation change-set is **purely additive**: 32 `A` entries (9 code/docs files and 23 evidence
files; see §3). No `D`/`M`/`R` entries. No amend, rebase, reset, cherry-pick or history rewrite was
performed by the producer or by this review.

**Production and historical preservation verified (empty diffs):**

```text
git diff --exit-code 8f8b7e4 5295137 -- src BoltSharp.slnx Directory.Build.props \
    tests/BoltSharp.Tests/BoltSharp.Tests.csproj     -> exit 0
git diff --exit-code 8f8b7e4 5295137 -- docs/reviews/B1-* docs/reviews/B2-* \
    docs/reviews/probes docs/reviews/evidence/B2-A-Q2 docs/reviews/evidence/B2-B-* \
    docs/decisions docs/boltsharp-spec.md docs/tasks   -> exit 0
```

No accepted B2-A/B2-B production code, reviewer probe, evidence file, decision or specification was
changed.

## 2. Reviewer identity and independence provenance

- **Reviewer:** the current qualification session (`DeepSeek V4.1 Flash`, provider `nvidia`),
  distinct from the B2-C-01 implementation session.
- **Participation in implementation:** none. This session did not author, edit or commit
  `tests/BoltSharp.Tests/Harness/*`, `tests/BoltSharp.DriverIntegrationTests/*`, the B2-C-01 record
  or its evidence. Reviewer output is isolated to `docs/reviews/probes/B2-C-Q1/`,
  `docs/reviews/evidence/B2-C-Q1/` and this report.
- **Independence status: INDEPENDENCE UNVERIFIED.** The Git author identity
  (`BoltSharp Research Agent <agent@bolt-sharp.local>`) is shared across producer and reviewer
  commits and is not evidence of a distinct model or person; no implementer model/session identity
  is established. Model diversity alone is not treated as proof. Results below are executable
  technical evidence, not established author-independent signoff.
- **Review technique:** the reviewer project links the **exact** producer harness source
  (`HarnessStream.cs`, hash in §1) into an independent test assembly, so the shipped bytes are
  probed directly without modifying the producer project. The production framing/handshake APIs are
  exercised from `src/BoltSharp`.

## 3. Changed-file inventory and evidence-manifest verification

**Implementation change-set (`git diff --name-status 8f8b7e4 5295137`):** 32 added files —
`tests/BoltSharp.Tests/Harness/{HarnessStream,HarnessWire,HarnessProbes}.cs`;
`tests/BoltSharp.DriverIntegrationTests/{*.csproj,DriverTestSettings.cs,DriverInteropScaffoldTests.cs,README.md}`;
`docs/reviews/B2-C-01-harness-implementation.md`; 23 files under `docs/reviews/evidence/B2-C-01/`.

**Evidence manifest:** the B2-C-01 `sha256.json` was independently re-computed against all 23
listed files → **23 entries, 0 missing, 0 mismatched**, and **no unlisted files**. All seven
B2-C-01 TRX files were independently parsed and match the producer's `summary.json` exactly:
solution 817, harness 31, baseline-excluding-harness 786, driver 4 passed/10 skipped, reviewer 67,
q2 46, aq2 18. No identity or evidence discrepancy was found; proceeding was authorized.

## 4. Independent source-review conclusions

**Harness semantics.** `HarnessChannel` is a lock-protected, bounded byte queue with a
`TaskCompletionSource` signal; writes and completion signal the channel, and asynchronous reads wait
on the signal (no timers/sleeps). `HarnessStream` exposes read fragmentation (fixed or seeded
random), write shaping (`MaxWriteFragment`), injected read/write/flush faults, deterministic
cancellation, explicit `CompleteWrites` (EOF), and observable counters. `HarnessDuplex.Create`
connects two endpoints through two bounded channels.

- **Ordered-byte-stream semantics:** confirmed by A1/A6/F1 (single-byte and coalesced delivery,
  production writer→harness→production reader round-trip).
- **Write contract:** normal writes append every byte (splitting only shapes the append); fault
  writes write a prefix and **throw** — they never report success while discarding bytes (probe
  `D1`, `H04` equivalent). No silent byte loss was found.
- **Bounded buffering:** a write that would exceed the cap throws `InvalidOperationException` and
  accepts nothing (probes `D1`, `D4`; 200 repeated failures leave the buffer empty).
- **EOF/half-close:** `CompleteWrites` wakes a pending reader with clean EOF; buffered bytes remain
  readable before EOF (probes `C7`, `E3`).
- **Cancellation:** a blocked read is released by cancellation (`C5`).
- **Disposal:** `Dispose` does **not** wake a blocked read (finding Q1-F02).
- **Production reuse:** the harness is a plain `Stream`; it does not duplicate framing or handshake
  logic, and the probes route real `BoltMessageReader`/`BoltHandshakeNegotiator` through it.

**Could the harness hide a production defect?** The only identified masking risk is the synchronous
non-blocking read returning 0 on an empty channel (OBS1), which a test can mistake for EOF; all
producer and reviewer probes either supply bytes first or use the async path. No evidence was found
that the harness fabricates success, drops bytes silently, or bypasses the production decoders.

## 5. Reviewer probe design and exact results

New project `docs/reviews/probes/B2-C-Q1/` (`B2CQ1.Probes.csproj`, excluded from the solution;
30 discovered cases, all pass — `evidence/B2-C-Q1/b2cq1.trx`). Fixtures are literal hex/radix and
production writers are used only for a round-trip probe.

| Group | Cases | Coverage |
| --- | ---: | --- |
| A fragmentation | 6 | single-byte reads; seeded-random reproducibility; handshake at every split; multi-byte manifest VarInt under 1-byte fragmentation; chunk header/payload splits; coalesced writes → ordered messages |
| B byte integrity | 6 | lost byte; reordered byte; duplicated byte; incorrect payload boundary; premature terminator; bytes after handshake preserved |
| C lifecycle | 8 | EOF during handshake/header/body; cancellation before/while blocked; dispose-does-not-wake; completion leaves buffered bytes; reader completion with pending writes (bounded) |
| D resource limits | 4 | full buffer refuses wholly; oversized framed message; invalid declared length; repeated failures do not accumulate |
| E concurrency/determinism | 3 | concurrent writer/reader without deadlock (bounded 10 s); 20 repeated handshakes identical; blocked read completes when data arrives |
| F production round-trip | 1 | `BoltMessageWriter` → harness → `BoltMessageReader` |
| G driver scaffold | 2 | exact package pin and confinement (XML-parsed); ten gated tests and loopback default |

All 30 pass; no intermittent behavior was observed across the repeated/deterministic cases.
Deadlock probes use explicit bounded waits; a harness timeout (E1) is reported as a harness timeout,
not as a protocol timeout guarantee.

## 6. Negative-control effectiveness

The producer H05/H07 controls were independently re-derived (groups B, C, D above) with reviewer
fixtures, and shown to detect: malformed magic/reserved proposals, unsupported versions (zero
response + close), unoffered/superset manifest selections, oversized messages, invalid declared
lengths, lost/reordered/duplicated bytes, incorrect payload boundaries, premature terminators,
broken EOF mid-header/body, and buffer-capacity violations. The controls do not share an assumption
with production code: inputs are hand-built and decoded by the production reader. No control was
found to pass only because the harness and assertion share a wrong assumption.

## 7. Driver scaffold and gating assessment

- `Neo4j.Driver` is pinned exactly to **`6.4.0`** in
  `tests/BoltSharp.DriverIntegrationTests/` and appears as a `PackageReference` in **no other
  project**; `src/BoltSharp` does not reference it (probe `G1`, XML-parsed).
- The project builds independently (0 warnings, 0 errors) and is intentionally outside
  `BoltSharp.slnx`.
- Default endpoint is `bolt://127.0.0.1:7687` (loopback, direct scheme).
- Authentication is environment-driven; no credentials are hardcoded.
- Timeout is applied via `WithConnectionTimeout`; TLS is expressed only through the URI scheme and
  no unsupported security claim is made.
- Driver/session disposal uses `await using` (`D01`, `D03`–`D09`); `D09` disposes explicitly.
- **All ten D01–D10 tests are explicitly `[Fact(Skip = ...)]`** with the reason
  `Requires B3 message/session engine and B5-compatible network transport.` Each body contains real
  driver API usage (connectivity, session/run, typed nodes, reset, TLS/auth failure), so they are
  valid future scaffolds, not empty placeholders. A skipped test is reported as skipped and cannot
  become a false-positive compatibility claim; enabling one without a functioning endpoint fails,
  it does not silently pass.
- **Deficiency (Q1-F01):** the settings documentation states routing schemes "are rejected", but no
  code enforces rejection (see §8).

## 8. Findings by severity

| ID | Severity | File / location | Reproduction | Expected vs observed | Impact / remediation |
| --- | --- | --- | --- | --- | --- |
| Q1-F01 | MINOR | `tests/BoltSharp.DriverIntegrationTests/DriverTestSettings.cs` lines 10–11 (doc) and `DriverInteropScaffoldTests.NewDriver` | Set `BOLTSHARP_DRIVER_URI=neo4j://127.0.0.1:7687`; `DriverTestSettings.UsesRoutingScheme` is `true`, but `NewDriver` passes the routing URI straight to `GraphDatabase.Driver`. `Scaffold_Rejects_Routing_Scheme_For_A_Direct_Server` only checks the default and a literal `Uri` scheme; it does not exercise enforcement. | Doc says routing "rejected"; observed: detected only, never rejected at driver creation. | Nonblocking (tests gated; default is direct loopback), but contradicts the direct-only rule. Remediate by throwing for `UsesRoutingScheme` in `DriverTestSettings`/`NewDriver` and correcting the doc wording. |
| Q1-F02 | MINOR | `tests/BoltSharp.Tests/Harness/HarnessStream.cs` `Dispose` (and `HarnessChannel`) | Reviewer probe `C6_Dispose_alone_does_not_wake_a_blocked_read_documented`: start a pending `ReadAsync`, call `Dispose`; the read remains pending (>200 ms) until cancellation. | A reusable test harness should release pending operations on disposal; observed: only cancellation releases them. | Nonblocking (production never disposes caller streams; cancellation works). Remediation: have `Dispose` complete the read channel (signal EOF) or document the cancellation requirement. No production defect is implied. |

No BLOCKING or MAJOR finding was reproduced. No fabricated `HELLO`/`LOGON`/`SUCCESS`, no
message/session handling, authentication, backend, TCP/TLS/WebSocket server, LatticeDB code or
NuGet publication change was found in the reviewed change-set.

## 9. Remaining limitations and unsupported claims

- Executable official-driver compatibility is **not** established: D01–D10 are gated and were not
  executed; no live connectivity attempt was made in this qualification.
- No Neo4j, Neo4j Browser or `Neo4j.Driver` compatibility claim is supported by B2-C-01.
- The B0-A revision remains **PROVISIONAL**; the live client probe remains `NOT RUN`.
- B3 (message/session) and B5 (TCP/TLS transport) are not implemented; the disposable loopback
  handshake probe in the producer record §10 is a **plan only** and was not executed.
- The harness is in-memory and deterministic; it does not model socket scheduling, TLS, or
  backpressure, and its synchronous read is non-blocking (OBS1).
- OBS1: synchronous `Read` on an empty open channel returns 0 (EOF-like); asynchronous reads block
  via signal. OBS2: `HarnessDuplex.Create` uses the minimum of the two endpoints' buffer caps.
  OBS3: the harness is not a thread-safe multi-writer stream (standard `Stream` contract).

These limitations are nonblocking because B2-C-01 delivers test infrastructure and a gated scaffold
only, and the two MINOR findings do not affect the correctness of the harness for the exercised
scenarios or the gating of the driver tests.

## 10. Formal qualification disposition

**SUPPORTED WITH QUALIFICATIONS.** The in-memory harness is deterministic, correctly bounded, and
faithfully exercises the accepted production framing/handshake implementations; the official driver
scaffold is correctly pinned, confined to its test project and appropriately gated. All producer
counts reproduce under independent execution (see §11). The two MINOR findings (Q1-F01 doc/behavior
mismatch; Q1-F02 no wake-on-dispose) are nonblocking and are recorded for remediation or explicit
owner acceptance.

## 11. Reproduction results (Q1-06)

Environment: Windows `10.0.26200`, `win-x64`; .NET SDK `10.0.401`; MSBuild `18.9.11+e34a38d2a`;
target `net8.0`; .NET 8 runtime `8.0.31`. Commands recorded in
`docs/reviews/evidence/B2-C-Q1/` (`Run-Evidence.ps1`, `commands.json`, `summary.json`); all
processes exited `0`.

| Executed set | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Full solution (`BoltSharp.slnx`) | 817 | 0 | 0 |
| — existing baseline (excluding new harness) | 786 | 0 | 0 |
| — new H01–H07 harness tests | 31 | 0 | 0 |
| Driver scaffold (`BoltSharp.DriverIntegrationTests`) | 4 | 0 | 10 |
| B2-B-Q1 reviewer probes | 67 | 0 | 0 |
| B2-B-Q2 reviewer probes | 46 | 0 | 0 |
| B2-A-Q2 reviewer probes | 18 | 0 | 0 |
| **B2-C-Q1 reviewer probes (new)** | **30** | **0** | **0** |

Overlapping subsets are not double-counted. Core solution build and driver scaffold build both
report **0 warnings, 0 errors**.

## 12. Evidence binding and handoff

Reviewer probes: `docs/reviews/probes/B2-C-Q1/`. Evidence:
`docs/reviews/evidence/B2-C-Q1/` with a SHA-256 manifest. This report and its evidence are committed
additively on top of `5295137`; the reviewer commit/tree is recorded in the final handoff message
(this file cannot contain its own SHA).

At entry, local `main` was 1 commit ahead of `origin/main` (`8f8b7e4`); no push or remote change
was performed by this review. Owner acceptance and any remediation of Q1-F01/Q1-F02 are separate
owner decisions. This record does not self-certify B2-C acceptance and does not authorize driver
compatibility or the next milestone.
