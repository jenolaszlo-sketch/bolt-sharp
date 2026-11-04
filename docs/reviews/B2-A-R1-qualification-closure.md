# B2-A-R1 — Qualification documentation closure

**Status:** COMPLETE — documentation-only remediation of B2-A-Q1 findings Q1–Q3
**Date:** 2026-11-04
**Task:** B2-A-R1 (authorized by the owner decision below)
**Canonical branch:** `main`
**Starting commit:** `4dbd75a86df8a0f691665bc05d72391dc218ccd1`
**Remediation commit:** recorded in the final handoff message of this task (this file is part
of that commit, so it cannot contain its own SHA)

This is a separate additive record. It does **not** modify or rewrite the historical
qualification report `docs/reviews/B2-A-Q1-chunked-codec-qualification.md`, the B2-A
implementation record `docs/reviews/B2-A-chunked-message-codec.md`, or any production
behaviour.

## 1. Owner decision

- B2-A-Q1 verdict **SUPPORTED WITH QUALIFICATIONS** was accepted.
- **Documentation-only** remediation of Q1–Q3 was authorized.
- **No production behaviour changes** were authorized.

No owner acceptance of B2-A itself is recorded here; B2-A remains at its independent
qualification gate.

## 2. Authoritative documentation changes

The authoritative contract for B2-A is the XML documentation on the framing types. Only
`///` documentation lines were changed; no executable statement, public signature, or
protocol behaviour was touched.

### Q1 — Reader end-of-stream semantics

`src/BoltSharp/Framing/BoltMessageReader.cs` (class-level contract) gains an
**End-of-stream contract (B2-A-R1, finding Q1)** paragraph:

> The source stream must follow the standard `Stream` contract, in which a positive-length
> read that returns zero indicates the end of the stream. A stream that returns zero to signal
> temporary unavailability rather than end of stream is **not** supported: the reader would
> treat that zero-byte read as a clean end of stream between messages and stop. At a message
> boundary a zero-byte read yields `null`/`false`; after payload bytes have begun it raises
> `BoltFramingTruncatedException`.

This matches the existing implementation (a zero-byte read is treated as EOF at a boundary
and as truncation mid-message); it only makes the stream requirement explicit.

### Q2 — `MaxMessageSize` is a receiver-side limit

- `src/BoltSharp/Framing/BoltFramingLimits.cs` (class contract) gains a **Scope (B2-A-R1,
  finding Q2)** paragraph: `MaxMessageSize` is enforced by `BoltMessageReader`; the writer
  emits whatever message it is given and does not enforce the receiving peer's configured
  limit.
- The `MaxMessageSize` property summary was expanded with the same statement.
- `src/BoltSharp/Framing/BoltMessageWriter.cs` (class contract) gains a **Size limits (B2-A-R1,
  finding Q2)** paragraph stating that the writer does not enforce the receiving peer's
  `BoltFramingLimits.MaxMessageSize`.
- `BoltMessageReader`'s "Message size limit" paragraph was annotated: the limit is a
  receiver-side safeguard and the writer does not enforce it.

### Q3 — `TryReadMessage` appends to the destination

`src/BoltSharp/Framing/BoltMessageReader.cs`: the summaries of `TryReadMessage` and
`TryReadMessageAsync` now state that the message bytes are **appended** to the supplied
`IBufferWriter<byte>`, that existing destination content is preserved (the destination is not
cleared), and that the caller must supply a fresh or cleared buffer to capture only this
message (B2-A-R1, finding Q3). The implementation already appended; only the contract text
changed.

## 3. Accepted limitations preserved (not remediated)

These are recorded as accepted limitations and were intentionally left unchanged in behaviour.

- **Q4 — no separate chunk-count limit.** The `BoltMessageReader` "Memory" paragraph now notes
  that there is no separate limit on the number of chunks in a message: memory stays bounded by
  `MaxMessageSize` and by bytes received, but a peer can still send a message composed of many
  tiny chunks and cost I/O proportional to the bytes received.
- **A1 — zero-length chunk interpretation.** A zero-length chunk before any payload is a NOOP
  and is skipped; a zero-length chunk after payload terminates the message. This follows the
  official driver behaviour (`ChunkReader.cs`, `PipelinedMessageReader.cs`) and is already
  documented in the `BoltMessageReader` and `BoltMessageWriter` contracts.
- **A2 — sender-side message isolation.** The framing receiver cannot detect a sender that
  places more than one message inside a single chunk; a chunk cannot be split by the framing
  layer. Deferred to the message layer (B3).

## 4. Preservation evidence

- Changed files: `src/BoltSharp/Framing/BoltMessageReader.cs`,
  `src/BoltSharp/Framing/BoltFramingLimits.cs`, `src/BoltSharp/Framing/BoltMessageWriter.cs`,
  and this record. No test file, no project file, and no other source file changed.
- Every changed line in the three source files is an XML-documentation (`///`) line; a filtered
  diff of non-comment changed lines is empty.
- Public types and member signatures are unchanged (no member added, removed, or re-signed).
- Protocol behaviour is unchanged: the same code paths produce the same bytes and exceptions.
- The historical Q1 report and the B2-A implementation record are untouched.

## 5. Build and test results

.NET SDK `10.0.401`, target `net8.0`:

```text
dotnet build BoltSharp.slnx -c Debug --no-restore → 0 Warning(s), 0 Error(s)
dotnet test  BoltSharp.slnx --no-build -c Debug   → Failed: 0, Passed: 604, Skipped: 0
```

604 = 509 B1 baseline + 54 B2-A framing tests + 41 B2-A-Q1 probes. Baseline unchanged
(expected 604 passing, 0 failures).

## 6. Finding disposition summary

| Finding | Severity | Disposition |
| --- | --- | --- |
| Q1 reader EOF semantics | LOW (doc) | CLOSED by XML contract clarification (§2) |
| Q2 `MaxMessageSize` receiver-side | LOW (obs) | CLOSED by XML contract clarification (§2) |
| Q3 `TryReadMessage` append semantics | LOW (doc) | CLOSED by XML contract clarification (§2) |
| Q4 no chunk-count limit | LOW (obs) | PRESERVED as accepted limitation, documented (§3) |
| A1 zero-length chunk interpretation | ambiguity | PRESERVED, follows official drivers, documented (§3) |
| A2 sender-side message isolation | scope | PRESERVED, deferred to B3 (§3) |

All six B2-A-Q1 findings/observations are accounted for: Q1–Q3 closed by documentation, Q4/A1/A2
preserved as accepted limitations.

## 7. B2-A-Q2 entry readiness

The B2-A-Q1 verdict's documentation qualifications are closed; no production behaviour or test
changed, and the 604-test baseline is green. B2-A is ready for an independent B2-A-Q2
qualification pass that re-checks Q1–Q3 against the updated authoritative XML documentation and
confirms Q4/A1/A2 remain accepted.

**Stop condition:** this task stops at the B2-A-Q2 independent qualification gate. B2-B is not
begun, nothing is pushed, and no package is published.
