# B5-A-OWNER-01 — Formal Owner Acceptance: TCP/TLS Transport and Bolt 5.8 Network Integration

## 1. Decision metadata

| Field | Value |
| --- | --- |
| Task | `B5-A-OWNER-01` |
| Role | Owner-decision recording agent (documentation-only acceptance) |
| Milestone | B5-A — TCP/TLS transport and Bolt 5.8 network integration |
| Protocol target | Bolt 5.8 / PackStream 1.0 (bounded) |
| Proposed disposition verified | **B5-A — ACCEPTED WITH QUALIFICATIONS — CLOSED** |
| Decision date | 2026-11-09 |
| Starting HEAD | `d57a9e1695be64d59676b0f05e002fd6bd331dd7` |
| Starting tree | `879f4df85574b7120e6841974de8f719f9268431` |
| Next engineering milestone | B5-B — official Neo4j .NET driver interoperability (not authorized by this decision) |

## 2. Starting state verification

Recorded from the canonical checkout before this decision was written:

- Branch `main`; exactly one worktree (`D:/projects/bolt-sharp`).
- HEAD exactly `d57a9e1695be64d59676b0f05e002fd6bd331dd7` ("docs(boltsharp): independently
  qualify B5-A TCP TLS transport", author/committer `BoltSharp Research Agent
  <agent@bolt-sharp.local>` 2026-11-09); tree exactly `879f4df85574b7120e6841974de8f719f9268431`.
- Working tree and index clean; no prior `docs/decisions/B5-A-OWNER-01-owner-acceptance.md`.
- Branch `main` is **51 commits ahead of `origin/main`**; no fetch or push was performed.
- B5-A implementation commits and the Q1 reviewer commit are present in ancestry (§5).
- B3-C owner acceptance `853580659cc57ad959c026800c1e17de44cdddc1` is an ancestor of HEAD
  (`git merge-base --is-ancestor` exit 0), which places all three B3 owner acceptances in the
  lineage.

## 3. Acceptance authority and scope

This document is the formal B5-A acceptance record. The producer implementation report and the
Q1 qualification report are **evidence inputs**; this decision is the **acceptance authority**.
The accepted scope is exactly §21 (explicit accepted scope) of this document, restricted by the
non-claims in §22. Nothing in this decision authorizes production changes, remediation, B5-B
implementation, B4/B6 work, package publication or repository push.

## 4. Evidence reviewed

Read in full before recording this decision:

- `docs/reviews/B5-A-IMPL-01.md` (producer implementation report, §1-§26).
- `docs/reviews/B5-A-Q1-independent-qualification.md` (reviewer qualification, including its
  prominently disclosed reviewer-independence limitation).
- `docs/reviews/evidence/B5-A-IMPL-01/` (19 files) and `docs/reviews/evidence/B5-A-Q1/`
  (11 files), manifests verified per §6.
- `docs/reviews/probes/B5-A-Q1/` (reviewer probe source: independent PackStream encoder/parser,
  independent chunked-framing reader, independent golden oracles).
- `docs/decisions/B3-A-OWNER-01-owner-acceptance.md`,
  `docs/decisions/B3-B-OWNER-01-owner-acceptance.md`,
  `docs/decisions/B3-C-OWNER-01-owner-acceptance.md` (byte-identical at HEAD; §6).
- `docs/boltsharp-spec.md`, `docs/implementation-tasks.md`, `docs/compatibility-target.md`;
  B0-A/B0-B reports; B3-B-Q3 and B3-C-Q1 qualifications; accepted B2 handshake/framing.
- Actual transport source: `src/BoltSharp/Transport/BoltTcpServer.cs`,
  `BoltTcpConnection.cs`, `BoltTcpServerOptions.cs`, `BoltTlsServerOptions.cs`,
  `BoltTransportDiagnostics.cs` — including direct source verification of the F01 write-loop
  shape (§15).
- Producer tests (`tests/BoltSharp.Tests/Transport/`, 80 tests) and reviewer probes
  (`docs/reviews/probes/B5-A-Q1/B5AQ1Probe/`).

## 5. Qualification chronology

| Stage | Disposition | Recorded revision |
| --- | --- | --- |
| B3-A | ACCEPTED WITH QUALIFICATIONS — CLOSED | owner acceptance in ancestry |
| B3-B | ACCEPTED WITH QUALIFICATIONS — CLOSED | owner acceptance in ancestry |
| B3-C | ACCEPTED WITH QUALIFICATIONS — CLOSED | `8535806` (ancestor of HEAD, verified) |
| B5-A-IMPL-01 | IMPLEMENTED, READY FOR INDEPENDENT B5-A-Q1 QUALIFICATION | final producer revision `21e649acae8b0f05e002fd6bd331dd7` |
| B5-A-Q1 | SUPPORTED WITH QUALIFICATIONS | reviewer revision `d57a9e1695be64d59676b0f05e002fd6bd331dd7` |
| **B5-A-OWNER-01** | **ACCEPTED WITH QUALIFICATIONS — CLOSED** (this decision) | starting HEAD `d57a9e1` |

Producer implementation-chain commits (verified present, in order, on `main`):

1. `943774c7388ad510090cdd6bf1664cd6e53ffb6b` — feat(boltsharp): add bounded TCP and TLS Bolt
   transport (tree `58c6d8fc4ad0f181583cd57ae807d6bd9e8e6c08`).
2. `07bcc29ddba139a7a644e76d11a8b047e5b4bb2a` — fix(boltsharp): report transport connection
   diagnostics exactly once (tree `064c7eeca30597bf79a7a06ca7896bc37833796f`).
3. `11e5c85d22a7b3832e6721c72c39c2cf96502c68` — test(boltsharp): add B5-A transport network
   integration tests (tree `0f88b037460d319430e650036481bf1f0b2af763`).
4. `4ff26e1c2ae90508ade740cd5f8fd2f47d3a7a70` / `30c66a7` — B5-A producer evidence.
5. `21e649acae8b0f04c945a9899f314f4b345a9c3a` — B5-A-IMPL-01 report finalization (reviewed HEAD).

The B5-A implementation baseline was the B3-C owner acceptance
`853580659cc57ad959c026800c1e17de44cdddc1` (tree `a00ab7b189608da4ebf5a61ead6557e402d0bac4`),
as recorded in the producer report.

## 6. Manifest integrity

Independently re-verified during this decision (raw-byte SHA-256, all hashed sections):

- **B5-A producer evidence:** 18 declared entries, all matched; + the manifest itself = 19 files.
- **B5-A-Q1 reviewer evidence:** 10/10 entries matched (11 files including the manifest).
- **Eleven historical packages, 352/352 total:** B3-C-Q1 29/29; B3-C implementation 26/26;
  B3-B-Q3 33/33; B3-B-R2 29/29; B3-B-Q2 38/38; B3-B-Q1 12/12; B3-A-Q1 14/14; B3-A-R1 13/13;
  B3-A-Q2 15/15; B0-B producer 24/24; B0-B-Q1 119/119.

No historical evidence was rewritten by B5-A, B5-A-Q1, or this decision. All three B3 owner
decision documents remain byte-identical at HEAD (verified earlier by blob comparison and again
by the absence of any diff touching `docs/decisions/` in `git diff 8535806..d57a9e1`).

**Change isolation** (`git diff --name-status 8535806..d57a9e1`): exactly five new transport
production files, eleven new transport test files, the producer report/evidence, and the additive
Q1 reviewer report/probes/evidence. **No changes** to B2, B3-A, B3-B, B3-C, PackStream,
`*.csproj`, `*.slnx`, accepted owner decisions, historical artifacts, or runtime dependencies.

## 7. Regression results

Committed results (producer and Q1), re-confirmed by a **fresh read-only execution** performed
during this decision (commands and results recorded here; no new evidence files created):

| Suite | Command (canonical form) | Fresh result |
| --- | --- | --- |
| Production build | `dotnet build src/BoltSharp/BoltSharp.csproj -c Debug --no-incremental --nodeReuse:false` | Build succeeded, 0 warnings, 0 errors |
| Core total | `dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --no-build --nodeReuse:false` | **Passed! 1,217 / 1,217** |
| B5-A transport | `... --filter 'FullyQualifiedName~BoltSharp.Tests.Transport'` (fresh processes; producer ×10, Q1 ×3) | **80/80** |
| B3-A messages | filter `BoltSharp.Tests.Messages` | **98/98** |
| B3-B sessions | filter `BoltSharp.Tests.Sessions` | **119/119** |
| B3-C backend | filter `BoltSharp.Tests.Backend` | **94/94** |
| B0-B driver integration | `dotnet test tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj -c Debug --nodeReuse:false` | **19/19** |
| Solution build | `dotnet build BoltSharp.slnx -c Debug --no-incremental --nodeReuse:false` | 0 errors; **2 warnings** (the two historical `xUnit2012` advisories at `tests/BoltSharp.Tests/Messages/BoltRequestDecodeTests.cs:71-72`, outside B5-A scope) |

The two xUnit2012 advisories are pre-existing and remain outside B5-A scope.

## 8. TCP listener acceptance — ACCEPTED

Accepted within the independently verified scope (producer report §3, §5, §7; Q1 §7 — 20/20
checks; reviewer probe `ProbeListener`):

1. Asynchronous TCP listener (`TcpListener`-based, single-use lifecycle).
2. Loopback-first plaintext configuration (default bind `127.0.0.1`).
3. Ephemeral port support (`Port = 0`; bound endpoint reported).
4. Bounded connection admission (`MaxConnections`, default 100).
5. Correct capacity refusal (immediate close on acceptance, refusal counted).
6. Per-connection processing (one pipeline task per connection).
7. Independent session ownership (own B3-B session, framing state, cancellation context).
8. Clean connection disposal (exactly-once session cleanup and diagnostics accounting).
9. Listener failure isolation (accept-loop survives per-connection faults).
10. Bounded shutdown within documented operating conditions (`StopAsync` awaited all active
    connection tasks within `ShutdownTimeout` in eight race scenarios).

**External plaintext binding is refused by the accepted configuration**: `BoltTcpServerOptions.Validate()`
throws for plaintext binds to `IPAddress.Any` and `IPAddress.IPv6Any` (verified from source and
by probe checks B3). This is loopback-loopback testing only; **no production deployment safety
is inferred** (see §22 non-claims).

## 9. TLS acceptance — ACCEPTED (bounded scope)

Verified by producer tests (`TlsIntegrationTests`) and Q1 (22/22 checks, `ProbeTls`):

- Explicit TLS configuration; certificate required with private key (null refused at
  construction; key-less refused at `Validate()`).
- TLS negotiation strictly before any Bolt byte.
- TLS 1.2 and TLS 1.3 operation (1.3 observed with `TLS_AES_256_GCM_SHA384`).
- Deprecated protocol versions (Ssl3/Tls/Tls11) refused by `Validate()`; TLS 1.1 client
  handshake refused by the server policy.
- Correct client rejection of untrusted certificates (strict-chain probe); server closes — no
  downgrade path exists.
- Hostname validation behavior verified (name-validating client rejects a mismatched host).
- TLS handshake timeout (bounded phase) closes unauthenticated connections.
- Complete Bolt query execution inside the authenticated TLS stream (RECORD `B1 71 91 2A`).
- Plaintext Bolt handshake against a TLS-only listener fails closed; server-visible TLS
  failures are counted in diagnostics.

**Certificate revocation:** `checkCertificateRevocation=false` is the server-side parameter of
`SslStream.AuthenticateAsServerAsync`, which governs validation of a *client* certificate chain
during mutual TLS. `ClientCertificateRequired` defaults to `false` and is never enabled; the
only authentication boundary is B3-B LOGON. Therefore no required certificate check is bypassed.
Server-certificate validation is performed by the connecting client (proven by the
strict-validation and hostname probes). Retained qualifications: platform-dependent TLS
protocols, cipher negotiation (platform defaults within the TLS 1.2+ floor), certificate policy
depth, revocation handling, production deployment configuration, and client trust configuration
are **not** qualified. **No security certification and no comprehensive TLS deployment audit are
claimed.**

## 10. Handshake and manifest acceptance — ACCEPTED

**Conventional handshake.** Correct Bolt identification; the conventional Bolt 5.8 reply
`00 00 08 05` verified byte-exactly; unsupported revisions answered with the zero word and
closed; fragmented (one-byte-at-a-time) handshake negotiates; no handshake-byte loss —
handshake-plus-HELLO in a single network write is preserved (B2 consumes exactly the handshake
bytes).

**Manifest v1 — formal Q4 disposition.** The producer's qualification Q4 (exact wording from the
committed producer report §23: *"Manifest handshake end-to-end coverage is not asserted over TCP
in this suite (B2 manifest support exists and is integrated via the same negotiator path);
B5-A-Q1 should probe it explicitly."*) was targeted by Q1 with 26/26 independent TCP checks
(`ProbeManifest`): the marker `00 00 01 FF` answered with an offer (marker + VarInt count=1 +
range `00 00 08 05` + capability mask zero); a valid final selection transitions into normal
Bolt messages; an unoffered selection (6.0) and unsupported capability bits are refused and the
connection closed with continued listener health; one-byte fragmentation negotiates; the final
selection pipelined with HELLO in one write is preserved; `manifestV1Enabled: false` treats the
marker as an unsupported version; unsupported-only proposals get the zero word.

> **Q-B5A-04 — CLOSED BY VERIFIED Q1 EVIDENCE.** The closure applies to the B5-A raw-TCP
> manifest coverage gap only. It does **not** establish official Neo4j.Driver manifest
> interoperability, which remains a B5-B obligation (§23).

## 11. Framing and pipelining acceptance — ACCEPTED

Accepted within the independently verified scope (Q1 §13 — 10/10 checks; producer tests):

chunked message reading; fragmented two-byte headers; fragmented payloads at every byte
boundary; multi-chunk messages; multiple requests per network write with exact ordering; NOOP
handling (a zero-length chunk between messages is keep-alive, never an application request);
correct `00 00` termination; truncated header/payload refusal (fail closed); cumulative
oversize limits enforced by the reused receiver-side B2 reader before allocation; no execution
of incomplete requests (provider never consulted); correct output framing (RECORD `B1 71 91 2A`
byte-identical to the accepted golden vector); ordered session responses; no work after GOODBYE
or terminal DEFUNCT.

Framing **reuses the accepted B2 implementation** (`BoltMessageReader`/`BoltMessageWriter`);
no second protocol engine was created. **No general network fuzzing and no production load
certification are claimed.**

## 12. B3 integration and reference queries — ACCEPTED

Complete network execution verified through the accepted pipeline:
TCP/TLS → B2 handshake/framing → B3-A decode → B3-B session → B3-C reference provider → B3-B
ordered responses → B3-A encode → B2 framing → TCP/TLS, with zero production changes to the B2/
B3 layers.

- Scalar `RETURN 42 AS answer`: RUN/PULL request bytes match the authentic golden vectors
  (`B3 10 D0 13 … A0 A0`, `B1 3F A1 81 6E C9 03 E8`); RECORD output `B1 71 91 2A` verified
  byte-compatible by both producer and reviewer oracles.
- Graph `MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b`: RECORD byte-identical to the
  accepted golden vector; Alice `1`/`n:1`, Bob `2`/`n:2`, KNOWS `1`/`r:1`, direction Alice→Bob,
  `since=2024`, record arity 3.
- Parameters: `RETURN $value AS answer` with integer, string (Unicode), boolean (true/false),
  null, float, list and nested-map values — all preserved through the full socket path.
- Pagination: 2,500-row result served in bounded passes (`has_more=true` between passes,
  terminal summary without `has_more`), honoring the default `MaxRecordsPerResponse` (§17).

No general Cypher execution and no arbitrary parameterized queries are claimed (§22).

## 13. Authentication and protocol-state preservation — ACCEPTED

Verified over raw TCP by Q1 (§15) and producer tests: HELLO (SUCCESS with `server`,
`connection_id`, `hints`); LOGON (SUCCESS with accepted credentials); LOGOFF (SUCCESS from
READY; protocol violation when a cursor is open); invalid authentication refusal
(`Neo.ClientError.Security.Unauthorized`, terminal); missing-verifier fail-closed (production
default: verifier null → LOGON rejected); authentication-exception containment (throwing
verifier fails closed, no credential material leaks); RUN-before-LOGON refusal (terminal
protocol violation per accepted B3-B); RUN/PULL sequencing; unsupported-query FAILURE
(recoverable); FAILED → pipelined work IGNORED with the byte-exact zero-field encoding
**`B0 7E`** → RESET → READY; GOODBYE without fabricated responses.

The zero-field IGNORED encoding was confirmed over raw TCP. **The B3-A F-02 official-driver
live-IGNORED obligation remains open until B5-B.** B3-B's single-owner session semantics are
preserved (sequential session calls within one connection pipeline; no concurrent session use).

## 14. Fail-closed resource ownership — ACCEPTED (mandatory, binding)

The B3-B normative rule remains binding, now verified at the network boundary:

**After an unsuccessful operation-retirement attempt, the connection becomes permanently DEFUNCT
and cannot be restored through RESET.**

Q1 verified the network-level sequence over real TCP with a reviewer fault provider:

```text
RUN            → operation #1 opens (PrepareCount = 1)
PULL           → operation #1 exhausts; disposal throws
RESET, RUN     → sent afterwards; never processed
```

Observed: exactly one operation opened; exactly one disposal attempt (no retry); FAILURE
reported instead of successful completion (the failed retirement is never reported as SUCCESS);
DEFUNCT connection state; the transport closes the connection; no RESET recovery; no second
RUN; no second operation; no disposal retry (`MessagesProcessed = 4`: HELLO, LOGON, RUN, PULL
only). Producer test `DisposalFailureDuringPull_FailClosed_NoResetRecovery_NoSecondOperation`
and Q1 probe `K1` agree.

**The owner decision explicitly incorporates the full inherited B3-B resource-ownership
contract.** A failed `Dispose` does **not** imply the external resource was successfully
released; the guarantee is containment (irreversible DEFUNCT) and refusal of further execution
on the connection.

## 15. Q1 F01 — Write-loop documentation finding

**Verified from source** (`src/BoltSharp/Transport/BoltTcpConnection.cs`, `WriteResponsesAsync`):
the loop assigns `written = payload.Length` immediately after a single
`await stream.WriteAsync(payload.AsMemory(written), writeCts.Token)` — structurally resembling a
partial-write retry loop while operationally performing one write, with a comment claiming
"partial-write robustness".

**Disposition: ACCEPTED DOCUMENTATION FINDING — NON-BLOCKING.** The .NET `Stream.WriteAsync`
contract completes the supplied write or fails; `NetworkStream`/`SslStream` on blocking sockets
do not return a partial byte count that the caller must loop over. The reported code shape
therefore does not establish a partial-write truncation defect on the accepted paths, and no
probe (producer or reviewer) observed truncation. A later readability cleanup may simplify the
code or correct its comment. **No production fix is authorized by this decision.**

## 16. Q1 F02 / producer Q6 — Slow-client backpressure

**Q-B5A-06 — OPEN, INCONCLUSIVE COVERAGE.**

The reviewer's slow-client experiment delivered **262,155,007 bytes** of server output without
the client reading (drain-count evidence); on the tested Windows loopback environment the
writes did **not block** — kernel buffering absorbed the entire bounded response pass.
Consequently: WriteTimeout was not exercised; pending-write cancellation was not conclusively
exercised; actual blocked-write resource behavior remains unverified. What *was* verified:
healthy-client isolation during the oversized response, listener health, exactly-once provider
consultation, and bounded shutdown under the tested conditions.

This is not marked PASS, and it is not classified as a demonstrated runtime defect. The
implementation honors the accepted B3-B record-count bound (`MaxRecordsPerResponse`), but that
does not by itself establish a sufficiently small network-byte or memory budget for a blocked
write.

**Future obligation (before release-grade acceptance):** independently qualify (1) an actual
blocked network write, (2) write-timeout enforcement, (3) cancellation of pending output,
(4) connection resource retirement under that condition, (5) bounded memory behavior, (6)
healthy-client isolation, (7) shutdown behavior under genuine backpressure — preferring
deterministic socket-buffer pressure or a controlled stream test combined with real socket
validation. Note for that future work: the Q1 evidence shows naive loopback saturation is
defeated by kernel buffering at these scales. **No immediate implementation change is required
or authorized** absent a confirmed defect.

## 17. Q1 F03 / producer Q7 — Session-options passthrough

**Q-B5A-07 — OPEN, ACCEPTED CONFIGURATION LIMITATION.**

Q1 established (source verification + TCP probe) that the transport constructs each B3-B session
with `new BoltSessionOptions { Verifier = ... }` — the accepted **default** B3-B configuration;
`MaxRecordsPerResponse`, `ServerName`, `ConnectionId` etc. are not configurable through
`BoltTcpServerOptions`. The default `MaxRecordsPerResponse` remains effective over TCP (the
2,500-row probe required multiple bounded responses). Nondefault session configuration is not
publicly configurable through the transport as tested.

The accepted reference transport operates on documented defaults, but B5-A must not claim
arbitrary session-option configuration. Before exposing a configurable production server API,
separately assess limit propagation, verifier injection, provider injection, session-option
validation, and resource-bound configuration. **No transport API change is authorized by this
decision.**

## 18. Q1 F04 — Reviewer probe corrections

**Disposition: ACCEPTED REVIEWER-EVIDENCE DISCLOSURE — NON-BLOCKING.**

The reviewer documented three mistakes in its own probe code (double-send of the manifest
handshake; a three-byte chunk header in an early scratch diagnostic; a PackStream map header
using the string base `0x82` instead of the map base `0xA2`), corrected within the reviewer
artifacts. Verified:

- The corrected probes are committed (`docs/reviews/probes/B5-A-Q1/B5AQ1Probe/`) and their
  resulting evidence (`evidence/B5-A-Q1/probe-all.log`, 165 checks) is reproducible from the
  committed probe source.
- Historical evidence was not rewritten; the probe defects are disclosed in the Q1 report
  (finding B5-A-Q1-F04) and were never attributed to production code.
- Each transport *rejection* of the malformed original probe bytes (NOOP interpretation of the
  `00 00` prefix; B3-A `InvalidPackStreamValue` codec rejection of the malformed map) was
  independently verified as **correct fail-closed behavior** of the accepted implementation.

## 19. Reviewer independence and bounded governance waiver

**Recorded facts:** producer — DeepSeek V4.1 Flash (NVIDIA provider, OpenCode). Reviewer —
GLM 5.3 Flash, model `z-ai/glm-5.3-flash` (NVIDIA provider, OpenCode). The Q1 report states:
different models, **shared OpenCode session, same operator, same host, no established
organizational or fully separate executor-session independence.**

**Assessment:** the review provides meaningful model-level separation and independently authored
technical probes, but **does not establish strict reviewer-executor independence.**

**Owner decision:** the existing owner policy already records a bounded same-session waiver with
rationale and a continuing verification requirement (see the B3-B owner acceptance's recorded
same-session waiver). Consistent with that policy, this decision records a **bounded governance
waiver for B5-A only**, supported by: (1) a different model reviewed the implementation; (2)
independent network probes were constructed from normative and historical oracles; (3) golden
byte vectors were used as independent oracles; (4) manifest negotiation was tested over actual
TCP; (5) TLS trust was independently challenged; (6) resource ownership was independently
verified with counters; (7) negative controls detected their targets; (8) probe failures were
openly corrected and disclosed; (9) no material production defect was established; (10) B5-B
release-grade interoperability remains separately gated.

**Continuing requirement:** B5-B interoperability qualification and eventual release-grade
verification **must use a genuinely separate executor session/model**. A model switch within one
shared execution session is not full independence, and no organizational independence is claimed
for B5-A-Q1. This waiver does not discharge Q-B5A-01 (§20).

## 20. B5-A binding qualifications Q-B5A-01 … Q-B5A-10

Exact identifiers and definitions from the committed producer report §23, with the Q1-supported
narrowing where applicable:

| Qualification | Owner disposition |
| --- | --- |
| **Q-B5A-01 — Reviewer independence** ("this producer verification is same-executor; B5-A-Q1 must use a different reviewer executor/model") | Retained as a **bounded waiver** (§19): model-level separation occurred; organizational independence not claimed. The continuing release-grade independence requirement is restated in §23. |
| **Q-B5A-02 — TLS depth** (cipher-suite policy, `checkCertificateRevocation=false` for server auth, CA-trust chains, certificate rotation unconfigured; client-side URI schemes documented, not driver-qualified) | **Retained**, documented scope; Q1's trust-model analysis confirms no bypass of a required check at the current trust model, but the qualification's deployment-depth scope is unchanged. |
| **Q-B5A-03 — Cipher/protocol negotiation** (platform `SslStream` defaults within the TLS 1.2+ floor; no explicit cipher policy surface) | **Retained.** Q1 additionally observed TLS 1.3 with `TLS_AES_256_GCM_SHA384` and refusal of TLS 1.1, narrowing the observed behavior without changing the qualification. |
| **Q-B5A-04 — Manifest handshake end-to-end TCP coverage** | **CLOSED BY VERIFIED Q1 EVIDENCE** (26/26 TCP checks, §10 of this decision). Driver-level manifest interoperability remains B5-B. |
| **Q-B5A-05 — Slow-provider responsiveness** (bounded by the accepted B3-B limitation; no mid-flight RESET jump-ahead interruption; transport cancellation does not implement that behavior) | **Retained.** |
| **Q-B5A-06 — Write-timeout slow-client behavior** (implemented but not exercised by a buffer-filling test) | **REMAINS OPEN — INCONCLUSIVE COVERAGE** (§16). |
| **Q-B5A-07 — Session options passthrough** (default `BoltSessionOptions` only; verifier injected) | **REMAINS OPEN — ACCEPTED CONFIGURATION LIMITATION** (§17). |
| **Q-B5A-08 — Request encode surface** (B3-A has no public request encoder; test clients re-implement the wire encode, byte-verified against golden vectors) | **Retained** — and independently confirmed: the reviewer's own encoder reproduced the authentic golden bytes without access to production encoding. |
| **Q-B5A-09 — Throughput/performance** (no benchmark claims) | **Retained** — no performance claims exist in the producer report, Q1 report, or this decision. |
| **Q-B5A-10 — Shutdown guarantee** (bounded `StopAsync`; connections that cannot finish are abandoned only after the bound; edge behavior under pathological load not tested) | **Retained**, subject to Q1 evidence: eight race scenarios passed; pathological-load edges remain qualified. |

No qualification other than Q-B5A-04 is closed by this decision.

## 21. Inherited B2/B3 binding qualifications

All applicable accepted B2/B3 qualifications are carried forward **with their authoritative
identifiers and language intact**; B5-A acceptance silently discharges **none** of them:

- B3-B fail-closed operation retirement (restated and network-verified, §14).
- B3-B single-owner session execution (restated and network-verified).
- B3-B bounded `PULL(n=-1)` (bounded passes with `has_more`; network-verified).
- No full RESET jump-ahead interruption (slow-provider qualification Q-B5A-05 retained).
- `ReceiveBatch` unprocessed-request ownership (B3-B; the transport does not use `ReceiveBatch`).
- Provisional public B3 session/provider APIs.
- Placeholder/generic error classifications.
- No general provider cancellation-token API.
- B3-C exact-form query allowlist (supported scalar/graph/parameter forms only).
- Bounded reference-graph materialization.
- Supported parameter-surface limitations.
- B3-B terminal `has_more` semantics (`SUCCESS {}` at exhaustion; byte-level reconciliation with
  the real-server `has_more:false` golden remains B5-B).
- Real-driver interoperability not yet verified (the entire B5-B scope).
- Reviewer-independence governance (restated in §19; B5-B requires a separate executor
  session/model).
- B3-A F-02 zero-field IGNORED live-driver obligation (raw-socket verification does not
  discharge it).
- B2 version honesty / protocol-zero handling (verified intact through the transport).

Where Q1 narrowed a qualification (Q4 closure; Q6 inconclusive coverage evidence; Q7
confirmation), §20 records the supported narrowing and its evidence. No inherited qualification
is silently discharged by B5-A acceptance.

## 22. Explicit accepted scope

If read together with §23, B5-A is accepted for exactly:

1. Bounded TCP listener (§8).
2. Loopback-safe plaintext configuration (§8).
3. TLS transport using configured certificates (§9).
4. Bolt 5.8 conventional handshake (§10).
5. Bolt Manifest v1 over TCP within the verified capability set (§10; Q-B5A-04 closed).
6. B2 chunk framing integration (§11).
7. Fragmented and pipelined input (§11, §15 of the Q1 report).
8. B3-A message encoding/decoding (§12).
9. B3-B connection-session integration (§12-§13).
10. B3-C reference graph query integration (§12).
11. Supported scalar and graph responses (§12).
12. Typed parameters within the supported surface (§12).
13. Authentication gating (§13).
14. Ordered FAILURE/IGNORED/RESET behavior (§13).
15. Per-connection session ownership (§14).
16. Bounded connection admission (§8).
17. Connection isolation (§8, §11).
18. Disconnect and shutdown behavior within tested limits (§8, §11).
19. Fail-closed resource retirement (§14).
20. Reproducible evidence and regression preservation (§6-§7).

Every claim above is tied to the implementation source or the committed Q1/producer evidence
cited in the referenced section.

## 23. Explicit non-claims

This decision does **not** claim:

- Complete Neo4j.Driver interoperability (B5-B).
- Complete Bolt 5.8 conformance.
- General Cypher execution; general graph database functionality; graph mutation.
- Transaction support beyond accepted B3 limitations.
- Production security certification or complete TLS deployment qualification.
- Tested blocked-write cancellation (Q-B5A-06 open, inconclusive).
- Production high-throughput performance or proven scalability under arbitrary concurrent load.
- Complete user-configurable session options (Q-B5A-07 open).
- Full mid-flight cancellation or full RESET jump-ahead interruption.
- Browser/WebSocket compatibility (B4).
- Stable production public API.
- General protocol-version negotiation (Bolt 5.8 bounded; no other revision selected).
- NuGet release readiness or production deployment readiness.
- Organizational reviewer independence for B5-A-Q1 (bounded waiver only, §19).

**B5-A is a qualified transport foundation, not release certification.**

## 24. B5-B entry implications and gate

B5-B remains a separate engineering milestone: the actual official Neo4j .NET driver tested
against BoltSharp's TCP/TLS server. Required investigations (none begun, none claimed passed):

- **Connectivity:** plaintext `bolt://`; TLS `bolt+s://`; self-signed `bolt+ssc://` where
  supported; real handshake and manifest behavior; HELLO metadata; authentication; session
  creation and disposal.
- **Query interoperability:** scalar RUN/PULL; graph RUN/PULL; parameters; result exhaustion;
  DISCARD; repeated queries; error recovery.
- **Historical obligations:** B3-A F-02 zero-field IGNORED; terminal `has_more` representation;
  driver metadata expectations; precise failure classification; manifest behavior with the
  driver.
- **Transport risk carryover:** genuine blocked-write qualification (Q-B5A-06); session-option
  limits (Q-B5A-07); cancellation and shutdown; TLS policy depth (Q-B5A-02/03).
- **Independence:** B5-B release-relevant qualification must use a genuinely separate executor
  session/model (§19).

## 25. Formal owner disposition

**B5-A — ACCEPTED WITH QUALIFICATIONS — CLOSED**

Recorded on the basis of:

- The producer implementation (B5-A-IMPL-01) being complete for its bounded milestone, with
  `21e649acae8b0f05e002fd6bd331dd7` as its final revision.
- Q1 supporting the implementation with qualifications (165 probe checks: 162 pass, 3
  inconclusive-classified; negative controls 8/8).
- Full existing regression passing (core 1,217/1,217; transport 80/80; B3 suites 98/119/94;
  B0-B 19/19; builds clean with the 2 historical advisories), freshly re-confirmed during this
  decision.
- Historical evidence integrity preserved (352/352; B5-A 18+1; Q1 10/10; B3 owner decisions
  byte-identical).
- TCP/TLS execution accepted within the tested scope (§8-§9).
- B2/B3 protocol integration preserved (§11-§13).
- Manifest v1 over TCP qualified; **Q-B5A-04 formally closed**.
- Fail-closed resource ownership binding and network-verified (§14).
- F01 accepted as non-blocking documentation (§15).
- F02/Q6 open and inconclusive (§16).
- F03/Q7 accepted as an open configuration limitation (§17).
- F04 accepted as an openly corrected reviewer-probe disclosure (§18).
- Reviewer independence covered by a bounded governance waiver with a continuing separate-
  executor requirement for B5-B and release-grade work (§19).
- All other Q-B5A qualifications and all inherited B2/B3 qualifications remain binding (§20-§21).
- Complete official-driver interoperability is not claimed; B5-B/release implementation is not
  authorized by this decision.

## 26. Verification and commit record

- Decision file: `docs/decisions/B5-A-OWNER-01-owner-acceptance.md` (this document; the only file
  authorized and committed by B5-A-OWNER-01).
- Parent commit: `d57a9e1695be64d59676b0f05e002fd6bd331dd7`
  (tree `879f4df85574b7120e6841974de8f719f9268431`).
- Decision commit and tree: recorded in the commit record appended below and verifiable via
  `git log --format='%H %T' -1` after the decision commit.
- Post-commit verification performed per commission §27 (read-back from the committed blob;
  scope/non-claims/Q4/Q6/Q7/independence/F01-F04/inherited-qualifications/B5-B checks; exactly
  one new file; clean tree; one worktree; no push).
