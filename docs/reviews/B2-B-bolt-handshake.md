# B2-B — Bolt handshake: implementation and qualification handoff

**Status:** IMPLEMENTED — awaiting independent qualification (owner acceptance PENDING)
**Date:** 2026-11-04
**Task:** [B2-B](../tasks/B2-bolt-integration.md) (Bolt handshake), under the governing B2
contract §3 and acceptance criteria §4, authorized to begin by
[B2-A-A1](B2-A-A1-owner-acceptance.md) §9
**Canonical branch:** `main`
**Starting commit:** `a46c916e7f6e72e315504e2d284921d406ee6c74`
**Deliverable commit:** recorded in the final handoff message of this task (this file is part of
that commit, so it cannot contain its own SHA)

This record contains no claim of Neo4j, Neo4j Browser, or `Neo4j.Driver` compatibility. B2-B
implements the handshake layer only: no `HELLO`/`LOGON`/`RUN`/`PULL` or other messages, no
authentication, no session state, no sockets/TLS/WebSocket/application server, no driver
integration, and no NuGet publication. The B0-A compatibility target remains **PROVISIONAL**.

---

## 1. Scope delivered

| File | Purpose |
| --- | --- |
| `src/BoltSharp/Handshake/BoltHandshakeException.cs` | `BoltHandshakeException`, `BoltHandshakeFormatException`, `BoltHandshakeTruncatedException`, `BoltHandshakeProtocolException` |
| `src/BoltSharp/Handshake/BoltProtocolVersion.cs` | Concrete `major.minor` version and Bolt 4.0 style encoding |
| `src/BoltSharp/Handshake/BoltVersionRange.cs` | Bolt 4.3 style minor-version range, expansion and range generation |
| `src/BoltSharp/Handshake/BoltVarInt.cs` | Bounded unsigned 64-bit Manifest v1 VarInt codec |
| `src/BoltSharp/Handshake/BoltHandshakeLimits.cs` | Manifest version-count cap and handshake byte budget |
| `src/BoltSharp/Handshake/BoltHandshakeOptions.cs` | Supported versions, Manifest v1 toggle, offered capabilities |
| `src/BoltSharp/Handshake/BoltHandshakeResult.cs` | Validated outcome and `BoltHandshakeMode` |
| `src/BoltSharp/Handshake/BoltManifestV1.cs` | Manifest marker and offer/selection codec |
| `src/BoltSharp/Handshake/BoltHandshakeNegotiator.cs` | Legacy + Manifest v1 server negotiation over a duplex stream |
| `src/BoltSharp/Handshake/HandshakeByteReader.cs` | Internal bounded exact-read/VarInt reader |
| `tests/BoltSharp.Tests/Handshake/*` | 155 new tests plus a duplex fragmenting test transport and independent fixtures |

No existing file was modified; no `.csproj`/`.props`/`.slnx` change; **no new dependency** in the
core project or the test project. The B1 and B2-A source and tests are unchanged.

## 2. Public API and supported protocol contract

Namespaces/types (all in `src/BoltSharp/Handshake`):

- `BoltHandshakeNegotiator(BoltHandshakeOptions options, BoltHandshakeLimits? limits = null)`
  - `BoltHandshakeResult Negotiate(Stream stream)`
  - `ValueTask<BoltHandshakeResult> NegotiateAsync(Stream stream, CancellationToken)`
  - `BoltHandshakeOptions Options`, `BoltHandshakeLimits Limits`,
    `IReadOnlyList<BoltVersionRange> OfferRanges`, static `ReadOnlyMemory<byte> Magic`.
- `BoltHandshakeOptions(IEnumerable<BoltProtocolVersion> supportedVersions, bool manifestV1Enabled, ulong offeredCapabilities = 0)`
  with `Create(...)`, `SupportedVersions`, `ManifestV1Enabled`, `OfferedCapabilities`, and `Default`.
- `BoltHandshakeLimits(maxManifestVersions = 256, maxHandshakeBytes = 4096)` with `Default`.
- `BoltHandshakeResult` with `Mode`, `Accepted`, `ShouldClose`, `SelectedVersion`,
  `NegotiatedCapabilities`; `BoltHandshakeMode { Legacy, ManifestV1 }`.
- `BoltProtocolVersion(int major, int minor)` with `ToUInt32`, `WriteBytes`, `FromUInt32`.
- `BoltVersionRange(int major, int minor, int range)` with `ToUInt32`, `WriteBytes`, `FromUInt32`,
  `Expand`, `Contains`, `FromVersions`.
- `BoltVarInt` with `MaxEncodedLength = 10`, `Write`, `GetEncodedLength`, `TryRead`, `Read`,
  `ReadAsync`.
- `BoltManifestV1` with `Marker = 0x000001FF`, `MarkerBytes`, `WriteOffer`, `WriteSelection`,
  `ReadOffer`, `ReadSelection`; plus `BoltManifestOffer` and `BoltManifestSelection`.

**Protocol contract (what this layer does):**

1. The handshake is a fixed exchange following the four magic bytes `60 60 B0 17`.
2. The client submits exactly four big-endian 32-bit proposals; version zero is a placeholder.
3. A proposal is a Bolt 4.0 style concrete version (`00 00 minor major`) or a Bolt 4.3 style
   range (`00 range minor major`). The reserved leading byte must be zero; a range may not span
   a major version.
4. Proposals are scanned in client preference order. The first proposal yielding a concrete
   version the server declares supported is selected; within a range the highest supported
   minor is chosen.
5. If nothing matches, the server writes `00 00 00 00` and the result requests closure.
6. If the client offers the manifest marker and Manifest v1 is enabled with at least one
   supported version, the server writes the marker, a VarInt range count, that many Bolt 4.3
   style ranges (major descending, minor descending, maximal contiguous runs), and a VarInt
   capability bitmask, then reads the client's concrete version and capability subset.
7. A manifest selection must be a concrete (not range) version that the server offered, and its
   capability bits must be a subset of the offered bits; otherwise no result is published.

**Selection is always constrained to `BoltHandshakeOptions.SupportedVersions`.** The default
options declare no version and disable Manifest v1.

## 3. Implementation contract

- **Transport independence.** The layer uses only `System.IO.Stream` and `IBufferWriter<byte>`.
  It has no socket, TLS, WebSocket, `System.IO.Pipelines` or ASP.NET dependency and never relies
  on transport frame boundaries. Manifest v1 uses one duplex stream: the offer is written and
  flushed before the selection is read, as on a real connection.
- **Exact reads / pipelining.** Every field is read with an exact-length loop; a VarInt is read
  one byte at a time. Nothing is buffered past the handshake, so a pipelined first message
  remains in the stream.
- **Bounded VarInt.** Least-significant 7-bit group first, high bit as continuation, at most
  ten bytes, and the tenth byte may contribute only bit 63. Overflow and over-long encodings are
  rejected; a truncated VarInt is a deterministic truncation. Non-minimal (zero-padded) encodings
  are accepted because the specification does not require the shortest form.
- **Bounded version list.** The manifest count is validated against
  `BoltHandshakeLimits.MaxManifestVersions` before any array is allocated; the byte reader also
  enforces `MaxHandshakeBytes`, so a declared count cannot drive unbounded allocation.
- **Deterministic errors.** Bad magic, a set reserved byte, a major-spanning range, an
  over-long/overflowing VarInt and an over-budget handshake raise `BoltHandshakeFormatException`;
  an early disconnect raises `BoltHandshakeTruncatedException`; an unoffered version or a
  capability superset raises `BoltHandshakeProtocolException`. No negotiated state is published
  before validation completes.
- **Ownership.** The negotiator never disposes the supplied stream. `ShouldClose` instructs the
  caller to close on a zero-version rejection; an invalid manifest selection throws and the caller
  closes. The stream must follow standard `Stream` semantics (a positive-length read returning
  zero is end of stream).

## 4. Golden wire vectors

Independently defined expected bytes (Bolt handshake specification, "Version negotiation",
"Handshake Manifest v1", "VarInt", "Bolt version 4.3" and "Bolt version 4.0"):

```text
Concrete versions (Bolt 4.0 style, reserved, minor, major):
  1.0 → 00 00 00 01     4.1 → 00 00 01 04     5.7 → 00 00 07 05
  5.8 → 00 00 08 05     6.0 → 00 00 00 06     255.255 → 00 00 FF FF

Ranges (Bolt 4.3 style, reserved, range, minor, major):
  5.8 down to 5.6 → 00 02 08 05     4.4 down to 4.0 → 00 04 04 04
  5.8 only        → 00 00 08 05

Legacy negotiation (specification examples):
  C 60 60 B0 17  00 00 00 01 00 00 00 00 00 00 00 00 00 00 00 00   → S 00 00 00 01
  C 60 60 B0 17  00 00 00 02 00 00 00 01 00 00 00 00 00 00 00 00   → S 00 00 00 02
  C 60 60 B0 17  00 00 00 03 00 00 00 02 00 00 00 01 00 00 00 00   → S 00 00 00 02
  C 60 60 B0 17  00 00 00 03 00 00 00 00 00 00 00 00 00 00 00 00   → S 00 00 00 00 + close
  C ... 00 03 03 04 00 00 01 04 00 00 00 04 00 00 00 03            → S 00 00 01 04

Manifest v1 (specification example):
  C 60 60 B0 17
  C 00 00 01 FF 00 00 04 04 00 00 00 03 00 00 00 02
  S 00 00 01 FF 02 00 02 08 05 00 04 04 04 09
  C 00 00 07 05 08

VarInt:
  01 → 1      7F → 127      FF 82 71 → 1851775
  80 01 → 128   FF 01 → 255   AC 02 → 300
  FF FF FF FF FF FF FF FF FF 01 → 18446744073709551615 (2^64 − 1)
```

Reader golden vectors are asserted against hand-built fixtures by
`tests/BoltSharp.Tests/Handshake/HandshakeTestHelpers.cs`, and the production writers are
checked against those same fixtures, so the reader and writer are falsified independently.

## 5. Negative-test and resource-bound evidence

| Behaviour | Evidence |
| --- | --- |
| Wrong magic → `BoltHandshakeFormatException`, no response written | `HandshakeErrorTests.WrongMagic_ThrowsFormat`, `MalformedMagic_DoesNotWriteAResponse` |
| Truncated magic / proposals / selection → `BoltHandshakeTruncatedException` | `HandshakeErrorTests.*Truncated*`, `HandshakeInteropTests.Negotiate_RequiresFourProposals_TruncatedAfterThree` |
| Set reserved byte in a proposal or selection → format | `LegacyHandshakeTests.MalformedProposalBeforeAMatch_ThrowsFormat`, `HandshakeCodecTests.ReadSelection_RangeValue_ThrowsFormat` |
| Range spanning a major version → format | `BoltVersionRangeTests.FromUInt32_RejectsRangeSpanningMajorVersion`, `LegacyHandshakeTests.RangeProposal_NotSpanningMajor_IsRejectedAsFormat` |
| VarInt overflow (tenth-byte bits, eleventh byte) → format | `BoltVarIntTests.Read_TenthByteOverflow_ThrowsFormat`, `Read_EleventhByteContinuation_ThrowsFormat` |
| Truncated VarInt → truncation | `BoltVarIntTests.Read_TruncatedContinuation_ThrowsTruncated`, `Read_EmptyStream_ThrowsTruncated` |
| Manifest version count over the cap → format before allocation | `HandshakeCodecTests.ReadOffer_TooManyVersions_ThrowsFormatWithoutAllocating` |
| Handshake byte budget exceeded → format | `HandshakeErrorTests.HandshakeExceedingByteBudget_ThrowsFormat`, `HandshakeCodecTests.ReadOffer_ExceedingByteBudget_ThrowsFormat` |
| Unoffered manifest version → protocol, no result | `ManifestHandshakeTests.UnofferedVersion_ThrowsProtocol` |
| Manifest capability superset → protocol, no result | `ManifestHandshakeTests.CapabilitySuperset_ThrowsProtocol` |
| No version match → `00 00 00 00` and `ShouldClose` | `LegacyHandshakeTests.SpecExample_UnsupportedVersion_ReturnsZeroAndCloses`, `HandshakeInteropTests.RejectedHandshake_RequestClosureAndLeavesStreamOpen` |
| Over-limit limits rejected at construction | `HandshakeErrorTests.Limits_RejectNonPositiveValues` |
| Pipelined first message preserved (sync and async, fragmented) | `HandshakeInteropTests.Legacy_/Manifest_PipelinedFirstMessage_*` |
| VarInt split across one-byte reads | `BoltVarIntTests.Read_FragmentedOneByteAtATime_*`, `ManifestHandshakeTests.MultiByteCapabilities_SplitAcrossReads_AreDecoded` |
| Sync/async equivalence | `HandshakeInteropTests.SyncAndAsync_ProduceTheSame*` |

Allocations are bounded by received bytes and by the validated version count; no count-driven
array is allocated before its cap check.

## 6. Test results (exact command output)

Environment: Windows `10.0.26200`, `win-x64`, .NET SDK `10.0.401`, target `net8.0`, installed
.NET 8 runtime `8.0.31`, MSBuild `18.9.11`. Commands run from the canonical root:

```text
dotnet build BoltSharp.slnx -c Debug
  → Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test BoltSharp.slnx --no-build -c Debug
  → Passed! Failed: 0, Passed: 759, Skipped: 0, Total: 759

dotnet test BoltSharp.slnx --no-build -c Debug --filter "FullyQualifiedName~Tests.Handshake"
  → Passed! Failed: 0, Passed: 155, Skipped: 0, Total: 155     ← new B2-B tests

dotnet test docs/reviews/probes/B2-A-Q2/B2AQ2.Probes.csproj -c Debug
  → Passed! Failed: 0, Passed: 18, Skipped: 0, Total: 18        ← separate Q2 probes
```

| Set | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| B1 historical | 509 | 0 | 0 |
| B2-A producer framing | 54 | 0 | 0 |
| B2-A-Q1 independent probes | 41 | 0 | 0 |
| **B2-B new handshake tests** | **155** | **0** | **0** |
| **Full solution** | **759** | **0** | **0** |
| B2-A-Q2 independent reviewer probes (separate project) | 18 | 0 | 0 |
| **Grand total** | **777** | **0** | **0** |

The new handshake tests by class: `BoltVarIntTests` 40, `BoltVersionRangeTests` 24,
`BoltProtocolVersionTests` 21, `LegacyHandshakeTests` 16, `HandshakeCodecTests` 15,
`ManifestHandshakeTests` 13, `HandshakeErrorTests` 11, `HandshakeInteropTests` 10,
`BoltHandshakeOptionsTests` 5.

The previously accepted **604-test solution baseline is preserved unchanged** (509 + 54 + 41),
and the separately executed Q2 probes remain 18/18. No existing test file was modified.

## 7. Supported guarantees

- Exact Bolt legacy handshake framing: magic, four big-endian proposals, ordered selection, and
  the `00 00 00 00` zero-version rejection.
- Bolt 4.0 and 4.3 version encodings (concrete and range), range expansion, and deterministic
  contiguous-range generation.
- Manifest v1 offer/selection with bounded VarInts, bounded version lists, capability subset
  enforcement, and rejection of unoffered versions and invalid selections.
- Exact-read behaviour that preserves a pipelined first message, for both sync and async paths.
- Deterministic typed failures for malformed, truncated, over-budget and invalid-negotiation
  input.
- Sync/async equivalence for the whole handshake, including fragmentation.

## 8. Known limitations and explicit non-guarantees

- **Version honesty.** `BoltHandshakeOptions.Default` advertises **no** protocol version and
  disables Manifest v1, so a default handshake is always rejected. Advertising Bolt 5.8 (or any
  version) is an explicit configuration used by tests and by later milestones; it is **not** a
  claim that the Bolt 5.8 messaging protocol is implemented. B3+ must supply that.
- **Handshake only.** No messages, authentication, session state, cursors, sockets, TLS,
  WebSocket, Kestrel, reference server or backend contract is implemented or claimed.
- **No driver compatibility.** No Neo4j, Browser, or `Neo4j.Driver` interoperability is claimed.
  That requires an executed integration test at a later executable milestone.
- **No rollback.** After a handshake exception the caller must discard the connection; the layer
  neither resynchronises nor rewinds the stream. A stream must follow standard EOF semantics.
- **Byte budget interaction.** A manifest offer must fit within `MaxHandshakeBytes` **and** its
  range count within `MaxManifestVersions`; raising one without the other can reject a
  legitimate offer. The defaults (256 ranges, 4096 bytes) are consistent.
- **Non-minimal VarInts accepted.** The codec bounds a VarInt to ten bytes but does not require
  the shortest encoding.
- **Malformed-proposal policy.** A non-zero reserved byte is treated as malformed and raises a
  format exception when that proposal is reached; a matching earlier proposal still wins because
  proposals are scanned in preference order. This is deterministic and documented, not an
  under-specified acceptance.
- **No concurrency/flood policy** beyond the byte and count caps; no timeout, traffic budget or
  automatic resynchronisation.
- In-memory Windows/net8.0 evidence only; no cross-platform, deployment, packaging or
  production-security acceptance.

## 9. Self-review summary

- Verified the version golden encodings from the specification (`00 00 01 04` = 4.1,
  `00 00 08 05` = 5.8) and that the major version is the least-significant byte.
- Verified the manifest offer bytes against the specification example exactly
  (`00 00 01 FF 02 00 02 08 05 00 04 04 04 09`).
- Verified VarInt golden vectors (`FF 82 71` = 1851775) and all boundaries including 2^64 − 1.
- Verified exact reads preserve a pipelined first message by parsing it back through the B2-A
  `BoltMessageReader` after the handshake, sync and async, fragmented and not.
- Verified no count-driven allocation occurs before the cap check.
- Verified the negotiator does not dispose the stream and that failures do not write a response.
- Found and corrected test-authoring errors during development (two wrong range golden literals
  and two tests that offered a supported version while intending rejection); no production change
  resulted.

## 10. Independent qualification handoff

The qualifier must **not** be the implementer and must treat the code as potentially incorrect.
See the separate [B2-B-Q1 qualification handoff](B2-B-Q1-qualification-handoff.md) for the full
entry criteria, falsification targets and required evidence. Summary:

1. Confirm ancestry from `a46c916` and that only additive `Handshake/` source and tests changed.
2. Re-derive the golden vectors from the Bolt handshake specification sections named in §4 and
   verify them independently of the production encoders.
3. Attempt to falsify: magic, four-proposal framing, preference ordering, range expansion and
   generation, zero-version rejection, the manifest offer/selection bytes, VarInt overflow and
   truncation, capability subset enforcement, unoffered-version rejection, byte/count budgets,
   fragmentation and pipelined-message preservation.
4. Re-run the full solution (759) and the separate Q2 probes (18) and confirm 0 warnings/errors.
5. Report concrete findings by severity with file/line and reproduction; passing happy-path
   tests is insufficient.

Admissibility: compilation, screenshots and descriptions are not evidence. Any
driver-compatibility claim remains out of scope and requires an executed integration test at a
later milestone.

## 11. Acceptance receipt

```text
Task: B2-B
Baseline commit: a46c916e7f6e72e315504e2d284921d406ee6c74
Changed files: src/BoltSharp/Handshake/{BoltHandshakeException,BoltProtocolVersion,BoltVersionRange,
                 BoltVarInt,BoltHandshakeLimits,BoltHandshakeOptions,BoltHandshakeResult,
                 BoltManifestV1,BoltHandshakeNegotiator,HandshakeByteReader}.cs;
               tests/BoltSharp.Tests/Handshake/{HandshakeDuplexStream,HandshakeTestHelpers,
                 BoltProtocolVersionTests,BoltVarIntTests,BoltVersionRangeTests,
                 BoltHandshakeOptionsTests,LegacyHandshakeTests,ManifestHandshakeTests,
                 HandshakeCodecTests,HandshakeErrorTests,HandshakeInteropTests}.cs
Selected protocol revision: handshake codec (revision-honest); B0-A revision still PROVISIONAL,
                 no version advertised by default
Commands run: dotnet build BoltSharp.slnx -c Debug;
              dotnet test BoltSharp.slnx --no-build -c Debug;
              dotnet test docs/reviews/probes/B2-A-Q2/B2AQ2.Probes.csproj -c Debug
Tests: passed 759 solution / 0 failed / 0 skipped (new 155 handshake);
       separate Q2 probes 18/0/0; grand total 777
New golden fixtures: Bolt handshake spec legacy examples, Manifest v1 example, VarInt table,
               4.0/4.3 version encodings
Known limitations: handshake-only; no version advertised by default; no messages/auth/session/
               transport/driver compatibility; no rollback after exception
Source commit: recorded in the final handoff message
Independent reviewer verdict: PENDING
Owner acceptance: PENDING
```
