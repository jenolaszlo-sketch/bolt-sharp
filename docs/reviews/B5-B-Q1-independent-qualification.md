# B5-B-Q1 — Independent Official Neo4j .NET Driver Interoperability Qualification

- **Task:** `B5-B-Q1`
- **Role:** Independent adversarial interoperability reviewer
- **Repository:** `D:\projects\bolt-sharp`, canonical `main`, single worktree, never pushed
- **Starting HEAD:** `646c572912de9a2ca415ec3548a99159cb2f4905`
- **Starting tree:** `7670702e59933d84a685d09a0e44744a2dce34ec` (prefix `7670702e` verified against the full hash)
- **Protocol target:** Bolt 5.8 / PackStream 1.0 (bounded, unchanged)
- **Official client:** `Neo4j.Driver` **6.4.0** (exact pin; assembly `6.4.12.0`, informational
  `6.4.0+4ed413b01906c5d1682c62a3ee3c982cb28f99cd`)
- **Producer:** DeepSeek V4.1 Flash (provider `nvidia`, OpenCode), report `docs/reviews/B5-B-IMPL-01.md`
- **Reviewer probes:** `docs/reviews/probes/B5-B-Q1/` (53 probes, all passing, two fresh processes)
- **Evidence:** `docs/reviews/evidence/B5-B-Q1/` (SHA-256 manifest `manifest.json`)
- **Disposition:** **B5-B-Q1 — SUPPORTED WITH QUALIFICATIONS**

---

## 1. Reviewer independence

### 1.1 Provenance (recorded honestly)

| Item | Value |
| --- | --- |
| Producer of B5-B-IMPL-01 | DeepSeek V4.1 Flash, provider `nvidia`, OpenCode (per committed producer report §23 and its independence note) |
| Reviewer executor | A dedicated **new child OpenCode session**, spawned in fresh context by the operator session for this commission |
| Reviewer model | `nvidia/z-ai/glm-5.3-flash` (NVIDIA provider) — **different from the producer model** |
| Model/provider assignment | **Operator-designated.** The reviewer cannot independently attest the session/model separation from inside the session; the separation statement below rests on the operator's session topology and this session's fresh-context construction |
| Organizational independence | **NOT claimed.** Same operator, same machine, same repository |

### 1.2 Separation statement

Session/model separation is **established at the execution level**: this qualification was produced
in a genuinely separate OpenCode session with a different assigned model, in fresh context, without
access to the producer session's hidden working context. All expected outcomes were constructed
independently from (a) the normative Bolt 5.8 semantics, (b) the official driver's documented public
API, and (c) the historical accepted evidence (B5-A owner decision §12–§14 golden vectors and
reference semantics). No producer conclusion was adopted as an oracle.

Organizational independence is **not** claimed and is not required by the commission: §2 permits
qualification when execution sessions are genuinely separate, and this reviewer does not rely on any
of the B3-C/B5-A bounded waivers — none were applied here.

### 1.3 Governance gate

The B5-A owner acceptance requires B5-B release-relevant qualification to use a genuinely separate
executor session/model. That requirement is satisfied by this session's topology. The formal
SUPPORTED disposition below is issued on that basis, with the operator-designation limitation and
the same-machine limitation recorded verbatim in §1.1.

---

## 2. Preflight (Stage A)

All preflight checks passed before any change:

| Check | Result |
| --- | --- |
| Branch | `main` |
| HEAD | `646c572912de9a2ca415ec3548a99159cb2f4905` (exact match) |
| HEAD tree | `7670702e59933d84a685d09a0e44744a2dce34ec` (full hash verified, prefix `7670702e`) |
| Worktrees | Exactly one (`D:/projects/bolt-sharp`) |
| Working tree/index | Clean (`git status --short` empty) |
| Stash | None |
| Push | None (local `main` is 57 commits ahead of `origin/main` at `9abf5e3`) |
| Prior B5-B-Q1 work | None (no matching paths existed) |
| Ancestry | B3-A owner (`c2881ea`), B3-B owner (`aae3d0e`), B3-C owner (`8535806`), B5-A owner (`2bebd3e`), B5-A-Q1 (`d579…`/`d57a9e1`) all ancestors of HEAD |
| Production isolation | `git diff --name-status 2bebd3e..HEAD` contains **zero `src/` changes**; only the B5-B test project, producer report and producer evidence were added |

---

## 3. Producer evidence and historical integrity (independently re-verified)

The reviewer wrote its own verification script
(`docs/reviews/evidence/B5-B-Q1/Verify-AllManifests-Independent.ps1`) that recomputes SHA-256 over
working-tree bytes; the producer's script was not reused.

| Manifest group | Expected | Independently matched |
| --- | ---: | ---: |
| Producer `B5-B-IMPL-01` | 62 | **62/62** |
| `B5-A-Q1` | 10 | **10/10** |
| `B5-A-IMPL-01` | 18 | **18/18** |
| Eleven earlier packages (B3-C-Q1 … B0-B-Q1) | 352 | **352/352** |
| **Historical total** | **380** | **380/380** |
| **All manifests total** | 442 | **442/442, 0 unmatched** |

The four accepted owner decisions are byte-identical to their committed HEAD blobs:
B3-A `ab84ffa9…`, B3-B `211a5d56…`, B3-C `323303bd…`, B5-A `385c820e…` (identical=True for all).
No historical manifest, fixture or owner decision was modified by this review.

---

## 4. Regression baseline (fresh execution at the reviewer's starting HEAD)

Executed by `docs/reviews/evidence/B5-B-Q1/Run-Q1Regression.ps1` (logs and TRX in the evidence
directory):

| Suite | Floor | Observed |
| --- | ---: | ---: |
| B5-B producer live-driver suite | — | **40/40** (twice, two fresh processes) |
| Core test project | 1,217 | **1,217/1,217** |
| B5-A transport | 80 | **80/80** |
| B3-C backend | 94 | **94/94** |
| B3-B sessions | 119 | **119/119** |
| B3-A messages | 98 | **98/98** |
| B0-B official-driver integration | 19 | **19/19** |
| Production build | 0 warnings / 0 errors | **0/0** |
| Solution build | 0 errors / 2 historical advisories | **0 errors / exactly the two pre-existing `xUnit2012` advisories** (`BoltRequestDecodeTests.cs:71-72`) |

Environment: Windows 10.0.26200, .NET SDK 10.0.401, runtime .NET 8.0.31, x64.

### 4.1 Producer test inspection (commission §6 checklist)

The reviewer inspected the producer harness. Strengths: real server + real driver, per-connection
counters, byte-exact wire transcripts, bounded waits, LOGON redaction at message and chunk level,
an evidence writer with a credential sentinel guard. Weaknesses found (non-blocking, recorded):

- `ConnectivityTests.B5B2_07` guards its protocol-version assertion with
  `if (versionText is not null)` — the assertion vanishes when the value is absent (weak
  conditional; the sibling agent/version assertions still hold).
- `ConnectivityTests.B5B2_01` uses a fixed 250 ms delay before asserting `Accepted == 0`; sound as a
  negative check but timing-based.
- No producer test asserts the golden RUN/PULL *hex* against the driver's live capture in the same
  probe (the wire capture tests do assert the handshake bytes; the scalar golden comparison exists
  in B5-A evidence). This review closes that gap independently (§7.2).

---

## 5. Official driver identity (independently recorded)

| Item | Value |
| --- | --- |
| Package | `Neo4j.Driver` `6.4.0` (exact pin, probe `csproj`; never upgraded/downgraded) |
| Assembly version | **6.4.12.0** (`typeof(GraphDatabase).Assembly.GetName().Version`) |
| Informational version | `6.4.0+4ed413b01906c5d1682c62a3ee3c982cb28f99cd` |
| HELLO `user_agent` (wire) | `neo4j-dotnet/6.4` |
| HELLO `bolt_agent.product` (wire) | `neo4j-dotnet/6.4.12` |
| Platform (wire) | `Microsoft Windows 10.0.26200;X64`, `.NET 8.0.31` |

All values captured from the actual wire (evidence `q1b01-hello-logon.json`), matching the
producer's recorded identity exactly.

---

## 6. Reviewer harness architecture (Stage B)

`docs/reviews/probes/B5-B-Q1/` is a test-only xunit project, deliberately **outside
`BoltSharp.slnx`** (repository convention). It builds with 0 warnings/0 errors and passes
`dotnet format --verify-no-changes`.

```text
Infrastructure/
  Q1Server.cs         Real B5-A BoltTcpServer host; plaintext/TLS/reject-all; reviewer verifier;
                      optional per-connection OperationProviderFactory injection (public seam)
  Q1FaultProvider.cs  Per-connection fault-injecting IBoltOperationProvider with per-connection
                      counters (Prepare/OperationsOpened/Dispose/DisposeFailures/ReadRow)
  Q1WireTap.cs        Passive loopback tap (forwards every byte unchanged; records per-read copies)
                      + independent handshake/chunk-frame/PackStream structural parser with
                      raw-chunk-level LOGON redaction
  Q1Credentials.cs    Reviewer-owned deterministic verifier (values deliberately differ from the
                      producer's)
  Q1Certificates.cs   In-memory test CA/leaf/self-signed certificates; no disk, no trust store
  Q1Driver.cs         Official driver construction via documented public API only; bounded waits
  Q1Evidence.cs       Evidence writer with plain/hex/UTF-16-hex credential sentinel guard
Scenarios/            Q1Connectivity, Q1HelloLogon, Q1ScalarGraph, Q1Parameter, Q1CursorPooling,
                      Q1Tls, Q1Findings (F01/F02/IGNORED), Q1FailedRetirement, Q1Lifecycle
```

Key independent oracles:

1. **Per-connection provider identity.** The transport calls `OperationProviderFactory` once per
   accepted connection (verified in `src/BoltSharp/Transport/BoltTcpConnection.cs:107`), so one
   reviewer provider instance = one physical server connection. This is the reviewer's
   physical-connection oracle for pooling, F02 disposition and failed-retirement scoping.
2. **Passive tap transparency.** The tap only records; the scalar probe's byte-exact golden
   RUN/PULL/RECORD match (§7.2) simultaneously proves server correctness and tap transparency.
3. **Expected values** come from the commission (§11–§13) and the accepted B5-A owner decision
   §12–§14 (golden vectors, Alice/Bob fixture, `B0 7E` IGNORED, fail-closed retirement), never from
   the producer's report.

The tap never rewrites, delays, drops or injects bytes, never strips request options and never
alters TLS trust behavior. LOGON payloads are redacted at the raw-chunk level before storage, and
the evidence writer re-checks every document for the credential sentinel in plain, hex and
UTF-16-hex form. A final sentinel scan over all 73 evidence files (both the reviewer's and the
producer's credential values) found **0 leaks**.

**Probe result: 53/53 passing, executed twice in fresh processes** (TRX in the evidence directory).

---

## 7. Core interoperability results (Stage B)

### 7.1 Q1-A — Connectivity and negotiation — **PASS**

- Constructing `IDriver` opens no connection (`Accepted == 0` after construction);
  `VerifyConnectivityAsync` over `bolt://localhost:{ephemeral}` establishes one real TCP
  connection, completes the handshake, authenticates, and releases cleanly
  (`accepted=1, completed=1, failed=0`, `active→0`).
- **Manifest v1 is the negotiated path with the real driver.** Independently captured
  (`q1a02-manifest-handshake.json`):
  - Client preamble `6060B017` + proposals `000001FF` (marker), `00080805` (5.0–5.8),
    `00020404` (4.2–4.4), `00000003` (3.0).
  - Server manifest offer `000001FF 01 00000805 00` (one range, capabilities 0).
  - Client selection `00000805 00` (5.8, zero capabilities) **pipelined directly with the framed
    HELLO** in the same client byte stream (no server reply is expected after selection); the
    server preserved the pipelined HELLO (first parsed message = HELLO).
  - Server-reported handshake mode: `ManifestV1`.
- **Conventional fallback verified** with manifest disabled (`q1a03`): server replies `00000805`
  (legacy), mode `Legacy`, and a full scalar query succeeds — the fallback is not inferred from an
  unspecified mode.
- **Negative controls** (`q1a04–q1a06`): a server advertising nothing fails the driver with zero
  HELLO sent (`MessagesProcessed == 0`, handshake failure counted); connection-refused fails closed;
  an early-EOF raw endpoint fails closed without hanging. Driver retry behavior was observed to be
  bounded and driver-side; no additional server connection was opened by the negative flows beyond
  the driver's own attempts.

### 7.2 Q1-B — HELLO/LOGON and authentication — **PASS**

Independently captured through the reviewer tap (`q1b01`):

- HELLO keys `[user_agent, routing, bolt_agent]`; `user_agent=neo4j-dotnet/6.4`;
  `routing=null`; `bolt_agent.product=neo4j-dotnet/6.4.12` — the accepted B3-A schema is exercised
  live by the real driver.
- LOGON keys `[scheme, principal, credentials]` with scheme `basic`; the payload is **redacted at
  the raw-chunk level** (stored as `<redacted LOGON payload: 81 bytes>`); the credential value
  appears nowhere in any evidence document (sentinel scan: 0 hits).
- HELLO SUCCESS carries exactly `{server: "BoltSharp/0.1.0", connection_id: "bolt-b3b-1", hints: {}}`
  ; LOGON SUCCESS is the empty `B170A0`. The driver operates correctly without the optional
  `protocol_version` and `advertised_address` fields — independently confirmed by the successful
  flows, not inferred.
- **Authentication negative controls** (`q1b02–q1b04`): wrong credentials → driver-visible
  authentication error with code `Neo.ClientError.Security.Unauthorized`, terminal LOGON FAILURE
  (`50N42`/Unauthorized) on the wire, exactly HELLO+LOGON processed, **no RUN ever sent**, listener
  stays healthy; missing verifier fails closed identically; a throwing verifier fails closed with
  no exception text surfaced to the driver. No credential echo appears in any FAILURE description.

### 7.3 Q1-C — Scalar query — **PASS**

`RETURN 42 AS answer` through the implicit auto-commit API (`q1c01`):

- Driver-visible: one record, `answer` exposed as `System.Int64` `42`; cursor exhausts (second
  `FetchAsync` false); `ConsumeAsync` returns a summary; a subsequent supported query returns 42
  again on the same pooled connection (`q1c02`, one per-connection provider instance serving both
  RUNs).
- Wire oracle (independent capture, byte-exact):
  - RUN payload **`B310D01352455455524E20343220415320616E73776572A0A0`** — identical to the
    accepted B5-A golden vector.
  - PULL payload **`B13FA1816EC903E8`** (`{n: 1000}`) — identical to the golden vector.
  - RUN SUCCESS `fields=["answer"]`; exactly one RECORD, **`B171912A`** (golden); terminal
    **`B170A0`** (`SUCCESS {}`, no `has_more`).
  - No ROUTE, TELEMETRY or DISCARD; exactly one RUN and one PULL.
- A byte match alone is not the basis of the PASS: the driver-visible typed value, exhaustion and
  summary were asserted through the public API in the same probe.

### 7.4 Q1-D — Graph query — **PASS**

`MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b` (`q1d01`), driven through the official
driver's public result API:

- Alice: `INode`, numeric id **1**, element id **`n:1`**, label `Person`, `name="Alice"`.
- Bob: `INode`, numeric id **2**, element id **`n:2`**, label `Person`, `name="Bob"`.
- KNOWS: `IRelationship`, numeric id **1** (deliberately colliding with Alice's node id),
  element id **`r:1`**, type `KNOWS`, start `n:1`, end `n:2`, `since=2024`.
- Node/relationship identity remains distinct (`n:1` ≠ `r:1`; distinct materialized objects); the
  direction is Alice→Bob as required (endpoint oracle, never reversed).
- Wire: exactly one RECORD with field list `a,r,b`.

### 7.5 Q1-E — Parameters and typed values — **PASS**

`RETURN $value AS answer` through `RunAsync` with real parameter maps (12 probe cases, `q1e01-*`):

- Preserved with correct driver-visible types: `42`, `-7`, `long.MinValue` (boundary), Unicode
  `"héllo ✓ 世界"`, `true`, `false`, explicit `null` (record value null), list `[1,2,3]`, nested map
  `{x:1, inner:{y:"z"}}`.
- **Query-like payload control** (`q1e02`): the destructive Cypher text sent strictly as a
  parameter value is returned verbatim as a string; the graph verifiably still returns Alice/KNOWS/
  Bob afterwards; no interpolation occurred.
- **Missing parameter** (`q1e03`): the driver transmits the query with empty parameter maps (wire:
  RUN payload ends `A0 A0`) and the server refuses it (`50N42`/`Neo.ClientError.Request.Invalid`)
  — the failure originates in the **reference-backend validation** (the refusal appeared after RUN
  reached the server), not in driver validation or transport decoding. Not classified as equivalent
  to any other failure.

### 7.6 Q1-F — Cursor completion and pooling — **PASS**

- **Full scalar and graph consumption**, multiple sequential queries (`q1c02`, `q1l01`).
- **Multi-record result** (`q1f01`): `[Alice, Bob]` in one default PULL; terminal `SUCCESS {}`
  (`B170A0`); **no extra PULL after terminal completion** (exactly one PULL).
- **`has_more:true` pagination** (`q1f02`): with `WithFetchSize(1)` the driver issues one
  `PULL n=1` per record; the server answers the first with `has_more=true` and completes with
  `SUCCESS {}`; exactly 2 RECORDs total — no duplication, no omission.
- **DISCARD on partial consumption** (`q1f03`): `ConsumeAsync` after a partial fetch sends DISCARD;
  the same session/connection continues with the next query.
- **Session disposal with an outstanding result** (`q1f04`): exactly one operation retired with
  exactly one disposal attempt; the connection remains healthy.
- **Pooling is genuinely established, not assumed** (`q1f05`): three sequential sessions produce
  **one physical server connection** (one per-connection provider instance; `accepted=1`), one
  HELLO, one LOGON, three RUN/PULL cycles with RESET between sessions, one GOODBYE, and no
  FAILURE/IGNORED frames.
- Driver disposal closes the physical connection (`q1f06`).
- **Terminal `SUCCESS {}` acceptance** is verified throughout (every consumed cursor above ends
  with `B170A0`).

### 7.7 Q1-G — TLS interoperability — **PASS within the claimed modes; CA-trust arrangement Qualified**

- **`bolt+ssc://` self-signed** (`q1g01`): complete authenticated connection + scalar query (42);
  `TlsHandshakeFailures=0`; negotiated TLS version recorded from the server's own diagnostics
  (`q1g07`).
- **`bolt+s://` untrusted certificate** (`q1g02`): rejected; **zero Bolt bytes processed** (no
  HELLO); no downgrade path. Recorded as the distinct **system-trust path** — its failure is *not*
  proof of the trust-list arrangement below.
- **Isolated driver trust-list arrangement** (`q1g03`): CA-signed leaf validated through the
  documented `WithEncryptionLevel(Encrypted)` + `WithCertificateTrustRule(TrustList, [test CA])`
  public configuration on a `bolt://` URI; connectivity and scalar query succeed; no machine trust
  store touched. Classified **Qualified** (an isolated arrangement, explicitly not identical to
  `bolt+s://` system-store validation).
- **Negative controls** (`q1g04–q1g06`): plaintext client against TLS listener fails closed with
  `TlsHandshakeFailures ≥ 1` and zero Bolt messages; TLS client against plaintext listener fails
  closed with zero Bolt messages; invalid credentials over TLS rejected with the accepted
  Unauthorized classification and no downgrade.

---

## 8. Adversarial qualification (Stage C)

### 8.1 Q1-H — Finding F01 (producer `B5-B-IMPL-F01`): unsupported commands — independent adjudication

Reviewer probes `q1-h01` (explicit `BeginTransactionAsync`), `q1-h02` (managed
`ExecuteReadAsync`), `q1-h03` (`neo4j://` routing scheme) — all through the unmodified official
driver.

**Independent reproduction** (`q1-h01-f01.json`, verbatim):

- The driver sends HELLO, LOGON, then **BEGIN** — structurally valid Bolt 5.8, recognized by the
  B3-A codec (`BoltMessageCodec.cs:84-91` explicitly lists BEGIN/COMMIT/ROLLBACK/ROUTE as
  "defined by Bolt 5.8 but is not implemented").
- The server responds with **nothing** (`server_failure_responses: []`); the transport closes the
  connection with diagnostic outcome **`codec:UnsupportedMessage`** and `failed=1`.
- `MessagesProcessed = 2` (HELLO, LOGON only) — the BEGIN was never counted as processed.
- Driver-visible: `ServiceUnavailableException` ("Unexpected end of stream…", retriable=true).
- For BEGIN (`q1-h01`): **COMMIT and ROLLBACK were never exercised** — the driver never reached
  them because BEGIN failed first; the report does not claim otherwise.
- For `ExecuteReadAsync` (`q1-h02`): the managed retry loop retries within its budget against a
  repeatedly closed connection and surfaces the same connection failure; the routed scheme
  (`q1-h03`) fails on its first ROUTE with the same shape.

**Boundary analysis** (commission §16 questions):

1. Structurally valid for Bolt 5.8? **Yes.**
2. Does B3-A recognize the signature? **Yes** (explicit `UnsupportedMessage` category).
3. Does the transport use the expected decoder entry point? **No.** The transport's message loop
   (`BoltTcpConnection.cs:219-220`) calls `codec.DecodeRequest(message)` + `session.Receive(request)`;
   the B3-B documented decode-refusal path `BoltConnectionSession.ReceiveBytes`
   (`BoltConnectionSession.cs:107-135`, mapping `UnsupportedMessage` → terminal `08N06` FAILURE) is
   **never invoked by the transport**.
4–5. B3-B has an accepted unsupported-command path (`08N06` FAILURE + close), exercised at session
   level by B3-B's own tests, but it is **unreachable through the B5-A transport**.
6. Does the behavior match the accepted capability boundary? The features are out of scope and are
   refused (no execution, no mutation, connection terminated) — the *capability* boundary holds.
7. Does the protocol require FAILURE rather than closure? For a protocol-level refusal, Bolt
   permits terminating the connection; closure without a response is conformant for a protocol
   error, so this is **not a normative violation**.
8. Expected consequence or wiring defect? A **cross-component wiring difference**: BoltSharp's own
   accepted B3-B contract documents a deterministic `08N06` FAILURE for these messages, and the
   transport bypasses that contract before B3-B can decide.
9. Can the documented `08N06` behavior be exercised through an existing public API? **At the
   session level, yes** (`BoltConnectionSession.ReceiveBytes` is public and covered by B3-B tests).
   **Through the transport/driver path, no.**
10. Would routing payloads through `ReceiveBytes` alter accepted behavior elsewhere? The same
    codec performs the same decode on the same complete payload; supported-message flows are
    unaffected. Only decode-refusals (unsupported/malformed) change shape: typed terminal FAILURE
    (`08N06`) instead of silent closure. A bounded requalification of the refusal surface would be
    required.

**Reviewer verdict (B5-B-Q1-F01): QUALIFICATION — existing accepted capability limitation with a
material documentation/contract inconsistency.** The contradiction is precisely: *the accepted B3-B
contract promises a deterministic `08N06` FAILURE for valid-but-unsupported commands, while the B5-A
transport closes the connection before B3-B can decide.* It does not invalidate the supported
interoperability surface, does not violate a normative Bolt contract, and is not
acceptance-blocking; it must be fixed only by a separately authorized, bounded remediation
commission with regression requirements (smallest proposal: map complete-payload decode refusals
through `BoltConnectionSession.ReceiveBytes`, or translate codec `UnsupportedMessage`/malformed
categories into the accepted terminal FAILURE before closing, in the transport layer only, with
B3-A/B3-B behavior unchanged).

### 8.2 Q1-I — Finding F02 (producer `B5-B-IMPL-F02`): generic error classification and connection disposal

Reviewer probe `q1i01` (`q1i01-f02-unsupported-query.json`), official driver, unsupported query
`MATCH (n) DETACH DELETE n`:

- Query rejected: `ProtocolException` with `Code=Neo.ClientError.Request.Invalid`,
  `GqlStatus=50N42`, `GqlClassification=UNKNOWN`, `IsRetriable=false` (recorded verbatim).
- Reference graph unchanged (subsequent graph query returns Alice/KNOWS/Bob).
- No automatic retry: exactly one RUN for the refused query (3 RUN events total in the whole probe
  = 1 refused + 2 supported; no duplicates).
- **Physical connection disposition (instrumented):** the refused connection (provider #0:
  `prepare_calls=1, operations_opened=0`) was **destroyed** — the driver's recovery query used a
  **fresh** connection (provider #1: `prepare_calls=2, operations_opened=2`), `accepted=2`. A later
  successful query on a new connection is *not* treated here as RESET recovery.
- Next supported query returns 42; recovery is transparent at session level.

**Adjudication** (commission §17 questions):

1. `50N42` complies with the accepted bounded error-code contract (B3-C owner acceptance explicitly
   permits generic bounded classifications; the seam cannot convey refusal reasons on the wire).
2. The driver is entitled to discard the connection under its own rules for an
   UNKNOWN-classified failure; the server cannot require RESET-based reuse given that
   classification.
3. **Interoperability limitation, not a protocol defect** — the FAILURE metadata is well-formed
   Bolt 5.8.
4. Reuse efficiency only: every refusal costs one new connection + handshake + authentication. No
   correctness impact, no duplicate execution, no mutation observed.
5. No — `IsRetriable=false`; the duplicate-execution control (RUN counters) shows exactly one RUN
   per refused query.
6. A more precise status code is **not required** for the B5-B minimum success claim.
7. Refinement would alter FAILURE metadata — a B3-B/B3-C contract change requiring separate
   authorization and requalification (anticipated by the B3-C owner acceptance).

**Reviewer verdict (B5-B-Q1-F02): QUALIFICATION — bounded reuse-efficiency limitation under the
accepted generic classification; non-blocking; no correctness impact demonstrated.** Optional
refinement (driver-recognized client-error classification for unsupported queries) remains a
separately authorized candidate change.

### 8.3 Q1-J — Live-driver FAILURE/IGNORED sequence (historical B3-A F-02 obligation)

Reviewer probe `q1j01` (`q1j01-live-driver-ignored.json`) — produced **naturally** through the
official driver's public API (no injected bytes):

- Client (pipelined): `RUN "MATCH (n) DETACH DELETE n" {} {}` + `PULL {n=1000}` — both
  driver-generated and actually sent.
- Server: `FAILURE {gql_status=50N42, neo4j_code=Neo.ClientError.Request.Invalid, …}` then
  **IGNORED with byte-exact zero-field encoding `B07E`** for the pipelined PULL.
- The driver consumed the sequence **without desynchronization** (subsequent messages on the
  recovery connection parse cleanly; no phantom RECORD/SUCCESS: the refused connection's response
  stream contains only the two authentication SUCCESSes between the FAILURE and the IGNORED).
- Driver reported the original RUN failure (`ProtocolException`, 50N42/Request.Invalid,
  non-retriable).
- **No unintended query execution:** per-connection oracle shows `operations_opened=0`,
  `read_row_calls=0` on the refused connection (Prepare invoked and refused; nothing executed).
- **Physical connection disposition:** the driver **destroyed** the failed connection and recovered
  on a fresh connection (`physical_connections=2`; connection #1 served the recovery queries) —
  observed directly, not inferred.

**Closure assessment (narrow):**

- **Established:** the official driver consumes a genuine zero-field `B07E` IGNORED response without
  desynchronization in the observed FAILURE-then-IGNORED scenario.
- **Not established:** same-connection RESET recovery after the scenario — under the accepted
  generic `50N42` classification the driver destroys the connection instead of sending RESET, so
  the recovery behavior cannot be exercised with the current classification (this is F02's
  boundary, not a new defect).

**Recommendation:** the historical B3-A F-02 obligation may be **narrowed to "wire encoding +
official-driver consumption established"**, retaining "same-connection RESET recovery after
live-driver IGNORED" as a separate, explicitly open qualification (blocked by the F02
classification). No broader recovery qualification is closed by this evidence.

### 8.4 Q1-K — Mandatory fail-closed retirement through the official driver — **PASS**

Reviewer-owned fault provider injected through the supported
`BoltTcpServerOptions.OperationProviderFactory` seam (no production change). The first operation's
`Dispose` throws after an otherwise-valid result.

**Main control** (`q1k01-failed-retirement.json`):

| Mandatory assertion | Observed |
| --- | --- |
| One operation opened on the affected connection | `operations_opened=1` (provider #0) |
| One disposal attempt | `dispose_calls=1` |
| No disposal retry | `dispose_calls` remains 1 after all subsequent activity |
| No successful terminal result after the retirement failure | driver-visible `DatabaseException` (`50N00`/`Neo.DatabaseError.General.UnknownError`, non-retriable); wire terminal is FAILURE, never SUCCESS (`q1k02`) |
| Failure/termination observable by the driver | yes — `DatabaseException` surfaced through the public API |
| No RESET restores the connection | server processed exactly HELLO, LOGON, RUN, PULL (4 events; no `sig=0x0F`); connection closed with EOF |
| No subsequent RUN executes on that connection | provider #0 `prepare_calls` stays 1 forever; all later work used connection #1 |
| No second operation opens on that connection | `operations_opened` stays 1 |
| Server resources cleaned up | `active→0`, connections accounted (`completed`), no leaked counters |

The driver then legitimately opened a **new** physical connection for later work (provider #1,
healthy, `dispose_failures=0`) — recorded per-connection so the new connection is not mistaken for
resurrection of the failed session.

**Wire oracle** (`q1k02`): client `HELLO, LOGON, RUN, PULL`; server `SUCCESS, SUCCESS, SUCCESS,
RECORD, FAILURE` — the terminal response after the failed retirement is **FAILURE (never
SUCCESS)**, the already-read RECORD is preserved in-stream (accepted behavior), the server closes
with EOF, and the driver observes `IOException`.

**Additional controls:**

- **Recoverable provider failure** (`q1k03`): `Prepare` throws → server FAILURE, **zero operations
  opened**, zero disposal calls, and the subsequent supported query returns 42.
- **Client disposal during an active result** (`q1k04`): the outstanding operation is retired
  exactly once with no failure.
- **Fatal retirement failure**: the main control itself.

**Verdict: PASS.** A confirmed ability to continue executing on the same DEFUNCT session was **not**
observed — the fail-closed rule holds end-to-end through the official driver.

### 8.5 Q1-L — Driver lifecycle — **PASS**

`q1l01–q1l04`: construction, connectivity verification, session create/dispose, sequential
sessions, partial consumption, driver disposal, graceful server shutdown, driver recovery after
server closure (stale endpoint fails closed; fresh server + fresh driver work), two independent
driver instances (both served; `accepted=2`). Authentication failure and unsupported-query failure
are covered by §7.2 and §8.2. Per-connection identity for lifecycle claims is the reviewer's
provider log, not aggregate counters (e.g., `q1l01` records per-connection provider state).

### 8.6 Q1-M — Capability boundary checks

| Capability | Public-API probe | Classification |
| --- | --- | --- |
| Explicit transactions (`BeginTransactionAsync`) | `q1-h01` | **Refused at transport/decode** (F01 shape); no execution, no mutation |
| Managed transactions (`ExecuteReadAsync`) | `q1-h02` | **Refused at transport/decode** (F01 shape) |
| Routing (`neo4j://` → ROUTE) | `q1-h03` | **Refused at transport/decode** (F01 shape) |
| Multi-database (`WithDatabase("neo4j")`) | `q1m01` | **Correctly refused** (driver-visible error; no silent multi-database activation) |
| General Cypher / mutations (`DETACH DELETE`, `CREATE`, `UNWIND`, `RETURN 1 AS x`) | `q1m02` | **Correctly refused** by the reference backend (recoverable `50N42`); graph verifiably unchanged after every refusal |
| LOGOFF | — | **Not exercised** (no public driver API drives LOGOFF in the tested flows) |
| TELEMETRY | — | **Not exercised** (the driver sends none without telemetry hints; none fabricated) |
| Cluster behavior | — | **Outside scope** |
| General notification semantics | — | **Not exercised** (server fabricates no notification data) |

An unsupported feature's correct refusal is nowhere treated as evidence that the feature is
implemented.

---

## 9. Negative-control coverage (commission §22)

| Required control | Reviewer probe | Result |
| --- | --- | --- |
| Driver never establishes a network connection | `q1a01` (construction-only check + counters) | Control exercised; behavior correct |
| Wrong protocol version selected | `q1a02`/`q1a03` (byte-exact handshake oracle) | Control exercised; 5.8 selected on both paths |
| Manifest selection loses HELLO | `q1a02` (pipelined-HELLO stream oracle) | Control exercised; HELLO preserved |
| Driver-visible scalar result incorrect | `q1c01` typed oracle | Control exercised; value correct |
| Graph direction reversed | `q1d01` endpoint oracle | Control exercised; direction correct |
| Node/relationship ID collision mishandled | `q1d01` identity oracle | Control exercised; identities distinct |
| Parameter text interpreted as query | `q1e02` query-like payload + graph-unchanged check | Control exercised; no interpolation |
| Terminal result never exhausts | `q1c01`/`q1f01`/`q1f02` exhaustion + terminal-hex oracles | Control exercised; exhausts |
| Connection reuse falsely claimed | `q1f05`/`q1c02` per-connection provider identity | Control exercised; pooling real |
| Unsupported query executes | `q1e03`/`q1i01`/`q1m02` operations-opened counters + graph check | Control exercised; never executes |
| IGNORED has wrong arity | `q1j01` raw-wire `B07E` + driver consumption | Control exercised; zero-field correct |
| Same DEFUNCT connection executes again | `q1k01`/`q1k02` per-connection counters + processed events | Control exercised; fail-closed holds |
| Driver retry duplicates execution | `q1i01`/`q1j01` RUN event counters | Control exercised; no duplicates |
| TLS trust bypassed | `q1g02`/`q1g04`/`q1g05` | Control exercised; trust enforced |
| Credentials leak | sentinel scan over all 73 evidence files (plain + hex + UTF-16-hex, reviewer and producer sentinels) | Control exercised; 0 leaks |
| Unsupported command silently succeeds | `q1-h01/02/03` | Control exercised; never succeeds |

---

## 10. Findings (reviewer IDs; cross-references to producer IDs)

### B5-B-Q1-F01 (= producer B5-B-IMPL-F01) — QUALIFICATION

1. Reproduction: official driver `BeginTransactionAsync` / `ExecuteReadAsync` / `neo4j://` against
   the real server.
2. Driver API: `IAsyncSession.BeginTransactionAsync()`, `ExecuteReadAsync`, routing URI.
3. Driver version: 6.4.0 (assembly 6.4.12.0).
4. Wire: `HELLO, LOGON, BEGIN` → **no response, connection closed** (`codec:UnsupportedMessage`).
5. Expected (normative): a protocol refusal may end the connection (conformant); BoltSharp's own
   accepted B3-B contract additionally defines a terminal `08N06` FAILURE for these messages.
6. Actual server: closes with zero bytes written for the refused message.
7. Driver-visible: `ServiceUnavailableException` ("Unexpected end of stream…", retriable=true).
8. Connection state: transport-classified failure; server-side session disposed; no operation open.
9. Source: `src/BoltSharp/Transport/BoltTcpConnection.cs:156-162,219-220` vs
   `src/BoltSharp/Sessions/BoltConnectionSession.cs:107-135,698-702`.
10. Basis: accepted B3-B contract (`ReceiveBytes` unsupported-message handling) + normative Bolt
    5.8 (closure on protocol error is permitted).
11. Impact: driver-visible refusal shape differs from BoltSharp's documented behavior; no execution
    or state change; no security impact.
12. Severity: low (bounded capability boundary).
13. Acceptance-blocking: **no**.
14. Smallest remediation: separate bounded commission — route complete decoded payloads through
    `BoltConnectionSession.ReceiveBytes` (or map codec refusal categories to the accepted terminal
    FAILURE in the transport) with regression requirements covering the refusal surface and all
    supported flows.
15. Evidence: `q1-h01-f01.json`, `q1-h02-f01.json`, `q1-h03-f01.json`.

### B5-B-Q1-F02 (= producer B5-B-IMPL-F02) — QUALIFICATION

1. Reproduction: official driver `RunAsync` of an unsupported query.
2. Driver API: implicit auto-commit `RunAsync` + `FetchAsync`.
3. Driver version: 6.4.0.
4. Wire: `RUN` → `FAILURE {50N42, Neo.ClientError.Request.Invalid}` (+ pipelined `PULL` → `B07E`).
5. Expected (accepted contract): recoverable generic refusal (`50N42`) — compliant.
6. Actual server: recoverable FAILURE; connection left open by the server; the **driver** then
   discards the connection.
7. Driver-visible: `ProtocolException` (UNKNOWN classification), `IsRetriable=false`.
8. Connection disposition: destroyed by the driver; recovery on a fresh physical connection
   (provider-log instrumented).
9. Source: accepted `UnsupportedQueryFailure` (`BoltConnectionSession.cs:671-675`); classification
   interplay is driver-side.
10. Basis: B3-C owner acceptance (bounded refusal granularity, Q-B3C-02).
11. Impact: reuse efficiency only (one extra connection per refusal); no correctness or security
    impact; no duplicate execution.
12. Severity: low.
13. Acceptance-blocking: **no**.
14. Smallest remediation (optional, separately authorized): driver-recognized client-error
    classification for unsupported queries, with requalification of the refusal surface.
15. Evidence: `q1i01-f02-unsupported-query.json`, `q1j01-live-driver-ignored.json`.

### B5-B-Q1-N01 — Producer test observation (DOCUMENTATION-grade, non-blocking)

`ConnectivityTests.B5B2_07` conditionally guards its protocol-version assertion
(`if (versionText is not null)`), so the assertion silently disappears if the value is absent.
Recorded as a producer-suite robustness note; this review's own probe (`q1a01`) asserts the
equivalent values unconditionally. No action required for acceptance.

No BLOCKING findings. No INCONCLUSIVE findings: every mandatory gate was executed and reproduced.

---

## 11. Required acceptance matrix (independent)

| Capability | Required result | Reviewer result | Evidence |
| --- | --- | --- | --- |
| Official-driver connectivity | PASS | **PASS** | q1a01 |
| Bolt 5.8 negotiation | PASS | **PASS** | q1a02, q1a03 |
| Manifest v1 with actual driver | PASS | **PASS** (fallback also verified) | q1a02, q1a03 |
| HELLO/LOGON | PASS | **PASS** | q1b01–q1b04 |
| Scalar RUN/PULL | PASS | **PASS** (golden byte match + typed oracle) | q1c01, q1c02 |
| Graph RUN/PULL | PASS | **PASS** | q1d01 |
| Typed parameters | PASS | **PASS** | q1e01-*, q1e02, q1e03 |
| Cursor exhaustion | PASS | **PASS** | q1c01, q1f01 |
| Sequential queries | PASS | **PASS** | q1c02, q1l01 |
| Connection pooling/reuse | PASS | **PASS** (instrumented) | q1f05, q1f06 |
| Terminal `SUCCESS {}` | PASS | **PASS** | q1c01, q1f01, q1f02 |
| `has_more:true` pagination | PASS | **PASS** | q1f02 |
| Self-signed TLS | PASS | **PASS** | q1g01 |
| CA-trust arrangement | Qualified | **Qualified** (isolated trust list; `bolt+s` system-trust limitation explicit) | q1g02, q1g03 |
| Unsupported-query refusal | PASS | **PASS** | q1e03, q1i01, q1m02 |
| Error metadata/recovery | Qualified | **Qualified** (F02: connection destroyed on refusal; recovery via fresh connection) | q1i01 |
| Live-driver IGNORED | PASS for observed sequence | **PASS** | q1j01 |
| Same-connection RESET after IGNORED | Separate verification | **NOT established** (blocked by F02 classification; driver destroys connection) | q1j01 |
| F01 unsupported-command handling | Explicit adjudication | **QUALIFICATION** (wiring inconsistency; conformant closure) | q1-h01–03 |
| F02 connection-disposal behavior | Explicit adjudication | **QUALIFICATION** (reuse efficiency only) | q1i01 |
| Failed retirement through official driver | PASS required | **PASS** | q1k01, q1k02, q1k03, q1k04 |
| Explicit transactions | Unsupported, correct behavior required | **Refused** (F01 shape); correct refusal, no execution | q1-h01 |
| Routing/multi-database | Unsupported, correct behavior required | **Refused** (F01 shape / allowlist); no execution | q1-h03, q1m01 |
| Full Cypher | Outside scope | Outside scope (refused correctly where probed) | q1m02 |
| Production security/performance | Outside scope | Outside scope | — |
| Blocked-write backpressure | Inherited release obligation | Carried forward unchanged (Q-B5A-06) | — |

No unexecuted row is marked PASS.

---

## 12. Inherited qualifications (carried forward; none silently closed)

All accepted B3-A/B3-B/B3-C/B5-A qualifications remain binding, including single-owner execution,
the failed-retirement DEFUNCT rule, bounded PULL behavior, no full RESET jump-ahead, generic status
code limitations, the exact-form query allowlist, the supported parameter surface, the finite
reference backend, provisional public APIs, no cancellation-token API, session-options passthrough
limitation (Q-B5A-07 — the transport still constructs default `BoltSessionOptions`), TLS depth
limitations, blocked-write backpressure (Q-B5A-06, open), no throughput claims, and the reviewer
independence requirement.

### Qualification closures proposed by this review (for owner decision; not self-accepted)

| Identifier | Proposed scope of closure | Evidence | Remaining limitation |
| --- | --- | --- | --- |
| B3-B "driver-level `has_more` / terminal-exhaustion" deferral (B5-B scope) | Terminal `SUCCESS {}` treated as exhaustion by the official driver; `has_more:true` drives further PULLs without duplication | q1c01, q1f01, q1f02 | Bounded to this driver version, direct-bolt, and the finite reference surface |
| B3-B "connection reuse after consumption" live-driver deferral | Pooled single-connection reuse across sequential sessions with RESET check-in | q1f05, q1c02 | Same bounds as above |
| B3-A F-02 obligation (narrowing) | Zero-field `B07E` IGNORED wire encoding + official-driver consumption without desynchronization | q1j01 | Same-connection RESET recovery after live-driver IGNORED remains open (blocked by F02 classification) |

---

## 13. No-silent-fix attestation

The reviewer modified no production code, no producer test, no historical fixture, no owner
decision and no dependency; no driver version changed; no expected outcome was adjusted after
observing behavior (every probe expectation traces to the commission, the accepted owner decisions,
or the normative handshake/message semantics; initial probe failures were probe-code defects — a
wrong parse offset, a wrong RESET signature constant, an over-strict type assertion — fixed in the
probes, never in expectations). The tap never rewrote traffic; no success was manufactured.

---

## 14. Formal reviewer disposition

### **B5-B-Q1 — SUPPORTED WITH QUALIFICATIONS**

Basis (all independently established, per commission §28):

1. Reviewer session/model separation established (§1); the operator-designation and same-machine
   limitations are recorded and do not rise to the governance blocker defined in §2.
2. Official-driver connectivity succeeds (§7.1).
3. Scalar and graph results are correct (§7.3, §7.4).
4. Parameters are preserved, including null/missing distinction and query-like safety (§7.5).
5. Cursor completion succeeds (§7.6).
6. Connection reuse is instrumented and real (§7.6).
7. Manifest behavior is independently established byte-exactly, with conventional fallback (§7.1).
8. TLS interoperability verified within the claimed modes; the CA-trust arrangement is explicitly
   Qualified and distinct from system-trust validation (§7.7).
9. FAILURE/IGNORED behavior is correctly represented (`B07E` zero-field, no desynchronization)
   (§8.3).
10. Failed retirement cannot allow same-connection reuse — instrumented fail-closed confirmation
    (§8.4).
11. F01/F02 are independently adjudicated as non-blocking qualifications with precise remediation
    boundaries (§8.1, §8.2).
12. No material unresolved defect invalidates the accepted interoperability scope.
13. Historical regressions (40/40, 1,217/1,217, 80/80, 94/94, 119/119, 98/98, 19/19; builds clean)
    and evidence integrity (442/442 manifests; owner decisions byte-identical) are intact (§3, §4).

This is a reviewer disposition, not owner acceptance. **Stop after B5-B-Q1.** Remediation of F01
(bounded, recommended), F02 refinement (optional), and B5-B owner acceptance are separate,
individually authorized next steps.

---

## 15. Reviewer execution provenance

- Reviewer model: `nvidia/z-ai/glm-5.3-flash` (NVIDIA provider), dedicated new OpenCode child
  session in fresh context.
- Platform: Windows 10.0.26200, .NET SDK 10.0.401, runtime .NET 8.0.31, x64.
- Probe project builds 0 warnings/0 errors; `dotnet format --verify-no-changes` exit 0.
- All probes passing 53/53, executed twice in fresh processes.
