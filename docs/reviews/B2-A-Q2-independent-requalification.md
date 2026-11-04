# BOLTSHARP-B2-A-Q2 — Independent requalification

**Date:** 2026-11-04 (Asia/Manila)

**Verdict:** **SUPPORTED WITH QUALIFICATIONS**

**Recommendation:** B2-A is suitable for owner acceptance within its framing-only scope, with the residual limitations and LOW documentation observation below.

**Owner acceptance:** PENDING. Stop at the B2-A owner decision gate.

## 1. Provenance and review identity

The canonical checkout is `D:\projects\bolt-sharp`, on `main`. Its entry worktree was clean. The following are exact Git object identities, independently read from the local object database:

| Stage | Commit | Tree |
| --- | --- | --- |
| Scope baseline | `500f9fc874e6afada2fdc632cd5e17350f877557` | `a836682b352b4a045499a400ee3a8a5bd69e7476` |
| B2-A implementation | `94aebcaa80a387608b710f2de7001e143fb9c096` | `937109ce87cfec7d5eb43cad6fcf84c2d6ca861e` |
| Q1 qualification | `4dbd75a86df8a0f691665bc05d72391dc218ccd1` | `cf7ea5b5eb75742d1e5e3389c324a36f1e9bcfba` |
| R1 and reviewed entry | `6f685487707ab86598415723ad4175ce1893d2b3` | `9d7a80b719adee0a5a31c953fe4112c9649563f2` |

Each row after the first has the preceding commit as its sole parent. Thus ancestry is direct, not merely inferred from subjects or timestamps. The reviewed entry's `src` tree is `bb30c2203f2491b71d65669bd3a5e78601b12633`; its `tests` tree is `fba785e9bf1bf71ea833317acc636ec11e2fc267`.

**Independent reviewer declaration.** This reviewer did not implement B2-A, author Q1, or perform R1. This is a separate review session using fresh fixtures, a new transport, and new destination instrumentation. Q1's conclusions were challenged rather than adopted. Q1 itself discloses that its author also implemented B2-A; its label alone therefore does not establish author independence. This review supplies separate session-level review evidence. The repository's shared generic Git author identity is not evidence of a distinct human reviewer or model vendor.

Read: the governing specification §6.2/§9/§11, task ledger B2-A/B3, B2 scope/contract, implementation handoff, Q1 report and all 41 probes, R1 record, all four framing source files and their XML contracts, and producer framing tests/helpers.

R1 changes exactly three source files (`BoltFramingLimits.cs`, `BoltMessageReader.cs`, `BoltMessageWriter.cs`) and adds its remediation record. Removing XML documentation lines from each source version produces byte-identical remaining lines. No executable statement, signature, test, project, or dependency changes. R1's test diff is empty. This independently confirms documentation-only remediation.

The reproducible [verifier](probes/B2-A-Q2/verify_evidence.py) checks direct parents, trees, the exact R1 file set, XML-only source differences, result accounting, and Git blob identity for **all 58 entry-tracked files**. Its [provenance manifest](evidence/B2-A-Q2/provenance.json) includes every historical file's blob and LF-normalized SHA-256 hashes for reviewer sources and captured execution evidence, stable across Git line-ending conversion. Historical implementation, qualification, and remediation records remain untouched. This report binds claims to the reviewed entry; the additive qualification commit is identified by the handoff and Git history, avoiding a self-referential commit hash in this file.

## 2. Authoritative framing rules, independently re-derived

Primary source, retrieved on the review date: [Neo4j Bolt message specification, Chunking](https://neo4j.com/docs/bolt/current/bolt/message/#chunking).

- Headers encode payload length as an unsigned, big-endian 16-bit value; the maximum is 65,535 bytes, excluding the header.
- A message can span multiple chunks; a zero-size chunk marks its end. Payload contents do not establish framing boundaries.
- Chunking applies separately to messages, and one chunk must not contain multiple messages.
- Bolt 4.1+ supports empty NOOP chunks for keep-alive. The specification's inter-message NOOP example places an additional zero chunk after a message terminator.

The specification's split-message example contains an inconsistent explanatory header label (`00 01` versus the actual `00 10` bytes). This review follows the normative length rule and actual wire bytes, rather than copying that label.

The NOOP interpretation was also independently inspected in official driver source at exact commit `0c935b29dc0099ed0a026d2ef498b0c25e6b16f1`:

- [ChunkReader.cs](https://github.com/neo4j/neo4j-dotnet-driver/blob/0c935b29dc0099ed0a026d2ef498b0c25e6b16f1/Neo4j.Driver/Neo4j.Driver/Internal/IO/ChunkReader.cs), blob `8f8a999ddc2f01d203e4635d1982ca68a9243d48`: `ConstructMessageAsync` ends a constructed message on zero after data; leading zero chunks do not create messages. Its buffer-exhaustion handling is driver-specific and is not imported as a B2-A EOF contract.
- [PipelinedMessageReader.cs](https://github.com/neo4j/neo4j-dotnet-driver/blob/0c935b29dc0099ed0a026d2ef498b0c25e6b16f1/Neo4j.Driver/Neo4j.Driver/Internal/IO/PipelinedMessageReader.cs), blob `50600d3515c14f7579781adc110e7b8c2672cee9`: skips leading zeros, then collects nonzero chunks until zero.

These are source cross-checks, not executed driver integration or compatibility evidence.

## 3. Q1–Q3 closure verification

| Closure | Classification | Independent result |
| --- | --- | --- |
| Q1 — EOF | **VERIFIED** | Both paths return null/false only for EOF before a new header at a message boundary, including after complete NOOPs. A one-byte header, an admitted header with missing payload, incomplete payload, or missing/partial terminator throws `BoltFramingTruncatedException`. Position counts actual consumed bytes. |
| Q2 — receiver limit | **VERIFIED** | Reader checks `chunkLength > Limits.MaxMessageSize - total` immediately after the complete header and before payload reads or destination requests. This handles initial and cumulative overflow without overflowing an addition. Writer has no receiver-limit input or enforcement; it emits a four-byte payload that a reader limited to three rejects at position 2. |
| Q3 — append | **VERIFIED** | Both Try methods preserve existing destination content and append through `GetSpan`/`GetMemory` and `Advance`. Neither clears or assumes ownership of the caller's buffer. The sync summary explicitly requires a fresh/cleared buffer for message-only capture; the async summary explicitly states append and preservation, with the same resulting caller responsibility. |

Q1's updated class contract explicitly requires standard positive-length `Stream` read semantics. A zero return meaning temporary unavailability is unsupported and was not counted as a defect. The new transport returns zero only at actual EOF. The EOF paragraph's shorthand about payload having begun does not erase the surrounding explicit mid-header/mid-message failure contract: even a complete nonzero header followed by immediate EOF is truncation.

Q2's cap is per message, not per call history or destination length. A prefilled destination already larger than the cap remains usable; three subsequent messages each get a fresh payload counter, even when APIs alternate sync/async. Tests instrument both the source and destination: rejected `FF FF` headers make **zero** new payload-storage requests and no payload read. After an accepted two-byte chunk, an over-limit next header preserves that prefix and adds no storage request. Convenience reads have a small initial allocation (`min(256, MaxMessageSize)`), not an allocation based on the rejected declaration.

Q3 does **not** promise transactional output. Each successful partial payload read advances the destination immediately. On truncation, limit rejection, `IOException`, or cancellation, pre-existing content and already-advanced payload remain. Completed earlier messages also remain. No failing reader was reused to infer recovery. The documented framing-exception discard requirement remains applicable. Destination implementations control their own storage and clearing; a reader cannot clear an arbitrary `IBufferWriter<byte>` through that interface.

## 4. Q4 / A1 / A2 assessment

**Q4 — accepted, correctly scoped.** There is no independent chunk-count cap. A 257-byte message in 257 one-byte chunks succeeds at limit 257; at limit 256 the 257th header is rejected at position 770, before its payload. Nonzero chunks are bounded indirectly by the byte cap. Leading NOOPs have no finite count bound, but do not accumulate message payload; CPU/I/O remain proportional to received traffic. XML and historical records disclose the limitation. A separate flood policy was not promised for B2-A and is not required by this review.

**A1 — accepted, correctly scoped, with a LOW wording qualification (§7).** A boundary zero is skipped, a zero after payload ends the message, and embedded `00 00` payload bytes remain payload. Empty messages are not surfaced and the writer rejects them. This agrees with the inspected driver behavior. Historical planning language about ignoring NOOPs during a message must be read in this context; it does not authorize indistinguishable in-message zero markers or require treating repeated boundary zeros as malformed.

**A2 — accepted, correctly scoped.** The new opaque-payload fixture `00 04 B0 02 B0 0F 00 00` returns one four-byte framing payload, despite containing two apparent zero-field PackStream message structures. B2-A cannot infer semantic message boundaries from opaque payload. The writer frames one caller-supplied payload per invocation; validation that it contains exactly one valid Bolt message belongs to B3. Q1's table saying one chunk never holds two messages is supportable as a sender rule, not as demonstrated receiver validation. This review makes no such receiver claim.

None conceals a reproduced B2-A executable contract violation.

## 5. New adversarial probes

Sources: [FramingProbes.cs](probes/B2-A-Q2/FramingProbes.cs) and [isolated project](probes/B2-A-Q2/B2AQ2.Probes.csproj). They are outside the production and historical test trees and are not added to the solution. Package versions match existing test infrastructure; core dependencies remain unchanged.

| Probe family (method name prefix) | Cases | New evidence |
| --- | ---: | --- |
| `MixedApis` | 2 | Alternate sync/async Try and allocating APIs on one reader; prefilled destination, exact per-message cap reset, zero-valued payload, pipelining, NOOPs, and repeated EOF with exact positions. |
| `EverySecondMessagePrefix` | 2 | All 12 prefixes of a NOOP-prefixed, two-chunk second message after a completed first message; destination content and position checked at each failure/boundary in both modes (24 internal scenarios). |
| `RejectedHeaders` | 2 | Initial/cumulative `FF FF` rejection with a payload-read tripwire and destination allocation instrumentation; convenience read also checked. |
| `TransportExceptions` | 2 | Same `IOException` instance propagated at seven exact offsets each, including fragmented headers and partial payload; prefix and position retained (14 internal scenarios). |
| `Cancellation` | 5 | Caller token reaches genuinely suspending async transport; cancellation at offsets 0, 1, 2, 5, 8 preserves committed prefix and position, with no sync fallback. |
| `TinyChunks` | 2 | Byte-cap admission/rejection over 257 one-byte chunks; storage requests and failure boundary counted. |
| `FramingDoesNotValidate` | 2 | Co-packed message-like structures remain one opaque framing payload. |
| `WriterCanExceedReceiverLimit` | 1 | Producer/receiver asymmetry checked against literal expected wire and receiver-side storage/read boundary. |
| **Total** | **18** | **18 passed, 0 failed, 0 skipped.** |

Most cases have a sync/async pair. Unlike Q1's predominantly sync size/split matrix, these inspect destination state at failures, actual async suspension and token propagation, injected transport failures, mixed sync/async state transitions, and both sides of the allocation admission boundary. Header fragmentation uses a new varying partial-read schedule, not the producer's transport or Q1's encoder. Reader fixtures are literal hex or independently assembled one-byte chunks. Only the explicitly named writer-asymmetry test uses the production writer, and its output is checked against literal hex.

Cancellation and transport exceptions are observed propagation behavior, not promises of rollback, reader reuse, bounded cancellation latency, timeout enforcement, or forced interruption of a stream that ignores its token. The XML documents no such stronger guarantees.

## 6. Regression and reproducibility

Environment: Windows `10.0.26200`, `win-x64`, .NET SDK `10.0.401`, MSBuild `18.9.11`, target `net8.0`, installed .NET 8 runtime `8.0.31`. Exact output is in [environment.txt](evidence/B2-A-Q2/environment.txt).

Run from the canonical root:

```powershell
dotnet build BoltSharp.slnx -c Debug --no-restore
dotnet test BoltSharp.slnx --no-build -c Debug --logger 'trx;LogFileName=baseline.trx' --results-directory docs/reviews/evidence/B2-A-Q2/baseline-results
dotnet test docs/reviews/probes/B2-A-Q2/B2AQ2.Probes.csproj -c Debug --logger 'trx;LogFileName=reviewer.trx' --results-directory docs/reviews/evidence/B2-A-Q2/reviewer-results
dotnet build docs/reviews/probes/B2-A-Q2/B2AQ2.Probes.csproj -c Debug --no-restore
python docs/reviews/probes/B2-A-Q2/verify_evidence.py
```

| Set | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| B1 historical | 509 | 0 | 0 |
| B2-A producer | 54 | 0 | 0 |
| B2-A-Q1 | 41 | 0 | 0 |
| **Full unchanged solution** | **604** | **0** | **0** |
| **Separate Q2 reviewer project** | **18** | **0** | **0** |

Both solution and reviewer builds succeeded with **0 warnings, 0 errors**. The verifier derives group counts from executed TRX test identities, not estimates from attributes. Logs: [baseline build](evidence/B2-A-Q2/baseline-build.txt), [baseline tests](evidence/B2-A-Q2/baseline-test.txt), [reviewer build](evidence/B2-A-Q2/reviewer-build.txt), [reviewer tests](evidence/B2-A-Q2/reviewer-test.txt). Raw TRX files are preserved beside those logs and explicitly included despite the repository's general TRX ignore rule.

One reviewer harness setup error occurred before tests executed: its project reference initially traversed one directory too far. That relative path was corrected; the initial compiler output is preserved in [reviewer-initial-project-path-error.txt](evidence/B2-A-Q2/reviewer-initial-project-path-error.txt). It was not a production failure. There were no failed behavioral probes to preserve.

## 7. Findings by severity

**CRITICAL / MAJOR / MODERATE:** none reproduced.

**Q2-OBS1 — LOW, documentation precision; non-blocking qualification.** At reviewed entry, `src/BoltSharp/Framing/BoltMessageWriter.cs:74`, `WriteNoop`'s local summary says a receiver skips it “without ending the current message”. Taken as an unconditional in-message claim, that is inaccurate: once payload has begun, the same zero marker terminates the message. The class-level writer/reader contracts and R1 record correctly explain the distinction; R1 did not change this older member summary.

Minimal wire reproduction: `00 01 AA 00 00 00 01 BB 00 00` produces `[AA]`, then `[BB]`, not `[AA,BB]`. The `MixedApis` probe likewise checks that zero-valued payload is preserved but the following zero header completes the first message at position 8. Source inspection independently shows both reader loops returning true for zero when accumulated payload is positive. Thus A1 is correctly implemented; this is a misleading local phrase, not a reason to treat arbitrary in-message NOOPs as legal. Normal `WriteMessage` calls emit their own complete terminator before `WriteNoop` can be invoked sequentially.

Suggested future documentation clarification: state that `WriteNoop` emits a keep-alive **between messages**, and that the same bytes terminate a message after payload. No production or historical documentation was changed in this qualification. This observation does not invalidate Q1–Q3 closures or require behavioral remediation before owner acceptance.

## 8. Residual limitations and owner gate

- The cap bounds new logical payload per message, not a byte-exact heap budget. Caller-owned pre-existing destination data is outside it; `IBufferWriter` implementations may reserve excess capacity. For an admitted chunk, a storage request of up to its remaining validated length can occur before all its bytes arrive; convenience APIs allocate and copy. No claim of zero allocation or allocation only after every byte arrives is made.
- No independent chunk-count/NOOP-rate policy, total traffic budget, internal timeout, concurrency safety, or automatic resynchronisation is established.
- After framing failure, discard the reader and connection. Cancellation/transport failures can leave output and stream consumption partial; safe resume was not promised or qualified. A stream must honor standard EOF semantics, and cancellation responsiveness depends on the transport.
- Empty messages are not exposed; semantic validation and one-message-per-payload enforcement remain B3 work.
- This is in-memory Windows/net8.0 framing evidence. It establishes no handshake, listener, socket/TLS, Browser, real-server, driver interoperability, cross-platform, or production security acceptance. The protocol revision decision remains provisional.

**Recommendation:** owner acceptance of B2-A is supported with these explicit qualifications. The remaining local NOOP wording is LOW and does not conceal an executable framing defect. Q1, Q2, and Q3 are VERIFIED. Q4/A1/A2 remain scoped limitations rather than unfulfilled B2-A behavior. Only additive reviewer evidence is committed locally on canonical `main`; nothing is pushed or published. B2-B is not begun, and owner acceptance is not recorded by this report.
