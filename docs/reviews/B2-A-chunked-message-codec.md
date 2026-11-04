# B2-A — Chunked message codec: implementation and qualification handoff

**Status:** IMPLEMENTED — awaiting independent qualification (owner acceptance PENDING)
**Date:** 2026-11-04
**Task:** [B2-A](../tasks/B2-bolt-integration.md) §7 (first bounded B2 task)
**Canonical branch:** `main`
**Baseline commit:** `500f9fc874e6afada2fdc632cd5e17350f877557`
**Deliverable commit:** recorded in the final handoff message of this task (this file is part
of that commit, so it cannot contain its own SHA)

This record contains no claim of Neo4j, Neo4j Browser, or `Neo4j.Driver` compatibility. B2-A
implements the chunked-message framing layer only: no handshake (B2-B), no driver integration
(B2-C), no messages, state, backend, sockets or TLS.

---

## 1. Scope delivered

| File | Purpose |
| --- | --- |
| `src/BoltSharp/Framing/BoltFramingLimits.cs` | Per-chunk cap (fixed 65,535) and configurable max message size (default 64 MiB) |
| `src/BoltSharp/Framing/BoltFramingException.cs` | `BoltFramingException` / `BoltFramingFormatException` / `BoltFramingTruncatedException` |
| `src/BoltSharp/Framing/BoltMessageWriter.cs` | Writes messages as 2-byte big-endian chunks + `00 00` terminator; `WriteNoop` |
| `src/BoltSharp/Framing/BoltMessageReader.cs` | Reassembles messages from an ordered byte stream; NOOP-aware; size-bounded |
| `tests/BoltSharp.Tests/Framing/*` | 54 new tests plus a fragmenting-stream test transport |

No existing file was modified; no `.csproj`/`.props`/`.slnx` change; **no new dependency** in
the core project or the test project.

## 2. Implementation contract

- **Transport independence.** The codec runs over `System.IO.Stream` and `IBufferWriter<byte>`
  only. It uses no socket, TLS, `System.IO.Pipelines` or ASP.NET dependency, and never relies
  on transport frame boundaries.
- **Writer.** Each message is split into chunks of at most 65,535 bytes (a length is a 16-bit
  big-endian unsigned integer) and terminated by `00 00`. Output is deterministic. An empty
  message is rejected, because a lone `00 00` cannot be distinguished from a NOOP. `WriteNoop`
  emits a keep-alive chunk.
- **Reader.** Reads a 2-byte big-endian length; `0` ends the message when payload bytes have
  been accumulated, otherwise it is a NOOP and is skipped. Empty messages are never returned;
  a clean end of stream between messages yields `null` (or `false`).
- **Bounded memory.** The reassembled message may not exceed `Limits.MaxMessageSize`. The
  limit is checked *before* a chunk payload is read, so an over-limit declared length cannot
  drive a large allocation. Allocation is driven by bytes actually received.
- **Failure contract.** Premature end of stream raises `BoltFramingTruncatedException` with
  `Position` reflecting bytes consumed; an over-limit message raises
  `BoltFramingFormatException`. There is no rollback or resynchronisation: after a framing
  exception the caller must discard the reader and the connection.

## 3. NOOP / empty-chunk semantics (authoritative basis)

Source: Bolt message specification, "Chunking"
(https://neo4j.com/docs/bolt/current/bolt/message/#chunking) and the per-version summary
(NOOP introduced 4.1, allowed in all states while idle from 4.3). The behaviour was
cross-checked against two official driver implementations:

- `neo4j-dotnet-driver` `Internal/IO/ChunkReader.cs` — a zero chunk after data ends the
  message; a zero chunk with no data is skipped.
- `neo4j-dotnet-driver` `Internal/IO/PipelinedMessageReader.cs` — leading zero chunks are
  skipped; the first non-zero chunk begins the message and a zero chunk ends it.

Adopted semantics: a zero-length chunk before any payload byte is a NOOP (skip); a zero-length
chunk after payload bytes terminates the message. Empty messages are never surfaced.

## 4. Golden wire-format evidence

Independently defined expected bytes (Bolt message spec examples and derived boundaries):

```text
message [AB]                     → 00 01 AB 00 00
message 00..0F (16 bytes)        → 00 10 00 01 .. 0F 00 00
message 256×5A                   → 01 00 5A×256 00 00
message 65,535 bytes             → FF FF <65535> 00 00
message 65,536 bytes             → FF FF <65535> 00 01 <1> 00 00
NOOP (WriteNoop)                 → 00 00
```

Reader golden vectors include the specification's two-chunk example (a 20-byte message split
16 + 4) and pipelined/NOOP-interleaved sequences. Round-trip tests are supplemented by these
independently specified byte vectors (round-trip alone is not treated as sufficient).

Boundary payload lengths exercised: `0` (NOOP), `1`, `255`, `256`, `65,535`, `65,536`,
`70,000`. Fragmentation exercised with fixed split sizes `1,2,3,7,64,999,1000,∞` and six
pseudo-random seeds.

## 5. Test results (exact command output)

.NET SDK `10.0.401`, target `net8.0`:

```text
dotnet build BoltSharp.slnx -c Debug --no-restore
  → Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test BoltSharp.slnx --no-build -c Debug
  → Passed! Failed: 0, Passed: 563, Skipped: 0, Total: 563

dotnet test … --filter "FullyQualifiedName~Tests.Framing"
  → Passed! Failed: 0, Passed: 54, Skipped: 0, Total: 54        ← new B2-A tests

dotnet test … --filter "FullyQualifiedName!~Tests.Framing"
  → Passed! Failed: 0, Passed: 509, Skipped: 0, Total: 509      ← B1 baseline unchanged

dotnet test … --filter "FullyQualifiedName~Golden"
  → Passed! Failed: 0, Passed: 285, Skipped: 0, Total: 285      ← golden fixtures unchanged
```

**New test count: 54** (8 writer golden, 35 reader, 11 reader error). **Regression baseline:
509 → 509** (unchanged), 0 warnings, 0 errors.

## 6. Supported guarantees

- Exact Bolt chunked framing for the writer: 2-byte big-endian lengths, ≤ 65,535 per chunk,
  `00 00` terminator, deterministic multi-chunk splitting.
- Exact reconstruction of messages across arbitrary input fragmentation and pipelining.
- NOOP keep-alive chunks skipped before a message; a zero chunk after payload ends the message.
- Configurable total-message bound enforced before oversized allocation; `Position` tracking.
- Deterministic typed failures for truncated headers/payloads/terminators and over-limit
  messages.

## 7. Known limitations / explicit non-guarantees

- Empty messages are not representable (indistinguishable from a NOOP); the writer rejects
  them. This is a property of the Bolt chunked encoding, not a defect.
- No rollback after a framing exception; the reader/connection must be discarded (consistent
  with the B1 reader failure contract).
- A NOOP flood or many tiny chunks is bounded in memory but consumes I/O/CPU proportional to
  the bytes received; no separate chunk-count cap is enforced beyond the message-size cap.
- The async header read allocates a small (2-byte) array per header; not a correctness issue.
- No handshake, message decoding, state machine, backend, transport or driver behaviour is
  implemented or claimed.

## 8. Self-review summary

- Verified the reader never over-reads a chunk: `GetSpan`/`GetMemory` are sliced to the exact
  remaining length before `Read`.
- Verified the size check runs before payload reads (`OversizedSingleChunk_…` receives only a
  2-byte header yet throws `Format`, not `Truncated`).
- Verified both sync and async paths against truncation, fragmentation and EOF.
- Verified the writer's chunk boundaries at 65,535/65,536 and big-endian headers at 256.
- Verified NOOP skipping, leading/trailing/inter-message NOOPs, and that B1 and golden suites
  are unchanged. One authoring error in a test assertion (65,536 vs 65,535 slice) was found and
  corrected; no production change resulted from it.

## 9. Independent qualification handoff

The qualifier must **not** be the implementer and must treat the code as potentially incorrect.

1. Confirm ancestry: this deliverable descends from `500f9fc`, and the B1 tree is unchanged
   except for additive `Framing/` source and tests.
2. Re-derive the golden vectors from the Bolt message-spec "Chunking" section and the two
   driver implementations named in §3, and verify the bytes in §4 independently.
3. Attempt to falsify: chunk boundaries `0/1/65535/65536`, split points including inside a
   header, truncation at every boundary, NOOP placement (leading, between, trailing), repeated
   terminators, over-limit messages, and a header-only over-limit declaration.
4. Verify there is no unbounded allocation: an over-limit declared length must fail without
   reading the payload.
5. Re-run the full suite and confirm 563 total, 509 baseline, 285 golden, 0 warnings/errors.
6. Report concrete findings by severity with file/line and reproduction; do not accept on
   happy-path tests alone.

Admissibility: passing tests are necessary but not sufficient. Any driver-compatibility claim
remains out of scope for B2 and requires an executed integration test at a later milestone.

## 10. Acceptance receipt

```text
Task: B2-A
Baseline commit: 500f9fc874e6afada2fdc632cd5e17350f877557
Changed files: src/BoltSharp/Framing/{BoltFramingException,BoltFramingLimits,BoltMessageReader,BoltMessageWriter}.cs;
               tests/BoltSharp.Tests/Framing/{FragmentingStream,FramingTestHelpers,BoltMessageReaderTests,
               BoltMessageReaderErrorTests,BoltMessageWriterTests}.cs
Selected protocol revision: Bolt chunked framing (revision-independent); B0-A revision still PROVISIONAL
Commands run: dotnet build BoltSharp.slnx -c Debug --no-restore; dotnet test BoltSharp.slnx --no-build -c Debug
Tests: passed 563/563 (new 54 framing; baseline 509 unchanged; golden 285), 0 failed, 0 skipped
New golden fixtures: Bolt message spec "Chunking" examples (single-chunk 16-byte, two-chunk 20-byte); boundary vectors derived per §4
Known limitations: empty messages unrepresentable; no rollback after framing exception; no chunk-count cap beyond the message-size cap
Source commit: recorded in the final handoff message
Independent reviewer verdict: PENDING
Owner acceptance: PENDING
```
