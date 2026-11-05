# B0-A-TRACE-01 — Official Neo4j .NET driver handshake probe

**Status:** COMPLETE — executed authentic driver probe, consolidated after a
concurrent-authoring incident; ready for independent qualification
**Date:** 2026-11-05
**Task:** [B0-A-TRACE-01](../tasks/B0-A-protocol-reconnaissance.md) (recovery under
B0-A-TRACE-01-RECOVERY owner authorization)
**Canonical branch:** `main` (baseline `9abf5e3724a31aff085b3810ebb9bd3a7783ea2a`,
tree `a465d06c17325fddc045fb6d507a0e4df3358414`)
**Driver pin:** `Neo4j.Driver` **6.4.0** (unchanged; test-only dependency)

This record contains **no claim of Neo4j server compatibility, Browser compatibility, or
message/session compatibility**. It reports one executed handshake-level reconnaissance against
the official .NET driver and preserves the observed bytes.

---

## 1. Executive summary

A disposable, test-only TCP listener on `127.0.0.1` (OS-assigned ephemeral port) used BoltSharp's
existing B2-B `BoltHandshakeNegotiator` to negotiate with the real official driver
(`Neo4j.Driver` 6.4.0, `VerifyConnectivityAsync` over `bolt://127.0.0.1:<port>`). The listener
implements no HELLO/LOGON/session behaviour, so the driver's post-handshake failure is expected.

**Authentic observations (both Manifest v1 and legacy server configurations):**

1. **T01 — Identification (OBSERVED).** The driver opens with `60 60 B0 17` and proposes, in
   order: **Manifest v1** (`00 00 01 FF`), the range **5.8–5.0** (`00 08 08 05`), the range
   **4.4–4.2** (`00 02 04 04`), and concrete **3.0** (`00 00 00 03`).
   **The driver does not propose any Bolt 6.x revision.**
2. **T02 — Server negotiation (OBSERVED).** With Manifest v1 enabled and only Bolt 5.8
   supported, the production negotiator answered `00 00 01 FF 01 00 00 08 05 00`
   (marker, VarInt count 1, range 5.8, capability mask `0x0`). With Manifest v1 disabled it
   answered the four legacy bytes `00 00 08 05` (5.8).
3. **T03 — Client selection (OBSERVED).** On the manifest path the driver selected
   `00 00 08 05` (**5.8**) with capability subset `0x0` — within the advertised version and
   capability set. On the legacy path the driver continued to protocol messages after the
   four-byte selection response.
4. **T04 — First post-handshake activity (OBSERVED).** In both modes the driver immediately sent
   a valid chunked-framed message with body length **155**, structure `B1`, signature `0x01`
   (**HELLO**, 1 field) — and then **pipelined a second message** (15-byte body, signature
   `0x6A`, **LOGON**, 1 field) without waiting for any server response. The driver then remained
   waiting; when the listener closed, `VerifyConnectivityAsync` failed with
   `Neo4j.Driver.ServiceUnavailableException` (expected; no fabricated success was sent).

**Compatibility conclusion (handshake level only):** the actual driver selects Bolt **5.8**, the
provisional B0-A candidate, on both handshake paths, and never offers 6.x. This *supports
retaining* Bolt 5.8 + PackStream 1.0, but the global version decision is not finalized here; the
unknowns in §10 remain.

---

## 2. Concurrent-authoring incident (B0-A-TRACE-01-RECOVERY)

### 2.1 Timeline (all times UTC, 2026-11-05)

| Time | Event |
| --- | --- |
| 12:43–12:45 | Implementation **A** (this session) authored, built; 9 synthetic + 2 authentic tests passing; sanitized evidence captured |
| 12:48:16 | Foreign files appear: `HandshakeTraceModels.cs`, `RecordingStream.cs` |
| 12:50:13 | Foreign `HandshakeTraceListener.cs` appears |
| 12:53:11 | Foreign `HandshakeTraceTests.cs` appears |
| 12:55:21 | 11 `dotnet` processes spawn (other actor's build/test run) |
| 12:58:02 | Foreign `HandshakeTraceTests.cs` modified again (caught between snapshots) |
| 13:01:56 | Foreign `Run-Evidence.ps1`, `.gitattributes` appear in the evidence directory |
| 13:02:16 | **Session halted** per owner directive; read-only preservation taken |
| 13:07–13:09 | Post-stop owner authorization; quiescence proven (two snapshots 120 s apart, zero writes; last foreign write 13:01) |
| 13:10 | **B0-A-TRACE-01-RECOVERY** consolidation began |

The other agent was instructed to stop by the owner; quiescence was independently verified
before any write (no file writes for ~8 minutes, then two clean snapshots).

### 2.2 Preservation and backup verification

Both implementations — including uncommitted state — were preserved **before** any modification:

| Copy | Location | Verification |
| --- | --- | --- |
| Incident snapshot | `C:\Users\Laszlos\AppData\Local\Temp\opencode\B0-A-TRACE-01-parallel-incident-20261105T130216Z\` (+ `.zip`) | SHA-256 manifest: **17 entries, 0 mismatches** |
| Durable copy | `D:\projects\bolt-sharp-archives\B0-A-TRACE-01-incident-20261105T131053Z\` (+ `.zip`) | Re-hashed: **17 entries, 0 mismatches**, 18 files |

The durable copy is outside the working checkout and outside the ephemeral temp directory.

### 2.3 Integrity checks at resumption

- HEAD `9abf5e3` / tree `a465d06` on `main` — exactly the pinned baseline; **no commits were
  created by the other agent** (`git log --all`, `git reflog` clean).
- One worktree; working tree contained only the 17 untracked files of the two implementations;
  zero modified tracked files.

### 2.4 Implementation comparison

| Dimension | A (this session) | B (parallel actor) |
| --- | --- | --- |
| Negotiation engine | production `BoltHandshakeNegotiator` | production `BoltHandshakeNegotiator` |
| Connection bounds | bounded connection count (default 4), accept backlog, per-connection lifetime 10 s, capture cap 4 KiB, message cap 64 KiB | single connection, backlog 1, **no per-connection lifetime**, capture cap 1 MiB (oversized), 8 KiB post buffer |
| Handshake-phase timeout | none initially | **dedicated `HandshakeTimeout`** (unique, retained) |
| T02 replay | captured client bytes replayed through a fresh production negotiator; byte-equality vs live response | absent |
| T03 verification | selection decoded via production `BoltManifestV1.ReadSelection`; offer-containment + capability-subset checks | hand-rolled VarInt decode (duplicates `BoltVarInt`), no subset check |
| T04 observation | B2-A `BoltMessageReader`; first message + up to 4 additional framed messages with signature identification; closed/waiting classification | single raw bounded read; chunk length + first marker only; no message identity; NOOP chunk misread risk |
| Sanitization | handshake-only capture; post-handshake bytes counted, never stored; bodies never retained; errors truncated to 400 chars | post-handshake read from raw stream (not captured); OK |
| Authentic assertions | strict behavioural asserts (mode, selected version, signature, expected driver exception) | near-vacuous asserts (`Assert.NotNull(x is null ? x : x)`) |
| Negative tests | 9 (unsupported version, invalid magic, disconnect, fragmentation, unoffered manifest version, capability superset, cancellation, shutdown, loopback binding) | 7 (N01 unsupported, N02 invalid magic, N03 disconnect, N04 fragmented, **N05 reserved-byte selection** (unique), **N06 handshake timeout** (unique), N07 shutdown) |
| Evidence tooling | T01–T04 report builder + hex files + env-gated writer | **`Run-Evidence.ps1` full-suite evidence runner** (unique, retained), `.gitattributes` (retained) |

### 2.5 Canonical selection

**Implementation A is canonical.** Decision basis (source + test evidence, not preference): A
exercised more of the B0-A-TRACE-01 requirements (bounded connections/lifetime/cancellation,
T02 replay, T03 verification, T04 message identification, stricter authentic assertions) and had
an already-green executed run. B contributed three non-duplicated capabilities that were folded
into the consolidated implementation (§2.6).

### 2.6 Unique functionality retained from each implementation

From **B**:
1. Dedicated `HandshakeTimeout` (negotiation-phase bound, default 5 s) in
   `LoopbackListenerLimits`, with outcome `HANDSHAKE_TIMEOUT` and the N06-style test
   `HandshakeTimeout_DuringNegotiation_ClosesPendingConnection`.
2. Negative case N05 as `MalformedManifestSelection_ReservedByteSet_Is_FormatError`
   (reserved byte set in the manifest selection → `BoltHandshakeFormatException`).
3. `Run-Evidence.ps1` (adapted: canonical test names, `BOLTSHARP_TRACE01_EVIDENCE_DIR`, all six
   suites) and the evidence `.gitattributes` (`* -text whitespace=cr-at-eol`) that keeps captured
   bytes byte-stable under git.

From **A**: everything else — listener, trace models, analysis/report/replay, synthetic client,
both authentic probes, the 9 original negative tests.

### 2.7 Files removed (after preservation) and why

| Removed | Reason |
| --- | --- |
| `HandshakeTraceListener.cs` | duplicated the canonical listener with weaker bounds; no unique capability |
| `HandshakeTraceModels.cs` | duplicate type definitions (`HandshakeTrace`, `PostHandshakeObservation`) that broke the build (CS0101 ×2); superseded by canonical models |
| `RecordingStream.cs` | duplicated `HandshakeTracingStream` (which additionally stops capture after the handshake and counts post-handshake bytes) |
| `HandshakeTraceTests.cs` | duplicated the authentic/negative suites; its two unique cases (N05, N06) and runner were retained per §2.6; all other cases covered by canonical equivalents (mapping: N01↔UnsupportedVersion, N02↔InvalidBoltIdentification, N03↔ClientDisconnect, N04↔FragmentedHandshakeInput, N07↔Listener_Shutdown_With_PendingConnection) |
| `raw\` trace JSON/hex | superseded by regenerated post-consolidation evidence; bytes preserved in both incident backups |

### 2.8 Regression found and fixed during consolidation

The first post-consolidation run exposed **B2CQ2 `F01_Driver_construction_is_confined_to_validated_settings`**
failing (39/1 vs baseline 40): the authentic probe had called `GraphDatabase.Driver` directly,
creating a second construction path outside the validated factory (B2-C-R1, finding Q1-F01).
Fixed by routing construction through `DriverTestSettings.CreateDriver`, with a new additive
`DriverTestSettings(Uri)` constructor for the OS-assigned listener port (validation identical;
the single construction call site remains inside `DriverTestSettings.cs`). B2-C-Q2 is back to
**40/0**. This probe proved its worth: it caught a real invariant break.

---

## 3. Test-only listener design

`tests/BoltSharp.DriverIntegrationTests/HandshakeTrace/LoopbackHandshakeListener.cs` (test
project only; `src/BoltSharp` untouched):

- Binds `IPAddress.Loopback` only; port 0 → OS-assigned ephemeral port; `bolt://127.0.0.1:<port>`.
- Delegates all negotiation to `BoltHandshakeNegotiator` (no duplicated protocol logic).
- Bounds: `MaxConnections` (4 in probes), accept backlog, `HandshakeTimeout` (5 s),
  `ConnectionLifetime` (10 s), `FirstMessageObservationTimeout` (2 s),
  `PostMessageObservationWindow` (250 ms), `MaxObservedMessageBytes` (64 KiB),
  `MaxHandshakeCaptureBytes` (4 KiB), `MaxManifestVersions` (32).
- `HandshakeTracingStream` captures client/server handshake bytes up to the cap, then
  `StopCapture()` switches to counting post-handshake bytes only — bodies are never stored.
- After an accepted handshake, B2-A `BoltMessageReader` observes the first framed message plus up
  to four additional messages; only length, struct marker, field count and signature are
  recorded (signature names: HELLO, LOGON, RESET, RUN, …, else UNKNOWN). Socket `Poll` then
  classifies closed/waiting without reading further bytes.
- Deterministic shutdown: linked shutdown/lifetime CTSs, client disposal, handler task
  completion awaited with a bounded timeout; `FirstTrace` task completes on connection 1.

Tests: `LoopbackHandshakeListenerTests.cs` (11), `HandshakeTraceProbeTests.cs` (2 authentic),
`SyntheticHandshakeClient.cs` (test-controlled peer; never attributed to the driver),
`HandshakeTraceAnalysis.cs` (proposal decoding, T02 replay, T01–T04 report, evidence writer).

---

## 4. Exact sanitized handshake bytes (post-consolidation run)

### 4.1 Manifest v1 path (server offers Manifest v1 + 5.8 only)

```text
C 60 60 B0 17                                   # Bolt identification
C 00 00 01 FF                                   # proposal 1: Manifest v1
C 00 08 08 05                                   # proposal 2: range 5.8-5.0
C 00 02 04 04                                   # proposal 3: range 4.4-4.2
C 00 00 00 03                                   # proposal 4: Bolt 3.0
S 00 00 01 FF                                   # server: Manifest v1 accepted
S 01                                            # VarInt: 1 version range
S 00 00 08 05                                   # range: Bolt 5.8 (concrete)
S 00                                            # VarInt: capabilities 0x0
C 00 00 08 05                                   # driver selects Bolt 5.8 (4.0 style)
C 00                                            # driver capability subset 0x0
```

### 4.2 Legacy path (server offers 5.8 only, Manifest disabled)

```text
C 60 60 B0 17
C 00 00 01 FF  00 08 08 05  00 02 04 04  00 00 00 03   # same four proposals
S 00 00 08 05                                   # legacy selection: Bolt 5.8
```

Raw files: `driver-manifest-client-handshake.hex`, `driver-manifest-server-response.hex`,
`driver-legacy-client-handshake.hex`, `driver-legacy-server-response.hex`.

---

## 5. First post-handshake observation (both modes, OBSERVED)

| Property | Value |
| --- | --- |
| Valid chunked framing | yes (B2-A `BoltMessageReader`) |
| First message body | 155 bytes (`00 9B` chunk header + payload + `00 00` terminator) |
| First structure | marker `0xB1` (tiny struct, 1 field), signature `0x01` = **HELLO** |
| Second message (pipelined) | body 15 bytes, marker `0xB1`, 1 field, signature `0x6A` = **LOGON** |
| Pipelining fact | **LOGON is sent before any server response arrives** |
| Third message | none within the 250 ms window (driver waiting) |
| Client state after messages | connection open, driver waiting |
| Post-handshake bodies persisted | **no** (counts/signatures only) |

---

## 6. Client exception / closure behaviour (OBSERVED, expected)

The listener closes after observation without answering HELLO/LOGON. `VerifyConnectivityAsync`
then fails:

```text
Neo4j.Driver.ServiceUnavailableException
Connection with the server breaks due to IOException: Unable to read data from the transport
connection: An existing connection was forcibly closed by the remote host. ...
```

Sanitized in evidence; no credentials or payloads included. Driver construction used
`AuthTokens.None` via the single validated `DriverTestSettings.CreateDriver` factory; the driver
was always disposed. Full connectivity was **not** achieved and is not claimed.

---

## 7. Negative and boundary results (synthetic peers; never attributed to the driver)

| Test | Result |
| --- | --- |
| UnsupportedVersion → `00 00 00 00` + close, trace `REJECTED`/`ShouldClose` | PASS |
| InvalidBoltIdentification → no response, `FORMAT_ERROR` | PASS |
| ClientDisconnect during handshake → `TRUNCATED` | PASS |
| FragmentedHandshakeInput (1-byte writes) → accepted 5.8, identical bytes | PASS |
| MalformedManifestSelection unoffered version (5.9) → `PROTOCOL_ERROR` | PASS |
| MalformedManifestSelection capability superset → `PROTOCOL_ERROR` | PASS |
| MalformedManifestSelection reserved byte set → `FORMAT_ERROR` (from B/N05) | PASS |
| HandshakeTimeout stall → `HANDSHAKE_TIMEOUT`, closed (from B/N06) | PASS |
| Cancellation during negotiation → `CANCELLED`, closed | PASS |
| Listener shutdown with pending connection → deterministic, `CANCELLED` | PASS |
| Loopback-only binding + ephemeral port | PASS |

---

## 8. Comparison against the provisional B0-A candidate (Bolt 5.8 / PackStream 1.0)

- **Does the driver offer 5.8?** Yes — as the upper bound of its 5.x range (OBSERVED).
- **Newer revisions?** **No 6.x proposal is ever sent** (OBSERVED). Newest offered Bolt revision
  is 5.8.
- **Manifest v1?** Offered (slot 1) and used end-to-end (OBSERVED).
- **Selects a version our handshake implementation supports?** Yes — 5.8 on both paths
  (OBSERVED, bytes in §4).
- **Continues to protocol messages?** Yes — valid framed HELLO, then pipelined LOGON (OBSERVED).
- **Handshake-level incompatibilities?** None observed: both negotiation paths complete; the
  driver accepts a 5.8-only manifest offer and a 5.8-only legacy selection with capabilities
  `0x0`.
- **What remains unknown (message/session):** whether the driver accepts our future SUCCESS
  metadata for HELLO/LOGON; required metadata fields (`user_agent` handling, `bolt_agent`,
  routing hints, notifications); RESET/GOODBYE/RUN/PULL behaviour; PackStream field details of
  driver-sent messages (not decoded by design); TLS/`bolt+s://` behaviour (not probed).

---

## 9. Explicit compatibility non-claims

1. A completed handshake does **not** prove Neo4j server, Browser, or driver *compatibility*.
2. No message/session compatibility is claimed: HELLO and LOGON were observed but never answered.
3. The pipelined LOGON identification rests on the signature byte `0x6A` and body length only;
   its fields were not decoded (by design).
4. Bolt 5.5 is never negotiable (B2-B-R1) and was not offered.
5. The driver's refusal of 6.x cannot be generalized beyond this pinned 6.4.0 build and its
   default configuration.
6. One handshake trace cannot finalize the global protocol-version decision (owner decision
   pending independent qualification).

---

## 10. Recommendation for B3 planning

**Retain the provisional candidate: Bolt 5.8 + PackStream 1.0.** Rationale from observed bytes:
the official driver's newest offer is 5.8, it selects 5.8 through both handshake paths, and no
6.x proposal exists to require PackStream 1.1/UUID or the 6.0 structures. B3 must implement the
message/session engine, and the following observed behaviours should shape it:

1. **HELLO and LOGON may arrive pipelined back-to-back** — the server must read and queue the
   pipelined LOGON instead of assuming strict request/response gaps.
2. HELLO body ≈155 bytes, LOGON ≈15 bytes with `AuthTokens.None`; drivers may send
   `TELEMETRY`/`ROUTE`-class extras — handle unknown-but-valid messages per the selected
   revision's rules.
3. Manifest v1 with an empty capability mask works; capability bits should stay `0x0` until
   individually implemented.

A future re-probe (post-B3) with an answering server is required before any driver-compatibility
claim, per B5 acceptance.

---

## 11. Test commands and results (exact, post-consolidation)

Environment: Windows 11 Pro build 10.0.26200, win-x64, .NET SDK `10.0.401`, MSBuild `18.9.11`,
targets `net8.0`, runtime `.NET 8.0.31`, `Neo4j.Driver` 6.4.0
(SHA-256 of package DLL: `C0F34E7008F57D70C158E2FA3FDF2E96E3D93506BA1714929B2BD8F4F34B14CF`).
All commands from the repository root; full outputs in
`docs/reviews/evidence/B0-A-TRACE-01/*.txt` and `*.trx`, aggregated in `summary.json` /
`commands.json`.

```text
powershell -NoProfile -ExecutionPolicy Bypass -File docs\reviews\evidence\B0-A-TRACE-01\Run-Evidence.ps1
  → EXIT=0 (elapsed 20.7 s)

dotnet test BoltSharp.slnx --no-build -c Debug               → 826 passed / 0 failed / 0 skipped
dotnet test docs/reviews/probes/B2-A-Q2/B2AQ2.Probes.csproj  →  18 passed
dotnet test docs/reviews/probes/B2-B-Q1/B2BQ1.Probes.csproj  →  67 passed
dotnet test docs/reviews/probes/B2-B-Q2/B2BQ2.Probes.csproj  →  46 passed
dotnet test docs/reviews/probes/B2-C-Q2/B2CQ2.Probes.csproj  →  40 passed
dotnet test tests/BoltSharp.DriverIntegrationTests/...csproj →  37 passed / 0 failed / 10 gated skips (D01–D10)
  of which B0-A-TRACE-01: 11 listener/boundary + 2 authentic, all passed
dotnet test ... --filter FullyQualifiedName~HandshakeTraceProbeTests → 2 passed (authentic trace TRX: trace.trx)
```

All pre-existing baseline numbers are preserved exactly (826 / 67 / 46 / 18 / 40 / 24+10).

---

## 12. Deliverable file inventory (source)

| File | Purpose |
| --- | --- |
| `tests/BoltSharp.DriverIntegrationTests/HandshakeTrace/LoopbackHandshakeListener.cs` | disposable bounded listener, tracing stream, trace models (canonical, from A + B's timeout) |
| `tests/BoltSharp.DriverIntegrationTests/HandshakeTrace/HandshakeTraceAnalysis.cs` | proposal decode, T02 replay, T01–T04 report, evidence writer |
| `tests/BoltSharp.DriverIntegrationTests/HandshakeTrace/HandshakeTraceProbeTests.cs` | authentic driver probes (manifest + legacy) |
| `tests/BoltSharp.DriverIntegrationTests/HandshakeTrace/LoopbackHandshakeListenerTests.cs` | 11 negative/boundary tests (A's 9 + B's N05/N06) |
| `tests/BoltSharp.DriverIntegrationTests/HandshakeTrace/SyntheticHandshakeClient.cs` | test-controlled synthetic peer |
| `tests/BoltSharp.DriverIntegrationTests/DriverTestSettings.cs` | additive `DriverTestSettings(Uri)` constructor (single construction factory preserved) |

Changed existing files: **only** `DriverTestSettings.cs` (additive; §2.8). `src/BoltSharp`,
`BoltSharp.slnx`, `Directory.Build.props`, and all accepted review/evidence records are
byte-identical to the baseline.

Evidence: `docs/reviews/evidence/B0-A-TRACE-01/` (TRX + TXT per suite, `summary.json`,
`commands.json`, sanitized traces, `Run-Evidence.ps1`, `.gitattributes`, `sha256.json`).

Commit identities are recorded in the final handoff message (this file cannot contain its own SHA).

---

## 13. Receipt

```text
Task: B0-A-TRACE-01 (recovery B0-A-TRACE-01-RECOVERY)
Baseline commit: 9abf5e3724a31aff085b3810ebb9bd3a7783ea2a / tree a465d06c17325fddc045fb6d507a0e4df3358414
Incident: concurrent implementation B detected and preserved; consolidation per owner's
          B0-A-TRACE-01-RECOVERY; quiescence verified before resumption
Backups: temp (17 entries, 0 mismatches) + D:\projects\bolt-sharp-archives\...131053Z (17 entries, 0 mismatches)
Canonical implementation: A (evidence-based); retained from B: HandshakeTimeout, N05, N06,
          Run-Evidence.ps1, .gitattributes
Driver: Neo4j.Driver 6.4.0, bolt://127.0.0.1:<ephemeral>, AuthTokens.None, VerifyConnectivityAsync
Observed: driver offers MANIFEST_V1/5.8-5.0/4.4-4.2/3.0 (no 6.x); selects 5.8 on both paths;
          sends HELLO(155 B) + pipelined LOGON(15 B); then waits; fails with
          ServiceUnavailableException when listener closes (expected)
Tests: 826 solution; 18/67/46/40 probes; 37 passed + 10 gated skips driver project (incl. 13 new)
Non-claims: no server/Browser/driver/message-session compatibility; handshake evidence only
Recommendation: retain Bolt 5.8 + PackStream 1.0 (provisional until B3/B5 executable proofs)
Independent reviewer verdict: PENDING
Owner acceptance: PENDING
```
