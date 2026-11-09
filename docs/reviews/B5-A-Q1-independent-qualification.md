# B5-A-Q1 — Independent TCP/TLS Transport Qualification

- **Task:** `B5-A-Q1`
- **Role:** Independent adversarial qualification reviewer
- **Reviewed HEAD:** `21e649acae8b0f04c945a9899f314f4b345a9c3a` (commit: "docs(boltsharp): finalize B5-A-IMPL-01 report with complete commit chain", author/committer `BoltSharp Research Agent <agent@bolt-sharp.local>` 2026-11-09 11:48:50 +0800)
- **Reviewed tree:** `ec5cb4a8fe52ecf85dbcfe8fb1ff1b8a9561592a`
- **Evidence:** `docs/reviews/evidence/B5-A-Q1/`
- **Probes:** `docs/reviews/probes/B5-A-Q1/` (reviewer-owned console probe: own PackStream encoder/parser, own chunked-framing reader, own golden oracles — no oracle derives from the code under review)

> ## Reviewer independence — material limitation, prominently disclosed
>
> **Organizational/executor-session independence is NOT established.** The producer's report
> records its executor as "DeepSeek V4.1 Flash (provider nvidia, OpenCode)" and explicitly
> requires B5-A-Q1 to run on an independent executor/model. This review was executed by
> **GLM 5.3 Flash (provider nvidia, model `z-ai/glm-5.3-flash`)** — a *different model* than the
> recorded producer — **but within the same OpenCode session context and on the same machine as
> the producer work.** Therefore:
>
> - Model-level separation: claimed, based on the producer's recorded executor metadata vs the
>   reviewer's own runtime model identity.
> - Session/organizational separation: **not established** (same session, same operator, same
>   host). This review must not be counted as organizationally independent qualification.
> - Mitigations applied: all probes were constructed from the normative Bolt 5.8 specification
>   and the accepted B2/B3 contracts, with byte-level oracles taken from the *historical*
>   B0-B/B3-A evidence (not from producer test expectations); probe failures found during the
>   review were three separate reviewer-side bugs (documented in §10/F-04) which were fixed in
>   the *probe*, never in production code.

---

## 1. Preflight

Branch `main`; HEAD and tree exactly as commissioned (verified via full commit metadata, not
abbreviated hashes); one worktree (`D:/projects/bolt-sharp`); working tree clean; no prior
B5-A-Q1 work existed (`docs/reviews/B5-A-Q1*`, `probes/B5-A-Q1`, `evidence/B5-A-Q1` all absent);
branch `ahead 50`, never pushed; B3-C-OWNER commit `8535806` is an ancestor of HEAD.

## 2. Evidence integrity and implementation isolation

**B5-A producer manifest** (committed at HEAD): 18 declared `evidence_files` entries + the
manifest itself = **19 files, exactly as the producer claimed**; all 18 hashes verified against
working-tree bytes.

**Historical manifests** — every hashed entry across all sections (`evidence_files`,
`probe_source_files`, `report_files`, `changed_source_and_report_files`, `files`, `reports`)
verified by raw-byte SHA-256:

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
| **Total** | **352/352** |

**Implementation isolation** (`git diff --name-status 8535806..21e649a`): exactly 5 new
production files (`src/BoltSharp/Transport/`), 11 new test files
(`tests/BoltSharp.Tests/Transport/`), and the B5-A report/evidence. **Zero changes** to B2
handshake/framing, B3-A messages, B3-B session, B3-C backend, PackStream, `*.csproj`, `*.slnx`,
`docs/decisions/*` or any historical evidence. No new runtime dependencies.

## 3. Producer test reproduction

| Suite | Expected | Observed (reviewer, fresh processes) |
| --- | ---: | ---: |
| Core total | 1,217 | **1,217** (0 failures) |
| B5-A transport | 80 | **80** (×3 fresh-process repeats, stable) |
| B3-A messages | 98 | **98** |
| B3-B sessions | 119 | **119** |
| B3-C backend | 94 | **94** |
| B0-B driver integration | 19 | **19** |
| Production build | 0 err/0 warn | **0/0** |
| Solution build | 0 err / 2 warn | **0/2** (the two pre-existing `xUnit2012` at `BoltRequestDecodeTests.cs:71-72`) |

Producer test inspection (`tests/BoltSharp.Tests/Transport/`): assertions are byte-level where
golden vectors exist; synchronization uses bounded `WaitUntil` polling (no uncontrolled sleeps);
catch blocks are scoped and commented (socket-closure tolerance, not assertion swallowing). No
weak assertions found.

## 4. Independent probe results (reviewer-owned)

`B5AQ1Probe --all` (`probe-all.log`): **165 checks — 162 PASS, 3 classified INCONCLUSIVE (§D),
0 unexpected failures.**

### §7 TCP listener — verdict: SUPPORTED (20/20 PASS)

Default bind = IPv4 loopback + ephemeral port; plaintext default (Tls null); default verifier
null (B3-B fail-closed preserved); **plaintext `IPAddress.Any` and `IPv6Any` binds refused at
construction**; lifecycle start/stop/idempotent-stop; capacity 2 admission + excess refusal +
refusal counting; accepted clients unaffected by refusal; no acceptance after shutdown;
active-count drains to zero; completion counted exactly once.

### §8 Critical A: Manifest v1 handshake over TCP — verdict: SUPPORTED (26/26 PASS)

The producer had no TCP manifest test (its Q4). Independent reviewer evidence closes this gap:

- Marker `00 00 01 FF` answered with a manifest offer (not the zero word): **A1**.
- Offer byte-exact: marker + VarInt(count=1) + range word `00 00 08 05` + capabilities `00`: **A2**.
- Valid selection (`00 00 08 05` + VarInt caps 0) transitions into normal Bolt messages
  (HELLO SUCCESS decoded by the reviewer parser): **A2**.
- One-byte-at-a-time manifest preamble negotiates: **A3**.
- Manifest preferred over conventional fallbacks (5.8/5.9/5.7 proposals) works: **A4**.
- Unoffered selection (6.0) → `BoltHandshakeProtocolException` → connection closed, listener
  healthy afterwards: **A5**.
- Version-range proposal (Bolt 4.3 style, `00 02 08 05` covering 5.8-5.6) selects 5.8: **A6**.
- Selection with unoffered capability bits refused and closed: **A7**, **A11**.
- **Selection + framed HELLO pipelined in ONE write: HELLO processed correctly** — no byte
  discard or misclassification beyond the manifest boundary: **A8**.
- Unsupported-only proposals → zero word + close: **A9**.
- Disconnect before final selection: no listener fault, listener healthy: **A10**.
- `manifestV1Enabled: false` → marker treated as an unsupported version (zero reply): **A12**.

The transport's default `BoltHandshakeOptions` (`Create([5.8])`) enables Manifest v1 by default,
and the integration is correct end-to-end over real sockets.

### §9 Critical B: TLS configuration and trust — verdict: SUPPORTED (22/22 PASS)

- TLS 1.3 negotiated (`TLS_AES_256_GCM_SHA384` observed); TLS 1.2 succeeds; **TLS 1.1 refused**
  (server policy; platform: "The function requested is not supported"); `Validate()` refuses
  Ssl3/Tls/Tls11 configurations.
- Strict-chain client rejects the self-signed server certificate; server closes the connection —
  **no downgrade path exists**.
- Hostname mismatch rejected by a name-validating client.
- Full Bolt conversation (HELLO/LOGON/RUN/PULL → `B171912A`) inside the authenticated TLS stream.
- Plaintext Bolt handshake against a TLS-only listener fails closed; server-visible TLS failures
  are counted (`TlsHandshakeFailures`).
- TLS authentication timeout (600 ms) closes unauthenticated connections; client abort during
  TLS contained (active→0, no listener fault).
- Null certificate refused at construction; key-less certificate refused at `Validate()`.

**Certificate revocation assessment (§9.1-9.6):** `checkCertificateRevocation=false` is the
*server-side* parameter of `SslStream.AuthenticateAsServerAsync`, which governs validation of a
**client** certificate chain during mutual TLS. `BoltTlsServerOptions.ClientCertificateRequired`
defaults to `false` and B5-A never enables it; authentication is exclusively B3-B LOGON.
Therefore **no required certificate validation is bypassed** — there is no client certificate to
revoke-check. Server-certificate validation is the client's responsibility, proven by the
strict-validation probe (C2) and hostname probe (C3). The producer's classification is accurate.

### §10 Critical C: Slow-client backpressure — verdict: **INCONCLUSIVE** (probe evidence, Q6 remains open)

Probe design: reviewer provider streams 250 records × 1 MB (262,155,007 bytes observed at the
client) in one bounded pass; the client sets an 8 KB receive buffer and never reads
(`Poll`-without-`Read`); the server's `WriteTimeout` was configured to 2 s.

Observed: **Windows loopback absorbed the entire 262,155,007-byte response into kernel buffers
without a single client read** — the write completed (`MessagesProcessed=4`), no closure, the
WriteTimeout never engaged. Escalation from 65 MB to 262 MB made no difference.

Per commission §10's explicit rule ("If the experiment never actually blocks a write, classify
it as INCONCLUSIVE rather than PASS"):

- **D1/D2: INCONCLUSIVE** — the WriteTimeout mechanism for a *genuinely blocked* write could not
  be exercised on this platform through the public seam. Producer Q6 (slow-client write-fill
  gap) **remains open**, now with the additional documented constraint that Windows loopback
  kernel buffering defeats naive saturation attempts at these scales (a B5-A-Q1 reviewer handoff
  note for any future attempt: saturation requires either non-loopback transport, socket-buffer
  manipulation on the server's accepted socket (not exposed), or multi-gigabyte responses).
- **D3-D6: PASS** — the provider was consulted exactly once; a healthy concurrent client
  completed `RETURN 42 AS answer` byte-identically while the oversized response was in flight;
  the listener stayed healthy; shutdown completed within its documented bound.

**Memory-bounding observation (QUALIFICATION note, no defect):** the transport encodes an entire
bounded response pass (up to `MaxRecordsPerResponse` records) into one buffer before writing —
bounded by the accepted B3-B mechanism, but record size × records-per-pass is not otherwise
capped (262 MB here). This is the inherited B3-B bounded-pass behavior, not a new B5-A defect.

### §11 Critical D: Shutdown and cancellation races — verdict: SUPPORTED (19/19 PASS)

Stop during accept (bounded, no unobserved tasks); stop during TLS handshake (accepted client
signalled — deterministic via `ConnectionsAccepted` wait; a backlog-only client that was never
accepted has OS-defined fate and is outside transport ownership); stop during Bolt handshake
(no leaked connection); stop during partial chunk read (read cancelled/closed, accounted); stop
with three active clients (all awaited, `accepted == completed`); four simultaneous disconnects
(all accounted, no double-accounting); GOODBYE + dispose (single completion); stop during active
RUN/PULL (bounded, active→0).

### §12 Critical E: Session-options passthrough — verdict: QUALIFICATION confirmed (Q7 accurate)

Source fact (verified): `BoltTcpConnection` constructs `new BoltSessionOptions { Verifier = ... }`
only — `MaxRecordsPerResponse`, `ServerName`, `ConnectionId` etc. are **not** configurable
through `BoltTcpServerOptions`. The reviewer verified over TCP that the **default**
`MaxRecordsPerResponse` (1000) is honored (2,500 rows → 2 has_more passes of 1,000 + terminal
1,000-less pass... observed: 2,500 records total across 2 has_more passes + terminal summary
without `has_more`). The default is honored; customization is not exposed. This matches the
producer's Q7 exactly: a bounded configuration limitation, not a defect in a claimed capability.

## 5-7. Framing, queries, TLS traces (§13-§14) — verdict: SUPPORTED

- **Framing (10/10):** single chunk; header split across writes; payload split at every byte
  boundary; many small chunks; NOOP between messages is keep-alive (next request processed, no
  phantom request); truncated header/payload + EOF close safely; incomplete request never
  executes (provider never consulted); oversized cumulative message refused before accumulation;
  two messages in one write preserve order.
- **Scalar:** reviewer encoder reproduces the authentic golden request bytes
  (`B310D013…A0A0`, `B13FA1816EC903E8`) — the oracles are historical, not derived from the code
  under review. RUN SUCCESS fields=[answer]; RECORD [42] byte-identical (`B171912A`); terminal
  SUCCESS `B170A0` with no `has_more`; connection reusable.
- **Graph:** RECORD byte-identical to the accepted golden vector; arity 3; Alice `1/n:1`, Bob
  `2/n:2`, KNOWS `1/r:1`, direction Alice→Bob, `since=2024` (decoded via the reviewer parser).
- **Parameter:** `RETURN $value AS answer` with integer, string (Unicode), booleans, null,
  float, list and nested-map parameters — all preserved through the full socket path without
  text interpolation.
- **TLS:** full scalar conversation over TLS 1.3 (C1).

## 8. Authentication and protocol-state behavior (§15) — verdict: SUPPORTED

RUN before LOGON → FAILURE + terminal close (accepted B3-B semantics — this is the *protocol
violation* branch, not IGNORED); missing verifier → LOGON FAILURE; wrong credentials → FAILURE
(`Neo.ClientError.Security.Unauthorized`), terminal close, **no credential echo**; throwing
verifier → FAILURE, sentinels contained; LOGOFF → SUCCESS, then RUN → terminal protocol
violation (LOGOFF outside READY after a completed query returns to READY — verified); unsupported
query → recoverable FAILURE → pipelined work `B0 7E` (byte-exact) → RESET → READY; write query
refused (recoverable); invalid RUN extra (`db` wrong type) → codec rejection, terminal fail
closed; PULL before RUN → FAILURE; RUN while STREAMING → FAILURE (cursor held open via
PULL(1) of a 2-row result); GOODBYE with active result → closure, no fabricated response.

## 9. Pipelining and terminal closure (§16) — verdict: SUPPORTED

HELLO+LOGON+RUN+PULL in one write → exact response order; unsupported RUN + PULL + RESET +
supported RUN + PULL in one write → FAILURE, `B07E`, RESET SUCCESS, recovery; GOODBYE+RUN in one
write → closure with no fabricated response and no extra bytes.

## 10. Fail-closed resource ownership over TCP (§17) — verdict: SUPPORTED

The mandatory sequence over real TCP with a reviewer fault provider:

- RUN opens operation #1 (`PrepareCount=1`); PULL drains it (RECORD `B171912A` received);
- disposal throws → **FAILURE reported (never SUCCESS)**, session DEFUNCT, socket closes;
- pipelined RESET/RUN afterwards are never processed (`MessagesProcessed=4`: HELLO, LOGON, RUN,
  PULL only); `PrepareCount=1`, `DisposeAttempts=1` — **no second operation, no disposal retry**.
- Normal exhaustion retires the operation successfully (SUCCESS) — the disposal-failure refusal
  is specific to the failed retirement, per the accepted B3-B contract.

## 11. Multi-client isolation and admission (§18) — verdict: SUPPORTED

Interleaved independent sessions (A RUNs while B PULLs); parameter isolation (`from-A` returned
only to A); B closes while A continues; reconnection while the listener remains active; capacity
boundary enforcement (§7).

## 12. Negative controls (§19) — verdict: ALL DETECT (8/8)

| Control | Result |
| --- | --- |
| N1 negotiation oracle (any wrong version reply would fail) | detected-oracle PASS |
| N2 TLS downgrade (plaintext must get no Bolt reply) | PASS — refused |
| N3 external plaintext bind (IPAddress.Any) | PASS — refused |
| N4 oversized message must not execute | PASS — refused |
| N5 partial message must not execute (provider-open counter = 0) | PASS |
| N6 pipelining order oracle (out-of-order would fail) | PASS — exact order |
| N7 failed disposal must not permit reuse (prepare/dispose counters) | PASS — 1/1 |
| N8 credential sentinel scan in diagnostics | PASS — absent |

## 13. Confirmed findings

| ID | Severity | Finding |
| --- | --- | --- |
| **B5-A-Q1-F01** | DOCUMENTATION | `BoltTcpConnection.WriteResponsesAsync` (lines 267-275) contains a write loop that claims partial-write robustness but assigns `written = payload.Length` after the first call — a hypothetical partial write would silently truncate the response. On the shipped paths (`NetworkStream`/`SslStream` on blocking sockets) writes are complete-or-throw, so no runtime impact was observable in any probe. Smallest correction: loop on the actual returned count or replace the loop with a single write and a comment stating the blocking-socket contract. Not acceptance-blocking. |
| **B5-A-Q1-F02** | INCONCLUSIVE (coverage gap) | The slow-client backpressure experiment never blocked a write: Windows loopback absorbed 262,155,007 bytes into kernel buffers without a single client read, so `WriteTimeout` enforcement for a genuinely blocked write remains unverified. Producer Q6 stands. Not acceptance-blocking; recorded as the §10 evidence gap with the drain-count evidence. |
| **B5-A-Q1-F03** | QUALIFICATION (verified accurate) | Session options beyond the credential verifier are not configurable through `BoltTcpServerOptions` (verified from source + over TCP). Matches producer Q7; the default `MaxRecordsPerResponse` IS honored over TCP (F1). |
| **B5-A-Q1-F04** | DOCUMENTATION (reviewer-side) | Three probe defects were found and fixed **in the probe only** during this review: (a) double-send of the manifest handshake in the probe client; (b) a 3-byte chunk header written by an early scratch diagnostic (its `00 00` prefix was correctly interpreted by the transport as a NOOP — the transport's NOOP handling is what exposed the bug); (c) a PackStream map header using the string base (0x82) instead of the map base (0xA2), correctly rejected by the B3-A codec as `InvalidPackStreamValue`. Each transport rejection was verified correct; the probe was fixed and re-run. |
| — | NOTE | A client-side TLS certificate *rejection* manifests on the server as a client abort (B0-A clean disconnect), so `TlsHandshakeFailures` does not increment for that case; server-visible handshake failures do increment (C5). Classification nuance, not a defect. |
| — | NOTE | A TCP client still in the listener backlog when `StopAsync` runs is never accepted; its fate (reset or hang) is OS-defined and outside the transport's ownership. Accepted connections are signalled (E2/E5). |

## 14. Remaining qualifications

- **Q1 (reviewer independence): REMAINS OPEN** — see the disclosure at the top of this report.
  Organizational independence was not established; model-level separation is claimed per
  recorded metadata.
- **Q4 (manifest-over-TCP coverage): CLOSED by this review** — 26 independent TCP checks, all
  passing, including pipelined HELLO preservation.
- **Q6 (slow-client write-fill): REMAINS OPEN** — INCONCLUSIVE (F02), with platform evidence.
- **Q7 (session-options passthrough): CONFIRMED accurate** as a bounded limitation (F03).
- **Q2/Q3 (TLS policy/cipher/revocation depth): REMAIN OPEN** for production deployment
  (revocation model verified non-bypassing at the current trust model; cipher policy remains
  platform-default).
- **Q5 (slow-provider limitation), Q8 (test-only request encoder), Q9 (no performance claims),
  Q10 (shutdown edge behavior): stand as written**; Q10's documented bound was additionally
  verified under eight race scenarios (§11) — the edge behaviors beyond those scenarios remain
  qualified.
- **All B2/B3 inherited qualifications and B5-B obligations stand unchanged** (zero-field
  IGNORED live-driver verification, terminal `has_more` byte semantics, driver metadata
  behavior, TLS URI schemes, driver error classification).

## 15. Formal reviewer disposition

### **B5-A-Q1 — SUPPORTED WITH QUALIFICATIONS**

Basis (all independently verified over real loopback sockets with reviewer-owned oracles):

1. Real TCP and TLS query execution (scalar golden bytes, graph golden bytes, typed parameters).
2. B2/B3 protocol integration preserved (zero production changes outside the transport; reuse
   of the accepted negotiator/framing/codec/session/backend).
3. Manifest v1 negotiation over TCP is correct end-to-end, including pipelined HELLO
   preservation, refusal of unoffered selections/capabilities, and fragmentation tolerance.
4. Handshake and framing behavior correct within the claimed scope, including NOOP semantics,
   truncation fail-closed, oversize refusal and byte-exact output framing.
5. TLS fails closed (no downgrade; strict clients reject; timeouts bounded; deprecated
   protocols refused; revocation flag does not bypass a required check).
6. Connection admission, message-size limits and capacity refusal enforced.
7. No accepted request silently lost on a usable connection (pipelining oracles).
8. Per-connection session ownership safe (sequential; counters prove exactly-once retirement).
9. Shutdown/disconnect meet the documented guarantees across eight race scenarios.
10. Failed retirement permits no reuse (mandatory §17 sequence verified with counters).
11. No material unresolved defect: the only findings are one documentation-level code-hygiene
    item (F01), one honestly-inconclusive coverage gap (F02), and the confirmed Q7 limitation
    (F03) — none invalidate the required B5-A scope.
12. Remaining limitations are explicit (§14).

This disposition is **conditional on the independence limitation being accepted by the owner**:
organizational independence was not established; model-level separation is claimed per recorded
metadata. If the owner requires strict organizational independence, this review must be repeated
by a fully separate execution environment.

## 16. Owner recommendation

1. Accept B5-A-Q1 as **SUPPORTED WITH QUALIFICATIONS** with the independence caveat above.
2. Carry F01 (write-loop hygiene) as a bounded remediation candidate for the next authorized
   implementation milestone — one-line-or-two fix, no protocol impact.
3. Keep Q6 open and attach F02's platform evidence (loopback buffering defeats saturation at
   262 MB) to any future backpressure attempt.
4. Treat Q4 as closed with this evidence; keep Q1/Q2/Q3/Q5/Q7/Q8/Q9/Q10 open as written.
5. All B5-B obligations remain deferred and untouched.

---

*Reviewer: GLM 5.3 Flash (provider nvidia) — see the independence disclosure above.
Evidence: `docs/reviews/evidence/B5-A-Q1/` (probe log, per-suite regression logs, integrity
verification, SHA-256 manifest). Probes: `docs/reviews/probes/B5-A-Q1/`.*
