# B5-B-IMPL-01 — Official Neo4j .NET Driver Interoperability (Implementation Report)

- **Task:** `B5-B-IMPL-01`
- **Role:** Implementation agent (producer)
- **Repository:** `D:\projects\bolt-sharp`, canonical `main`, single worktree, never pushed
- **Starting HEAD:** `2bebd3ee77b12c190b2e48b763deb4444cc94e5d` (B5-A owner acceptance)
- **Starting tree:** `346611f5ebcd878432c924e50f0ff9d255b6822b`
- **Protocol target:** Bolt 5.8 / PackStream 1.0 (bounded, unchanged)
- **Driver baseline:** `Neo4j.Driver` **6.4.0** (exact pin, test-only; the previously qualified baseline)
- **Prerequisites:** B3-A/B/C and B5-A accepted with qualifications (all carried forward)
- **Evidence:** `docs/reviews/evidence/B5-B-IMPL-01/`
- **Disposition:** **B5-B-IMPL-01 — IMPLEMENTED, READY FOR INDEPENDENT B5-B-Q1 QUALIFICATION**

> **Reviewer/executor independence note (binding for B5-B-Q1):** this implementation and its
> producer verification were executed by the same executor session/model (DeepSeek V4.1 Flash,
> provider nvidia, OpenCode) that authored this report, which is the standing producer role. The
> B5-A owner acceptance explicitly requires that **B5-B release-relevant qualification use a
> genuinely separate executor session/model**. A model switch inside one OpenCode session is not
> sufficient. No behavior was self-qualified as release-grade here, and B5-B-Q1 was not started.

---

## 1. Preflight verification

Performed before any change (recorded in `logs/01-preflight.txt`):

1. Branch `main` — confirmed.
2. HEAD exactly `2bebd3ee77b12c190b2e48b763deb4444cc94e5d` — confirmed.
3. Tree exactly `346611f5ebcd878432c924e50f0ff9d255b6822b` — confirmed.
4. Exactly one worktree (`D:/projects/bolt-sharp`) — confirmed.
5. Working tree and index clean before any change — confirmed.
6. No concurrent writer; no stash; no previous B5-B implementation requiring reconciliation.
7. No push — branch remains ahead of `origin/main`; nothing was pushed.
8. Ancestry: B3-C owner acceptance `853580659cc57ad959c026800c1e17de44cdddc1` is an ancestor of HEAD;
   the four owner decisions (B3-A, B3-B, B3-C, B5-A) are present and byte-identical at HEAD
   (`logs/11-historical-manifests.txt`).
9. Solution configuration read before adding tests: `BoltSharp.slnx` lists only `src/BoltSharp`
   and `tests/BoltSharp.Tests`. The established repository convention (B2-C-01, B0-B-DRV-INT-01)
   keeps driver-dependent test projects out of the solution; B5-B follows it (§2).

Regression baseline observed at start (accepted floors): core 1,217, B5-A transport 80, B3-A 98,
B3-B 119, B3-C 94, B0-B 19; production build 0/0; solution 0 errors / 2 historical advisories.

## 2. Driver version, dependency isolation and test project

- **Package identity:** `Neo4j.Driver` version `6.4.0`, exact pin (no floating range), resolved for
  `net8.0` (verified from `project.assets.json` and the environment evidence).
- **Assembly identity recorded by the driver itself:** assembly version `6.4.12.0`, informational
  version `6.4.0+4ed413b01906c5d1682c62a3ee3c982cb28f99cd`; the driver's `bolt_agent.product`
  string it sends in HELLO is `neo4j-dotnet/6.4.12` (recorded verbatim; not normalized).
- **New test project:** `tests/BoltSharp.DriverInteropTests/` (xunit + the pinned driver + a
  project reference to `src/BoltSharp`). It is deliberately **not** added to `BoltSharp.slnx`, so
  the core solution restore/build stays free of the official driver and the accepted 1,217-test
  core-solution baseline is preserved. `src/BoltSharp` does not reference the driver (no
  production dependency was added; verified by `git diff` — only two new untracked paths appeared
  before commits).
- The historical B0-B project (`tests/BoltSharp.IntegrationTests/`) and the B2-C scaffold
  (`tests/BoltSharp.DriverIntegrationTests/`) are untouched; the B0-B tests still pass (19/19).

## 3. Harness architecture (test-only)

```text
tests/BoltSharp.DriverInteropTests/
  Infrastructure/  B5BServerHost, TestCredentials, TestCertificates, DriverFactory,
                   WireTapProxy, WireTranscript, EvidenceWriter, B5BCollection
  Scenarios/       ConnectivityTests, AuthenticationTests, ScalarQueryTests, GraphQueryTests,
                   ParameterInteropTests, CursorLifecycleTests, ErrorInteropTests,
                   TlsInteropTests, LifecycleAndMultiClientTests, CapabilityBoundaryTests
```

- **Real server.** Every scenario starts the genuine accepted `BoltTcpServer` (B5-A) on
  `IPAddress.Loopback` with an **ephemeral port**, the accepted secure defaults (plaintext
  loopback or explicit TLS), explicit Bolt 5.8 handshake options (manifest v1 enabled or
  disabled per scenario), the accepted B3-C reference provider via the transport's default
  provider factory, and B3-B's default session construction (the transport injects only the
  verifier — nothing in production was modified or parameterized).
- **Deterministic credentials.** `TestCredentials.Verifier` implements the accepted
  `IBoltCredentialVerifier` seam and accepts exactly one synthetic Basic principal/credential
  pair; missing verifiers and wrong credentials fail closed. Credential values never appear in
  evidence (writer guard + chunk-level LOGON redaction).
- **Official driver.** Constructed only through documented public APIs:
  `GraphDatabase.Driver(uri, AuthTokens.Basic(...), o => ...)`,
  `VerifyConnectivityAsync`, `GetServerInfoAsync`, `AsyncSession()`/`AsyncSession(o => ...)`,
  `RunAsync`, `FetchAsync`, `Current`, `ConsumeAsync`, `CloseAsync`/`DisposeAsync`,
  `BeginTransactionAsync`, `ExecuteReadAsync`. No transport replacement, no fake connection, no
  raw Bolt request injection into the driver. All driver operations are bounded; a hang fails a
  test.
- **Passive wire observation.** `WireTapProxy` is a loopback test-only tap between the driver and
  the real server that forwards every byte immediately and records a copy per read boundary
  (direction, timestamp, bytes, close kind). It never rewrites, delays or drops bytes; assertions
  and success claims rest on driver-visible results and server diagnostics, and the tap only
  supplements them with sanitized wire evidence. `WireTranscript` parses the captured streams
  byte-exactly (handshake preamble/proposals, manifest offer/selection, chunked frames),
  labels messages with the accepted B3-A codec for readability only, and redacts LOGON at both
  message and chunk level.
- **TLS material.** Certificates are generated in memory per scenario: a self-signed localhost
  certificate (`bolt+ssc://`) and an in-memory test CA plus a CA-signed localhost leaf (trusted
  only through the driver's documented `CertificateTrustRule.TrustList` configuration). Nothing
  is written to disk; no machine trust store is modified; private keys or PFX data are never
  committed.
- **Evidence.** With `BOLTSHARP_B5B_EVIDENCE_DIR` set, each scenario writes one sanitized JSON
  document (driver identity, URI scheme, handshake observations, transcripts, driver-visible
  results, exception classification, counters, events). The writer refuses any document
  containing the credential sentinel in plain or hex-encoded form.

## 4. Plaintext connectivity, handshake and Manifest v1

**`VerifyConnectivityAsync` over `bolt://127.0.0.1:{ephemeral}` succeeds** through the real
server (`b5b2-01`). Constructing an `IDriver` alone was verified not to connect
(`ConnectionsAccepted == 0` before the first operation — the mandatory distinction).
`GetServerInfoAsync` returns agent `BoltSharp/0.1.0` and protocol version 5.8, i.e. the driver
exposes the negotiated revision even though the server omits the optional manifest-only
`protocol_version` HELLO SUCCESS field.

**The driver's actual handshake** (`b5b2-02`), captured byte-exactly:

```text
C: 60 60 B0 17 | 00 00 01 FF | 00 08 08 05 | 00 02 04 04 | 00 00 00 03
   (manifest v1 marker; 5.0–5.8 range; 4.2–4.4 range; 3.0)
S: 00 00 01 FF | 01 | 00 00 08 05 | 00        (manifest offer: 1 range, capabilities 0)
C: 00 00 08 05 | 00                          (selection 5.8, capabilities 0)
   + framed HELLO immediately after the selection (no server reply is expected after selection)
```

- **Manifest v1 is the negotiated path** with the real driver and the real transport
  (`mode=ManifestV1` reported by the server; selection and HELLO share the client byte stream
  with no intervening server bytes).
- **Conventional fallback works** (`b5b2-03`): with `manifestV1Enabled: false`, the marker is an
  unsupported proposal, the driver's range proposal selects legacy 5.8 (`S: 00 00 08 05`), and a
  full scalar query succeeds.
- **Version honesty** (`b5b2-04`): a server advertising nothing answers the zero word; the driver
  fails without ever sending HELLO (`MessagesProcessed == 0`, handshake failure counted).
- **HELLO metadata** (actual): keys `[user_agent, routing, bolt_agent]` in wire order;
  `user_agent = "neo4j-dotnet/6.4"`; `routing = null` (direct bolt, no routing);
  `bolt_agent = {product: "neo4j-dotnet/6.4.12", platform: "Microsoft Windows 10.0.26200;X64",
  language_details: ".NET 8.0.31"}`. The accepted B3-A schema (mandatory `user_agent` +
  mandatory `bolt_agent.product`) matches the driver exactly; the B3-A `bolt_agent` obligation is
  exercised live.
- **LOGON shape**: keys `[scheme, principal, credentials]`, `scheme = "basic"`, principal and
  credentials redacted — never stored.
- **HELLO SUCCESS** actually sent: `{server: "BoltSharp/0.1.0", connection_id: "bolt-b3b-1",
  hints: {}}`; **LOGON SUCCESS** is the empty `SUCCESS {}` (`B1 70 A0`). The driver requires
  neither `protocol_version` nor `advertised_address` (both optional; classified in §14).
- **Close sequence** (`b5b2-06`): the driver's pooled lifecycle uses `RESET` (check-out/check-in)
  and terminates with `GOODBYE`; no response is fabricated for GOODBYE; the socket closes with a
  clean EOF; `accepted == completed == 1`, `failed == 0`, `active == 0`.

## 5. Authentication interoperability

- **Invalid credentials** (`b5b2-20`): `AuthenticationException` with
  `Neo.ClientError.Security.Unauthorized`; the server sends the accepted terminal LOGON FAILURE
  (`50N42`/`Unauthorized`) and closes; no RUN is ever sent; no credential material appears in
  diagnostics or evidence; the listener remains healthy for a correct driver.
- **Missing verifier** (`b5b2-21`): fail-closed preserved through the real transport; same
  driver-visible classification; exactly HELLO + LOGON processed.
- **Terminal, no retry** (`b5b2-22`): exactly one LOGON attempt on exactly one connection for a
  non-retriable authentication failure.

## 6. Scalar query interoperability (`RETURN 42 AS answer`)

- **Driver-visible result** (`b5b3-01`): one record, field `answer`, value `42` (`System.Int64`),
  cursor exhausts, `ConsumeAsync` returns a summary, and a subsequent query on the same pooled
  connection returns 42 again.
- **RUN/PULL wire capture** (`b5b3-02`): default (write-mode) session RUN extra is `{}`; read
  access mode adds exactly `{mode: "r"}`; both accepted by the B3-C allowlist. The default-form
  RUN bytes equal the accepted B5-A golden vector
  (`B310D01352455455524E20343220415320616E73776572A0A0`); PULL is `{n: 1000}`
  (`B13FA1816EC903E8`); RUN SUCCESS is `fields=["answer"]`; RECORD is `B171912A`; the terminal
  summary is `B170A0` (`SUCCESS {}`, no `has_more`). No ROUTE, TELEMETRY, DISCARD or duplicate
  RUN occurred.
- **Terminal `has_more` behavior** (`b5b3-03`, `b5b6-01/02/03`): the official driver treats the
  accepted `SUCCESS {}` (absent `has_more` defaults to false) as **result exhaustion** — a
  further `FetchAsync` yields no record, no extra PULL is issued, the cursor closes after
  consumption, and the connection remains reusable. `has_more:true` (driver fetch size 1)
  correctly drives additional PULLs (§10). This is the live-driver reconciliation the B3-B/B3-C
  qualification deferred; it is established for this driver and query path only.

## 7. Home-database metadata investigation

(`b5b3-04`; Bolt 5.8 defines RUN SUCCESS `db` as the resolved home database, present only when the
client did not explicitly select a database.)

- The accepted server omits `db` (and `t_first`); the driver works without it: two sequential
  implicit transactions on one connection return 42.
- **No database-resolution request occurs**: no ROUTE, no TELEMETRY, no synthetic query; one
  connection, one HELLO.
- PULL terminal metadata (`SUCCESS {}`) is likewise accepted.
- No fabricated database identity was introduced. Recorded as optional-metadata behavior, not a
  mandatory field.

## 8. Graph query interoperability

(`b5b4-01/02` — `MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b`)

- The official driver materializes **typed** values: Alice (`INode`, numeric id 1, element id
  `n:1`, label `Person`, `name = "Alice"`), Bob (`INode`, numeric id 2, element id `n:2`,
  `name = "Bob"`), and `KNOWS` (`IRelationship`, numeric id 1, element id `r:1`, type `KNOWS`,
  start `n:1` / end `n:2`, `since = 2024`).
- The deliberate numeric collision (node id 1 and relationship id 1) remains distinct typed
  identity; element-id spaces differ (`n:1` vs `r:1`).
- The captured RECORD is **byte-identical to the accepted B5-A golden vector**, and the field
  list is exactly `["a","r","b"]`.

## 9. Parameter interoperability

(`b5b5-01/02/03` — `RETURN $value AS answer` through `RunAsync` with real parameter maps)

Preserved and returned with correct driver-visible types: positive integer `42`, negative
integer `-7`, string `"hello"`, Unicode string `"héllo ✓ 世界"`, boolean `true`/`false`,
explicit `null` (record value is null), list `[1,2,3]` (driver `List<object>` of longs), and a
nested map (`{x: 1, inner: {y: "z"}}` as `IDictionary<string, object>`).

- **Explicit null vs missing**: `{"value": null}` is transmitted (`C0`) and returned as null;
  a missing parameter reaches the wire with an empty parameter map (`A0`) and is refused
  **server-side** (recoverable `50N42`/`Request.Invalid`, no fabricated record) — this is a
  server-side refusal, not client-side validation.
- **No query-text interpolation**: a query-like payload (`MATCH (n) DETACH DELETE n`) is returned
  verbatim as a string; the graph verifiably remains Alice/Bob afterwards; the query text on the
  wire is exactly the parameterized form.
- The driver performed no local rejection of any value within the supported surface.

## 10. Cursor completion, pagination and connection reuse

- **Multi-record result** (`b5b6-01`): `MATCH (n:Person) RETURN n.name AS name ORDER BY name`
  yields `[Alice, Bob]` in one default-size PULL with a terminal `SUCCESS {}`.
- **Bounded pagination through the driver** (`b5b6-02`): with `WithFetchSize(1)` the driver issues
  one `PULL n=1` per record; the server answers the first with `has_more=true`
  (`B170A1886861735F6D6F7265C3`) and completes with `SUCCESS {}`; the driver consumed all
  records, exhausted the cursor, and reused the same connection for a subsequent query.
  (The accepted backend cannot produce >1,000 records in one result, so beyond-fetch-size
  pagination is exercised through the finite 2-record fixture.)
- **DISCARD on partial consumption** (`b5b6-03`): `ConsumeAsync` after a partial fetch sends
  `DISCARD n=-1` (`B12FA1816EFF`); the server's terminal `SUCCESS {}` is accepted as exhaustion;
  the same session/connection continues with the next query.
- **Session disposal with an outstanding result** (`b5b6-04`): the driver deterministically
  discards/resets the unconsumed result; the server retired the operation; the next query reused
  the same connection; no failed connections.
- **Repeated supported queries, one pooled connection, no retry** (`b5b6-05`): three sequential
  sessions produced exactly one HELLO, one LOGON, three RUNs, three PULLs and one GOODBYE, with
  no FAILURE/IGNORED frames.

## 11. Unsupported-query error interoperability

(`b5b8-01/03/04` — `MATCH (n) DETACH DELETE n`)

- Driver exception classification: `Neo4j.Driver.ProtocolException` (a `Neo4jException` subtype)
  with `Code = Neo.ClientError.Request.Invalid`, `GqlStatus = 50N42`,
  `GqlClassification = UNKNOWN`, `IsRetriable = false`, description
  `"The requested operation is not supported by this server."` (recorded verbatim).
- The session recovered and subsequent scalar/graph queries returned the correct reference
  values; the graph was verifiably unchanged; the FAILURE metadata carries no query-text echo.
- **No automatic retry / no duplicate execution**: exactly one RUN for the refused query with the
  default configuration and with `WithDisableAutoCommitRetries(true)`.
- **Connection-level behavior** (finding F02, §15): after the FAILURE the driver destroys the
  connection (no RESET is sent) because the unknown classification is treated as unrecoverable;
  recovery is transparent for the next operation via a fresh connection.

## 12. Live-driver IGNORED (historical B3-A F-02 obligation)

(`b5b8-02` — the commission's §17 investigation; no bytes were injected into the driver.)

Observed, naturally produced by the driver's public API:

```text
C (one pipelined client write): RUN "MATCH (n) DETACH DELETE n" {}{}   +   PULL {n:1000}
S: FAILURE {gql_status:50N42, neo4j_code:Neo.ClientError.Request.Invalid, ...}   (for RUN)
S: IGNORED   -> byte-exact B0 7E (zero-field; the B3-A F-02-correct encoding)    (for PULL)
C: (nothing further on this connection; the driver destroys it)
```

- The driver's public API **reproducibly produces the qualifying sequence**: it pipelines PULL
  with a failing auto-commit RUN, so the PULL is answered with `B0 7E` after the FAILURE.
- The driver consumed the sequence without desynchronization, decoded no phantom record, reported
  the RUN failure, and recovered (session-level, new connection).
- **Same-connection RESET recovery after the IGNORED was NOT exercised** under the accepted
  `50N42` classification: the driver treats the unknown classification as a `ProtocolException`
  and destroys the connection instead of sending RESET. The historical B0-B T06 scripted
  evidence shows `RESET` + reuse for a driver-recognized `42001` classification, which the
  accepted reference backend does not produce.
- **Producer status: LIVE-DRIVER IGNORED — EXERCISED with recorded evidence.** The producer does
  **not** unilaterally mark the historical F-02 obligation fully closed; closure (and any
  decision about the classification interplay) belongs to B5-B-Q1/owner review.

## 13. TLS URI interoperability

- **`bolt+ssc://localhost:{port}` with a self-signed certificate** (`b5b7-01`): TLS handshake
  completes inside the real listener, `VerifyConnectivityAsync` and the scalar query succeed
  (42), counters clean, close sequence normal.
- **`bolt+s://localhost:{port}` with a test certificate** (`b5b7-02`): rejected —
  `ServiceUnavailableException`; **no Bolt byte was processed** (no HELLO) and no downgrade
  path exists. The driver documents that `+s` always validates against the operating system CA
  store and ignores the trust list, so a test-only CA cannot be trusted that way without an
  intrusive, permanent machine trust change. Recorded as an **explicit trust-arrangement
  limitation**, not pretended as satisfied.
- **CA-trusted TLS, isolated arrangement** (`b5b7-03`): a CA-signed leaf validated through the
  driver's documented `WithEncryptionLevel(Encrypted)` +
  `WithCertificateTrustRule(CertificateTrustRule.TrustList, [test CA])` public API on a `bolt://`
  URI; connectivity and scalar query succeed. No machine trust store modification; the CA and
  leaf exist only in memory.
- **Negative controls** (`b5b7-04/05`): plaintext `bolt://` against a TLS-only listener fails
  closed and is counted (`TlsHandshakeFailures >= 1`) with no Bolt message processed; invalid
  credentials over `bolt+ssc://` are rejected with the accepted Unauthorized classification and
  no downgrade. Both listeners remained healthy for correct clients afterwards.

## 14. Metadata and notification behavior classification

Distinguishing normative mandatory fields, optional protocol fields, driver-expected fields and
future compatibility enhancements (commission §19):

| Field | Status | Observed behavior |
| --- | --- | --- |
| HELLO `user_agent`, `bolt_agent.product` | **Normative mandatory** (Bolt 5.3+) | Present; accepted by B3-A |
| HELLO `routing: null` (direct bolt) | Optional; normative default | Present as null; accepted |
| HELLO SUCCESS `server`, `connection_id` | Normative-defined; driver-expected | Present; driver reports `BoltSharp/0.1.0` |
| HELLO SUCCESS `hints` | Optional (4.3+) | Present as `{}`; accepted; no telemetry hint, so the driver sends no TELEMETRY |
| HELLO SUCCESS `protocol_version` | **Optional; only present under manifest v1 (5.7+)** | **Absent**; the driver does not require it and exposes 5.8 itself. Adding it is a future compatibility enhancement (would be a B3-B/session-metadata change; not authorized here) |
| LOGON SUCCESS `advertised_address` | **Optional (5.8+)** | **Absent**; not required and not used in the direct-bolt flows (absence is conformant; no address fabricated) |
| LOGON SUCCESS `credentials_expired` | Optional; default false | Absent; driver treats as false |
| RUN SUCCESS `fields` | Normative-defined | Present |
| RUN SUCCESS `t_first`, `qid` | Optional | Absent; not required |
| RUN SUCCESS `db` (home database) | **Optional (5.8), implicit transactions only** | Absent; driver works; no resolution request (§7) |
| PULL/DISCARD `has_more` | Optional; **absent defaults to false (normative)** | Absent at exhaustion; driver treats as exhaustion |
| PULL terminal `bookmark`, `db`, `stats`, `notifications`, `statuses`, `type`, `t_last` | Optional | Absent; not required for the accepted surface; no notification/counter data is fabricated |
| FAILURE `gql_status`, `neo4j_code`, `description`, `diagnostic_record` | Normative-defined (5.7+) | Present; classified by the driver (UNKNOWN classification recorded) |
| FAILURE `cause` | Optional | Absent; accepted |

## 15. Confirmed findings, classifications and remediation boundaries

### B5-B-IMPL-F01 — Unsupported transaction/routing messages are refused by connection closure, not by the B3-B documented FAILURE

1. **Driver API call:** `IAsyncSession.BeginTransactionAsync()` / managed
   `ExecuteReadAsync(...)` / `neo4j://` first query (ROUTE).
2. **Driver version:** 6.4.0.
3. **Connection scheme:** `bolt://` and `neo4j://`.
4. **Actual negotiation:** Bolt 5.8 manifest v1 (normal).
5. **Last successful exchange:** HELLO/LOGON (+ pooled RESETs) — all succeeded.
6. **Failing request/response:** client sends `BEGIN` (`B1 11 A0`; managed adds `{mode:"r"}`) or
   `ROUTE` (`B1 66 ...`); the server responds with **nothing** and closes the connection.
7. **Expected normative behavior:** at minimum, a protocol error ends the connection (conformant);
   BoltSharp's own accepted B3-B contract additionally defines a terminal `08N06` FAILURE
   ("Explicit transactions and routing are not supported by this server.") for these messages.
8. **Actual BoltSharp behavior:** the B5-A transport decodes each message with the B3-A codec
   before the session; BEGIN/COMMIT/ROLLBACK/ROUTE raise `BoltMessageException(UnsupportedMessage)`,
   which the transport classifies as a codec/transport failure and closes without writing any
   response. The B3-B `ReceiveBytes` unsupported/malformed handling is not reachable through the
   accepted transport. The same applies to codec-level malformed-message rejections.
9. **Driver exception and classification:** `ServiceUnavailableException`
   ("Unexpected end of stream ...") for BEGIN/ROUTE; the managed-transaction retry loop retried
   within its bounded retry budget and then surfaced the same connection failure.
10. **Unsupported options sent:** not applicable (the messages are unsupported message types).
11. **Provider execution began:** no — no RUN ever executed and the graph was unchanged.
12. **Session resources retired:** yes — the server closes and disposes the session; no operation
    was open; no failed-retirement path was entered.
13. **Minimum reproduction:** `B5B10_01` / `B5B10_02` / `B5B10_03` (real driver + real server +
    passive capture).
14. **Relevant source location:** `src/BoltSharp/Transport/BoltTcpConnection.cs`
    (`RunMessageLoopAsync` codec call; `catch (BoltMessageException)` closure) versus
    `src/BoltSharp/Sessions/BoltConnectionSession.cs` (`ReceiveBytes` →
    `UnsupportedMessageFailure`).
15. **Recommended remediation boundary:** a **separately authorized bounded production change**
    (B5-A or B3-B/B5-A wiring): feed complete payloads through the accepted
    `BoltConnectionSession.ReceiveBytes` entry point, or map codec `UnsupportedMessage`/malformed
    rejections to the accepted terminal FAILURE response before closing. **No production change
    was made by B5-B.**

**Classification:** *Accepted capability limitation / cross-component wiring difference.* It is
**not** a normative Bolt defect (closing on a protocol error is conformant) and **not**
acceptance-blocking: all four features are explicitly out of scope, execution is refused, no
state changes, and the driver surfaces an observable error. It is recorded precisely because the
driver-visible refusal shape differs from BoltSharp's own documented B3-B failure.

### B5-B-IMPL-F02 — The accepted generic 50N42 classification makes the driver discard connections after refusals

Under the accepted generic `50N42`/`Request.Invalid` classification, the driver surfaces a
non-retriable `ProtocolException` (classification UNKNOWN) and destroys the connection rather
than RESET-recovering it; session-level recovery works transparently via a fresh connection. The
B3-C owner acceptance explicitly permits evaluating more granular error mapping during B5-B; a
refinement (for example a driver-recognized client-error classification for unsupported queries)
is a **candidate for a separately authorized change with requalification**, not implemented here.

**Classification:** *Driver-specific behavior interacting with the accepted bounded error
classification* (optional-behavior difference; not a protocol defect). Not acceptance-blocking.

### No-silent-fix attestation

No RUN extras were rewritten, no driver metadata was fabricated, no handshake behavior changed,
no authentication failure was suppressed, no FAILURE/IGNORED behavior was modified, no automatic
server-side retry exists, no record was invented after a refusal, and TLS validation was never
disabled to manufacture success.

## 16. Handshake fragmentation observations (commission §18)

- The driver's real negotiation over the accepted transport succeeds on both paths (manifest and
  conventional fallback) with byte-exact replies; no fragmentation-dependent failure occurred in
  any of the 40 scenarios (including repeated runs).
- Observed server write shapes (loopback read boundaries, recorded in evidence, never assumed by
  the server): the manifest offer and the 4-byte legacy reply are each delivered as a single
  read chunk in all captured runs (`manifest_offer_single_read_chunk`, 
  `legacy_reply_single_read_chunk` observations). A TCP implementation must not rely on write
  atomicity; no transport workaround exists or was introduced.
- The historical B0-B observation (the driver's handshake reader rejecting a *split* 4-byte
  legacy reply) concerns the driver's own reader behavior under artificial server-side splitting;
  B5-A/B5-A-Q1 already cover fragmented client-side delivery and pinned pipelining. B5-B adds the
  real-driver single-write observation above and introduces no sleeps or forced fragmentation.
- The handshake bytes generated by the production negotiator are asserted byte-exactly
  (`00 00 01 FF 01 00 00 08 05 00` offer; `00 00 08 05` legacy reply).

## 17. Capability matrix (producer)

Full machine-readable version with evidence references: `compatibility-matrix.json`. Statuses
never use PASS for unexecuted scenarios.

| Capability | Target | Producer status |
| --- | --- | --- |
| Direct plaintext connection | Required | **Passed** |
| Driver authentication | Required | **Passed** |
| Scalar auto-commit query | Required | **Passed** |
| Graph auto-commit query | Required | **Passed** |
| Supported parameters | Required | **Passed** |
| Full result consumption | Required | **Passed** |
| Connection reuse after consumption | Required | **Passed** |
| Manifest negotiation | Required if exercised by driver | **Passed** (driver exercises it; fallback also verified) |
| Terminal `has_more` behavior | Required | **Passed** |
| Self-signed TLS | Targeted | **Passed** |
| CA-trusted TLS | Qualified where test trust available | **Passed** with an isolated trust list; literal `bolt+s://` system-trust limitation recorded |
| Unsupported-query error | Targeted | **Passed** (classification recorded; F02 connection policy noted) |
| Driver-level IGNORED recovery | Investigate, do not fabricate | **Attempted** — live sequence exercised; same-connection RESET recovery not exercised (F02); not marked closed |
| Explicit transactions | Not supported | **Attempted** — refused (F01) |
| Managed transaction callbacks | Not supported | **Attempted** — refused (F01) |
| General Cypher | Not supported | **Attempted** — refused; allowlist preserved |
| Mutations | Not supported | **Attempted** — refused; graph unchanged |
| Routing / `neo4j://` | Not supported | **Attempted** — refused (F01) |
| Cluster behavior | Not supported | Outside scope |
| Multi-database support | Not supported | **Attempted** — refused by the allowlist |
| Query notifications completeness | Not claimed | **Not exercised** (nothing fabricated) |
| Release-grade security/performance | Not claimed | Outside scope |

## 18. Regression gates

Executed fresh by `Run-B5BImpl.ps1` (logs and TRX in the evidence directory):

| Suite | Floor | Observed |
| --- | ---: | ---: |
| New official-driver interoperability suite | — | **40 / 40 passing** |
| B5-A transport | 80 | **80 / 80** |
| B3-C reference backend | 94 | **94 / 94** |
| B3-B session engine | 119 | **119 / 119** |
| B3-A message codec | 98 | **98 / 98** |
| Core test project (total) | 1,217 | **1,217 / 1,217** |
| B0-B official-driver integration (separate project, unchanged) | 19 | **19 / 19** |
| Production build | 0 warnings / 0 errors | **0 / 0** |
| Solution build | 0 errors / 2 historical warnings | **0 errors / 2 warnings** (the two pre-existing `xUnit2012` advisories at `BoltRequestDecodeTests.cs:71-72`, untouched) |
| Scoped format check (new test project) | exit 0 | **exit 0** |

New tests increase the relevant total: **40 new tests**. No existing test was weakened or
modified; no production dependency was added.

## 19. Historical evidence integrity

Verified during the run (`logs/11-historical-manifests.txt`, `historical-integrity.json`):

- All eleven historical manifests: **352/352** hashed entries matched (B3-C-Q1 29, B3-C-IMPL 26,
  B3-B-Q3 33, B3-B-R2 29, B3-B-Q2 38, B3-B-Q1 12, B3-A-Q1 14, B3-A-R1 13, B3-A-Q2 15,
  B0-B 24, B0-B-Q1 119).
- B5-A producer **18/18** and B5-A-Q1 **10/10** matched (total 380/380).
- All four accepted owner decisions (B3-A, B3-B, B3-C, B5-A) are byte-identical to their
  committed blobs at HEAD.
- The B0-B fixtures, scripted server and expected values were not modified; nothing historical
  was rewritten.

## 20. Changed files

Production: **none** (no `src/` file and no accepted B2/B3/B5-A file was touched).

New test project (commits 1–2):

```text
tests/BoltSharp.DriverInteropTests/BoltSharp.DriverInteropTests.csproj
tests/BoltSharp.DriverInteropTests/README.md
tests/BoltSharp.DriverInteropTests/Infrastructure/B5BCollection.cs
tests/BoltSharp.DriverInteropTests/Infrastructure/B5BServerHost.cs
tests/BoltSharp.DriverInteropTests/Infrastructure/DriverFactory.cs
tests/BoltSharp.DriverInteropTests/Infrastructure/EvidenceWriter.cs
tests/BoltSharp.DriverInteropTests/Infrastructure/TestCertificates.cs
tests/BoltSharp.DriverInteropTests/Infrastructure/TestCredentials.cs
tests/BoltSharp.DriverInteropTests/Infrastructure/WireTapProxy.cs
tests/BoltSharp.DriverInteropTests/Infrastructure/WireTranscript.cs
tests/BoltSharp.DriverInteropTests/Scenarios/AuthenticationTests.cs
tests/BoltSharp.DriverInteropTests/Scenarios/CapabilityBoundaryTests.cs
tests/BoltSharp.DriverInteropTests/Scenarios/ConnectivityTests.cs
tests/BoltSharp.DriverInteropTests/Scenarios/CursorLifecycleTests.cs
tests/BoltSharp.DriverInteropTests/Scenarios/ErrorInteropTests.cs
tests/BoltSharp.DriverInteropTests/Scenarios/GraphQueryTests.cs
tests/BoltSharp.DriverInteropTests/Scenarios/LifecycleAndMultiClientTests.cs
tests/BoltSharp.DriverInteropTests/Scenarios/ParameterInteropTests.cs
tests/BoltSharp.DriverInteropTests/Scenarios/ScalarQueryTests.cs
tests/BoltSharp.DriverInteropTests/Scenarios/TlsInteropTests.cs
```

Evidence (commit 3): `docs/reviews/B5-B-IMPL-01.md` and
`docs/reviews/evidence/B5-B-IMPL-01/` (scenario JSON documents, environment, compatibility
matrix, TRX results, historical verification, logs, reproduction scripts, SHA-256 manifest).

## 21. Implementation commits

| Commit | Subject | Tree |
| --- | --- | --- |
| `f79fb8d4f7f4bcd8efb7953c37c084b09ee48ec7` | `test(boltsharp): add official Neo4j driver live integration harness` | `4e4e81a39c0c5503341af9581fff6cef973172ae` |
| `b749a655760ede015042ad2f8662e2bb64f46631` | `test(boltsharp): verify official driver scalar graph and TLS flows` | `339c77b26b65f797d26f1083eb4c7f2b6f893eca` |
| (final commit, this report + evidence) | `docs(boltsharp): record B5-B official driver interoperability evidence` | recorded in the final handoff |

Parent of commit 1: `2bebd3ee77b12c190b2e48b763deb4444cc94e5d`
(tree `346611f5ebcd878432c924e50f0ff9d255b6822b`). No push; no history rewrite.

## 22. Remaining qualifications (carried forward; none silently closed)

Every accepted B3/B5-A qualification remains binding, including: exact-form Cypher allowlist
(Q-B3C-01); bounded refusal granularity (Q-B3C-02); parameter surface (Q-B3C-03); bounded
materialization (Q-B3C-04); normalization (Q-B3C-05); B3-B bounded `PULL(n=-1)` behavior
(Q-B3B-01); no full RESET jump-ahead (Q-B3B-02); `ReceiveBatch` ownership (Q-B3B-03);
single-owner sessions (Q-B3B-04); provisional public APIs (Q-B3B-05); bounded/generic error
classifications (Q-B3B-06, F02); TELEMETRY structural boundary (Q-B3B-07); no provider
cancellation API (Q-B3B-08); driver interoperability scope (Q-B3B-09, narrowed by this
milestone's evidence); the two historical advisories; reviewer independence (Q-B3B-11, Q-B5A-01);
the B3-A F-02 obligation (partially advanced, not closed — §12); fail-closed supersession
(Q-B3B-13); diagnostic semantics (Q-B3B-14); TLS depth (Q-B5A-02/Q-B5A-03); slow-provider
responsiveness (Q-B5A-05); slow-client write-timeout (Q-B5A-06, remains open/inconclusive);
session-options passthrough (Q-B5A-07 — the transport still constructs default
`BoltSessionOptions` with only the verifier injected, so driver-driven tests run under the
default `MaxRecordsPerResponse`); request encode surface (Q-B5A-08); no performance claims
(Q-B5A-09); bounded shutdown (Q-B5A-10); plus the new F01/F02 boundaries and the
trust-arrangement limitation.

**Exact resolution scope of what this milestone did establish:** for the **official
Neo4j.Driver 6.4.0** over **BoltSharp 5.8/direct-bolt** with the **accepted reference backend**,
the required scalar/graph/parameter/cursor/TLS/lifecycle paths work end-to-end; terminal
`SUCCESS {}` exhaustion and the zero-field `B0 7E` IGNORED were observed live. This does not
prove universal driver compatibility across other driver versions, routing, transactions, or
other protocol revisions.

## 23. Producer execution provenance

- Executor: DeepSeek V4.1 Flash (provider `nvidia`) in OpenCode — the same producer model as
  B5-A-IMPL-01. Platform: Windows 10.0.26200, .NET SDK 10.0.401 (per `logs/01-preflight.txt`),
  runtime .NET 8.0.31, x64.
- B5-B-Q1 **must be executed in a genuinely separate executor session/model** (B5-A owner
  acceptance §19/§28 of this commission). This producer does not self-qualify and records no
  owner acceptance.

## 24. B5-B-Q1 reviewer handoff

1. Start from `docs/reviews/evidence/B5-B-IMPL-01/README.md`; reproduce with
   `Run-B5BImpl.ps1` (the scenario JSON documents are regenerated by the suite itself).
2. Challenge the driver-side claims independently: the transcripts are passive observations;
   re-derive expected bytes from the normative Bolt specification and the historical B0-B/B3-A
   vectors rather than from this report.
3. Probe the areas this producer left open: (a) live-driver IGNORED closure decision and the
   `50N42` classification interplay (F02) with an instrumented provider; (b) the F01 boundary
   (BEGIN/ROUTE closure vs documented FAILURE) and whether any accepted contract requires the
   richer refusal; (c) session-options passthrough (Q-B5A-07) with non-default
   `MaxRecordsPerResponse` via an instrumented provider (the transport exposes no option, so a
   reviewer probe must exercise the seam directly or request a bounded change);
   (d) failure-retirement behavior driven by the official driver with a fault-injecting provider
   (the mandatory Q1 overlap control); (e) genuine blocked-write backpressure remains
   inconclusive (Q-B5A-06/F02) and is not claimed.
4. Verify the historical manifests and owner-decision byte identity independently; confirm no
   production file changed (`git diff --name-status 2bebd3e..HEAD`).
5. The reviewer must not silently close any carried qualification, must not mark the F-02
   obligation fully closed on the producer's evidence alone, and must state any independence
   limitations explicitly.

## 25. Formal producer disposition

### **B5-B-IMPL-01 — IMPLEMENTED, READY FOR INDEPENDENT B5-B-Q1 QUALIFICATION**

Basis:

1. The unmodified official `Neo4j.Driver` 6.4.0 establishes a genuine TCP connection to the real
   B5-A server, negotiates Bolt 5.8 (manifest v1, with conventional fallback verified), and
   authenticates through the documented public API.
2. Scalar (`RETURN 42 AS answer`), graph (typed Alice/KNOWS/Bob with the accepted golden RECORD)
   and parameterized queries succeed with correct application-visible values.
3. Result exhaustion, terminal `SUCCESS {}` semantics, bounded fetch-size pagination, `DISCARD`
   on partial consumption and pooled connection reuse are verified.
4. Authentication boundaries, TLS `bolt+ssc://`, the isolated CA-trust arrangement, and all
   required negative controls behave as documented; the literal `bolt+s://` trust limitation is
   explicit.
5. Unsupported operations are refused without execution or mutation; the driver-visible error
   classification is recorded; the live-driver IGNORED sequence was exercised and honestly
   bounded (not marked fully closed).
6. Two interoperability findings (F01 wiring boundary, F02 classification interplay) are
   precisely documented with minimal remediation candidates and **no production change**; neither
   invalidates the minimum interoperability claim.
7. All regression gates pass (40 new + 1,217 core + 80 + 94 + 119 + 98 + 19; builds and format
   clean with only the two historical advisories); historical evidence integrity is 380/380 with
   four owner decisions byte-identical.
8. This is producer evidence, not independent qualification and not owner acceptance.

**Recommended next step:** commission **B5-B-Q1** with a genuinely separate executor session/model
per the B5-A owner acceptance, focused on the reviewer handoff in §24 (especially the IGNORED
closure decision, F01/F02, the fail-closed retirement overlap control with the official driver,
and the open B5-A qualifications). Any production remediation of F01/F02 must be separately
authorized and requalified.
