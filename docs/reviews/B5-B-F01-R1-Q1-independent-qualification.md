# B5-B-F01-R1-Q1 — Independent Transport/Session Boundary Remediation Qualification

- **Task:** `B5-B-F01-R1-Q1`
- **Role:** Independent adversarial remediation reviewer (Q1)
- **Repository:** `D:\projects\bolt-sharp`, canonical `main`, one worktree, never pushed
- **Starting HEAD:** `4cebc7cc40785f8cf502aeb25be74e39d1883670`
- **Starting tree:** `947d8826eb53ba70eb467c055cce852ea872a380`
- **Pre-remediation baseline inspected:** `3953bdd9962a8e4dc4c43c0bb3996b5fed76fb08`
- **Implementation commit audited:** `1e3c8f89f1bab38903001dc25edc96b7a7adbc01`
- **Evidence commit (producer):** `4cebc7cc40785f8cf502aeb25be74e39d1883670`
- **Protocol:** Bolt 5.8 / PackStream 1.0
- **Official client:** `Neo4j.Driver` 6.4.0 (exact pin, unmodified)
- **Evidence:** `docs/reviews/evidence/B5-B-F01-R1-Q1/`
- **Probes:** `docs/reviews/probes/B5-B-F01-R1-Q1/` (test-only, outside `BoltSharp.slnx`)
- **Disposition:** **B5-B-F01-R1-Q1 — SUPPORTED WITH QUALIFICATIONS**

---

## 1. Reviewer independence

| Item | Value |
| --- | --- |
| Reviewer model | GLM-5.3-Flash (`nvidia/z-ai/glm-5.3-flash`) |
| Reviewer provider | NVIDIA (`nvidia`) |
| Reviewer session | Dedicated NEW OpenCode child session, spawned in fresh context by the operator session for this task; the producer's execution context was not continued |
| Fresh context | Yes |
| Producer model (per committed evidence) | DeepSeek V4.1 Flash, provider `nvidia`, OpenCode, distinct producer session (the implementation report itself binds this note) |
| Producer session separation | Established: different model, different session; no producer context reused |
| Shared infrastructure | Same machine, repository, .NET SDK and NuGet caches (organizational independence is not required and is not claimed) |
| Limitations on independence | Recorded honestly: (1) session/model separation is established by this dedicated child session plus the different model; (2) the reviewer model/provider assignment was operator-designated and is not independently attestable from inside the session; (3) same operator, machine and repository — no organizational independence |

All oracles used in this review are reviewer-constructed: an independent PackStream encoder
(`ProbePackStream`, written from the PackStream 1.0 marker tables, not the production writer), an
independent raw Bolt client (own handshake, own B2 chunk framing, own payload bytes), a reviewer-
owned recording TCP proxy, reviewer-owned `IBoltOperationProvider` implementations, and the
direct accepted `BoltConnectionSession.ReceiveBytes` entry point. No expectation was derived from
the modified transport implementation. The modified transport's own encoder is used only to
serialize the session's responses for comparison against independently derived bytes.

## 2. Preflight (all twelve gates verified)

1. Branch exactly `main` — confirmed.
2. HEAD exactly `4cebc7cc40785f8cf502aeb25be74e39d1883670` — confirmed.
3. Tree exactly `947d8826eb53ba70eb467c055cce852ea872a380` — confirmed.
4. Exactly one canonical worktree (`D:/projects/bolt-sharp`) — confirmed.
5. Working tree and index clean at start — confirmed.
6. No stash — confirmed.
7. No concurrent writer observed during the review.
8. No previous F01-R1-Q1 work (no report, probes or evidence existed) — confirmed.
9. All five accepted owner decisions present — confirmed.
10. Implementation commit `1e3c8f8` and evidence commit `4cebc7c` present — confirmed.
11. Ancestry: `1e3c8f8` is an ancestor of the starting HEAD; the parent chain reaches the B5-B
    owner acceptance (`6b2a0e3`) — confirmed.
12. No push: 63 commits ahead of the locally recorded `origin/main`; no fetch performed — confirmed.

Evidence: `01-provenance-preflight.txt`.

## 3. Historical F01 comparison (Q1) — original defect independently established

**Fresh runtime reproduction (not historical inspection alone).** The pre-remediation source was
extracted with `git archive 3953bdd -- src` into an isolated temp directory (no second repository
writer; canonical tree untouched; no worktree created). A reviewer-written probe was built
against the historical source and executed:

- Raw BEGIN over real TCP at `3953bdd`: handshake ✓, HELLO ✓ (`B170A3…`), LOGON ✓ (`B170A0`),
  BEGIN → server diagnostic `ConnectionFailed: codec:UnsupportedMessage`, **no response**,
  connection closed (`closedWithoutResponse=True`).
- Direct accepted B3-B `ReceiveBytes` on the SAME historical revision: BEGIN → `state=Defunct`,
  `shouldClose=True`, `prepareCount=0`, single FAILURE with byte payload
  `B17FA48A…646961676E6F737469635F7265636F7264A0` — exactly the accepted `08N06` refusal.

**Source inspection (git, read-only):** at `3953bdd`, `RunMessageLoopAsync` pre-decoded with
`codec.DecodeRequest(message)` and only then called `session.Receive(request)`; the escaping
`BoltMessageException` was caught in `RunAsync` as `failed=true; outcome="codec:{category}"` and
closed the connection without writing any response. The accepted session entry point
`BoltConnectionSession.ReceiveBytes` — which already mapped `UnsupportedMessage` to the terminal
`UnsupportedMessageFailure()` and everything else to `ProtocolViolationFailure()` — was
unreachable through the production adapter for categories B and C.

**Conclusion (Q1.1):** the original transport genuinely bypassed the accepted B3-B
unsupported-command path. The root-cause claim is independently confirmed by fresh execution,
not accepted from the producer's report. Evidence: `17-historical-f01-comparison.txt`.

## 4. Implementation-diff audit (§5)

`git diff 3953bdd 1e3c8f8 -- src/` touches exactly one production file:
`src/BoltSharp/Transport/BoltTcpConnection.cs` (+22/−7). The full method
(`RunMessageLoopAsync`) and the whole file were inspected:

- **Request decoding:** owned by the B3-B session entry point
  (`session.ReceiveBytes(message)`); the transport no longer pre-decodes. No second parser was
  created — the session holds its own `BoltMessageCodec` (B3-A), the same structural decoder the
  accepted session path always used.
- **Unsupported-command classification:** `BoltConnectionSession.ReceiveBytes` →
  `UnsupportedMessageFailure()` (session-owned).
- **Malformed-message classification:** `BoltConnectionSession.ReceiveBytes` →
  `ProtocolViolationFailure()` (session-owned).
- **Session state transitions:** `BoltConnectionSession` (`FailTerminal` → `Defunct`) — unchanged.
- **Connection-close decision:** the transport loop's `result.ShouldClose || session.IsDefunct`
  check — unchanged semantics; the transport writes the final responses (including the terminal
  FAILURE) before returning.
- **Protocol FAILURE generation:** `BoltConnectionSession` failure constructors — unchanged.
- **`MessageProcessed` signature:** now from `DescribePayloadSignature(message)` (diagnostic-only
  peek) instead of the decoded request — see §9.
- **Input-buffer ownership:** unchanged — the B2 reader returns an owned payload array;
  `ReceiveBytes` takes it as `ReadOnlyMemory<byte>` and retains nothing. The response encoding
  path (`WriteResponsesAsync` with the B3-A `EncodeResponse`) is untouched; the transport remains
  responsible for framing, network lifecycle and output delivery.

**Architectural requirement met:** the correction delegates message interpretation to the accepted
session-processing boundary without creating a second parser; no new ownership ambiguity was
found. The defensive `catch (BoltMessageException)` in `RunAsync` remains as a safety net with an
updated comment (a codec fault escaping the session boundary would still be a transport error —
correctly classified).

## 5. Direct B3-B refusal oracle (§7)

Using `BoltConnectionSession.ReceiveBytes` directly with reviewer-built payloads (independent
encoder), after HELLO+LOGON (READY), for BEGIN/COMMIT/ROLLBACK/ROUTE:

- Single terminal FAILURE, `state=Defunct`, `shouldClose=True`, provider `PrepareCount == 0`.
- Byte payload equals the reviewer-derived expected encoding
  `B17FA48A67716C5F7374617475738530384E30368A6E656F346A5F636F6465D01F4E656F2E436C69656E744572726F722E496E76616C69648B6465736372697074696F6ED0434578706C69636974207472616E73616374696F6E7320616E6420726F7574696E6720617265206E6F7420737570706F727465642062792074686973207365727665722ED011646961676E6F737469635F7265636F7264A0`
  — hand-verified against PackStream 1.0 semantics (B1 7F FAILURE structure, A4 four-entry map,
  `gql_status=08N06`, `neo4j_code=Neo.ClientError.Request.Invalid`, 67-byte description
  "Explicit transactions and routing are not supported by this server.", empty
  `diagnostic_record`).
- **State-dependence probes:** BEGIN in NEGOTIATION (before HELLO) is still refused by the
  unsupported path (terminal, no execution); BEGIN while an auto-commit result is ACTIVE
  (STREAMING) retires the owned operation exactly once (`DisposeAttempts == 1`) and becomes
  terminal — unsupported commands are never recoverable in any state.
- Malformed inputs (`B0 10` arity-0 RUN, unknown signature `B1 7B A0`) receive the terminal
  protocol-violation FAILURE with description "The message cannot be handled in the current
  connection state."
- After terminality, `ReceiveBytes` on the DEFUNCT session returns zero responses and
  `shouldClose=True`; no provider activity.

Evidence: `03-direct-session-oracle.txt`; probe tests
`DirectSessionOracleTests` (9 tests).

## 6. Real TCP refusal (§8) — BEGIN/ROUTE/COMMIT/ROLLBACK

Over the real `BoltTcpServer` (loopback, ephemeral port), after handshake+HELLO+LOGON, each
valid unsupported command sent in its own framed message:

| Command | Response | Byte identity | Close | Provider |
| --- | --- | --- | --- | --- |
| BEGIN (`B1 11 A0`) | single FAILURE, then EOF | == independent expected == direct-session oracle | observed | `PrepareCount == 0` |
| COMMIT (`B1 12 A0`) | single FAILURE, then EOF | == | observed | 0 |
| ROLLBACK (`B1 13 A0`) | single FAILURE, then EOF | == | observed | 0 |
| ROUTE (`B3 66 …`) | single FAILURE, then EOF | == | observed | 0 |

Mandatory assertions verified per command:

- The payload is structurally valid (correct arity/fields per Bolt 5.8).
- The message reaches the accepted session path (server event `MessageProcessed sig=0x11/0x12/0x13/0x66 … responses=1 state=Defunct`).
- The FAILURE is **transmitted**: the assertion reads an actual complete message from the socket
  (response-read, not an inference from server state) and compares bytes.
- Byte equality: actual TCP payload == reviewer-independent expected encoding == direct-session
  oracle (three-way agreement).
- Status/metadata decoded from raw bytes: `gql_status=08N06`,
  `neo4j_code=Neo.ClientError.Request.Invalid`, description as accepted, `diagnostic_record` present.
- No RECORD, no SUCCESS, no second response after the FAILURE (`HasAdditionalMessage == false`).
- No provider operation opens (`PrepareCount == 0`); reference graph unchanged (queried after refusals).
- Session terminal (`state=Defunct`), connection closes after the response, and the connection
  accounting is a completion, not a transport fault (`ConnectionsFailed == 0`).
- `MessagesProcessed == 3` (HELLO, LOGON, the refused command).

Evidence: `04-tcp-refusal.txt`; probe tests `TcpRefusalTests` (6 tests incl. the three-way byte
identity).

## 7. Official-driver BEGIN refusal (§9)

Unmodified `Neo4j.Driver` 6.4.0 against the real server through the reviewer-owned recording
proxy:

- Driver `BeginTransactionAsync` → `Neo4j.Driver.ProtocolException` (a `Neo4jException` subtype),
  `Code = Neo.ClientError.Request.Invalid`, `GqlStatus = 08N06`, `IsRetriable = false` — the
  protocol-level refusal, not an unexplained transport error.
- Wire (reviewer proxy, redacted LOGON): BEGIN actually sent as `B111A0`; exactly one FAILURE
  received on that connection; **no RUN** on that connection; no transaction began
  (`PrepareCount == 0`).
- The affected physical connection ended (server terminal close); recovery used a **new**
  physical connection (distinct proxy connection identity) where `RETURN 42 AS answer` returned
  42 — same-session recovery is not claimed and was not observed.
- Server counters: the BEGIN connection completed as a terminal outcome, not a fault.

**ROUTE (live driver):** the `neo4j://` scheme attempt was exercised through the documented
public configuration. The reviewer proxy observed that **ROUTE is actually emitted** by the
driver and the server answers with the accepted FAILURE; the driver cannot route (no routing
tables are implemented and none are fabricated) and never executes the query. Live-driver ROUTE
refusal coverage is therefore honestly observed (not merely inferred).

**COMMIT/ROLLBACK (live driver):** not reachable — BEGIN itself is refused first. No end-to-end
driver coverage is claimed for these; raw-protocol and direct-session paths cover their refusal
byte-identically (§6).

Evidence: `13-driver-begin.txt`, `16-driver-route.txt`; probe tests
`DriverInteropTests` (4 tests).

## 8. Malformed-message boundary (§10)

Battery of structurally malformed payloads (all correctly framed, sent after authentication):
wrong arity (`B0 10`), unknown signature (`B1 7B A0`), wrong field type (list as RUN query),
truncated structure (declared arity 2, one field), malformed nested value (list as RUN
parameters), invalid request metadata (HELLO carrying prohibited legacy `scheme`), invalid
marker (non-structure payload `0F`), correctly framed invalid protocol tag (`B1 0F A0`), and a
one-byte payload (`B1`). For every probe:

1. Classification: malformed (category C) — not unsupported, not framing.
2. The layer that decided: the accepted B3-B session (`ReceiveBytes` → `ProtocolViolationFailure`).
3. Response: terminal FAILURE, byte-identical to the independently derived protocol-violation
   encoding (`08N06` / `Neo.ClientError.Request.Invalid` / "The message cannot be handled in the
   current connection state."), **transmitted** before closure.
4. Terminal: the connection closes; a subsequent RUN on the same connection is never answered and
   never reaches the provider (`PrepareCount == 0`).
5. **Critical invariant holds: malformed input acquired no recoverable unsupported-command path.**
   Category B and C are distinguished only by description text; both are terminal.

Framing-level failures (kept separate, accepted B2 contract = close without response):
truncated chunk header, truncated chunk body (EOF mid-message), oversized message (against a
1024-byte configured bound), and premature EOF — each closed with no response and no execution.
A zero-length chunk (`00 00`) is a NOOP keep-alive per the accepted B2 contract (connection stays
open and remains usable); the codec-level empty-payload path remains terminal when a zero-length
payload reaches the session directly.

Evidence: `05-malformed-probes.txt`; probe tests `MalformedMessageTests` (12 tests).

## 9. Diagnostic signature-peek assessment (§13)

`DescribePayloadSignature(ReadOnlySpan<byte>)` (the only new transport code):

- Reads at most `payload[0]`/`payload[1]` — the structure marker low nibble check
  (`(payload[0] & 0xF0) == 0xB0`) and the tag byte. It is bounded by a length guard (`>= 2`) and
  cannot throw on short payloads (verified at runtime with a one-byte payload: `sig=unknown`
  reported, protocol outcome unchanged).
- It allocates only the diagnostic string; parses no nested values; mutates nothing; takes the
  payload as a read-only span; changes no ownership.
- **Non-authoritative by construction and by position:** it runs after `ReceiveBytes` and after
  the responses have been written, and its value is used only in the `MessageProcessed` detail
  string. It cannot influence execution, protocol responses, or connection closure. Response-
  equivalence control: refusal bytes observed on the wire equal the direct-session bytes (the
  diagnostic path ran in the transport case and not in the direct case).
- A malformed message yields `sig=unknown` (no reliable signature) or the first two bytes' tag —
  an accurately uncertain diagnostic, never a classification.
- **Event accounting** (`MessageProcessed`): exactly one event per processed message; HELLO
  (`sig=0x01`), LOGON (`sig=0x6A`), BEGIN (`sig=0x11`, `responses=1`, `state=Defunct`); no
  double-reporting; refused malformed payloads count exactly once (`MessagesProcessed == 3` for
  HELLO+LOGON+refused payload); no diagnostic exception escaped into network processing
  (`ConnectionsFailed == 0` on diagnostic-exercising refusals).

Evidence: `10-diagnostic-peek.txt`; probe tests `DiagnosticPeekTests` (3 tests).

## 10. Pipelining and terminality (§11)

One TCP write per scenario (fresh connection per attempt; see the OS note below):

- `BEGIN, PULL, RESET, RUN, PULL` → exactly one FAILURE (byte-identical to the accepted oracle),
  then close. The pipelined tail is never processed (`MessagesProcessed == 3` per connection:
  HELLO, LOGON, BEGIN; no `sig=0x0F`, no `sig=0x10` events); no provider activity; nothing after
  the terminal outcome executes.
- `ROUTE, RUN` → single FAILURE, close, RUN never processed.
- `malformed, RUN, PULL` → single terminal protocol-violation FAILURE, close, RUN never processed.
- RESET written to a connection closed after the terminal refusal: no revival, no response, no
  provider activity (same physical connection).
- GOODBYE closes without any fabricated response.

Already-framed-but-unprocessed pipelined messages are abandoned by the accepted terminal close;
the caller-ownership rules (the B2 reader's owned payload array, never retained) are unchanged.

**OS note (recorded as evidence, not a product defect):** when the accepted terminal close happens
with unread pipelined input in the socket buffer, Windows converts the close into an abortive RST
which can discard the in-flight response before a client reads it. The server-side diagnostics
confirm the FAILURE was written and the session became Defunct in every observed case; the
pipelined probes therefore retry the scenario on fresh connections, require byte-exactness of
every delivered message, require at least one delivery, and record every attempt. This matches
the producer's documented observation ("the close may be abortive (RST) when pipelined input
remains in the socket buffer — in both cases no further message is delivered").

Evidence: `06-pipelining-terminality.txt`; probe tests `PipeliningTests` (5 tests).

## 11. Recoverable FAILED/IGNORED/RESET intact (§12)

Raw protocol, reference backend:

- Unsupported query (`CREATE (n:Review)`) → FAILURE `gql_status=50N42`,
  `neo4j_code=Neo.ClientError.Request.Invalid` (F02 classification unchanged), recoverable.
- Pipelined PULL after that FAILURE → zero-field IGNORED, byte oracle **`B0 7E`** exact.
- RESET → SUCCESS; the SAME connection continues to serve supported queries afterwards.
- No desynchronization; the official driver still consumes the FAILURE/IGNORED sequence
  (producer suite `B5B8_02` green in the re-run §15).
- `Q-B5B-05` (same-connection RESET recovery after the generic 50N42 refusal when the driver
  destroys the connection) is untouched by F01: the F01 change does not close it.

Evidence: `11-ignored-f02.txt`; probe tests `IgnoreAndF02Tests` (2 tests).

## 12. Supported-query, handshake/framing and authentication regression (§14-§16)

Raw transport, reference backend (independent client and oracles):

- `RETURN 42 AS answer`: RUN → SUCCESS `B170A1866669656C64739186616E73776572`; PULL → RECORD
  **`B171912A`**; terminal SUCCESS **`B170A0`** (historical wire oracles exact). Connection reuse:
  a second query on the same connection succeeds. Exactly-once retirement observed.
- `RETURN $value AS answer` (typed integer parameter): RECORD `B1719107` — the typed value passes
  through unchanged (not stringified, not re-typed).
- `MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b`: one `(a, r, b)` record, cursor
  exhaustion observed, graph unchanged.
- Handshake: conventional negotiation ✓ (`00000805`), pipelined HELLO+LOGON in one write ✓,
  fragmented (split) handshake ✓, multiple requests in one write ✓, NOOP (`00 00`) versus
  terminator distinction ✓, unsupported-version proposal → `00000000` + closure ✓.
- Authentication: valid Basic ✓; RUN before LOGON → terminal protocol violation ✓; default
  (missing) verifier → fail-closed LOGON rejection ✓; LOGOFF in READY → SUCCESS and the
  connection can be re-authenticated ✓; no unauthorized provider operation opened in any
  negative case ✓; credential redaction verified (§16 below).

Official driver (`Neo4j.Driver` 6.4.0 through the reviewer proxy): scalar 42; typed parameter 7;
graph query exposing Alice `INode`, `KNOWS` `IRelationship` (direction: Alice→Bob via
element-ID endpoints), Bob `INode`; three sequential sessions served by a small number of pooled
physical connections (pooling intact, not fanned out per session).

Evidence: `07-supported-regression.txt`, `08-handshake-framing.txt`,
`09-authentication-regression.txt`, `14-driver-supported-regression.txt`; probe tests
`SupportedAndHandshakeTests` (4 tests).

## 13. Credential redaction (§16)

- The reviewer proxy redacts LOGON payloads at the decoded-message level AND the raw-chunk level
  (`REDACTED(LOGON)`); no credential value appears in any evidence file (scanned: no
  `reviewer-secret`, no LOGON hex, no `credentials` key material — see the commit-time scan).
- Synthetic loopback-only credentials are used; no real secrets anywhere.

## 14. Fail-closed resource ownership (§17)

Reviewer-owned fault-injecting provider through the supported public seam
(`BoltTcpServerOptions.OperationProviderFactory`):

- RUN → SUCCESS (one operation opened); PULL → record delivered, then retirement fails
  (injected `Dispose` throw): the completion is reported as FAILURE
  (`gql_status=50N00`, `Neo.DatabaseError.General.UnknownError`) — **never a successful summary**;
  the connection closes (DEFUNCT).
- Exactly **one** disposal attempt and exactly one recorded failure; **no retry** (the counter
  increments on every `Dispose` call, so a retry would be visible).
- No second RUN on the failed connection (write-after-close is never processed; no response);
  RESET cannot revive it; no second operation; no disposal retry.
- A new physical connection remains fully usable (`RETURN 42 AS answer` → `B171912A`): healthy
  connections unaffected; successful reconnection is not treated as resurrection of the failed
  session.

Evidence: `12-fail-closed-retirement.txt`; probe tests `FailClosedRetirementTests` (2 tests).

## 15. F02 non-regression (§18)

- Raw: unsupported query → generic `50N42` FAILURE; pipelined PULL → `B0 7E`; recovery per the
  accepted rules; reference graph unchanged after refusals.
- Official driver: unsupported query → `ProtocolException` (the recorded driver classification
  for 50N42 refusals, matching committed scenario `B5B8-01`), `Code=Neo.ClientError.Request.Invalid`,
  `GqlStatus=50N42`, `IsRetriable=false`; no execution (`SuccessCount == 0`); recovery via a fresh
  connection returns 42; no duplicate execution of the refused query.
- Error codes, GQL classification, retryability, driver reuse behavior and provider refusal logic
  are unchanged. F01 and F02 do not interact (message-signature-level terminal refusal vs
  query-level recoverable refusal). The two remediations are not combined.

Evidence: `11-ignored-f02.txt`, `15-driver-f02.txt`.

## 16. Producer test-diff audit (§19)

`git show 1e3c8f8` touches exactly three files: the production file and two test files.

- `tests/BoltSharp.Tests/Transport/UnsupportedCommandRefusalTests.cs` (new): 6 facts + 1 theory
  with 4 cases = **10 new core transport tests** (claim verified). Assertions include two locked
  hex constants (the accepted FAILURE encodings), provider counters, `MessagesProcessed`
  accounting, `ConnectionsFailed == 0`, and pipelined-tail non-execution.
- `tests/BoltSharp.DriverInteropTests/Scenarios/CapabilityBoundaryTests.cs`: three existing tests
  updated + one new (`B5B10_05`) = **41/41 driver suite** (claim verified).

For each updated test: old assertion → new assertion:

1. `B5B10-01`: `Assert.Equal(0, transcript.CountServer("FAILURE"))` (asserting the buggy
   closure-without-response) → `Assert.Single(transcript.ServerMessages, m => m.Name == "FAILURE")`
   plus `gql_status=08N06`, `Neo.ClientError.Request.Invalid` and the accepted description
   substring, and a server event with `sig=0x11` and `state=Defunct`. The new assertion is
   strictly stronger about the protocol outcome and **distinguishes protocol-level FAILURE from
   unexplained closure** (the required property).
2. `B5B10-02`: additive `Assert.All(... FAILURE with gql_status=08N06 ...)` over every BEGIN
   attempt — additive strengthening; the RUN-count zero assertions remain.
3. `B5B10-03`: same pattern as (1) for ROUTE — strictly stronger, protocol-level.
4. New `B5B10_05`: driver-visible `Neo4jException` code/gql/retriability, wire `B111A0`, single
   FAILURE with the accepted description, no RUN on the BEGIN connection, recovery 42 on a new
   connection, `counters.Failed == 0`.

No test was deleted; no assertion was weakened; negative controls remain meaningful (the old
negative controls asserted the defective behavior and were replaced by accepted-contract
assertions, which is what the accepted contract requires). The changes were not modified during
Q1; the reviewer added its own independent tests instead.

## 17. Regression execution (§21) — fresh, reviewer-run

| Suite | Expected | Reviewer result |
| --- | ---: | ---: |
| Core (BoltSharp.Tests, full) | 1,227/1,227 | **1,227/1,227** (run 1, TRX `q1-core-full.trx`) |
| Core re-run (fresh processes) | stable | **1,227/1,227** × 2 (runs 2-3) |
| Transport filter | 90/90 | **90/90** |
| B3-C backend filter | 94/94 | **94/94** |
| B3-B sessions filter | 119/119 | **119/119** |
| B3-A messages filter | 98/98 | **98/98** |
| B5-B official driver suite | 41/41 | **41/41** (TRX `q1-driver-suite.trx`) |
| B0-B historical integration (`BoltSharp.IntegrationTests`) | 19/19 | **19/19** |
| (additional, unchanged project) `BoltSharp.DriverIntegrationTests` | — | 75 passed / 10 skipped / 0 failed |

Reviewer probes: **47/47 passing**, stable across **14+ fresh runs** (6 stability runs of the
non-driver set at 43/43 and 6 full-suite runs at 47/47, plus the driver-focused runs). Reviewer
results are NOT merged into the producer counts.

Build expectations:

- Production: **0 warnings, 0 errors**.
- Solution: **0 errors, exactly the two pre-existing `xUnit2012` advisories**
  (`tests/BoltSharp.Tests/Messages/BoltRequestDecodeTests.cs:71-72`).
- Scoped formatting (`dotnet format --verify-no-changes`, project+include form): production
  `Transport/BoltTcpConnection.cs` exit 0; tests `Transport/UnsupportedCommandRefusalTests.cs`
  exit 0; driver interop project exit 0 — **no new violations**.

Evidence: `18-build-production.txt`, `19-build-solution.txt`, `20-core-full-regression-run1.txt`,
`21-core-regression-runs2-3.txt`, `22-transport-filter.txt`, `23-b3c-backend.txt`,
`24-b3b-sessions.txt`, `25-b3a-messages.txt`, `26-driver-suite.txt`, `27-b0b-integration.txt`,
`28-format-checks.txt`, TRX files.

## 18. Historical baseline and integrity (§20)

Reviewer-owned read-only verification (`Verify-HistoricalManifests.ps1` in the probe directory;
output `29-historical-manifest-verification.txt`):

- **All sixteen committed manifests verified by raw-byte SHA-256: 588/588 entries matched,
  0 mismatches.** Inherited: eleven historical packages 352/352, B5-A 18/18 + 10/10, B5-B producer
  62/62, B5-B-Q1 76/76 (= 518/518 inherited); producer's new B5-B-F01-R1 evidence 70/70.
- All five accepted owner decisions are byte-identical to their committed blobs (working tree
  clean for `docs/decisions`; `git diff HEAD -- docs/decisions` empty).
- **Source-evolution statement independently confirmed: no historical manifest references
  `src/BoltSharp/Transport/BoltTcpConnection.cs`** (0 of 16 manifests).
- No historical evidence was rewritten by the reviewer; nothing was reset, rebased or checked out
  in the canonical tree (the historical revision was inspected via `git archive` into a temp
  directory).

## 19. Negative controls (§22)

| Invalid behavior | Control | Result |
| --- | --- | --- |
| Unsupported BEGIN silently closes | exact FAILURE oracle (socket read + byte compare) | would fail if no FAILURE were delivered — PASS |
| Unsupported ROUTE silently closes | exact FAILURE oracle | PASS |
| Wrong `08N06` status | decoded metadata assertion from raw bytes | PASS |
| FAILURE constructed but not transmitted | actual socket read of the complete message | PASS |
| BEGIN accidentally executes | provider `PrepareCount == 0` | PASS |
| ROUTE fabricates routing data | response + provider oracle (no RECORD/SUCCESS, no operation) | PASS |
| Malformed request treated as recoverable | terminal-state probe (close + no further execution) | PASS |
| Unknown signature accepted | unknown-signature probe (terminal violation) | PASS |
| Pipelined RUN executes after DEFUNCT | per-connection provider counter + `MessagesProcessed` | PASS |
| RESET revives terminal session | same-connection probe (no response, no activity) | PASS |
| Diagnostic peek throws | one-byte payload probe (`sig=unknown`, protocol unchanged) | PASS |
| Diagnostic peek changes execution | response equivalence vs direct session | PASS |
| Driver sees transport error instead of FAILURE | official-driver exception type/code/gql check | PASS |
| Scalar result changes | historical wire oracle `B171912A` | PASS |
| Graph direction changes | typed endpoint oracle (element-ID start/end) | PASS |
| IGNORED arity changes | `B0 7E` byte oracle | PASS |
| F02 status changes | `50N42` driver probe (code/gql/retriability) | PASS |
| Disposal retried | every-call attempt counter (`DisposeAttempts == 1`) | PASS |
| Historical tests weakened | Git test-diff audit (§16) | PASS |
| Credential leakage | sanitized evidence scan (§13) | PASS |

Each control was actually exercised and distinguishes the intended incorrect behavior; no
unexercised control is marked PASS.

## 20. Findings

**No BLOCKING findings.**

- **B5-B-F01-R1-Q1-F01 — QUALIFICATION (OS-level, non-product).** On Windows, the accepted
  terminal close with unread pipelined input surfaces as an abortive RST which can discard the
  in-flight response before a client reads it (observed on loopback; reproduced by the reviewer's
  probes; consistent with the producer's documented observation). This is an OS close artifact of
  the accepted terminal behavior, not a protocol defect: server-side diagnostics confirm the
  FAILURE is written and the session becomes Defunct in every case, and delivery is reliably
  observable whenever the client read is pending. No product change is proposed; recording this
  behavior as a documented limitation is recommended for release-grade hardening (which is
  already an inherited obligation, `Q-B5B-09`). Reproduction: `PipeliningTests` retry evidence,
  `06-pipelining-terminality.txt`.
- **B5-B-F01-R1-Q1-F02 — DOCUMENTATION.** The producer's implementation report (§13) states the
  pipelined malformed+RUN+PULL case answers "only the FAILURE" and asserts
  `MessagesProcessed == 3` — accurate. However, the same report's §15 wording ("tolerate the
  platform's abortive (RST) close when pipelined input remains in the socket buffer") could be
  read as covering only the post-FAILURE read; in fact the abortive close can also suppress
  client-side delivery of the FAILURE itself under specific timing. Suggested (non-blocking)
  documentation clarification for the next producer/owner document touch. No runtime impact.
- **B5-B-F01-R1-Q1-F03 — DOCUMENTATION.** The malformed-input behavior change (previously a
  silent close; now the B3-B terminal protocol-violation FAILURE before closure) is verified to
  match the accepted B3-B `ReceiveBytes` contract (the session contract predates the fix and is
  byte-verified). This is the change the producer's §22 flagged for reviewer confirmation: **the
  reviewer confirms it matches the accepted contract; no accepted contract requires a different
  disposition.** The change is terminal in both cases and introduces no recoverable path.

## 21. Remaining qualifications (all retained, none closed by Q1)

- `Q-B5B-03` — **proposed for owner closure as "F01 boundary remediation independently
  verified"** (only the owner may formally close it).
- F02 generic `50N42` classification (`Q-B5B-04`) — unchanged, retained.
- `Q-B5B-05` same-connection RESET recovery boundary — untouched, retained.
- `Q-B5B-06` TLS trust-arrangement limitations; `Q-B5B-07` no general
  transactions/routing/multi-database (BEGIN/COMMIT/ROLLBACK/ROUTE are refused, not implemented —
  correct refusal is not feature support); `Q-B5B-08` no cross-driver claim; `Q-B5B-09`
  release-grade hardening (now also carrying the OS-level abortive-close observation above);
  `Q-B5B-01`/`Q-B5B-02` driver version and bounded query surface.
- B5-A blocked-write backpressure (`Q-B5A-06`), TLS policy limitations, session-options
  passthrough (`Q-B5A-07`), provider cancellation limitations.
- B3-B single-owner session contract, failed-retirement DEFUNCT behavior, bounded incremental
  PULL; B3-A exact-form contracts; B3-C exact-form query allowlist (`Q-B3C-01`).

## 22. Scope and artifact integrity

- Reviewer-created paths (all committed in this task only):
  `docs/reviews/B5-B-F01-R1-Q1-independent-qualification.md`,
  `docs/reviews/probes/B5-B-F01-R1-Q1/` (probe project + verification script),
  `docs/reviews/evidence/B5-B-F01-R1-Q1/` (evidence + `.gitattributes` + `manifest.json`).
- No changes to `src/`, `tests/`, producer reports/evidence, owner decisions, `BoltSharp.slnx`,
  dependencies, or any historical file. No push, no branch creation, no worktree creation, no
  reset/rebase/history rewrite.
- Evidence manifest: `docs/reviews/evidence/B5-B-F01-R1-Q1/manifest.json` (SHA-256 of every
  evidence file's raw bytes; `.gitattributes` with `* -text` per repository convention).
- Credential scan: no passwords, private keys, or unredacted authentication frames in any
  committed artifact (LOGON redacted at decoded-message AND raw-chunk level in the proxy).

## 23. Formal reviewer disposition

**B5-B-F01-R1-Q1 — SUPPORTED WITH QUALIFICATIONS.**

The original F01 cause is independently confirmed by fresh execution at the pre-remediation
revision; valid unsupported commands now produce the accepted B3-B refusal over actual TCP with
byte-verified, transmitted FAILURE responses before closure; the official driver receives the
expected BEGIN refusal; malformed requests remain terminal with the accepted classification; the
diagnostic signature inspection is non-authoritative and safe; no unsupported operation executes;
no terminal session is revived; supported scalar/graph/parameter paths, live-driver IGNORED, F02
and fail-closed retirement are unchanged; producer test changes are justified and verified
against Git; regressions pass at the expected counts; historical evidence remains intact; and no
blocking finding remains. Independence is established at the session/model level with the
operator-designation limitation recorded (§1).

This is independent reviewer qualification, not formal owner closure.
