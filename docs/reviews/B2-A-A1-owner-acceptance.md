# B2-A-A1 — B2-A owner acceptance and closeout

**Status:** ACCEPTED WITH QUALIFICATIONS (owner decision recorded 2026-11-04)
**Date:** 2026-11-04
**Task:** B2-A-A1 — owner acceptance and closeout of B2-A (chunked message codec)
**Canonical branch:** `main`
**Starting commit:** `65738c3892b7f0f8117b42a1c55e24ef8af29541`
**Documentation-correction commit (C1):** `6d1196a3cc43babdcd7d1a1123af381289dc4f51`
**B2-A-A1 closeout commit (C2):** recorded in the final handoff message of this task (this file
is part of that commit, so it cannot contain its own SHA)

This record closes B2-A. It contains **no implementation** beyond the documentation wording
correction in §4, and it makes **no claim** of Neo4j, Neo4j Browser, or official `Neo4j.Driver`
compatibility. Driver compatibility is not established (§9).

---

## 1. Owner decision

**Owner decision: ACCEPTED WITH QUALIFICATIONS.**

The owner reviewed the B2-A deliverable against the B2-A-Q2 independent requalification
(`65738c3`), the B2-A-R1 documentation closure (`6f68548`), the B2-A-Q1 independent
qualification (`4dbd75a`), and the B2-A implementation handoff (`94aebca`), and records:

1. B2-A is accepted with qualifications, within the framing-only scope of §7.
2. The single LOW documentation observation reported by Q2 (`Q2-OBS1`) is corrected by a
   documentation-only wording change (§4). No additional independent qualification cycle is
   required for it.
3. B2-A-Q1 findings Q1–Q3 remain **verified closed**; Q4/A1/A2 remain accepted limitations
   (§5, §6).
4. **B2-B (Bolt handshake) implementation is authorized** to begin on canonical `main`, under
   the governing B2 task's §3 contract and §4 acceptance criteria.
5. **Driver compatibility is not established** and remains deferred to the later executable
   integration milestone (§9).
6. **NuGet package publication is NOT authorized** (§9).
7. Push of canonical `main` after verified closeout is authorized as a normal (non-force)
   fast-forward push.

**Owner acceptance vs. independent recommendation.** The independent B2-A-Q2 reviewer's verdict
was **SUPPORTED WITH QUALIFICATIONS** and its recommendation was that "owner acceptance of B2-A
is supported with these explicit qualifications" (`docs/reviews/B2-A-Q2-independent-requalification.md`
§8). That is a *reviewer recommendation*; it is not the owner decision. The owner decision above
is separate, is recorded here, and accepts B2-A only for the bounded scope in §7 while carrying
forward every qualification the reviewer and Q1 raised. Recording acceptance does not modify,
supersede, or rewrite the Q1, R1, or Q2 reports, which remain the historical evidence.

---

## 2. Commit identities

Exact Git object identities (full SHA-1), independently read from this checkout:

| Stage | Commit | Tree |
| --- | --- | --- |
| B2 scope/contract baseline | `500f9fc874e6afada2fdc632cd5e17350f877557` | `a836682b352b4a045499a400ee3a8a5bd69e7476` |
| B2-A implementation | `94aebcaa80a387608b710f2de7001e143fb9c096` | `937109ce87cfec7d5eb43cad6fcf84c2d6ca861e` |
| B2-A-Q1 independent qualification | `4dbd75a86df8a0f691665bc05d72391dc218ccd1` | `cf7ea5b5eb75742d1e5e3389c324a36f1e9bcfba` |
| B2-A-R1 documentation closure | `6f685487707ab86598415723ad4175ce1893d2b3` | `9d7a80b719adee0a5a31c953fe4112c9649563f2` |
| B2-A-Q2 independent requalification | `65738c3892b7f0f8117b42a1c55e24ef8af29541` | `bb3b8178e272d54144863db7f61ec51e4110e177` |
| B2-A-A1 documentation correction (C1) | `6d1196a3cc43babdcd7d1a1123af381289dc4f51` | `90fe595a27e2e784ff397f6928439407ba2eb964` |

Ancestry is linear and direct: `500f9fc` → `94aebca` → `4dbd75a` → `6f68548` → `65738c3`
→ `6d1196a`. Each commit after the baseline has the preceding one as its sole parent. The
reviewed entry's `src` tree is `bb30c2203f2491b71d65669bd3a5e78601b12633` and its `tests` tree is
`fba785e9bf1bf71ea833317acc636ec11e2fc267`. The B2-A implementation added exactly 10 files
(+1243): four `src/BoltSharp/Framing/` sources, five `tests/BoltSharp.Tests/Framing/` files, and
the implementation handoff; no B1 source/test, project, or dependency file changed.

---

## 3. Q1–Q3 verified closed

Q2 independently re-derived the framing rules from the Bolt message specification and cross-checked
two official driver readers, then verified each R1 closure against the updated authoritative XML
contracts:

| Closure | Classification | Basis |
| --- | --- | --- |
| Q1 — end-of-stream semantics | **VERIFIED** | A positive-length read returning zero is treated as EOF at a message boundary (`null`/`false`), and as truncation after payload has begun (`BoltFramingTruncatedException`). The reader class contract states the standard `Stream` requirement. |
| Q2 — `MaxMessageSize` is receiver-side | **VERIFIED** | The reader enforces the cap immediately after each complete header, before payload reads or destination requests, for both initial and cumulative length; the writer documents that it does not enforce the peer's cap. |
| Q3 — `TryReadMessage(IBufferWriter<byte>)` appends | **VERIFIED** | Both `Try` methods preserve existing destination content and append via `GetSpan`/`GetMemory` and `Advance`; the summaries state the append/preservation contract and the caller's fresh/cleared-buffer responsibility. |

R1 established these closures by changing only `///` documentation lines in exactly three source
files (`BoltFramingLimits.cs`, `BoltMessageReader.cs`, `BoltMessageWriter.cs`); removing the XML
documentation lines from each version leaves byte-identical remaining lines. Q2 confirmed the
R1 test diff is empty and no executable statement, signature, test, project, or dependency
changed. No regression of Q1–Q3 was reproduced during A1.

---

## 4. Q2 LOW wording correction and exact disposition

**Q2-OBS1 — LOW, documentation precision (non-blocking).** At the reviewed entry,
`src/BoltSharp/Framing/BoltMessageWriter.cs`, the `WriteNoop` member summary said a receiver skips
the keep-alive "without ending the current message". Read as an unconditional in-message claim,
that is inaccurate: once payload has begun, the same `00 00` bytes terminate the message. The
class-level writer/reader contracts and the R1 record already explain the distinction; R1 left this
older member summary unchanged. Q2 reproduced the behaviour (`00 01 AA 00 00 00 01 BB 00 00`
yields `[AA]` then `[BB]`) and confirmed A1 is implemented correctly — the phrase, not the code,
was misleading.

**Disposition: CLOSED by documentation-only correction (C1, `6d1196a`).** The `WriteNoop` summary
now states that it emits a keep-alive **between messages**, that a receiver skips a zero-length
chunk that precedes any payload, and that the same `00 00` terminates the current message once
payload bytes have begun. The change is limited to the `///` summary block:

- 4 added and 2 removed lines, all XML documentation (`///`); the filtered non-comment diff is
  empty.
- No executable statement, public signature, wire format, limit, golden fixture, test, project,
  or dependency changed; the full test suite is unchanged (622/0/0, §8).
- No production behaviour changed and no new qualification cycle was required. This matches the
  owner decision that a focused wording correction is sufficient for a LOW documentation item.

---

## 5. Accepted limitations carried forward (Q4/A1/A2)

These are accepted, correctly scoped limitations — not unfulfilled B2-A behaviour — and are
preserved unchanged:

- **Q4 — no independent chunk-count cap.** Memory stays bounded by `MaxMessageSize` and by bytes
  actually received, but a peer may send a message composed of many one-byte chunks and cost I/O
  and CPU proportional to the received stream. No separate flood/chunk-rate policy is promised by
  B2-A.
- **A1 — the terminator, the NOOP, and an empty message are the same bytes (`00 00`).**
  Resolved per official driver behaviour: a zero chunk before any payload is a NOOP (skipped); a
  zero chunk after payload ends the message; empty messages are not surfaced and the writer
  rejects them. This is a documented resolution of an under-specified area, not a defect.
- **A2 — sender-side message isolation.** The framing receiver cannot detect a sender that places
  two messages inside a single chunk; a chunk cannot be split by the framing layer. Semantic
  message validation (one valid Bolt message per framed payload) remains B3 work, not a B2-A
  receiver guarantee.

---

## 6. Verification results

Environment: Windows `10.0.26200`, `win-x64`, .NET SDK `10.0.401`, target `net8.0`, installed
.NET 8 runtime `8.0.31`, MSBuild `18.9.11`. Commands run from the canonical root, against the
accepted source tree (`main` at C1 plus this record):

```text
dotnet build BoltSharp.slnx -c Debug --no-restore
dotnet test  BoltSharp.slnx --no-build -c Debug
dotnet test  docs/reviews/probes/B2-A-Q2/B2AQ2.Probes.csproj -c Debug
# group filters:
dotnet test BoltSharp.slnx --no-build -c Debug --filter "FullyQualifiedName~Tests.Framing"
dotnet test BoltSharp.slnx --no-build -c Debug --filter "FullyQualifiedName~B2Q1FramingProbes"
dotnet test BoltSharp.slnx --no-build -c Debug \
  --filter "FullyQualifiedName!~Tests.Framing&FullyQualifiedName!~B2Q1FramingProbes"
```

Discovered counts (derived from executed runs, not assumed):

| Set | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| B1 historical | 509 | 0 | 0 |
| B2-A producer framing | 54 | 0 | 0 |
| B2-A-Q1 independent probes | 41 | 0 | 0 |
| **Full solution** | **604** | **0** | **0** |
| B2-A-Q2 independent reviewer probes (separate project) | 18 | 0 | 0 |
| **Grand total** | **622** | **0** | **0** |

Both the solution and the reviewer-probe project built with **0 warnings, 0 errors**. The 18 Q2
reviewer probes are committed and discoverable under `docs/reviews/probes/B2-A-Q2/` (project,
probe source, and verifier) and are excluded from the solution by design; their captured TRX and
logs are preserved under `docs/reviews/evidence/B2-A-Q2/`. The counts match the Q2 report
(604 solution + 18 reviewer) exactly, confirming no executable or test drift during closeout.

---

## 7. B2-A acceptance scope and explicitly excluded guarantees

**Accepted scope (B2-A).** Over an abstract ordered byte-stream (`System.IO.Stream` /
`IBufferWriter<byte>`), with no socket, TLS, pipeline, or ASP.NET dependency:

- Bolt chunked transfer framing: 2-byte big-endian chunk lengths, ≤ 65,535 bytes per chunk,
  `00 00` end-of-message terminator, deterministic multi-chunk splitting.
- Exact reconstruction across arbitrary input fragmentation, back-to-back and pipelined
  messages, and NOOP interleaving.
- A configurable total-message cap distinct from the per-chunk cap, enforced before an oversized
  allocation, with recorded `Position`.
- Deterministic typed failures for truncated header/payload/terminator and over-limit messages;
  no rollback or resynchronisation (the reader/connection is discarded after a framing failure).

**Explicitly excluded (not established by this acceptance):**

- No handshake, message schemas, connection/session state, authentication, backend contract,
  cursors, sockets, `SslStream`, Kestrel, WebSocket, or real Neo4j server behaviour.
- No Neo4j Browser or official `Neo4j.Driver` compatibility — see §9.
- No finalized protocol revision: the B0-A target remains **PROVISIONAL** and the live client
  probe remains `NOT RUN`.
- No independent chunk-count/flood-rate policy, total-traffic budget, internal timeout,
  concurrency-safety guarantee, or automatic resynchronisation.
- No zero-allocation or allocation-only-after-arrival claim; caller-owned destination data and
  `IBufferWriter` capacity reservation are outside the cap.
- No cross-platform, deployment, packaging, or production-security acceptance; this is in-memory
  Windows/net8.0 framing evidence only.

---

## 8. Preservation evidence

Verified **byte-for-byte unchanged** across the A1 closeout (empty diffs between the reviewed
entry and C2 for all historical records and for every non-corrected source/test file):

```text
git diff 65738c3..<C2> -- docs/reviews/B2-A-Q1-chunked-codec-qualification.md   → empty
git diff 65738c3..<C2> -- docs/reviews/B2-A-R1-qualification-closure.md        → empty
git diff 65738c3..<C2> -- docs/reviews/B2-A-Q2-independent-requalification.md  → empty
git diff 65738c3..<C2> -- docs/reviews/B2-A-chunked-message-codec.md           → empty
git diff 65738c3..<C2> -- tests                                              → empty
git diff 65738c3..<C2> -- src -- ':!src/BoltSharp/Framing/BoltMessageWriter.cs' → empty
git diff 65738c3..<C2> -- docs/reviews/evidence docs/reviews/probes           → empty
git diff 65738c3..<C2> -- '*.csproj' '*.props' '*.slnx'                        → empty
```

The A1 closeout therefore changes exactly two paths: the `WriteNoop` XML summary in
`src/BoltSharp/Framing/BoltMessageWriter.cs` (documentation only) and this record. Q1, R1, and Q2
reports, the B2-A implementation handoff, all prior tests, all reviewer probes/evidence, and every
other production file are preserved as historical evidence.

---

## 9. Authorization for subsequent work

- **B2-B (Bolt handshake) implementation is AUTHORIZED** to begin on canonical `main`, against the
  governing B2 task's contract: magic `60 60 B0 17`, exactly four 32-bit big-endian version
  proposals, legacy selection response (a version, or `0` then close), and Manifest v1
  (marker `00 00 01 FF`, supported-version list, VarInt capabilities, client selection, pipelined
  first message) per the B0-A decision. B2-B is **not implemented** by this closeout.
- **Handshake-level protocol support is not driver compatibility.** B2-B builds only the
  handshake layer. **Driver compatibility is NOT established** and must not be claimed until an
  executed integration test against a pinned `Neo4j.Driver` version records environment, OS, TLS
  mode, command, and output (B2-C scaffold, B3/B5 protocol path). The B0-A revision remains
  provisional.
- **NuGet package publication is NOT authorized.** No package or other published artifact may be
  produced from this decision.
- The obsolete `feature/protocol-spec` branch is not to be pushed; only canonical `main` is.
- Push of `main` is authorized as a normal, non-force fast-forward.

---

## 10. Acceptance receipt

```text
Task: B2-A-A1 (owner acceptance and closeout of B2-A)
Baseline (B2 scope): 500f9fc874e6afada2fdc632cd5e17350f877557
Implementation: 94aebcaa80a387608b710f2de7001e143fb9c096
B2-A-Q1: 4dbd75a86df8a0f691665bc05d72391dc218ccd1
B2-A-R1: 6f685487707ab86598415723ad4175ce1893d2b3
B2-A-Q2 (reviewed entry / starting commit): 65738c3892b7f0f8117b42a1c55e24ef8af29541
B2-A-A1 documentation correction (C1): 6d1196a3cc43babdcd7d1a1123af381289dc4f51
B2-A-A1 closeout (C2): recorded in the final handoff message
Changed files: src/BoltSharp/Framing/BoltMessageWriter.cs (XML doc only);
               docs/reviews/B2-A-A1-owner-acceptance.md (this record)
Selected protocol revision: Bolt chunked framing (revision-independent); B0-A revision still PROVISIONAL
Commands run: dotnet build BoltSharp.slnx -c Debug --no-restore;
              dotnet test BoltSharp.slnx --no-build -c Debug;
              dotnet test docs/reviews/probes/B2-A-Q2/B2AQ2.Probes.csproj -c Debug
Tests: passed 622 / failed 0 / skipped 0 (509 B1 + 54 B2-A producer + 41 B2-A-Q1 + 18 B2-A-Q2)
Independent reviewer verdict: B2-A-Q2 SUPPORTED WITH QUALIFICATIONS (65738c3)
Owner acceptance: ACCEPTED WITH QUALIFICATIONS (this record)
Q1-Q3 closures: VERIFIED CLOSED (Q2 §3; C1 documentation correction)
Q2-OBS1: CLOSED by documentation-only correction (C1)
Q4/A1/A2: PRESERVED as accepted limitations
B2-B handshake implementation: AUTHORIZED
Driver compatibility: NOT ESTABLISHED (later executable integration milestone)
NuGet publication: NOT AUTHORIZED
```
