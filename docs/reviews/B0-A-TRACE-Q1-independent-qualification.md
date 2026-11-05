# B0-A-TRACE-Q1 — Independent qualification of the official-driver handshake probe

**Status:** COMPLETE — independent qualification delivered; owner acceptance PENDING
**Date:** 2026-11-05
**Task:** B0-A-TRACE-Q1 (independent qualification of [B0-A-TRACE-01](B0-A-TRACE-01-driver-handshake-probe.md)),
resumed under [B0-A-TRACE-Q1-RESUME-01](../tasks/B0-A-protocol-reconnaissance.md) owner authorization
**Canonical branch:** `main` — qualified HEAD `76058e5086e5d7c7f90eff54da9fb533a6155f76`,
tree `fdd16cb773be21582a094522270c7bfc4ac67321`

This is an independent reviewer record. It does not modify producer implementation, producer
reports, historical evidence or accepted protocol contracts, and records no owner acceptance.

---

## 1. Source and evidence identity verification (Q1-01)

| Check | Result |
| --- | --- |
| HEAD / tree at qualification time | `76058e5` / `fdd16cb` — exactly the pinned identities |
| Ancestry | `5984bf2^ = 9abf5e3`; `76058e5^ = 5984bf2` (linear; verified) |
| Implementation change-set `9abf5e3 → 5984bf2` | exactly 6 files: 5 new `HandshakeTrace/*` + modified `DriverTestSettings.cs` |
| Evidence change-set `5984bf2 → 76058e5` | exactly 32 files (review doc + evidence dir), no other paths |
| Production `src/BoltSharp`, `BoltSharp.slnx`, `Directory.Build.props` | byte-identical baseline→HEAD (`git diff` exit 0) |
| Historical B1/B2 reviews, probes and evidence | untouched (exit 0 across all paths) |
| Networking/session scan of `src/BoltSharp` | no socket/TLS/WebSocket/message-session code; 4 grep hits are doc-comment prose ("has no socket…") |
| Producer evidence manifest | **30/30 verified from the git object store (byte-faithful `git cat-file blob`) and 30/30 from the working tree**; no unlisted or missing files (the directory contains exactly the 30 manifest entries plus `sha256.json` itself) |
| Producer TRX independently parsed | solution 826/0/0; B2-A-Q2 18; B2-B-Q1 67; B2-B-Q2 46; B2-C-Q2 40; driver 37/0/10; trace 2/0/0 — all match the producer report |

## 2. Reviewer identity and independence statement

- **Reviewing session:** GLM-5.3-Flash (provider `nvidia`), OpenCode harness, a distinct session
  from the B0-A-TRACE-01 implementation/recovery sessions.
- **Participation in implementation:** none. This session authored only
  `docs/reviews/probes/B0-A-TRACE-Q1/**`, `docs/reviews/evidence/B0-A-TRACE-Q1/**` and this report.
- **Independence status: INDEPENDENCE UNVERIFIED.** The Git author identity is shared across
  producer and reviewer commits and proves nothing about model or session identity. Per repository
  precedent (B2-B-Q2 §2, B2-C-Q2 §2), all findings below are executable technical evidence, not a
  claim of author-independent signoff.
- **Provenance of the earlier halt:** this qualification was first halted on 2026-11-05 ~13:50 UTC
  by an active concurrent writer (preserved at
  `C:\Users\Laszlos\AppData\Local\Temp\opencode\B0-A-TRACE-Q1-staged-state-20261105T135013Z`);
  B0-A-TRACE-RECOVERY-02 restored the baseline before this resumption (its receipt is preserved
  outside the repository, by owner direction, at
  `D:\projects\bolt-sharp-archives\B0-A-TRACE-Q1-incident-02\RECOVERY-RECEIPT.md`).

## 3. Exclusive-writer gate (R0) and deferred amendments (R1)

Two gate snapshots (15:06:31 / 15:08:20 UTC, 90 s apart): HEAD/tree pinned, status/staged/
unstaged/untracked all zero, exactly one worktree, producer manifest 30/30, no repository writes
in 10+ minutes, no `dotnet`/`testhost` processes. Quiescence was observational only; ownership was
additionally protected by continuous `git status` monitoring during the work.

Deferred-amendment decisions (owner, RESUME-01): all three contested amendments (rewritten report,
broadened runner filter, regenerated evidence) **not adopted**. This qualification therefore
treats `76058e5`'s committed evidence as the sole producer baseline; the reviewer's own analysis
below adopts the OBSERVED / SPECIFIED / INFERRED / UNKNOWN vocabulary in its own terms. The
regenerated 13:36 evidence (corroborating only) independently reproduced all four handshake
byte files byte-identically; the broadened runner changed only TRX aggregation (13 = 11 + 2).

## 4. Official `Neo4j.Driver` 6.4.0 provenance (Q1-02)

- Exact pin in `tests/BoltSharp.DriverIntegrationTests/BoltSharp.DriverIntegrationTests.csproj`
  (`<PackageReference Include="Neo4j.Driver" Version="6.4.0" />`), resolved
  `"Neo4j.Driver/6.4.0"` in `project.assets.json`.
- Resolved package DLL `neo4j.driver/6.4.0/lib/net8.0/Neo4j.Driver.dll`:
  `FileVersion 6.4.0.0`, SHA-256 `C0F34E70…4CF` (matches the recorded value).
- Invocation path: the authentic producer probes construct the driver through
  `DriverTestSettings.CreateDriver` — the project's single `GraphDatabase.Driver` call site
  (enforced by B2-C-Q2's structural probe) — and call `VerifyConnectivityAsync()` over
  `bolt://127.0.0.1:<ephemeral>` with `AuthTokens.None`.
- **No synthetic substitution:** the producer trace's client bytes are read from the live
  `NetworkStream` inside the connection handler; there is no injection point for canned bytes.
  Reviewer controls T01/T02 (§7) demonstrate the capture reflects actual wire bytes.

## 5. Independent protocol trace analysis (Q1-03/Q1-04/Q1-05)

The reviewer decoded the committed evidence hex files with an independently written decoder
(probe T03) and reproduced the entire experiment against a **reviewer-owned listener implemented
from the published Bolt specification** — sharing no code with the producer's listener or
negotiator (probes T04/T05, `IndependentListener.cs`).

### 5.1 OBSERVED — raw bytes and structure

| Item | Value (both producer evidence and reviewer reproduction) |
| --- | --- |
| Bolt magic | `60 60 B0 17` |
| Client proposals, in order | `00 00 01 FF` (Manifest v1), `00 08 08 05` (range 5.8–5.0), `00 02 04 04` (range 4.4–4.2), `00 00 00 03` (Bolt 3.0) |
| Manifest server offer | `00 00 01 FF` `01` `00 00 08 05` `00` — marker, VarInt(1 range), 5.8, VarInt(capabilities 0x0) — **10 bytes** |
| Driver manifest selection | `00 00 08 05` `00` — Bolt 5.8, capability subset 0x0 (single-byte VarInt, continuation bit clear) |
| Legacy server response | `00 00 08 05` (exactly 4 bytes) |
| Post-handshake message 1 | chunked frame, body **155 bytes**, structure `0xB1` (tiny struct, 1 field), signature byte `0x01` |
| Post-handshake message 2 | chunked frame, body **15 bytes**, `0xB1`, 1 field, signature byte `0x6A`, **sent before any server response** (pipelined) |
| Post-handshake totals | 159 + 19 = **178 bytes**, identical in producer evidence and reviewer reproduction |
| Driver outcome | `Neo4j.Driver.ServiceUnavailableException` (expected: the listeners observe then close without answering) |

Byte counts and framing boundaries verified independently: the manifest handshake ends at client
byte 25 (20 preamble + 5 selection), the legacy handshake at byte 20; the next bytes parse as
chunk headers `00 9B` / `00 0F` with `00 00` terminators. No bytes were lost or double-attributed
(probe T09: a single coalesced write of both frames yields identical attribution to separate
delayed sends — probe T08 — and to byte-by-byte fragmentation — probe T07).

### 5.2 SPECIFIED — what the Bolt specification requires (met)

- Magic `60 60 B0 17`; exactly four big-endian 32-bit proposals; zero as placeholder (handshake spec).
- First-match-wins preference ordering; legacy response is one 32-bit version (4.0 style).
- Manifest v1: marker `00 00 01 FF`, VarInt range count, 4.3-style ranges, VarInt capabilities;
  client replies 4.0-style version plus a capability subset.
- Message signatures `0x01` (HELLO) and `0x6A` (LOGON) per the message-signature registry;
  chunked framing (2-byte big-endian chunk length, `00 00` terminator).

### 5.3 INFERRED — not directly established by captured bytes

- The **names** HELLO and LOGON are inferred from the signature bytes plus the published registry;
  the bodies were not decoded (by design) and their field semantics were not validated.
- "The driver accepted the selected version" is inferred from **continuation** (it emitted
  protocol messages after the selection); server-side acceptance alone would not prove it.
- The pipelined second message is inferred to be authentication-related from its `0x6A` signature
  and 15-byte length under `AuthTokens.None`; no authentication semantics were exercised.

### 5.4 UNKNOWN / scope limits

- No HELLO/LOGON SUCCESS was ever sent; **no authentication succeeded and no message/session
  semantics were validated**.
- "No Bolt 6.x proposal" is scoped to `Neo4j.Driver` 6.4.0 in this configuration on this OS; it
  does not generalize to other drivers, configurations, or future releases.
- TLS/`bolt+s://`, routing, RESET/GOODBYE/RUN/PULL behaviour: not probed.

## 6. Authentic versus synthetic distinction

All authentic-driver probes (producer `HandshakeTraceProbeTests`, reviewer T04/T05/T06) use the
real package over a real loopback socket. All negative/boundary probes use
`SyntheticHandshakeClient` or raw `TcpClient` peers whose bytes are never attributed to the
driver; the report and evidence keep the two groups separate.

## 7. Test results and negative controls (Q1-09)

Reviewer-owned probes (`docs/reviews/probes/B0-A-TRACE-Q1/`, 14 tests, **14 passed**):

| Probe | Verifies |
| --- | --- |
| T01 | **Non-fabrication control:** a connected client that sends nothing produces no handshake (no magic, no proposals, no messages) — the producer trace cannot exist without real traffic |
| T02 | **Capture fidelity:** distinctive synthetic proposals are reflected byte-for-byte in the producer trace (no hardcoded proposals) |
| T03 | Committed evidence hex files decoded by the reviewer's independent decoder: magic, proposal order/ranges, manifest offer/selection, legacy response — all conform to the reviewers' own spec-derived encodings |
| T04 | Real driver vs reviewer's from-spec listener (Manifest v1): selection 5.8 + caps 0x0; handshake ends at byte 25; HELLO(155)+pipelined LOGON(15); `ServiceUnavailableException` |
| T05 | Real driver vs reviewer's listener (legacy): continuation after the 4-byte selection response; handshake ends at byte 20; same messages |
| T06 | Producer capture stops exactly at the handshake boundary (25/10 bytes); post-handshake observation carries structural metadata only (no `byte[]` properties on the observation type) |
| T07 | Byte-by-byte fragmented message → correct attribution (155/HELLO) |
| T08 | Two messages with a real inter-message gap → order preserved |
| T09 | Coalesced single-write pipelining → identical attribution (178 bytes) |
| T10 | Bounded connections + bounded, exception-free disposal with a pending connection |
| T11 | 4100-byte handshake flood → deterministic `FORMAT_ERROR`, no server bytes, bounded capture |
| T12 | Message-cap overrun (2000-byte chunk vs 1024 cap) → `FORMAT_ERROR` before payload read |
| T13 | Listener disposal is idempotent |
| T14 | Package provenance (pin, resolution, DLL hash) |

Regression reproduction (reviewer-run, TRX in `docs/reviews/evidence/B0-A-TRACE-Q1/trx/`):
solution **826/0/0**; B2-A-Q2 **18**; B2-B-Q1 **67**; B2-B-Q2 **46**; B2-C-Q2 **40**; driver
project **37 passed / 0 failed / 10 gated skips** (D01–D10 remain gated); authentic producer
probes **2/2** fresh reproduction with all four handshake byte files **byte-identical** to the
committed producer evidence. The superseded B2-C-Q1 C6 probe was neither run nor rewritten.

Commands (from repository root; environment: Windows 11 build 10.0.26200, win-x64, .NET SDK
10.0.401, net8.0/.NET 8.0.31 runtime): `dotnet build BoltSharp.slnx -c Debug`;
`dotnet test BoltSharp.slnx --no-build -c Debug`; the four probe csproj runs; the driver project
run; the filtered authentic run with `BOLTSHARP_TRACE01_EVIDENCE_DIR` set to the reviewer's
*absolute* reproduction directory; and the reviewer probe run. Full console outputs and TRX files
are under `docs/reviews/evidence/B0-A-TRACE-Q1/`.

> Reproducibility note: `BOLTSHARP_TRACE01_EVIDENCE_DIR` and TRX `ResultsDirectory` values must
> be **absolute** paths; relative values resolve against the test host's bin directory.

## 8. Evidence sanitization findings (Q1-07)

- The producer capture stops at the handshake boundary (probe T06: 25 client / 10 server bytes on
  the manifest path); post-handshake reads are counted, never stored.
- The persisted trace JSON contains only structural fields (status, lengths, marker, field count,
  signature hex/name, flags); no `byte[]` property exists on the observation type.
- Credential-pattern scan (`password|secret|credential|Authorization|Bearer|principal…`) across
  all reviewer evidence, the fresh reproduction, and every TRX: **no hits**. Long-hex payload
  scan of the trace JSONs: **no hits**.
- No authentication tokens or unredacted post-handshake bodies exist in any committed or
  reviewer-generated artifact.

## 9. Concurrent-authoring incident closure assessment (Q1-08)

- Both incident-era implementations were preserved before consolidation and re-verified during
  recovery (17/17 and 20/21-file manifests, 0 mismatches; durable copies under
  `D:\projects\bolt-sharp-archives\`).
- The canonical tree contains **one** listener implementation; duplicate-type build errors
  (`CS0101` ×2) are gone (clean builds, 0 warnings).
- B's unique functionality is present: `HandshakeTimeout` + `HANDSHAKE_TIMEOUT` outcome,
  reserved-byte manifest-selection test, evidence runner (original filter version), evidence
  `.gitattributes`.
- The consolidation regression (B2-C-Q2 `F01`, 39/1) was remediated during recovery; this
  qualification re-ran B2-C-Q2 at **40/40**.
- The parallel writer was halted; quiescence was re-established and verified twice (recovery and
  this resumption) before any write. The incident is **CLOSED** at the repository level; the
  writer's identity was never established (see the recovery receipt).

## 10. Findings (Q1-11)

| ID | Severity | Finding |
| --- | --- | --- |
| TRACEQ1-F01 | **MAJOR** | `LoopbackHandshakeListener.SafeRemoteEndPoint` (line 657) dereferences `client.Client` without a null guard; `TcpClient.Close()` (used by `DisposeAsync` on pending clients) nulls it, producing a `NullReferenceException` that escapes `HandleConnectionAsync` (trace construction at line 312 sits outside its `try`) and propagates out of `DisposeAsync`. **Reproduced once** during reviewer probe T10 (stack trace preserved in `docs/reviews/evidence/B0-A-TRACE-Q1/nre-observation/`); deterministic re-triggers are scheduling-dependent, which is why the producer's own tests never surfaced it. Violates the B0-A-TRACE-01 §3 requirement of deterministic shutdown/reliable disposal. **Required action:** producer-side remediation (null guard, or move trace construction inside the handler's try); re-run the listener suite afterwards. |
| TRACEQ1-F02 | MINOR | `HandshakeTracingStream.CaptureTruncated` is unreachable: the negotiator never reads more than ~34 client bytes in any exchange, so the capture cap (≥ handshake size) can never be exceeded via the negotiator. Defensive-only; recommend removing or documenting, no behavioural impact. |
| TRACEQ1-F03 | OBSERVATION | The producer's `ObserveAdditionalMessagesAsync` window (150 ms default in probes) is shorter than a driver's typical inter-message gap would need if it did not pipeline; pipelined traffic is captured because it arrives back-to-back. Documented so B3 does not equate "observation window" with "protocol requirement". |
| TRACEQ1-F04 | OBSERVATION | The driver's 5.8–5.0 range proposal includes 5.5 as a range artifact; harmless (no server negotiates 5.5, BoltSharp advertises only 5.8) but worth remembering when parsing client ranges. |

Severity rationale: TRACEQ1-F01 is a demonstrated violation of a stated deliverable requirement
(deterministic shutdown) in the qualified deliverable, hence MAJOR; it is confined to test
infrastructure and does not affect the handshake-byte evidence, which this qualification
independently verified.

## 11. Protocol-version recommendation (Q1-10)

**RETAIN PROVISIONAL Bolt 5.8 / PackStream 1.0.**

- OBSERVED: the official 6.4.0 driver proposes Manifest v1 + ranges 5.8–5.0 / 4.4–4.2 + 3.0
  (never 6.x) and selects 5.8 under both Manifest v1 and legacy server configurations with
  capability mask 0x0 — independently reproduced by the reviewer's from-spec listener.
- SPECIFIED: 5.8 maps to PackStream 1.0; HELLO/LOGON separation and the 5.7+ GQL failure field
  set remain the documented shape for the next milestone.
- UNKNOWN (message/session): SUCCESS metadata acceptance, required metadata, authentication
  handling, RESET/GOODBYE/RUN/PULL, driver behaviour on failures. These require the B3 engine.

## 12. Implications for B3 (Q1-05/R2)

1. **Expect pipelined HELLO+LOGON**: the driver emitted LOGON before any server response. The B3
   connection loop must read and queue a pipelined message instead of assuming strict
   request/response pairing.
2. LOGON arrives even with `AuthTokens.None` (signature `0x6A`, 15-byte body) — B3's
   authentication design must handle a "none"-scheme LOGON explicitly rather than assuming its
   absence.
3. Capabilities 0x0 were selected against a 0x0 offer; B3 must reject unknown capability bits and
   must not advertise bits it does not implement.
4. After observing HELLO+LOGON the driver waits (no timeout-driven close within the observation
   window); a non-answering server stalls the driver until its connection timeout — B3 must always
   answer or close deterministically.

## 13. Remaining unknowns

Everything in §5.4, plus: driver behaviour once SUCCESS is answered, metadata expectations,
failure/GQL handling, TLS modes, multi-connection pool behaviour, cross-platform reproducibility
(this qualification is Windows-only, matching the producer evidence).

## 14. Disposition (Q1-11)

**REMEDIATION REQUIRED.**

The authentic driver observations of B0-A-TRACE-01 are verified: the handshake bytes, negotiation
outcomes, Bolt 5.8 selection and post-handshake HELLO→LOGON pipelining were independently
reproduced with reviewer-owned tooling, and the evidence, sanitization and regression baselines
all check out. However, reviewer probe T10 demonstrated finding **TRACEQ1-F01 (MAJOR)** — a
disposal-race `NullReferenceException` in `LoopbackHandshakeListener.SafeRemoteEndPoint` that
violates the deliverable's deterministic-shutdown requirement — and per the qualification rules a
remaining MAJOR defect requires remediation before this qualification can report SUPPORTED.
No blocking defect was found; no production code was affected; nothing was pushed.

**Required remediation:** producer-side fix for TRACEQ1-F01 (guard `client.Client`, or construct
the trace inside the handler's try), followed by a re-run of the listener/probe suites. This
reviewer's probes are designed to detect the fix (T10 asserts exception-free disposal).

## 15. Receipt

```text
Task: B0-A-TRACE-Q1
Qualified HEAD: 76058e5086e5d7c7f90eff54da9fb533a6155f76 / tree fdd16cb773be21582a094522270c7bfc4ac67321
Reviewer session: GLM-5.3-Flash (nvidia) via OpenCode; independence UNVERIFIED (shared Git identity)
Reviewer artifacts: docs/reviews/probes/B0-A-TRACE-Q1/** (14 probes),
                    docs/reviews/evidence/B0-A-TRACE-Q1/** (TRX x8, logs, authentic reproduction, sha256.json)
Results: solution 826/0/0; probes 18/67/46/40; driver 37/0/10 gated; authentic 2/2;
         reviewer probes 14/14; fresh reproduction byte-identical to committed producer evidence
Findings: TRACEQ1-F01 MAJOR (disposal-race NRE, reproduced once, preserved);
          TRACEQ1-F02 MINOR (unreachable CaptureTruncated); TRACEQ1-F03/F04 OBSERVATION
Disposition: REMEDIATION REQUIRED (single MAJOR, test-infrastructure only; handshake evidence verified)
Owner acceptance: PENDING
```
