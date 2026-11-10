# B5-B-F01-R1 — Restore Unsupported-Command Refusal Across the Transport/Session Boundary (Implementation Report)

- **Task:** `B5-B-F01-R1`
- **Role:** Bounded remediation implementation agent (producer)
- **Repository:** `D:\projects\bolt-sharp`, canonical `main`, single worktree, never pushed
- **Starting HEAD:** `3953bdd9962a8e4dc4c43c0bb3996b5fed76fb08`
- **Starting tree:** `f3fd25af19261f754d7d601b5c214f315c74af7d`
- **Protocol target:** Bolt 5.8 / PackStream 1.0 (unchanged)
- **Target finding:** `B5-B-IMPL-F01` / `B5-B-Q1-F01`
- **Evidence:** `docs/reviews/evidence/B5-B-F01-R1/`
- **Disposition:** **B5-B-F01-R1 — IMPLEMENTED, READY FOR INDEPENDENT QUALIFICATION**

> **Reviewer/executor independence note (binding for the F01-R1 qualification):** this
> implementation and its producer verification were executed by the same executor session/model
> (DeepSeek V4.1 Flash, provider nvidia, OpenCode) that authored this report. Independent
> qualification must use a genuinely separate executor session/model; a model switch inside one
> session is not independent execution. No behavior was self-qualified here.

---

## 1. Preflight verification

Performed before any change (recorded in `logs/01-preflight.txt`):

1. Branch `main` — confirmed.
2. HEAD exactly `3953bdd9962a8e4dc4c43c0bb3996b5fed76fb08` — confirmed.
3. Tree exactly `f3fd25af19261f754d7d601b5c214f315c74af7d` — confirmed.
4. Exactly one canonical worktree (`D:/projects/bolt-sharp`) — confirmed.
5. Working tree and index clean — confirmed before any change.
6. No stash requiring reconciliation — confirmed.
7. No concurrent writer — confirmed.
8. No prior F01-R1 implementation — confirmed (no report, evidence, or test artifacts existed).
9. All five owner decisions present (B3-A, B3-B, B3-C, B5-A, B5-B) — confirmed.
10. B5-B owner acceptance (`6b2a0e36654cd209bfd8a88a39bd9d58f5097aa6`) is an ancestor of HEAD —
    confirmed; the F01-R1 parent revision `3953bdd` is itself the B5-B owner decision chain tip.
11. No push — 61 commits ahead of `origin/main`; nothing pushed.

## 2. Accepted F01 contract (reconstructed from the accepted sources)

From the B5-B owner decision (§18/§26), the reviewer qualification (`B5-B-Q1-F01`), and the
accepted B3-B implementation:

| Category | Layer that recognizes | Validation owner | Refusal decision | Expected response | Expected state | Transport disposition |
| --- | --- | --- | --- | --- | --- | --- |
| A. Supported messages (HELLO, LOGON, LOGOFF, TELEMETRY, RUN, PULL, DISCARD, RESET, GOODBYE) | B3-A codec + B3-B session | B3-A structure/schema; B3-B state | B3-B state machine | unchanged | unchanged | stays open unless terminal |
| B. Structurally valid but unsupported (BEGIN, COMMIT, ROLLBACK, ROUTE) | B3-A codec recognizes the signature as `UnsupportedMessage` | B3-A signature recognition | **B3-B** (`ReceiveBytes` → `UnsupportedMessageFailure`) | terminal **FAILURE** `08N06` / `Neo.ClientError.Request.Invalid` / "Explicit transactions and routing are not supported by this server." | `Defunct` | writes the FAILURE, then closes |
| C. Structurally malformed (bad marker, wrong arity/type, truncated, unknown signature, prohibited/trailing fields) | B3-A codec category | B3-A | **B3-B** (`ReceiveBytes` → `ProtocolViolationFailure`) | terminal **FAILURE** `08N06` / `Request.Invalid` / "The message cannot be handled in the current connection state." | `Defunct` | writes the FAILURE, then closes |
| D. Valid message in an invalid session state (RUN before LOGON, PULL without an operation, messages while FAILED) | B3-B session | B3-B state machine | B3-B | unchanged (protocol violation terminal; FAILED → IGNORED for pipelined work) | unchanged | unchanged |

The accepted B3-B entry point `BoltConnectionSession.ReceiveBytes` is the single authoritative
owner for categories B and C; the defect (F01) was that the transport bypassed it by pre-decoding
with the B3-A codec, so category-B rejections escaped as transport-level closures and category-C
rejections were closed silently instead of receiving the session's terminal classification.

## 3. Existing transport/session call graph (before the fix)

```text
BoltTcpConnection.RunMessageLoopAsync
    -> reader.ReadMessageAsync()                    (B2 framing, unchanged)
    -> codec.DecodeRequest(message)                 (B3-A decode — THROWS for category B/C)
    -> session.Receive(request)                     (B3-B — never reached for B/C)
    -> WriteResponsesAsync(codec.EncodeResponse)    (B3-A encode)
    -> diagnostics "sig=0x.. responses=.. state=.."
```

The `catch (BoltMessageException ex)` in `RunAsync` classified the escaped decode fault as a
transport failure (`outcome = codec:UnsupportedMessage` / `codec:...`) and closed the connection
without writing any response. `BoltConnectionSession.ReceiveBytes` — which maps
`UnsupportedMessage` to the documented terminal FAILURE and every other decode rejection to the
terminal protocol-violation FAILURE — was unreachable through the production adapter.

## 4. Original F01 reproduction (before the change)

Captured with the reproduction probe at the unremediated revision
(`repro-pre-remediation.txt`; committed probe source `f01-repro-probe-source.cs`):

- **[A] Official driver 6.4.0** `BeginTransactionAsync` against the real `BoltTcpServer`:
  `BEGIN` (`B1 11 A0`) reaches the wire; the server sends **no response** and closes; the driver
  observes `Neo4j.Driver.ServiceUnavailableException` (no Neo4j code, `IsRetriable=true`); server
  counters `completed=0, failed=1, messages=2` (HELLO/LOGON only).
- **[B] Raw Bolt client**: BEGIN, COMMIT, ROLLBACK and ROUTE each receive
  `<none: closed-by-server>` — no FAILURE, connection closed, `failed=1`.
- **[C] Direct accepted B3-B** `BoltConnectionSession.ReceiveBytes` with the same payloads:
  each returns the terminal `FAILURE{gql_status=08N06, neo4j_code=Neo.ClientError.Request.Invalid,
  description="Explicit transactions and routing are not supported by this server."}`,
  `state=Defunct`, `shouldClose=True`, `prepareCount=0` (no operation opened); malformed input
  (RUN with zero fields) and an unknown signature return the terminal protocol-violation FAILURE.
- **[D] Supported scalar control**: RUN → `SUCCESS{fields=[answer]}`; PULL → RECORD `B171912A`;
  terminal `SUCCESS{}`; `completed=1, failed=0`.

The comparison demonstrates the boundary inconsistency directly: the accepted session path already
implements the required refusal; the transport never consulted it for categories B and C.

## 5. Root-cause analysis

1. **Where unsupported messages are rejected:** in `BoltMessageCodec.DecodeRequest`, which throws
   `BoltMessageException(BoltMessageErrorCategory.UnsupportedMessage)` for the BEGIN/COMMIT/
   ROLLBACK/ROUTE tags before any session involvement.
2. **Rejection before B3-B access:** yes — the transport decodes first, so the session never sees
   the payload for those signatures.
3. **Does `ReceiveBytes` already handle the case:** yes, exactly: `UnsupportedMessage` →
   `FailTerminal(UnsupportedMessageFailure())`; other decode rejections →
   `FailTerminal(ProtocolViolationFailure())`.
4. **Same structural decoder:** yes in both paths — B3-A `BoltMessageCodec` (the session holds
   its own instance; no second parser exists or was added).
5. **Error metadata generated by B3-B:** yes — `UnsupportedMessageFailure` /
   `ProtocolViolationFailure` are the accepted session-generated FAILURE shapes.
6. **Transport mistaking an unsupported command for a malformed message:** yes — both were
   classified as a generic transport decode fault and closed silently.
7. **Does a routing change preserve the session lifecycle:** yes — verified by the full
   regression (categories A and D unchanged; terminal closure semantics preserved).
8. **Does the change alter supported messages:** no — `ReceiveBytes` decodes valid payloads and
   calls the same `Receive(request)` path the transport used before.
9. **Request bytes caller-owned after framing:** yes — the B2 reader returns an owned payload
   array; `ReceiveBytes` takes it as `ReadOnlyMemory<byte>` and never retains it.
10. **Response ordering and terminal closure:** unchanged — responses are encoded and written in
    order, and `ShouldClose`/`IsDefunct` terminate the loop exactly as before.

## 6. Smallest implemented correction

`src/BoltSharp/Transport/BoltTcpConnection.cs` (`RunMessageLoopAsync`): the pre-decoding step was
replaced by the accepted single entry point:

```csharp
// before
var request = codec.DecodeRequest(message);
var result = session.Receive(request);

// after
var result = session.ReceiveBytes(message);
```

plus a diagnostic-only signature peek (`DescribePayloadSignature`: structure marker `0xB0|arity`
then the tag byte) so the transport `MessageProcessed` event keeps its
`sig=0x.. responses=.. state=..` detail without duplicating any decoding or classification. The
defensive `catch (BoltMessageException)` classification in `RunAsync` remains as a safety net with
an updated comment. Comments/docs on the connection class were updated to describe the session's
payload-processing entry point. No other production file was changed.

## 7. Changed production paths

```text
src/BoltSharp/Transport/BoltTcpConnection.cs                          (modified)
tests/BoltSharp.Tests/Transport/UnsupportedCommandRefusalTests.cs     (new, 10 tests)
tests/BoltSharp.DriverInteropTests/Scenarios/CapabilityBoundaryTests.cs (updated 3 + 1 new test)
```

No B3-A schema, B3-B state machine, B3-C backend, B5-A listener/TLS, solution or dependency file
was modified.

## 8. Corrected behavior (after the change)

`repro-post-remediation.txt`:

- **[A]** driver BEGIN: server writes the accepted FAILURE frame
  (`00A5B17FA48A…A00000`), then EOF; driver observes **`Neo4j.Driver.ProtocolException`,
  `code=Neo.ClientError.Request.Invalid`, `gql=08N06`, `IsRetriable=false`**; counters
  `completed=1, failed=0, messages=3` (the BEGIN is now a processed request, not a transport
  fault).
- **[B]** raw BEGIN/COMMIT/ROLLBACK/ROUTE: each receives the byte-exact accepted FAILURE
  (`B17FA48A67716C5F7374617475738530384E30368A6E656F346A5F636F6465D01F4E656F2E436C69656E744572726F722E526571756573742E496E76616C69648B6465736372697074696F6ED0434578706C69636974207472616E73616374696F6E7320616E6420726F7574696E6720617265206E6F7420737570706F727465642062792074686973207365727665722ED011646961676E6F737469635F7265636F7264A0`)
  and then the connection closes; `completed=1, failed=0`.
- **[C]** direct B3-B: unchanged (the transport now matches it byte-exactly).
- **[D]** supported scalar control: unchanged.

## 9. Supported-message regression

All accepted behavior is preserved (verified by the full suites — §17):

- **Handshake:** conventional Bolt 5.8, Manifest v1, pipelined HELLO, version selection and
  refusal — the B5-A transport handshake tests and B5-B driver scenarios are unchanged and green.
- **Authentication:** HELLO/LOGON, invalid credentials, missing verifier, throwing verifier,
  LOGOFF semantics — unchanged (session-level, same `Receive` path).
- **Queries:** `RETURN 42 AS answer`, the reference graph query with typed `INode`/`IRelationship`
  materialization, and `RETURN $value AS answer` parameters — unchanged; driver suite 41/41 with
  byte-exact goldens still asserted (`B310D013…A0A0`, `B13FA1816EC903E8`, `B171912A`, `B170A0`,
  graph golden RECORD).
- **Cursor retirement and connection reuse:** unchanged; the instrumented pooling scenario
  (three sessions, one physical connection) remains green.

## 10. BEGIN refusal results

- Raw transport (core test `Begin_AfterCompletedQuery_ReceivesAcceptedTerminalFailure_…` and the
  four-command theory): valid BEGIN after a completed supported query receives the accepted
  terminal FAILURE, the connection closes, the provider is never consulted for BEGIN
  (`PrepareCount` unchanged), and the server records a processed `sig=0x11 … state=Defunct`
  event with `ConnectionsFailed == 0`.
- Official driver (new `B5B10_05`): the driver observes `Neo4jException` with
  `Code=Neo.ClientError.Request.Invalid`, `GqlStatus=08N06`, `IsRetriable=false`; wire shows
  `BEGIN` (`B111A0`) then the accepted FAILURE; no RUN on that connection; victims: none — the
  reference graph is untouched and a subsequent auto-commit query returns 42.
- Byte contract: the transport's FAILURE payload for BEGIN equals the direct B3-B accepted
  encoding exactly (asserted as a locked hex constant in the new core test).

## 11. ROUTE refusal results

- Raw transport (theory case): ROUTE (`B1 66 A1 …"address"… 90 A0`) receives the same accepted
  terminal FAILURE and close; no operation opens.
- Official driver (`B5B10_03`, `neo4j://`): the routing attempt's ROUTE now receives the
  protocol-level FAILURE; the driver still cannot route (no routing tables are implemented and
  none are fabricated), observes an error, and never executes the query
  (`DoesNotContain sig=0x10`). The corrected scenario document records the refusal.

## 12. COMMIT/ROLLBACK coverage

Raw Bolt probes in the READY state receive the identical accepted terminal FAILURE and close
(theory cases COMMIT/ROLLBACK). **Reachability limit recorded:** the official driver never
reached COMMIT or ROLLBACK because BEGIN itself is refused first; live-driver coverage of
COMMIT/ROLLBACK is therefore **not claimed** — only the raw-protocol and direct-session paths
cover them, and both are byte-identical.

## 13. Structural-validation controls

The correction must not convert malformed input into a recoverable unsupported-command path. New
tests and evidence confirm:

- Wrong structure arity (`B0 10`, RUN with zero fields): terminal protocol-violation FAILURE
  (byte-locked), connection closed, no provider execution, **no RESET recovery possible**.
- Unknown signature (`B1 7B A0`): same terminal protocol-violation classification.
- Pipelined malformed + RUN + PULL in one write: only the FAILURE is answered; the RUN/PULL are
  never processed (`MessagesProcessed == 3`), provider untouched.
- Existing framing-level controls are unchanged and green: truncated header/payload, oversize,
  EOF mid-request, invalid framing garbage (transport suite 90/90).

Only the accepted B3-B classification distinguishes category B (drop-in `08N06` unsupported
FAILURE) from category C (terminal protocol-violation FAILURE, also `08N06` but with the
protocol-violation description); both are terminal, neither is recoverable.

## 14. Pipelining and FAILED/IGNORED/RESET regression

- Supported pipelining (`HELLO+LOGON+RUN+PULL` in one or more writes) remains ordered and green.
- Unsupported pipelining (`BEGIN, PULL, RESET, RUN, PULL` in one write): BEGIN is answered with
  the terminal FAILURE, then the connection closes; nothing after the terminal outcome executes
  (verified by provider counters and processed-message counts). No IGNORED is fabricated for the
  tail because the terminal outcome closes the connection (the accepted B3-B contract for
  unsupported/malformed payloads is terminal, not recoverable).
- The recoverable FAILED/IGNORED/RESET path (valid RUN refused by the backend, e.g. the
  unsupported-query and unknown-parameter cases) is unchanged: FAILURE → pipelined IGNORED
  (`B0 7E`) → RESET recovery (covered by existing tests and the driver suite's
  `B5B8_02` live-driver IGNORED scenario, still green).
- `GOODBYE` + following RUN: unchanged (connection closes; no execution).
- Terminal resource failure (`RUN, PULL → disposal failure, RESET, RUN`): unchanged — the
  fail-closed retirement tests remain green through the corrected path.

## 15. Terminal connection behavior

`ShouldClose`/`IsDefunct` handling is unchanged: the transport writes the final responses
(including the terminal FAILURE) and then returns, closing the connection. The new tests assert
the close via `WaitForClose` and tolerate the platform's abortive (RST) close when pipelined
input remains in the socket buffer — in both cases **no further message is delivered** and no
response is fabricated.

## 16. Fail-closed resource ownership

The binding B3-B contract is preserved (its own suite is green; no session code changed). The
existing mandatory fault-injection control (`TransportFailureTests` disposal-failure sequence:
one operation, one disposal attempt, FAILURE never SUCCESS, DEFUNCT, no RESET recovery, no second
RUN, no retry) passes unchanged through the corrected transport. The new routing cannot revive a
DEFUNCT session: every subsequent payload is refused by the session's defunct check before any
provider activity. The official-driver fail-closed control from `B5-B-Q1` (`q1k01`–`q1k04`) is
unaffected because no provider/lifecycle code changed.

## 17. Official-driver observations

- **Suite:** 41/41 passing (was 40/40): three capability tests updated to the corrected
  expectations, one focused BEGIN-refusal test added (`B5B10_05`); no test was deleted or
  weakened, and every new/updated assertion is strictly stronger than the previous
  old-behavior assertion.
- **Driver-visible refusal classification (corrected BEGIN):**
  `Neo4j.Driver.ProtocolException` (a `Neo4jException` subtype), `Code=Neo.ClientError.Request.Invalid`,
  `GqlStatus=08N06`, `IsRetriable=false`, connection completed cleanly (`failed=0`) and destroyed
  by the driver after the terminal FAILURE; recovery occurs on a fresh connection.
- **Managed transaction callback:** each BEGIN attempt now receives the accepted FAILURE; the
  callback never reaches RUN; the 1s retry bound still terminates the attempt loop.
- **Live-driver IGNORED:** unchanged and still green (`B5B8_02`): RUN → FAILURE (50N42), pipelined
  PULL → zero-field `B0 7E`, no desynchronization.
- **Connection pooling:** unchanged (three sessions, one physical connection — instrumented).

## 18. F02 non-regression

Explicitly unchanged: the reference backend still refuses unsupported queries with the generic
`50N42`/`Neo.ClientError.Request.Invalid` FAILURE; the driver still classifies it as non-retriable
and discards the affected connection; no retry semantics, status mapping, or reuse behavior was
touched. `B5B8_01`/`B5B8_03`/`B5B10_04` remain green. F01 and F02 do not interact: F01 concerns
message-signature-level refusal (terminal), F02 concerns query-level refusal (recoverable).

## 19. Full test results

Fresh executions (logs and TRX in the evidence directory):

| Suite | Baseline | Result |
| --- | ---: | ---: |
| Core test project (total) | 1,217 | **1,227 / 1,227** (+10 new F01-R1 tests) |
| B5-A transport filter | 80 | **90 / 90** |
| B3-C backend filter | 94 | **94 / 94** |
| B3-B sessions filter | 119 | **119 / 119** |
| B3-A messages filter | 98 | **98 / 98** |
| B5-B official-driver suite (separate project) | 40 | **41 / 41** (+1 new test, 3 updated) |
| B0-B historical driver integration (unchanged project) | 19 | **19 / 19** |

New F01-R1 tests added to the core project: 6 facts + 1 theory with 4 cases = 10 tests
(`UnsupportedCommandRefusalTests`).

## 20. Build and format results

- Production assembly: **0 warnings / 0 errors**.
- Solution: **0 errors; exactly the two pre-existing `xUnit2012` advisories**
  (`tests/BoltSharp.Tests/Messages/BoltRequestDecodeTests.cs:71-72`).
- Driver test project: 0 warnings / 0 errors.
- Scoped `dotnet format --verify-no-changes`: `src/BoltSharp/Transport` exit 0;
  `tests/BoltSharp.Tests/Transport` exit 0; driver project exit 0.
- Pre-existing observation (unchanged): the full `tests/BoltSharp.Tests` project format check
  reports WHITESPACE findings in `tests/BoltSharp.Tests/Messages/BoltMessageFixtures.cs`, the
  B3-A-era file recorded as a pre-existing observation in the B3-A owner acceptance §6.4 and not
  modified by this remediation.

## 21. Historical manifest verification

`Verify-HistoricalManifests.ps1` (evidence `logs/16-historical-manifests.txt`,
`historical-integrity.json`): **518/518** entries matched across all 15 manifests
(B5-B-Q1 76, B5-B producer 62, B5-A-Q1 10, B5-A producer 18, eleven historical packages 352);
all five accepted owner decisions are byte-identical to their committed blobs.

Source-evolution note: the authorized F01-R1 change supersedes
`src/BoltSharp/Transport/BoltTcpConnection.cs`. **No historical manifest hashes that file**
(verified: only `src/BoltSharp/Messages/BoltMessageCodec.cs`/`BoltRequests.cs` from B3-A-R1 and
the B3-B `Sessions/` files are historically hashed, and none of them changed), so the 518/518
verification is unaffected and no historical evidence was rewritten. The B3-A-R1 source hashes
(2 files) remain matching; the B3-B implementation-source manifests describe their original
revisions as already documented and are outside the verified aggregate by design.

## 22. Remaining qualifications

Carried forward from the B5-B owner decision unless explicitly noted: all B3-A/B3-B/B3-C/B5-A/B5-B
qualifications remain binding. F01-R1 resolves **only** the F01 cross-component boundary
(`Q-B5B-03`): unsupported commands now receive the accepted B3-B terminal refusal through the real
transport. F01-R1 does **not**:

- implement transactions, routing or any new capability (BEGIN/COMMIT/ROLLBACK/ROUTE remain
  unsupported and are refused, not executed);
- close or refine F02 (`Q-B5B-04` stays as accepted);
- establish same-connection RESET recovery after a FAILURE/IGNORED sequence (`Q-B5B-05` stays
  open);
- change TLS (`Q-B5B-06`), session-options passthrough (`Q-B5A-07`), blocked-write backpressure
  (`Q-B5A-06`), or any other inherited limitation.

New observation for the reviewer: for category C (malformed) inputs the transport now delivers
the B3-B terminal protocol-violation FAILURE before closing (previously a silent close). This is
the accepted B3-B `ReceiveBytes` contract and is terminal in both cases; no recoverable path was
introduced. The reviewer should confirm this matches the accepted contract and note it if the
owner prefers a different malformed-input wire behavior (which would be a separate decision).

## 23. Reviewer handoff

**To the independent F01-R1 qualifier (separate executor session/model required):**

1. Start from `docs/reviews/evidence/B5-B-F01-R1/README.md`; reproduce with `Run-F01R1.ps1`.
2. Verify the original F01 mismatch against the pre-remediation revision
   (`3953bdd`, this commit's parent) — the committed `repro-pre-remediation.txt` plus the
   committed probe source allow a fresh reproduction; do not trust the producer's capture alone.
3. Verify the corrected behavior byte-exactly: BEGIN/COMMIT/ROLLBACK/ROUTE FAILURE payload equals
   the direct B3-B accepted encoding; malformed/unknown-signature inputs remain terminal and
   non-recoverable; no operation opens; no graph mutation; connection completion counters.
4. Challenge the malformed-input behavior change (§22) against the accepted B3-B contract and
   decide whether any accepted contract requires a different disposition.
5. Confirm supported-message, IGNORED-reset, fail-closed-retirement, pooling, TLS and F02
   behavior are unchanged; confirm the driver suite and core counts; verify the 518/518
   historical manifests and five owner decisions independently.
6. Do not self-qualify from this report; record any independence limitations explicitly.

## 24. Implementation commits and trees

| Commit | Subject | Tree |
| --- | --- | --- |
| (implementation commit, this stage) | `fix(boltsharp): route unsupported Bolt messages through session refusal path` | recorded in the final handoff |
| (evidence commit, this stage) | `docs(boltsharp): record B5-B F01 remediation evidence` | recorded in the final handoff |

Parent of the implementation commit: `3953bdd9962a8e4dc4c43c0bb3996b5fed76fb08`
(tree `f3fd25af19261f754d7d601b5c214f315c74af7d`). Exact hashes and the final repository state
are recorded in the final handoff. No push; no history rewrite.

