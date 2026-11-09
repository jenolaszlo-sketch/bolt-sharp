# B5-A-IMPL-01 — TCP/TLS Transport and Bolt 5.8 Network Integration (Implementation Report)

- **Task:** `B5-A-IMPL-01`
- **Role:** Implementation agent (producer)
- **Repository:** `D:\projects\bolt-sharp`, canonical `main`, single worktree, never pushed
- **Starting HEAD:** `853580659cc57ad959c026800c1e17de44cdddc1`
- **Starting tree:** `a00ab7b189608da4ebf5a61ead6557e402d0bac4`
- **Final HEAD:** `30c66a7` (see §25 for the complete commit chain)
- **Protocol target:** Bolt 5.8 / PackStream 1.0 (bounded; no other revision negotiated)
- **Prerequisites:** B3-A, B3-B, B3-C accepted with qualifications (all carried forward)
- **Evidence:** `docs/reviews/evidence/B5-A-IMPL-01/`
- **Disposition:** **B5-A-IMPL-01 — IMPLEMENTED, READY FOR INDEPENDENT B5-A-Q1 QUALIFICATION**

> **Reviewer/executor independence note (binding for B5-A-Q1):** this implementation and its
> producer verification were executed by the same DeepSeek V4.1 Flash session (provider nvidia,
> OpenCode) that authored this report. That is the standing B5-A producer role. The next gate,
> **B5-A-Q1, requires an independent reviewer executor/model different from this producer**
> (per commission §23 and the standing reviewer-independence qualifications). No transport
> behavior was self-qualified as release-grade here.

---

## 1. Preflight verification

All checks in commission §2 were performed and recorded in
`logs/01-preflight.txt`:

1. Branch `main` — confirmed.
2. HEAD exactly `853580659cc57ad959c026800c1e17de44cdddc1` — confirmed.
3. Tree exactly `a00ab7b189608da4ebf5a61ead6557e402d0bac4` — confirmed.
4. Exactly one worktree (`D:/projects/bolt-sharp`) — confirmed.
5. Working tree and index clean — confirmed before any change.
6. No concurrent writer — confirmed.
7. No previous B5-A work — confirmed (`src/BoltSharp/Transport/` and
   `tests/BoltSharp.Tests/Transport/` did not exist; no prior transport report).
8. No push — confirmed; branch remains `ahead 44`+ of `origin/main` and never pushed.
9. Correct ancestry from all three B3 owner decisions — confirmed (commit chain reaches
   `8535806` = B3-C-OWNER-01 acceptance).

Regression baseline observed at start (all accepted suites passing, build 0 errors /
2 warnings): core 1,137, B3-C backend 94, B3-B sessions 119, B3-A messages 98,
B0-B integration 19.

## 2. Actual transport architecture

Five production files under `src/BoltSharp/Transport/` (commit `943774c`), exactly the
commission §4 pipeline with no duplicated protocol logic:

```text
TcpListener (BoltTcpServer)
      |  bounded admission (MaxConnections), backlog, per-connection tasks
      v
Accepted socket (BoltTcpConnection, one task per connection)
      |  [TLS: SslStream.AuthenticateAsServerAsync — TLS 1.2+, fail closed, never downgraded]
      v
B2 handshake  (BoltHandshakeNegotiator.NegotiateAsync — reuse, not reimplementation)
      v
B2 framing    (BoltMessageReader.ReadMessageAsync / BoltMessageWriter.WriteMessage — reuse)
      v
B3-A codec    (BoltMessageCodec.DecodeRequest / EncodeResponse — reuse)
      v
B3-B session  (BoltConnectionSession.Receive — sequential, single owner)
      v
B3-C backend  (ReferenceOperationProvider via OperationProviderFactory)
      v
responses re-framed through B2 and written (bounded write timeout, loop for partial writes)
```

- **`BoltTcpServer`** — listener lifecycle, admission, accept loop, bounded shutdown.
- **`BoltTcpConnection`** — per-connection pipeline (internal): TLS → handshake → sequential
  message loop → response write; exactly-once diagnostics reporting.
- **`BoltTcpServerOptions`** — narrow options object with secure defaults and deterministic
  `Validate()`.
- **`BoltTlsServerOptions`** — explicit certificate requirement, TLS 1.2+ policy.
- **`BoltTransportDiagnostics`** — thread-safe counters + contained event observer.

## 3. Listener and connection ownership

- **Single-use lifecycle.** `StartAsync` binds and starts the accept loop; `StopAsync`
  (idempotent) stops accepting, cancels the linked shutdown token, and awaits active connection
  tasks within `ShutdownTimeout` (default 5 s). Restart throws; a new instance is required.
  Double-start throws.
- **Bounded admission.** At most `MaxConnections` active connections; excess connections are
  closed immediately on acceptance and counted (`ConnectionsRejectedForCapacity`). The accept
  loop survives per-accept faults (observable via diagnostics) and exits cleanly on shutdown.
- **Per-connection isolation.** Each connection gets its own B3-B session, framing reader, B3-A
  codec, cancellation context and connection id (`conn-0001`…). Calls into a session are strictly
  sequential inside one pipeline task — the accepted single-owner B3-B contract is preserved
  without locking.
- **Exactly-once accounting.** Connection completion/failure is reported once from a single
  `finally` (commit `07bcc29` fixed an initial double-report found by the failure tests).
- **No detached unobserved tasks.** Connection tasks are tracked in a concurrent dictionary and
  removed on completion; `StopAsync` awaits all of them (bounded).

## 4. Configuration and secure defaults

Defaults in `BoltTcpServerOptions`:

| Setting | Default | Rationale |
| --- | --- | --- |
| `BindAddress` | `127.0.0.1` | loopback-only plaintext |
| `Port` | `0` | ephemeral for tests; actual endpoint reported |
| `Tls` | `null` | plaintext requires opt-in to expose beyond loopback |
| `MaxConnections` | `100` | bounded admission |
| `HandshakeOptions` | Bolt 5.8 opt-in | B2 default advertises nothing; B5-A configures 5.8 explicitly |
| `FramingLimits` | `BoltFramingLimits.Default` | 64 MiB receiver-side message bound |
| `CredentialVerifier` | `null` | B3-B fail-closed default preserved (LOGON rejected) |
| `OperationProviderFactory` | B3-C reference backend | default factory |
| All timeouts | finite (10 s TLS/handshake, 30 s read/write, 5 s shutdown) | no unbounded waits |

`Validate()` refuses at construction: plaintext non-loopback bind (IPv4 or IPv6 any), invalid
ports, zero/negative capacity and non-positive timeouts, and defers to `BoltTlsServerOptions.Validate()`
for TLS. Tests cover every refusal (`TcpListenerTests`).

## 5. TLS implementation

- **Mode B (TLS TCP)** requires an explicit `X509Certificate2` **with a private key**; null is
  refused at construction, key-less certificates refused at `Validate()`.
- **Protocol policy:** `SslProtocols.Tls12 | TlsProtocols.Tls13` default; `Validate()` refuses
  anything below TLS 1.2; deprecated SSL/TLS versions cannot be enabled through the surface.
- **Authentication before any Bolt byte:** `AuthenticateAsServerAsync` runs first, bounded by
  `TlsHandshakeTimeout`; failure closes the connection, increments `TlsHandshakeFailures`, and
  **never falls back to plaintext** (`TlsIntegrationTests.PlaintextBytesToTlsListener_FailsClosed_NoDowngrade`).
- **No STARTTLS, no mode guessing.** The mode is configuration.
- **Client URI conventions (`bolt://`, `bolt+s://`, `bolt+ssc://`) are documented as client-side
  concerns**; no official-driver TLS scenario is claimed qualified here (B5-B scope).
- **Test certificates:** self-signed RSA-2048 generated in memory per test; explicit thumbprint
  trust on the client side; no keys, PFX passwords or permanent secrets committed.
- Production-deployment security (real CA chains, revocation, cipher policy tuning) is **not**
  claimed; `checkCertificateRevocation` is passed as `false` for server-side auth in this bounded
  milestone and is recorded as a qualification (Q2, §23).

## 6. Handshake/version-negotiation behavior

The accepted B2 `BoltHandshakeNegotiator` is reused unchanged. Over real sockets the tests verify:

- exact selected-version reply bytes `00 00 08 05` for Bolt 5.8;
- one-byte-at-a-time handshake delivery negotiates successfully;
- invalid magic refuses safely (close, `BoltHandshakeFailures` counter, no crash);
- the four-proposal legacy format including zero-padding (`00 00 00 00` sentinel proposals);
- nearby revisions (5.7, 5.9, 6.0, 4.4) are never selected; unsupported-only proposals answer
  the zero word and close;
- the accepted B2 default (advertises nothing) rejects every handshake — version honesty carried
  into the transport;
- **handshake boundary safety:** a single network write containing the handshake *and* the framed
  HELLO is processed correctly (the B2 reader consumes exactly the handshake bytes, so pipelined
  protocol bytes are preserved);
- client disconnect and EOF during negotiation classify as clean completion (commit `07bcc29`
  maps `BoltHandshakeTruncatedException` to the B0-A clean-disconnect classification), and
  listener shutdown during negotiation completes within the bound;
- **Manifest v1:** B2 supports manifest negotiation; the transport integrates whatever the
  configured `BoltHandshakeOptions` allow (legacy in this milestone's default configuration).
  A manifest-capable client can negotiate through the same `NegotiateAsync` path. No new
  manifest behavior was fabricated; manifest-specific end-to-end coverage is a B5-A-Q1 probe
  concern and recorded as such (§23).

## 7. Chunk framing and fragmentation behavior

The accepted B2 `BoltMessageReader`/`BoltMessageWriter` are reused. Verified over TCP
(`TcpFramingTests`): chunk-length header split across writes; chunk payload split at every byte
boundary (1-byte chunks); several messages in one network write preserving order; one message
across multiple chunks; NOOP chunks between messages produce no session request; truncated
headers and payloads close safely (`BoltFramingTruncatedException` → fail-closed classification);
EOF mid-RUN never executes the incomplete request (provider never consulted); oversize
cumulative messages refused before allocation (receiver-side `MaxMessageSize`); and the output
side is byte-exact — the scalar RECORD reply equals the authentic `B171912A` golden vector.

## 8. Session adapter design

`BoltTcpConnection.RunMessageLoopAsync` is the complete adapter:

1. read one complete message (bounded by `MessageReadTimeout`, cancellation-linked);
2. `codec.DecodeRequest(message)` (B3-A);
3. `session.Receive(request)` (B3-B — single entry point, sequential; `ReceiveBatch` is not used
   by the transport, so its consumed/remaining ownership limitation is not exercised);
4. `codec.EncodeResponse` per response (B3-A);
5. frame all responses via B2 writer into one buffer and write with a loop that handles partial
   writes, bounded by `WriteTimeout`;
6. honor terminal outcomes: `ShouldClose || session.IsDefunct` closes after writing final
   responses; GOODBYE fabricates nothing;
7. `finally` calls `session.Disconnect()` then `Dispose()` exactly once (retiring any active
   operation per the accepted contract) and reports connection accounting exactly once.

## 9. Pipelining and message-order behavior

Multiple requests in a single network write are processed strictly in order, one complete
message at a time, with responses written in order (`SeveralMessagesInOneWrite_PreserveOrder`).
Pipelined work after a FAILED state is answered with the zero-field IGNORED structure
(`B07E`, asserted at byte level) and framing continues correctly afterwards. GOODBYE pipelined
with a following RUN closes the connection before the RUN is processed
(`MessagesAfterTerminalGoodbye_AreNotProcessed`). The transport never discards accepted requests
while the connection remains usable: the loop is sequential with natural stream backpressure and
no intermediate queue.

## 10. Backpressure and limits

- **Input:** no eager buffering; one message read at a time; `MaxMessageSize` enforced by the
  reused B2 reader before payload accumulation; `MessageReadTimeout` bounds a stalled client.
- **Output:** responses for one request are encoded into one bounded buffer
  (B3-B's accepted `MaxRecordsPerResponse` bounds the record volume per response pass) and
  written immediately; PULL pagination over TCP verified end-to-end (2,500 rows in bounded
  passes with `has_more=true`, terminal summary SUCCESS with **no** `has_more` field — the
  accepted B3-B semantics, byte-difference from the real-server `has_more:false` golden flagged
  for B5-B as already qualified).
- **Slow clients:** `WriteTimeout` bounds each write; a write timeout closes the connection and
  releases the session. A slow *provider* remains bounded by the accepted B3-B limitation (no
  mid-flight RESET jump-ahead interruption) — documented, not re-implemented (§23, Q5).
- **Capacity:** connection admission and refusal verified without disturbing existing clients
  (`TransportLimitsTests`, `MultiClientTests`).

## 11. Connection shutdown

Client disconnect (EOF/reset) at any stage — before handshake, during handshake, mid-session,
during an active result — stops reading/writing, disposes the session exactly once (retiring the
active operation), releases the socket and decrements the active count. Listener shutdown
signals the linked cancellation token, interrupts blocking reads, and awaits all connection
tasks within `ShutdownTimeout`; tests verify bounded completion with active clients and that
every active operation is retired (`ListenerShutdownDuringActiveResult_RetiresOperation`).

## 12. Authentication and confidentiality

- HELLO/LOGON flow over TCP and TLS with the accepted metadata (`server`, `connection_id`,
  `hints`); LOGON gating verified in both modes.
- Production fail-closed default preserved: no verifier configured → every LOGON fails
  (`MissingVerifier_FailsClosed`).
- Invalid credentials → `Neo.ClientError.Security.Unauthorized` FAILURE and terminal closure
  (accepted B3-B semantics); a throwing verifier fails closed and leaks nothing.
- TLS does not bypass B3-B authentication (`TlsDoesNotBypassAuthentication`).
- Diagnostics carry only transport metadata. Sentinel-credential tests assert no credential
  material ever appears in diagnostic output, including across clients.
- LOGOFF → SUCCESS; RUN after LOGOFF is a terminal protocol violation per accepted B3-B
  semantics (FAILURE + close) — reauthentication requires a new connection.

## 13. Plaintext scalar TCP trace

Full trace in `network-traces.txt` (probe source: `network-probe-source.cs`). Key bytes:

```text
C->S handshake: 6060B01700000805000000000000000000000000
S->C version:   00000805
C->S HELLO:     003CB101A28A757365725F6167656E74...0000   -> SUCCESS {server, connection_id, hints}
C->S LOGON:     003EB16AA386736368656D65856261736963...   -> SUCCESS {}
C->S RUN:       0019B310D01352455455524E20343220415320616E73776572A0A00000
S->C RUN:       B170A1866669656C64739186616E73776572      (fields=["answer"])
C->S PULL:      0008B13FA1816EC903E80000                  (n=1000)
S->C RECORD:    B171912A                                  (RECORD [42])
S->C summary:   B170A0                                    (SUCCESS {})
```

The RUN request bytes match the authentic B3-A/B0-B golden `B310D013…A0A0`; PULL matches
`B13FA1816EC903E8`; RECORD matches `B171912A`. The connection remained usable for a second
query, then GOODBYE closed it with no fabricated response.

## 14. Plaintext graph TCP trace

`MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b` over real TCP returns a 3-arity record
**byte-identical to the accepted golden vector**:

```text
S->C graph RECORD: B17193B44E019186506572736F6EA1846E616D6585416C696365836E3A31
                   B852010102854B4E4F5753A18573696E6365C907E883723A31836E3A31836E3A32
                   B44E029186506572736F6EA1846E616D6583426F62836E3A32
matches golden: True
```

Decoded assertions (also in `TcpGraphIntegrationTests`): Alice node (id 1, label Person, name
"Alice", elementId `n:1`), KNOWS relationship (id 1, type KNOWS, since=2024, elementId `r:1`),
Bob node (id 2, label Person, name "Bob", elementId `n:2`), direction Alice→Bob, correct numeric
and element IDs, record arity 3, terminal SUCCESS without `has_more`.

## 15. TLS scalar TCP trace

```text
TLS listener bound: 127.0.0.1:<ephemeral>
TLS authenticated: Tls13 cipher=TLS_AES_256_GCM_SHA384
C->S handshake (inside TLS): 6060B01700000805000000000000000000000000
S->C selected version (inside TLS): 00000805
C->S LOGON (inside TLS): 003EB16AA386736368656D6585...  -> SUCCESS {}
C->S RUN (inside TLS):   0019B310D013...               -> SUCCESS fields=["answer"]
C->S PULL (inside TLS):  0008B13FA1816EC903E80000       -> B171912A + B170A0
TLS PULL record value: 42
```

Bolt negotiation happens strictly inside the authenticated TLS stream; payloads travel encrypted
(SslStream); the test-suite TLS flow additionally covers HELLO/LOGON/RUN/PULL with full
assertions.

## 16. Fragmentation/NOOP/oversize results

All 80 transport tests pass; the fragmentation/framing rows of commission §14 are covered
1:1 by `TcpFramingTests` (see §7 above). Oversize refusal uses a 100-byte `MaxMessageSize` where
HELLO (≈63 B) and LOGON (≈54 B) still fit but a padded RUN (≈117 B) is refused before
accumulation. NOOP handling distinguishes terminator vs keep-alive via the reused B2 reader
semantics; a NOOP between HELLO and LOGON yields exactly two processed requests.

## 17. Failure-injection results

`TransportFailureTests` reproduces the accepted B3-B fault patterns through the real transport:

| Case | Observed behavior over TCP |
| --- | --- |
| Provider `Prepare` throws | recoverable FAILURE (FAILED state); connection stays usable; RESET recovers; provider consulted again on next RUN |
| Metadata getter throws | FAILURE; operation retired exactly once (`DisposeCount == 1`) |
| Record read throws | records already read are emitted, then FAILURE; retired exactly once |
| Record arity invalid | covered by B3-B accepted contract (arity validated before emission) |
| **Disposal throws (mandatory §16 sequence)** | RUN opens op #1 → PULL drains → disposal throws → FAILURE (never SUCCESS) reported for the failed retirement → **connection closes (DEFUNCT)** → pipelined RESET/RUN never processed (`MessagesProcessed == 4`: HELLO, LOGON, RUN, PULL) → `PrepareCount == 1`, `DisposeCount == 1` (no op #2, no retry) |
| Metadata failure + disposal failure | FAILURE reported exactly once, then fail-closed closure |
| Read failure + disposal failure | primary FAILURE preserved, then terminal closure |
| Client disconnect during active result | owned operation retired (`DisposeCount == 1`), no fault |
| Listener shutdown during active result | operation retired, active count drains |
| Second message after terminal failure | never processed (connection closed; write lands on closed socket or is dropped) |

## 18. Multi-client results

Two-plus clients: independent sessions, interleaved queries, independent closure; failure in one
client (framing garbage) does not affect the other; an idle client does not block an active one;
capacity bound (1 and 3) refuses excess connections without disturbing accepted ones; shutdown
with multiple active clients signals all of them; diagnostics remain credential-free across
clients.

## 19. Regression counts

| Suite | Floor | Observed |
| --- | ---: | ---: |
| Core (total assembly incl. all suites) | 1,137 | **1,217** (1,137 + 80 new transport) |
| B3-A messages | 98 | **98** |
| B3-B sessions | 119 | **119** |
| B3-C backend | 94 | **94** |
| B2 framing | — | 54 |
| B2 handshake | — | 182 |
| B5-A transport (new) | — | **80** |
| B0-B driver integration (separate project, unchanged) | 19 | **19** |

Full run: **1,217/1,217 passing, 0 failures** (`full-regression.trx`). Transport suite:
**80/80** (`transport-tests.trx`), stable across 10 consecutive repeats after timing-race
hardening. Driver integration: **19/19**.

## 20. Build warnings and errors

- Production assembly: **0 errors, 0 warnings**.
- Full solution: **0 errors, 2 warnings** — exactly the two pre-existing `xUnit2012` advisories
  at `tests/BoltSharp.Tests/Messages/BoltRequestDecodeTests.cs:71-72` (outside B5-A scope;
  actual count reported per commission §24). No new warnings attributable to B5-A (the
  deliberate `SYSLIB0039` and blocking-wait advisories introduced during development were
  eliminated before commit).

## 21. Historical manifest verification

`Verify-HistoricalManifests.ps1` verified **352/352** hashed entries across all 11 historical
evidence manifests, exactly matching the commissioned counts:

| Manifest | Verified |
| --- | ---: |
| B3-C-Q1 | 29/29 |
| B3-C implementation | 26/26 |
| B3-B-Q3 | 33/33 |
| B3-B-R2 | 29/29 |
| B3-B-Q2 | 38/38 |
| B3-B-Q1 | 12/12 |
| B3-A-Q1 | 14/14 |
| B3-A-R1 | 13/13 |
| B3-A-Q2 | 15/15 |
| B0-B producer | 24/24 |
| B0-B-Q1 | 119/119 |

No historical evidence artifact was modified. The three B3 owner decisions
(`docs/decisions/B3-{A,B,C}-OWNER-01-owner-acceptance.md`) are byte-identical to their committed
blobs (`logs/08-owner-decisions.txt`). Structured results: `historical-integrity.json`.

## 22. Changed paths

Production (commit `943774c` + fix `07bcc29`):

```text
src/BoltSharp/Transport/BoltTcpServer.cs
src/BoltSharp/Transport/BoltTcpConnection.cs
src/BoltSharp/Transport/BoltTcpServerOptions.cs
src/BoltSharp/Transport/BoltTlsServerOptions.cs
src/BoltSharp/Transport/BoltTransportDiagnostics.cs
```

Tests (commit `11e5c85`):

```text
tests/BoltSharp.Tests/Transport/TransportTestSupport.cs
tests/BoltSharp.Tests/Transport/TcpListenerTests.cs
tests/BoltSharp.Tests/Transport/TcpHandshakeTests.cs
tests/BoltSharp.Tests/Transport/TcpFramingTests.cs
tests/BoltSharp.Tests/Transport/TcpSessionIntegrationTests.cs
tests/BoltSharp.Tests/Transport/TcpGraphIntegrationTests.cs
tests/BoltSharp.Tests/Transport/TlsIntegrationTests.cs
tests/BoltSharp.Tests/Transport/ConnectionLifecycleTests.cs
tests/BoltSharp.Tests/Transport/TransportFailureTests.cs
tests/BoltSharp.Tests/Transport/TransportLimitsTests.cs
tests/BoltSharp.Tests/Transport/MultiClientTests.cs
```

Evidence (commits `4ff26e1`, `30c66a7`): `docs/reviews/B5-A-IMPL-01.md` and
`docs/reviews/evidence/B5-A-IMPL-01/` (19 tracked files). No B2/B3 source file was touched.

## 23. Remaining qualifications (B5-A-specific; all prior B3/B2 qualifications remain binding)

| # | Qualification |
| --- | --- |
| Q1 | **Reviewer independence:** this producer verification is same-executor; B5-A-Q1 must use a different reviewer executor/model. |
| Q2 | **TLS depth:** TLS configuration validates certificate+key and TLS 1.2+ policy, but cipher-suite policy, revocation checking (`checkCertificateRevocation=false` for server auth in this milestone), CA-trust chains, and certificate rotation are unconfigured — production deployment hardening is a later milestone. Client-side URI scheme semantics (`bolt+s://`, `bolt+ssc://`) are documented, not driver-qualified. |
| Q3 | **Cipher/protocol negotiation** uses platform `SslStream` defaults within the TLS 1.2+ floor; no explicit cipher policy surface. || Q4 | **Manifest handshake end-to-end coverage** is not asserted over TCP in this suite (B2 manifest support exists and is integrated via the same negotiator path); B5-A-Q1 should probe it explicitly. |
| Q5 | **Slow-provider responsiveness** remains bounded by the accepted B3-B limitation (no mid-flight RESET jump-ahead interruption); transport cancellation does not implement that behavior. |
| Q6 | **Write-timeout slow-client behavior** is implemented (bounded write + closure) but not exercised by a test that fills the OS loopback buffer; B5-A-Q1 may probe it. No output-buffer-growth qualification beyond B3-B's `MaxRecordsPerResponse` bound is claimed. |
| Q7 | **Session options passthrough:** the transport constructs each B3-B session with default `BoltSessionOptions` (only the verifier is injected); `MaxRecordsPerResponse`, `ServerName`, `ConnectionId` etc. are not yet configurable through `BoltTcpServerOptions`. |
| Q8 | **Request encode surface:** B3-A has no public request encoder; the test client re-implements the wire encode for the supported messages (documented test-only limitation, byte-verified against golden vectors). |
| Q9 | **Throughput/performance:** no benchmark claims are made anywhere in this milestone. |
| Q10 | **Shutdown guarantee:** `StopAsync` awaits active connections within `ShutdownTimeout`; connections that cannot finish in time are abandoned only after the bound (their sockets were signalled) — the bounded-wait semantics are tested, edge behavior under pathological load is not. |

## 24. B5-B interoperability dependencies

- Terminal-summary byte semantics: B3-B emits `SUCCESS {}` at exhaustion (no `has_more` field);
  the real-server t03 golden carries `has_more:false` — semantically equivalent, byte-different.
  Already qualified in B3-B; B5-B must reconcile at the byte level.
- The B3-A F-02 zero-field IGNORED live-driver obligation remains B5-B (raw-socket verification
  here does not discharge it).
- Manifest-handshake driver interplay (Q4) and TLS URI-scheme scenarios (Q2) need the official
  driver.
- HELLO/LOGON metadata vs driver expectations (`hints`, `connection_id`, patch negotiation
  absence) may surface driver-side behavioral differences that only B5-B can observe.

## 25. Implementation commits and trees

| Commit | Subject | Tree |
| --- | --- | --- |
| `943774c7388ad510090cdd6bf1664cd6e53ffb6b` | feat(boltsharp): add bounded TCP and TLS Bolt transport | `58c6d8fc4ad0f181583cd57ae807d6bd9e8e6c08` |
| `07bcc29ddba139a7a644e76d11a8b047e5b4bb2a` | fix(boltsharp): report transport connection diagnostics exactly once | `064c7eeca30597bf79a7a06ca7896bc37833796f` |
| `11e5c85d22a7b3832e6721c72c39c2cf96502c68` | test(boltsharp): add B5-A transport network integration tests | `0f88b037460d319430e650036481bf1f0b2af763` |
| `4ff26e1c2ae90508ade740cd5f8fd2f47d3a7a70` | docs(boltsharp): record B5-A transport integration evidence | `fb8eea7de308a6fe808feba15a13bb80951986e2` |
| `30c66a7` | docs(boltsharp): include B5-A evidence suite logs and TRX results | (final HEAD; see `git log`) |

## 26. Independent Q1 reviewer handoff

**To the B5-A-Q1 reviewer (independent executor/model required):**

1. Start from the evidence directory `docs/reviews/evidence/B5-A-IMPL-01/`; `README.md` lists
   every artifact and the reproduction driver (`Run-B5AImpl.ps1`).
2. The byte traces in `network-traces.txt` are regenerable; challenge them against the golden
   vectors in `TcpGraphIntegrationTests` and the B3-A/B0-B evidence.
3. Probe areas the producer suite does not fully close: manifest handshake over TCP (Q4),
   write-timeout slow-client buffer fill (Q6), TLS cipher/revocation policy (Q2/Q3), session
   options passthrough (Q7), shutdown edge behavior under pathological load (Q10).
4. The stop condition stands: this report is producer evidence, **not owner acceptance**, and
   B5-A-Q1 must not silently close any carried-forward B2/B3 qualification (§26 of the
   commission, including bounded `PULL(n=-1)`, single-owner execution, ReceiveBatch ownership,
   provisional public API surface, generic error classifications, no provider cancellation-token
   interface, exact-form query allowlisting, bounded graph materialization, typed parameter
   limits, terminal `has_more` semantics, and the B3-A F-02 live-driver obligation).
