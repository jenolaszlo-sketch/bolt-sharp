# B2-A-Q1 — Independent chunked-message codec qualification

**Status:** COMPLETE — verdict recorded (owner acceptance of B2-A still PENDING)
**Date:** 2026-11-04
**Implementation under review:** `94aebcaa80a387608b710f2de7001e143fb9c096`
**Baseline:** `500f9fc874e6afada2fdc632cd5e17350f877557`
**Canonical branch:** `main`
**Verdict:** **SUPPORTED WITH QUALIFICATIONS** (no functional defect reproduced; four LOW
documentation/usage qualifications and one recorded specification ambiguity)

This report is additive evidence. It does **not** modify the B2-A implementation, its tests,
or the B1 PackStream implementation.

> **Independence disclosure.** The reviewer is the same agent that produced the B2-A
> implementation in the preceding task. This is a structural independence limitation
> (identical to the one recorded in B1-Q1/B1-Q2). It is mitigated, but not removed, by: the
> use of new probes that do **not** reuse the producer's fixtures and that build wire bytes by
> hand; a line-by-line inspection of the production source; and an explicit falsification
> mandate. Author independence is **not** satisfied; owner acceptance must weigh this.

---

## 1. Evidence baseline

Commands run against the canonical checkout:

```text
git rev-parse HEAD                → 94aebcaa80a387608b710f2de7001e143fb9c096
git merge-base --is-ancestor 500f9fc 94aebca → exit 0 (descends from the baseline)
git diff --stat 500f9fc..94aebca  → exactly 10 files, +1243 (5 source/probe/doc + 5 test)
git diff --name-only 500f9fc..94aebca -- src/BoltSharp/PackStream tests/…/PackStream \
    tests/…/Qualification tests/…/Remediation docs/reviews/B1-* → empty (no B1 changes)
git diff --name-only 500f9fc..94aebca -- '*.csproj' '*.props' '*.slnx' → empty (no deps)
git status --porcelain            → empty before qualification probes
```

The 10-file scope matches the producer's receipt: four `src/BoltSharp/Framing/` files, five
`tests/BoltSharp.Tests/Framing/` files, and `docs/reviews/B2-A-chunked-message-codec.md`. No
B1 production or test file, no project file, and no package reference changed.

Reproduced baseline before adding any probe:

```text
dotnet build BoltSharp.slnx -c Debug --no-restore → 0 Warning(s), 0 Error(s)
dotnet test  BoltSharp.slnx --no-build -c Debug   → Failed: 0, Passed: 563, Skipped: 0
```

563 = 509 B1 baseline + 54 framing tests, matching the expected baseline exactly.

## 2. Re-derived protocol correctness

Primary reference: the Bolt message specification, "Chunking"
(https://neo4j.com/docs/bolt/current/bolt/message/#chunking) and its per-version summary
(NOOP empty chunk introduced in 4.1; transmittable in all states while idle from 4.3).
Cross-checked against the official Neo4j .NET driver:

- `Neo4j.Driver/Internal/IO/ChunkReader.cs` — `ConstructMessageAsync` breaks on a zero chunk
  once payload data was read, and otherwise `continue`s (skips) it.
- `Neo4j.Driver/Internal/IO/PipelinedMessageReader.cs` — a leading zero chunk is skipped (the
  `do/while` advances past any `size == 0`), the first non-zero chunk starts the message, and a
  zero chunk ends it.

Independently re-derived and confirmed against the codec:

| Property | Result |
| --- | --- |
| Chunk length is a 2-byte big-endian unsigned integer | Confirmed (writer golden + independent hand-built fixtures) |
| Maximum chunk payload 65,535; larger messages split | Confirmed (200,000 → 65,535×3 + 3,395; 131,070 = 2×65,535) |
| `00 00` terminates the current message | Confirmed |
| A message may span multiple chunks; one chunk never holds two messages | Confirmed |
| Messages are reassembled exactly | Confirmed (reading, fragmentation, pipelining) |
| NOOP (zero chunk before payload) is skipped; zero chunk after payload ends the message | Confirmed; matches both drivers |
| Sequential pipelined messages are read in order | Confirmed |

## 3. Adversarial qualification

New probes in `tests/BoltSharp.Tests/Qualification/B2Q1FramingProbes.cs` (41 cases) plus a
`MaxReadGuardedStream` helper. Fixtures are built by a hand-written `ManualEncode` that does
**not** call the production writer, so the writer and reader are falsified independently.

Coverage and outcome:

| Area | Probes | Outcome |
| --- | --- | --- |
| Big-endian headers at lengths 1/2/255/256/257/65,534/65,535 | 7 | pass |
| Max chunk never exceeded; exact chunk structure for 200,000 | 1 | pass |
| Multiple maximum chunks: 131,069 / 131,070 / 131,071 / 196,605 | 4 | pass |
| Headers split across reads (read sizes 1,2,3,4,5,7,8,13 on a 70,000-byte message) | 8 | pass |
| Consecutive messages with no delimiter | 1 | pass |
| Repeated terminators after a message | 1 | pass |
| Leading NOOP flood (500 NOOPs) | 1 | pass |
| NOOP-only stream → no messages | 1 | pass |
| Zero chunk after payload ends the message (no message merging) | 1 | pass |
| NOOPs interleaved between pipelined messages | 1 | pass |
| Coalesced vs 1-byte-fragmented equivalence | 1 | pass |
| Truncated header / truncated max chunk / position accuracy | 2 | pass |
| Every non-empty strict prefix of a valid message throws truncated | 1 (300 cases) | pass |
| Over-limit rejected before any large read (guarded stream) | 1 | pass |
| Over-limit on the second chunk | 1 | pass |
| Exactly at limit from two chunks | 1 | pass |
| Non-positive message limit rejected (0, −1, int.MinValue) | 3 | pass |
| Async NOOP + fragmentation | 1 | pass |
| Mixed `TryReadMessage` / `ReadMessage` on one reader | 1 | pass |
| `Position` includes NOOP header bytes | 1 | pass |
| Empty message not representable; lone terminator is a NOOP | 1 | pass |
| `WriteNoop` bytes match hand-built fixture and are skipped by the reader | 1 | pass |

All 41 probes pass. No CRITICAL, MAJOR or MODERATE defect was reproduced; the reader and
writer agree with the hand-built reference and with each other on every boundary tested.

## 4. Findings and qualifications

No functional defect was found. The following are LOW-severity documentation/usage
qualifications, recorded for completeness.

- **Q1 (LOW, documentation) — clean EOF is inferred from a zero-byte read.** Both the sync and
  async header reads treat a zero-byte `Read` at a message boundary as end of stream (returning
  `null`/`false`). This is consistent with the .NET `Stream` contract (0 means end of data), but
  a non-blocking `Stream` that returns 0 to mean "no data currently available" would be
  misinterpreted as a clean end and silently terminate framing. The reader contract should
  state that the source stream must only return 0 at end of stream.
- **Q2 (LOW, observation) — the writer does not enforce a maximum message size.** The scope
  calls for a "configurable, enforced maximum message size"; enforcement exists only on the
  reader. The writer will emit a message larger than a peer's configured cap. This is a
  deliberate sender/receiver asymmetry (the receiver owns the resource-safety limit) but should
  be stated explicitly.
- **Q3 (LOW, documentation) — `TryReadMessage(IBufferWriter<byte>)` appends to the
  destination.** It does not clear the writer. The XML summary says "reads ... into
  destination" without stating append semantics; a caller reusing a non-empty buffer must clear
  it first. (The producer's own test depends on append semantics.)
- **Q4 (LOW, resource observation) — no separate chunk-count cap.** Memory is bounded by
  `MaxMessageSize` and by bytes actually received, but a peer may still send a message composed
  of up to `MaxMessageSize` one-byte chunks, costing I/O and CPU proportional to the stream
  length. This is bounded, not a leak, but the bound is on total bytes rather than chunk count.

## 5. Specification ambiguities (recorded separately, not defects)

- **A1 — the end-of-message marker, the NOOP keep-alive, and an empty message are all the same
  bytes (`00 00`).** The specification calls the end marker "individual chunks of size 0" and
  separately defines the NOOP as an empty chunk. Consequently a zero-length message cannot be
  distinguished from a NOOP. The implementation resolves this by adopting the official-driver
  behaviour — skip a zero chunk before any payload, terminate on a zero chunk after payload —
  and by rejecting empty messages in the writer. This is a consistent, documented resolution of
  an under-specified area, not an implementation defect. This report follows the interpretation
  used by both `ChunkReader.cs` and `PipelinedMessageReader.cs`.
- **A2 — "one chunk cannot contain more than one message".** This is a sender-side constraint.
  A receiver cannot detect a sender that places two messages in a single chunk; the framing
  layer legitimately cannot split a chunk. Higher layers (B3) will parse the structure.

## 6. Test accounting and regression

```text
dotnet test BoltSharp.slnx --no-build -c Debug
  → Failed: 0, Passed: 604, Skipped: 0, Total: 604
     filter ~Tests.Framing                 → 54   (producer framing tests)
     filter ~B2Q1FramingProbes             → 41   (this qualification's probes)
     filter ~Golden                        → 285  (B1 golden fixtures unchanged)
     filter !~Tests.Framing & !~B2Q1…      → 509  (B1 baseline unchanged)
```

Accounting: 509 (B1) + 54 (producer framing) + 41 (B2-Q1 probes) = 604, 0 failed, 0 skipped,
0 warnings, 0 errors. The B1 baseline and golden set are unchanged.

## 7. Verdict

**SUPPORTED WITH QUALIFICATIONS.**

The B2-A chunked-message codec correctly implements the Bolt chunking rules that were
independently re-derived from the specification and cross-checked against two official driver
implementations. Independent probes — built from hand-written wire bytes rather than the
producer's fixtures — reproduced no functional defect across chunk boundaries, header/payload
fragmentation, multiple messages, NOOP placement, pipelining, truncation, limit enforcement,
and allocation safety. The qualifications above are documentation/usage clarifications and one
recorded specification ambiguity; none affects wire correctness or resource safety.

Admissibility caveat: the reviewer is not author-independent (see the disclosure at the top);
owner acceptance should account for that. Driver and Browser compatibility remain out of scope
for B2 and are not claimed.
