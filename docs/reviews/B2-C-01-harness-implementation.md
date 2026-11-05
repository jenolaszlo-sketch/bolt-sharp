# B2-C-01 — Integration harness and official driver scaffold

**Status:** IMPLEMENTED — awaiting independent qualification (B2-C-Q1); owner acceptance PENDING
**Date:** 2026-11-05 (Asia/Manila)
**Task:** B2-C-01 (B2-C integration harness and driver scaffold), authorized by the owner
**Canonical branch:** `main` (one worktree)
**Pinned baseline:** `8f8b7e43b7d7fb3a9576db6e9465c13b74264d87`
**Baseline tree:** `f7b09159be81d6855d0c7e4a4e632c242cd63187`

This record contains **no claim** of Neo4j, Neo4j Browser, or official `Neo4j.Driver`
compatibility. B2-C delivers test infrastructure and a **gated** scaffold only. The B0-A protocol
revision remains **PROVISIONAL**. B2-A and B2-B are already accepted and are not modified.

---

## 1. Baseline identity

| Object | Exact identity |
| --- | --- |
| Baseline commit | `8f8b7e43b7d7fb3a9576db6e9465c13b74264d87` |
| Baseline tree | `f7b09159be81d6855d0c7e4a4e632c242cd63187` |
| Ancestry | owner-accepted B2-B merge commit (parents `0c585c7`, `357df2d`) |

Entry `main` was clean with a single worktree. No production file, accepted evidence file, project
file, dependency or solution membership was modified. All B2-C-01 changes are additive.

## 2. Design and scope decisions

The four outstanding B2 scope decisions are resolved and recorded as owner decisions for B2-C:

1. **B2 includes the in-memory integration harness and a gated driver-test scaffold.** The
   harness and scaffold are in scope; executable driver interoperability is not.
2. **The official Neo4j driver dependency is authorized in a dedicated integration-test project
   only.** `Neo4j.Driver` 6.4.0 is referenced solely by
   `tests/BoltSharp.DriverIntegrationTests/`; it is never referenced by `src/BoltSharp`.
3. **Framing and handshake may proceed against their existing specification-defined behaviour**
   while B0-A protocol-version selection remains **provisional**. The harness and scaffold exercise
   the accepted B2-A/B2-B contracts without selecting or finalizing a revision.
4. **Executable driver interoperability and graph-returning claims remain deferred** until the
   required protocol sessions (B3) and transport (B5) exist. D01–D10 are therefore gated.

These decisions authorize **B2-C only** and do not expand B3, B4 or B5 authorization.

## 3. Harness architecture and component boundaries

The harness is a plain `System.IO.Stream` implementation. It does **not** reimplement framing or
handshake logic; production `BoltMessageReader` / `BoltMessageWriter` and
`BoltHandshakeNegotiator` are exercised through it unchanged.

`tests/BoltSharp.Tests/Harness/HarnessStream.cs`:

- **`HarnessChannel`** — a bounded, signal-backed byte queue. Writes beyond
  `MaxBufferedBytes` fail with `InvalidOperationException` rather than accumulating without bound.
  Completion is explicit. Asynchronous reads await a signal completed by a write or by
  completion; there are **no timers or sleeps**, so execution is deterministic.
- **`HarnessStream`** — one duplex endpoint over a read channel and a write channel. Supports a
  fixed or seeded-random read-fragmentation policy, an optional maximum write fragment
  (partial-write shaping), injected read/write/flush faults, deterministic cancellation at a byte
  offset, and observable `Consumed` / `Written` / `AvailableToRead` / `ReadCompleted` / `Disposed`
  state. Write completion (`CompleteWrites`) signals EOF to the peer.
- **`HarnessDuplex.Create(...)`** — wires two endpoints through two bounded channels so that
  bytes written on the client are read on the server and vice versa, with independent per-endpoint
  fragmentation and fault options.

`tests/BoltSharp.Tests/Harness/HarnessWire.cs` — independent literal wire fixtures (magic, four
proposals, Bolt 4.0/4.3 encodings, VarInt, chunk framing) so decoder tests do not depend on the
production writers.

Blocking behaviour is signal-driven rather than timing-driven. Bounded buffering, explicit
completion, half-close semantics, cancellation, disposal and injected faults are all available
without a background thread or clock.

## 4. Changed-file inventory

| File | Kind |
| --- | --- |
| `tests/BoltSharp.Tests/Harness/HarnessStream.cs` | new harness implementation |
| `tests/BoltSharp.Tests/Harness/HarnessWire.cs` | new independent wire fixtures |
| `tests/BoltSharp.Tests/Harness/HarnessProbes.cs` | new H01–H07 tests |
| `tests/BoltSharp.DriverIntegrationTests/BoltSharp.DriverIntegrationTests.csproj` | new opt-in project (driver pin) |
| `tests/BoltSharp.DriverIntegrationTests/DriverTestSettings.cs` | new configuration |
| `tests/BoltSharp.DriverIntegrationTests/DriverInteropScaffoldTests.cs` | new gated D01–D10 + scaffold checks |
| `tests/BoltSharp.DriverIntegrationTests/README.md` | new project documentation |
| `docs/reviews/B2-C-01-harness-implementation.md` | this record |
| `docs/reviews/evidence/B2-C-01/**` | new reproducible evidence |

No production code, accepted test, historical review, project file, solution file or dependency
was changed. `BoltSharp.slnx` still declares only the core and `BoltSharp.Tests` projects; the
driver project is intentionally **outside** the solution so that core restore/build stays
driver-free.

## 5. Test matrix and results

Environment: Windows `10.0.26200`, `win-x64`; .NET SDK `10.0.401`; MSBuild `18.9.11+e34a38d2a`;
target `net8.0`; .NET 8 runtime `8.0.31` (see `evidence/B2-C-01/environment.txt`).

### 5.1 Harness tests (`HarnessProbes`, 31 cases; all pass)

| ID | Method(s) | Coverage |
| --- | --- | --- |
| H01 | `H01_Legacy_handshake_outcome_is_invariant_under_fragmentation`, `H01_Seeded_random_fragmentation_is_invariant` (4) | Identical outcome (selected version, response bytes) under fixed fragment sizes 1–64 and seeded random fragmentation |
| H02 | `H02_Manifest_handshake_succeeds_at_every_split`, `H02_Framed_message_succeeds_at_header_body_and_terminator_boundaries` | Splits at magic/proposal/VarInt/chunk-header/chunk-body/terminator boundaries |
| H03 | `H03_Pipelined_messages_preserve_order_and_bytes`, `H03_Handshake_leaves_pipelined_frame_for_the_message_reader` | Multiple framed messages in one stream; handshake consumes exactly 20 bytes and leaves the frame |
| H04 | `H04_Cancellation_is_deterministic_and_publishes_no_result` (3), `H04_Clean_eof_between_messages_is_null`, `H04_Truncated_header_and_body_are_typed`, `H04_Partial_response_write_surfaces_deterministically`, `H04_Production_does_not_dispose_the_caller_stream` | Cancellation, clean EOF, truncation, partial output, ownership |
| H05 | `H05_Invalid_magic_and_reserved_proposal_are_format_errors`, `H05_Unsupported_version_writes_zero_and_requests_close`, `H05_Invalid_manifest_selection_is_a_protocol_error`, `H05_Oversized_message_exceeding_the_cap_is_a_format_error`, `H05_Declared_length_beyond_available_bytes_is_truncation` | Negative protocol behaviour preserved |
| H06 | `H06_Legacy_handshake_then_two_pipelined_messages` (2), `H06_Manifest_handshake_then_pipelined_message` (2) | Full in-memory handshake→framing integration (sync and async) |
| H07 | `H07_Lost_body_byte_is_detected`, `H07_Reordered_bytes_produce_a_different_payload`, `H07_Malformed_length_field_is_detected`, `H07_Broken_eof_mid_header_is_not_a_clean_end`, `H07_Bounded_buffer_refuses_unbounded_accumulation`, `H07_Fragmentation_policy_is_applied_and_writes_coalesce` | Negative controls proving the harness/pipeline detects lost bytes, reordering, malformed lengths and broken EOF, and that the harness itself is bounded |

### 5.2 Aggregate results (independently re-derived from TRX; overlaps not double-counted)

| Executed set | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Full solution (`BoltSharp.slnx`) | 817 | 0 | 0 |
| — pre-existing baseline (excluding new harness) | 786 | 0 | 0 |
| — new B2-C harness cases | 31 | 0 | 0 |
| Driver scaffold (`BoltSharp.DriverIntegrationTests`) | 4 | 0 | 10 |
| B2-B-Q1 reviewer probes (separate project) | 67 | 0 | 0 |
| B2-B-Q2 reviewer probes (separate project) | 46 | 0 | 0 |
| B2-A-Q2 reviewer probes (separate project) | 18 | 0 | 0 |

The accepted 786-test baseline is preserved unchanged (786 pass when the harness filter is
excluded). Core solution build: **0 warnings, 0 errors**. Driver scaffold build: **0 warnings,
0 errors**. All processes exited `0`.

## 6. Driver package and exact version

- Package: **`Neo4j.Driver` `6.4.0`**, exact pin (no floating range), in
  `tests/BoltSharp.DriverIntegrationTests/` only.
- The scaffold self-check `Scaffold_Driver_Package_Version_Is_Pinned_To_6_4_0` asserts the loaded
  assembly informational and file versions start with `6.4.0`.
- A future executable run must record the package version, endpoint, encryption mode, OS and the
  negotiated revision, per the B2 task's admissibility rule.

## 7. Gated driver tests and activation prerequisites

D01–D10 are `[Fact(Skip = "Requires B3 message/session engine and B5-compatible network
transport.")]`. They compile against the official driver API and are **skipped**, never reported
as passing compatibility evidence.

| Test | Future verification | Status now |
| --- | --- | --- |
| D01 | Driver establishes a real connection | SKIPPED / GATED |
| D02 | Bolt protocol revision negotiated | SKIPPED / GATED |
| D03 | HELLO and LOGON succeed | SKIPPED / GATED |
| D04 | Read-only RUN accepted | SKIPPED / GATED |
| D05 | PULL returns records | SKIPPED / GATED |
| D06 | Typed nodes and relationships decoded | SKIPPED / GATED |
| D07 | Unsupported request fails correctly | SKIPPED / GATED |
| D08 | RESET and failure recovery behave correctly | SKIPPED / GATED |
| D09 | Connection closes cleanly | SKIPPED / GATED |
| D10 | TLS and authentication failure paths | SKIPPED / GATED |

**Activation prerequisites:** (1) B3 message/session engine implementing
`HELLO`/`LOGON`/`RUN`/`PULL`/`RESET`/`GOODBYE`; (2) a B5-compatible TCP/TLS transport exposing the
same engine on a disposable loopback endpoint. Until then the scaffold asserts nothing about
compatibility. Configuration is environment-driven (`DriverTestSettings`); the default is a direct
loopback URI and no credentials are hardcoded. Routing URIs (`neo4j://`) are detected and
documented as unsupported.

Non-gated `Scaffold_*` tests (4, passing) validate the scaffold itself: loopback direct default,
exact package pin, routing-scheme rejection and documented gate reasons. They require no server.

## 8. Negative-test evidence

H05 and H07 exercise negative paths through the harness with **production** decoders (no production
mutation):

- invalid magic and reserved-byte proposals → `BoltHandshakeFormatException`;
- unsupported version → exactly `00 00 00 00` with `ShouldClose=true`, `SelectedVersion=null`;
- unoffered manifest version and capability superset → `BoltHandshakeProtocolException`, no result;
- oversized message beyond the receiver cap → `BoltFramingFormatException`;
- declared length beyond available bytes, half a header, and a dropped body byte →
  `BoltFramingTruncatedException` (not a silent success);
- byte reordering yields a different decoded payload (the pipeline detects it);
- harness buffer cap rejects unbounded accumulation;
- partial response write (2 bytes then fault) surfaces deterministically and the peer observes only
  the partial prefix, with the caller-owned stream not disposed.

## 9. Remaining compatibility unknowns

- No real driver connection, TLS handshake, `HELLO`/`LOGON`, query or graph decode has been
  executed; D01–D10 are gated.
- The B0-A revision (Bolt 5.8 / PackStream 1.0) remains **PROVISIONAL**; the live client probe is
  still `NOT RUN`.
- The exact client proposal list a current `Neo4j.Driver` 6.4.0 sends, and whether it prefers
  Manifest v1, is **UNKNOWN** until the probe in §10 runs.
- B3/B5 dependencies (message sessions, TCP/TLS transport) are not implemented.
- The harness is in-memory and deterministic; it does not model socket scheduling, TLS or backpressure.

## 10. Next-step recommendation — authentic driver handshake probe

A minimal, separately-approved follow-on probe can obtain genuine driver handshake evidence without
implementing a protocol server:

1. Start a **disposable, test-only** loopback listener (`System.Net.Sockets.TcpListener` bound to
   `127.0.0.1` on an ephemeral port) that accepts exactly one connection and records the first
   bytes, then closes.
2. Run the official driver against a direct URI (`bolt://127.0.0.1:<port>` or a TLS variant once
   TLS exists), using the pinned 6.4.0 package and the `DriverTestSettings` credentials.
3. Record: driver package version; direct URI and encryption mode; the four client proposals and
   whether `00 00 01 FF` is present; whether the server response would be legacy or Manifest; the
   selected revision (if a valid legacy response is replayed); the **exact observed handshake
   bytes** (excluding secrets); and the expected failure after negotiation because `HELLO`/`LOGON`
   is unavailable.
4. Keep sanitized evidence and record the command, OS and toolchain.

A driver that refuses to connect **after** a successful negotiation because session handling is
missing is **not** evidence that the handshake implementation failed; equally, successful
negotiation alone is **not** proof of driver compatibility. This probe requires a separate owner
scope decision and is **not implemented** or executed in B2-C-01. No production TCP listener was
added.

## 11. Commit and tree identities

The implementation/test commit and the evidence commit are recorded in the final handoff message
of this task; this file is part of one of them, so it cannot contain its own SHA. The baseline
identities are in §1; the `src`/`tests`/evidence trees are captured in
`evidence/B2-C-01/identity.txt`.

## 12. Acceptance receipt

```text
Task: B2-C-01 (integration harness + official driver scaffold)
Baseline commit: 8f8b7e43b7d7fb3a9576db6e9465c13b74264d87
Baseline tree:   f7b09159be81d6855d0c7e4a4e632c242cd63187
Changed files: tests/BoltSharp.Tests/Harness/{HarnessStream,HarnessWire,HarnessProbes}.cs;
               tests/BoltSharp.DriverIntegrationTests/{*.csproj,DriverTestSettings.cs,
                 DriverInteropScaffoldTests.cs,README.md};
               docs/reviews/B2-C-01-harness-implementation.md;
               docs/reviews/evidence/B2-C-01/**
Selected protocol revision: none selected; B0-A remains PROVISIONAL (5.8/PackStream 1.0 candidate)
Commands: dotnet build BoltSharp.slnx -c Debug;
          dotnet test BoltSharp.slnx --no-build -c Debug;
          dotnet test BoltSharp.slnx --filter FullyQualifiedName~HarnessProbes;
          dotnet build tests/BoltSharp.DriverIntegrationTests/*.csproj;
          dotnet test  tests/BoltSharp.DriverIntegrationTests/*.csproj;
          B2-B-Q1/B2-B-Q2/B2-A-Q2 reviewer probe projects
Tests: solution 817/0/0 (pre-existing 786 preserved + 31 new harness);
       driver scaffold 4 passed / 10 gated (skipped) / 0 failed;
       reviewer probes 67 + 46 + 18, 0 failed; build 0 warnings / 0 errors
Driver package: Neo4j.Driver 6.4.0 (exact pin, test-only)
Known limitations: no executable driver interoperability; D01–D10 gated; revision provisional;
       B3/B5 missing; no production TCP/TLS transport
Independent reviewer verdict: PENDING (B2-C-Q1)
Owner acceptance: PENDING
Stop: B2-C-01 implemented and verified; no B3/B4/B5 begun; no compatibility claimed
```
