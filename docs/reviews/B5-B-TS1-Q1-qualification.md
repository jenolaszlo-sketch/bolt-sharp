# B5-B-TS1-Q1 — Independent Regression Stability Qualification

## 1. Decision metadata

| Field | Value |
| --- | --- |
| Task | `B5-B-TS1-Q1` — independent qualification of the B5-B-TS1 test-only remediation |
| Role | Independent qualification reviewer |
| Target findings | `B5-B-F01-R1-OWNER-F01`, `B5-B-F01-R1-OWNER-F02` |
| Reviewed HEAD | `d8c28a9bed0af6305936b2b6e427ca1365bed757` |
| Reviewed tree | `9e7f07ca2664e44230b10b4b92c9f435fd58fdcd` |
| Implementation commit | `2e0b9da0b8920a2f6cdd7de9fa968891d779090e` (tree `3fbdbebf21aacd545fd3384c289c649e8c7ab75b`) |
| Implementation base | `17886a5e7f709ef1e01624e49c9d90b50808566c` (tree `e7d3be41483ab7aa22b51b27c619d7f211d8520b`) |
| Local `origin/main` | `79f7965a0e8e351baf2dd388218290804c31389a` |
| Divergence at start | 0 behind, 3 ahead of `origin/main` |
| Environment | Windows 11 10.0.26200; .NET SDK 10.0.401; `Microsoft.NETCore.App` 8.0.31; 18 logical CPUs; Debug |
| Disposition | **SUPPORTED WITH QUALIFICATIONS** |
| Decision date | 2026-11-15 |

This record is a qualification disposition. It changes no production source, canonical test,
test helper, project, package, public API, owner decision or historical evidence. It does not
issue owner acceptance and does not discharge any production qualification.

## 2. Commission and scope

The commission is to determine independently whether `B5-B-TS1` correctly resolves:

- `B5-B-F01-R1-OWNER-F01` — the terminal FAILURE delivery race in the two pipelined refusal
  tests; and
- `B5-B-F01-R1-OWNER-F02` — the multi-client counter observation race in
  `FailureInOneClient_IsolatedFromOthers`.

The producer's conclusions were not assumed. The implementation, evidence, protocol/transport
contracts, complete diffs, owner decisions and historical records were inspected independently,
and fresh campaigns and bounded negative controls were executed. All experiments that mutate
source ran in isolated `git archive` copies; the canonical tree was never modified.

Prohibited work was not performed: no production change, no canonical test/helper change, no
project/package/API change, no owner-decision or historical-evidence change, no remediation, no
F02 refinement, no B4/B6 work, no push.

## 3. Repository preflight

The repository matched the commission exactly before qualification work began.

| Check | Observed |
| --- | --- |
| Branch | `main` |
| HEAD | `d8c28a9bed0af6305936b2b6e427ca1365bed757` |
| HEAD tree | `9e7f07ca2664e44230b10b4b92c9f435fd58fdcd` |
| Implementation commit / tree | `2e0b9da0b8920a2f6cdd7de9fa968891d779090e` / `3fbdbebf21aacd545fd3384c289c649e8c7ab75b` |
| Base commit / tree | `17886a5e7f709ef1e01624e49c9d90b50808566c` / `e7d3be41483ab7aa22b51b27c619d7f211d8520b` |
| `origin/main` | `79f7965a0e8e351baf2dd388218290804c31389a` |
| Divergence | `0 3` (behind/ahead) |
| Worktrees | 1 |
| Stashes | 0 |
| Working tree | Clean before the Q1 evidence directory was created |

Ancestry is linear and as commissioned:

```text
79f7965  docs: record B5-B-F01-R1-Q1 reviewer provenance   (origin/main)
17886a5e docs: close B5-B F01 refusal remediation with qualifications
2e0b9da  test: stabilize terminal refusal and multi-client lifecycle checks
d8c28a9  docs: record B5-B TS1 stability qualification evidence   (reviewed HEAD)
```

The implementation commit changes exactly the three authorized test files:

```text
M tests/BoltSharp.Tests/Transport/MultiClientTests.cs
M tests/BoltSharp.Tests/Transport/TransportTestSupport.cs
M tests/BoltSharp.Tests/Transport/UnsupportedCommandRefusalTests.cs
```

The evidence commit adds exactly ten documentation/evidence files and nothing else. Across the
whole TS1 range (`17886a5e..d8c28a9`), the diff is 13 paths: the three test files plus ten
docs-only additions. The protected-scope check over `src/`, `*.csproj`, `*.sln`, `*.slnx`,
`*.props`, `*.targets`, `packages.lock.json` and `docs/decisions` returns **0 changes**.

## 4. Production contract trace

### 4.1 Session generation — Property A

The unsupported-command path is a session terminal refusal, not a decode-boundary close. In
`BoltConnectionSession.ReceiveBytes` a structurally valid but unsupported signature reaches
`FailTerminal(UnsupportedMessageFailure())`. `FailTerminal`:

1. attempts to detach and dispose any owned operation;
2. sets `_state = BoltConnectionState.Defunct` unconditionally; and
3. returns `BoltSessionResult.Single(_state, failure, shouldClose: true)`.

The FAILURE is a single response with `gql_status=08N06`,
`neo4j_code=Neo.ClientError.Request.Invalid`, the exact description
`Explicit transactions and routing are not supported by this server.`, and an empty
`diagnostic_record`. The standalone test pins the complete encoded payload byte-for-byte; the
pipelined tests decode the same fields and assert them. Property A is established by the
production path plus both oracle layers.

### 4.2 Transport write and terminal close — Property B

`BoltTcpConnection` processes one complete message at a time:

1. decode the request;
2. `session.ReceiveBytes(message)`;
3. `WriteResponsesAsync(stream, codec, result.Responses, ...)` — write **and flush**;
4. `diagnostics.Report(MessageProcessed, connectionId, "sig=... responses=N state=...")`;
5. if `result.ShouldClose || session.IsDefunct`, leave the loop and close the connection;
6. in `finally`, retire the session and report `ConnectionCompleted` or `ConnectionFailed`.

`MessageProcessed` is therefore emitted **after** the response write/flush, not before and not as
an eventual state sampled elsewhere. It proves the transport write completed; it does not prove
the client read the bytes. That distinction is exactly Property B versus Property C. The
`responses=1` and `state=Defunct` fields reflect the session result that was just written.

### 4.3 Diagnostics counters and events

`BoltTransportDiagnostics.Report` updates the counter for the event **before** it invokes the
observer synchronously:

- `ConnectionAccepted`: accepted++, active++;
- `ConnectionCompleted`: active--, completed++;
- `ConnectionFailed`: active--, failed++;
- `MessageProcessed`: messages++;
- terminal events also remove the connection id from the active set.

Observer exceptions are contained. Therefore, when a test observes a diagnostic event, the
corresponding counters are already final for that transition. The event is an authoritative
completion signal, not a hint that may race the counter update.

### 4.4 Terminality and closure classification

`FailTerminal` sets `Defunct` before returning, and `IsDefunct` is true thereafter. A terminal
refusal is not a transport fault: the connection task takes the normal completion path, reports
`ConnectionCompleted` with detail `closed`, and does not increment `ConnectionsFailed`. The tests
assert this exact accounting: accepted=1, completed=1, failed=0, active=0, messages=3.

## 5. OWNER-F01 assessment

### 5.1 Root cause

The claimed root cause is confirmed. When a client writes several framed messages in one TCP
write, the server refuses the first unsupported command and closes with unread tail bytes still
in its receive buffer. The OS may turn that close into an abortive close (RST). The RST can
discard the in-flight FAILURE before the client reads it. Server-side generation and write can
therefore be correct while client-visible delivery is suppressed. That is a real distinction, not
a test artifact.

The baseline was independently reproduced from an isolated `git archive` of `17886a5e`:

- old pipelined pair: **5/10 runs failed**, all `PipelinedUnsupportedCommand`, before any
  response, with `IOException` wrapping `SocketException` "forcibly closed by the remote host";
- old `FailureInOneClient_IsolatedFromOthers`: **20/20 passed in isolation**, consistent with a
  load-sensitive race rather than a deterministic defect.

The producer recorded 7/10 baseline failures; the difference is environment timing. The failure
class is the same.

### 5.2 Three-property contract

| Property | Meaning | How it is established | Residual |
| --- | --- | --- | --- |
| A — generation | The session generates the required terminal FAILURE. | Production trace plus byte-exact standalone encoding and decoded-field assertions in both pipelined tests. | None observed. |
| B — transport write | The transport writes the response before terminal close. | `WriteResponsesAsync` (write + flush) precedes `MessageProcessed`; the pipelined tests await that event and assert `responses=1`, `state=Defunct`. | The event proves the write/flush, not client receipt. |
| C — client observation | The client observes the FAILURE unless the characterized abortive close suppresses it. | Standalone test requires byte-exact FAILURE, clean EOF and no second message; pipelined tests require exact content when delivered and accept only the characterized reset otherwise; official-driver scenarios provide an independent non-pipelined client check. | RST suppression remains possible and is retained as `Q-B5B-09`. |

### 5.3 What the combined tests establish

The standalone `UnsupportedCommand_FailurePayloadIsByteIdenticalToTheAcceptedSessionEncoding`
test sends only `BEGIN` (no pipelined tail), reads one message and requires:

- non-null message;
- byte-exact equality with `AcceptedUnsupportedCommandFailureHex`;
- `ReadFramedMessage()` returns `null` — orderly EOF, no second message;
- `ConnectionsActive` reaches 0.

A reset or a truncated response would throw out of the read and fail the test. This is the
deterministic delivery gate.

The two pipelined tests send one write containing the terminal message plus a tail
(`PULL, RESET, RUN, PULL` or `RUN, PULL`). They:

- subscribe to the terminal `MessageProcessed` event and `ConnectionCompleted` after the client is
  authorized but before the terminal pipelined write;
- filter `MessageProcessed` on the terminal signature (`sig=0x11` or `sig=0x10`) so the preceding
  LOGON event cannot satisfy the probe;
- assert `responses=1` and `state=Defunct`;
- assert completion detail `closed`;
- assert exactly three processed messages, zero provider preparation, accepted=1, completed=1,
  failed=0, active=0;
- read the FAILURE and assert its exact fields when it is delivered;
- accept only a connection reset/abort when it is not delivered;
- if delivered, require closure and no further message (a characterized reset on the final read is
  tolerated; an extra response is not).

The combined suite therefore establishes A and B deterministically and C with the owner-authorized
abortive-close exception. The exception is not inferred from diagnostics; it is a client-side
socket outcome accepted only by the narrow classifier.

### 5.4 Diagnostics: ordering versus eventual state

`MessageProcessed` is not an eventual-state report. It is raised synchronously after the
write/flush in the same connection task. It proves Property B. It does not prove Property C, and
the tests do not treat it as such. `ConnectionCompleted` is raised after the connection task's
cleanup and counter finalization; it is the authoritative signal used by the F02 test. The
diagnostics are correctly used as synchronization for the properties they can prove, not as a
substitute for client delivery.

## 6. Oracle integrity

### 6.1 Standalone oracle

The standalone oracle rejects every non-conforming outcome:

| Condition | Outcome |
| --- | --- |
| Missing response / clean EOF before response | `ReadMessage()` returns null or throws; test fails. |
| Truncated response | Reader throws; test fails. |
| RST before or during response | `ReadFramedMessage()`/reader throws `IOException`; test fails. |
| Wrong FAILURE bytes | Hex equality fails. |
| Extra response after FAILURE | `Assert.Null(ReadFramedMessage())` fails. |
| Non-orderly close after FAILURE | A reset throws instead of returning null; test fails. |

### 6.2 Pipelined oracle

`TryReadTerminalFailure` accepts exactly two outcomes:

1. a decoded `FailureResponse` whose `gql_status`, `neo4j_code` and description match; or
2. an `IOException` whose inner exception is a `SocketException` with
   `SocketError.ConnectionReset` or `SocketError.ConnectionAborted`.

It explicitly excludes `EndOfStreamException` (clean EOF), timeouts, and every other I/O fault.
A non-FAILURE response fails `Assert.IsType`; wrong contents fail `AssertFailure`. After a
delivered response, `TryReadAnyFinalMessage` accepts only null (clean EOF) or the characterized
reset; an extra response fails `Assert.Null`.

`WaitForClose` alone is not used to distinguish clean EOF from reset: it returns `closed` for
clean EOF, reset or local disposal. The standalone test's clean-EOF requirement comes from the
read returning null, not from `WaitForClose`. The pipelined delivered path intentionally tolerates
the characterized reset after the response.

`ConnectionAborted` is accepted alongside `ConnectionReset`. The owner finding describes an OS
abortive close; Windows can surface a locally aborted socket as either code. Accepting both is
consistent with the characterized class, but it does broaden the accepted set (finding F05). The
material properties remain constrained by the server-side assertions and the standalone oracle.

### 6.3 Negative controls

Six source mutations were applied one at a time in an isolated HEAD copy and built there. The
canonical tree was not modified. The standalone byte oracle and the two pipelined tests were run
against each mutation.

| Mutation | Standalone | Pipelined | Result |
| --- | --- | --- | --- |
| M0 unmodified control | 0/1 failed | 0/2 failed | Green control. |
| M1 missing terminal write, event still reported | 1/1 failed | 0/10 failed | Standalone catches; pipelined is blind when RST suppresses the absent response. |
| M2 wrong FAILURE content | 1/1 failed | 8/10 failed | Standalone catches deterministically; two RST-suppressed pipelined runs accepted. |
| M3 extra fabricated response | 1/1 failed | 5/5 failed | Both catch. |
| M4 wrong terminal state (`Failed`, still closes) | 0/1 failed | 5/5 failed | Pipelined state assertion catches; standalone does not assert state. |
| M5 unrelated transport fault after write | 0/1 failed | 1/1 failed | Pipelined classifier rejects the fault; standalone does not exercise post-write faults. |
| M6 no response / no terminal event | 1/1 failed | 1/1 failed | Both catch; pipelined times out with diagnostic counters. |

The combined suite detects every injected defect. M1/M2 show the exact residual boundary: a
pipelined-only oracle cannot prove delivery when the OS discards the response, so the standalone
byte oracle is load-bearing. M4/M5 show the complementary boundary: the standalone oracle does
not assert terminal state or post-write faults, so the pipelined assertions are load-bearing.
Neither layer alone is sufficient; together they cover the contract.

Could a genuine regression satisfy the reset oracle? Only a regression that preserves correct
generation, write, terminal state and accounting while causing an abortive close that suppresses
delivery. That is the accepted `Q-B5B-09` boundary, not a new false positive. A regression that
removes or corrupts the FAILURE is caught by the standalone test (M1/M2). A regression that
changes state, adds a response, or raises an unrelated fault is caught by the pipelined test
(M3–M5). No campaign or control produced a false positive.

### 6.4 Method limitations

The mutations are source-level and the copies are isolated archives. RST timing is
environment-dependent; M1's 0/10 and M2's 8/10 are timing observations, not guarantees. The
deterministic result is the standalone oracle's 1/1 detection. The controls are bounded, not an
exhaustive fault injection campaign.

## 7. OWNER-F02 assessment

### 7.1 Root cause

The old test disposed the bad client and immediately asserted `ConnectionsFailed == 1`. Client
socket disposal is a client-side event; it does not mean the server's connection task has finished
failure accounting. Under load the counter could still be 0 when the assertion ran. That is a
test synchronization defect, not a production defect.

### 7.2 New synchronization

The test subscribes to `ConnectionFailed` and `ConnectionCompleted` before creating either
connection. It then:

1. connects the bad client and the good client;
2. sends invalid handshake bytes from the bad client and disposes it;
3. awaits `ConnectionFailed`, then asserts the failure detail is exactly
   `handshake:BoltHandshakeFormatException`;
4. asserts accepted=2, failed=1, completed=0, active=1;
5. sends a PULL on the good client and asserts two records;
6. disposes the good client and awaits `ConnectionCompleted`;
7. asserts completion detail `closed`; and
8. asserts final accepted=2, completed=1, failed=1, active=0, rejected=0, messages=4.

Because counters are updated before the synchronous event, step 3's completion guarantees step 4
is observing final values for that transition. The completion probe does the same for the healthy
connection.

### 7.3 Event identity and correlation

- The bad client is the only connection that can fail with `BoltHandshakeFormatException`; the
  good client has already completed HELLO and LOGON and is running a query.
- The `ConnectionCompleted` event is raised only for the healthy client; the bad client took the
  failed path. If the bad client completed instead, the `ConnectionFailed` await would time out.
- The server is created per test, so no previous connection can satisfy either probe.
- The exact counters (accepted=2, failed=1, completed=1, active=0) reject an extra or missing
  connection event.
- The good client's PULL is performed only after the failure event, so the healthy operation is
  not racing the failure accounting.

### 7.4 Race and concurrency review

- Subscription occurs before any connection, so no event can be lost.
- The event is raised synchronously on the connection task after the counter update; the TCS uses
  `RunContinuationsAsynchronously`, so the test continuation does not run on the transport thread.
- `TrySetResult` is atomic; a duplicate matching event is a no-op.
- The waits are bounded at 10 seconds and report the actual counters on timeout.
- No fixed sleep, arbitrary retry or unbounded wait was introduced.
- The old test passed 20/20 in isolated baseline runs; the new test passed 600/600 independent
  campaign B executions. The race is load-sensitive, which is exactly why a completion signal is
  required rather than a timing assumption.

F02 is correctly resolved at the test layer. No production synchronization change was needed or
made.

## 8. Shared test helper review

### 8.1 `ProbeEvent`

- **Subscription timing:** the multi-client probes are subscribed before either connection is
  created. The pipelined probes are subscribed after the client is authorized but before the
  terminal pipelined write; the terminal-signature filter excludes the earlier LOGON
  `MessageProcessed` event, and `ConnectionCompleted` cannot occur while the authorized client
  remains connected.
- **Event loss:** none for the tested transitions. The multi-client subscription is live before any
  connection; the pipelined subscription is live before the terminal write, and its signature
  filter excludes the only earlier `MessageProcessed` event. No relevant event can be missed.
- **Predicate correctness:** the pipelined probes filter on the terminal message signature; the
  multi-client probes filter on event kind. In the current single-connection/single-failure tests
  this is sufficient.
- **Correlation:** the pipelined probes do not filter by connection id; the tests assert
  accepted=1, so only one connection can produce the event. The multi-client failure is pinned by
  its exact detail and counters. Finding F04 records the future multi-connection caveat.
- **Buffering/concurrency:** the TCS is thread-safe and one-shot; continuations are asynchronous.
- **Timeout/cancellation:** `AwaitEventAsync` applies a finite 10-second timeout and reports the
  actual counters. There is no cancellation token, but the wait is bounded.
- **Disposal:** the observer is never unsubscribed (finding F03). This is bounded by per-test
  server disposal and has no observed correctness impact, but a future server reused across
  scenarios could retain stale probes.
- **Exception propagation:** observer exceptions are contained by the diagnostics `Report` method;
  test assertions still propagate normally.
- **Deadlocks/leaks:** no deadlock; the leak is bounded by the server lifetime.
- **Unrelated/out-of-order events:** the terminal signature filter and exact counters reject the
  preceding LOGON event and any post-terminal event.

### 8.2 The preceding LOGON event

The producer's N01 observation is structurally correct. `MessageProcessed` is reported after the
response write/flush, but the client can read the response before the server thread executes the
report. A probe created after `ConnectAndAuthorize` could therefore capture the LOGON event first.
Filtering by the terminal signature is **necessary**. It is **sufficient** in these tests because
the terminal signature is unique among processed messages, and the tests independently assert
that exactly three messages were processed and that the pipelined tail never executed. No fixed
sleep was added.

### 8.3 No timing shortcuts

The helper and tests contain no fixed sleeps, no arbitrary retries, no unbounded waits and no
blanket exception swallowing. The only catch that suppresses an exception is the characterized
reset classifier, which excludes EOF, timeouts and unrelated faults.

## 9. Independent campaigns

All campaigns ran in fresh `dotnet test` processes against the built HEAD tree, with Debug
configuration, `--no-build` and `--nodeReuse:false`. Failed iterations were preserved; none
occurred. The exact commands and raw logs are in the evidence package.

| Campaign | Independent processes | Tests per process | Test executions | Failures | Skips | Timeouts | Elapsed per process |
| --- | --- | --- | --- | --- | --- | --- | --- |
| A — terminal refusal | 100 | 10 | 1,000 | 0 | 0 | 0 | 1.411–4.042 s (median 1.490 s) |
| B — multi-client | 100 | 6 | 600 | 0 | 0 | 0 | 1.419–1.616 s (median 1.464 s) |
| C — full core suite | 30 | 1,227 | 36,810 | 0 | 0 | 0 | 3.375–3.650 s (median 3.501 s) |
| D — official driver | 3 | 41 | 123 | 0 | 0 | 0 | 121.968–124.223 s |
| E — regressions | 1 each | 90 / 94 / 119 / 98 / 19 | 420 | 0 | 0 | 0 | 3.398 s / 1.363 s / 1.337 s / 1.326 s / 24.168 s |

Campaign A passed 10/10 on every one of the 100 processes. Campaign B passed 6/6 on every one of
the 100 processes. Campaign C passed 1,227/1,227 on every one of the 30 processes. Campaign D
passed 41/41 on all three processes.

Campaign D's original TSV header was missing a tab between `exit` and `elapsed_ms`. The header in
`07-campaign-D-official-driver.tsv` is corrected; the three run rows and the raw log are
unmodified, and no run was discarded or restarted.

Total independent campaign/regression executions: **38,953** (1,000 + 600 + 36,810 + 123 + 420).
The baseline reproduction (30 executions) and mutation controls are additional and are reported
separately.

## 10. Build and formatting

| Check | Command | Result |
| --- | --- | --- |
| Production build | `dotnet build src\BoltSharp\BoltSharp.csproj -c Debug --no-incremental --nodeReuse:false` | 0 warnings, 0 errors |
| Solution build | `dotnet build BoltSharp.slnx -c Debug --no-incremental --nodeReuse:false` | 0 errors; 2 pre-existing `xUnit2012` advisories |
| Scoped formatting | `dotnet format tests\BoltSharp.Tests\BoltSharp.Tests.csproj --verify-no-changes --no-restore --include <three TS1 files>` | Exit 0, no changes |

The two solution warnings are in `tests/BoltSharp.Tests/Messages/BoltRequestDecodeTests.cs` at
lines 71 and 72 (`xUnit2012`). They are present in the isolated base-commit build log, so they are
pre-existing and not attributable to TS1. TS1 introduces no new warning.

## 11. Evidence integrity and portability

### 11.1 TS1 producer manifest

The manifest contains 12 hashed entries: 8 evidence files, 1 report and 3 changed test files. All
12 match the producer's current worktree. Eleven match Git blob bytes; the twelfth,
`TransportTestSupport.cs`, is hashed in CRLF worktree form:

```text
blob:    19,563 bytes  e26ea718d47dc7a8eebbddf5c3799b79877b52d08ea19dd3594a82e8d9496bd0
worktree: 20,066 bytes  3849b1e3db015daa1ac98992b2c015e20d5aceb5d0f25eced419b312cbefd4a8
manifest: 20,066 bytes  3849b1e3db015daa1ac98992b2c015e20d5aceb5d0f25eced419b312cbefd4a8
```

The worktree form is byte-identical to a deterministic LF→CRLF conversion of the blob (503 CRs).
The producer's worktree is mixed: `TransportTestSupport.cs` is `i/lf w/crlf`, while the report,
`MultiClientTests.cs` and `UnsupportedCommandRefusalTests.cs` are `i/lf w/lf`.

### 11.2 Historical aggregate

The canonical 17-manifest aggregate is **619/619** with raw working-tree bytes and **0
mismatches**. It consists of the 15 manifests in
`docs/reviews/evidence/B5-B-F01-R1/Verify-HistoricalManifests.ps1` (518 entries) plus
`B5-B-F01-R1` (70) and `B5-B-F01-R1-Q1` (31). Every manifest's declared entry count matches the
commission expectation.

The two excluded B3-B implementation-source manifests total **36/36**: 31 entries verified
against the current tree and 5 against their recorded implementation-commit blobs:

```text
src/BoltSharp/Sessions/BoltConnectionSession.cs                    @ 9f224cf4...
src/BoltSharp/Sessions/BoltSessionOptions.cs                       @ 9f224cf4...
tests/BoltSharp.Tests/Sessions/AuthenticationAndTransitionTests.cs @ 9f224cf4...
src/BoltSharp/Sessions/BoltConnectionSession.cs                    @ 454ae005...
tests/BoltSharp.Tests/Sessions/CleanupAndTelemetryRemediationTests.cs @ 454ae005...
```

No historical manifest references any file changed by TS1 (**0 references**).

### 11.3 Owner decisions and scope

All six accepted owner decisions are byte-identical to their committed blobs under Git's
clean-filter semantics (`git hash-object` equals `git ls-tree HEAD`), and none changed across
`17886a5e..d8c28a9`:

```text
B3-A        ab84ffa973217612a1cc7033936ce23dcefe273a
B3-B        211a5d56a296e29ff4f5faad4abe22c2a0bec2a0
B3-C        323303bd0eaa79e00458689af99e60fbf27a0836
B5-A        385c820e2e046694fbfa3735a22034545d8068c0
B5-B        7d23b6bb50c66f134bc33f8ba89cb7d09813c97e
B5-B-F01-R1 67f2846ea24e42616f5fb51cd46973fcc4725606
```

The implementation commit is exactly the three test files; the evidence commit is exactly ten
docs-only additions; protected-scope changes across TS1 are zero. The producer's campaign TSVs
are internally consistent with the report: A 100 rows / 10 tests, B 100 / 6, C 30 / 1,227,
D 3 / 41, all exit 0. The independent campaigns reproduce the same test counts.

### 11.4 Portability classification

An independent reviewer can reconstruct all 12 producer hashes, but not by a naive fresh
checkout:

| Checkout form | Producer manifest reproduction |
| --- | --- |
| Producer's mixed worktree | 12/12 |
| Git blob bytes at `d8c28a9` | 11/12 (only `TransportTestSupport.cs`) |
| Linux / `core.autocrlf=input` or `false` | 11/12 |
| Fresh Windows `core.autocrlf=true` | 9/12 (report, `MultiClientTests.cs`, `UnsupportedCommandRefusalTests.cs` become CRLF) |

The manifest is therefore **not checkout-reproducible** without reconstructing the producer's
mixed line-ending form. This is finding **F01**, severity LOW. It does not invalidate the
substantive evidence: all 12 preimages are derivable from the commit, and the independent
campaigns reproduce the execution results. It is an evidence-portability deficiency, not a test
or production defect.

The same raw-worktree convention applies to the historical 619-entry aggregate: 313 entries match
both blob and worktree, 306 match only the CRLF worktree form. The 619/619 result is valid in this
Windows `core.autocrlf=true` checkout, but it is not a cross-platform byte guarantee. This is
pre-existing historical behavior and is out of scope; no historical evidence was changed.

The Q1 package avoids the ambiguity: every evidence file is LF and the directory is marked
`* -text`; the manifest records SHA-256 over **Git blob bytes**; `Verify-Evidence.ps1` resolves
entries with `git cat-file blob`. The report is LF and is hashed from its blob, not from a
worktree form.

## 12. Protected scope verification

| Requirement | Result |
| --- | --- |
| Production source unchanged | 0 `src/` changes across TS1 |
| Public APIs unchanged | No production assembly/API change |
| Projects/packages unchanged | 0 `*.csproj`, `*.sln`, `*.slnx`, `*.props`, `*.targets`, lock-file changes |
| Owner decisions unchanged | 6/6 byte-identical; 0 changed across TS1 |
| Historical evidence unchanged | No historical file or manifest modified |
| Fixtures/probes outside scope unchanged | Only the three authorized test files changed |
| No production behavioral change | Test-only changes; internal test helper |
| No unsupported production claim | Tests and report retain `Q-B5B-09`; no production qualification claimed |

Preserved dispositions:

- `Q-B5B-03` remains **closed** by the B5-B-F01-R1 owner decision; TS1 does not reopen or extend
  it.
- `Q-B5B-04` (generic `50N42` classification) remains **open**.
- `Q-B5B-05` (same-connection RESET recovery after IGNORED) remains **open**.
- `Q-B5B-09` (release-grade abortive-close / client-visible delivery hardening) remains **open**.
- All other inherited qualifications remain unchanged.

Successful test stabilization does not discharge a production qualification. The terminal FAILURE
delivery limitation under pipelined input remains a production/release-hardening question, not a
test question.

## 13. Findings

| ID | Severity | Area | Finding | Impact | Required remediation |
| --- | --- | --- | --- | --- | --- |
| `B5-B-TS1-Q1-F01` | LOW | Evidence portability | The producer manifest hashes a mixed LF/CRLF worktree; `TransportTestSupport.cs` is CRLF while its blob is LF. Fresh Windows (`autocrlf=true`) reproduces 9/12; Linux reproduces 11/12; only the producer's worktree reproduces 12/12. | A naive checkout cannot self-verify the manifest; the preimages are still derivable and the substantive evidence is independently reproduced. | Future packages should use Git blob bytes and/or `* -text`. Do not rewrite the canonical TS1 manifest. |
| `B5-B-TS1-Q1-F02` | LOW | Oracle boundary | The pipelined oracle accepts RST suppression when the FAILURE is missing or wrong (M1 0/10, M2 8/10 detected). | Pipelined-only tests cannot prove delivery under the characterized abortive close; the standalone byte oracle covers it deterministically. | None for TS1; retained under `Q-B5B-09` for production graceful-close/drain. Any future change must not weaken the standalone oracle. |
| `B5-B-TS1-Q1-F03` | INFO | Test helper | `ProbeEvent` never unsubscribes its observer. | Bounded by per-test server disposal; no observed correctness impact; a reused server could retain stale probes. | Optional test-only improvement (return `IDisposable` or detach); requires separate authorization. |
| `B5-B-TS1-Q1-F04` | INFO | Test helper | Pipelined probes correlate by terminal signature, not connection id. | Sufficient for the current single-connection tests; a future multi-connection variant could be satisfied by another connection's event. | Optional; add connection-id filtering if a future test has multiple connections. |
| `B5-B-TS1-Q1-F05` | INFO | Oracle classifier | `ConnectionAborted` is accepted alongside `ConnectionReset`. | Consistent with the OS abortive-close class but broadens the accepted set; server-side assertions and the standalone oracle constrain the material properties. | None; retain the narrow classifier and the standalone gate. |

Producer non-blocking findings:

- **`B5-B-TS1-N01` — diagnostic event observation ordering.** Justified. `MessageProcessed` is
  raised after write/flush, but the client can read the response before the server thread reports
  it. A naive "next event" probe can capture the preceding LOGON event. The terminal-signature
  filter is necessary and sufficient in these tests; no production impact.
- **`B5-B-TS1-N02` — mislabeled failure class.** Justified. The invalid handshake bytes are
  rejected at the B2 handshake as `BoltHandshakeFormatException`; the test now pins that exact
  detail. No runtime impact.

No blocking findings were identified.

## 14. Disposition

**B5-B-TS1-Q1 — SUPPORTED WITH QUALIFICATIONS.**

The test-only remediation correctly resolves both target findings at the test layer:

- **OWNER-F01:** the standalone oracle now requires byte-exact FAILURE delivery with clean EOF and
  no second message; the pipelined tests assert generation, write, terminal state, no-tail
  execution and exact accounting, while treating client delivery as delivered-if-read under the
  owner-authorized abortive-close race. The combined suite detects every injected defect,
  including missing and wrong FAILUREs.
- **OWNER-F02:** the multi-client test now synchronizes on the authoritative diagnostics events
  after counter finalization, with exact event identity, failure classification and connection
  accounting. The old timing race is removed.

The qualifications are:

1. `Q-B5B-09` remains open: production graceful-close/drain and guaranteed client-visible delivery
   under unread pipelined input are not established. TS1 makes the tests faithful to the retained
   limitation; it does not remove it.
2. The pipelined delivery check is conditional by design. The standalone byte oracle is the
   deterministic delivery gate; weakening or removing it would invalidate the combined coverage.
3. The producer evidence manifest has a LOW-severity portability deficiency (F01). The substantive
   evidence is independently reproducible, but the manifest is not checkout-independent.
4. `Q-B5B-04`, `Q-B5B-05` and all inherited qualifications remain open and unchanged.
5. This disposition is not owner acceptance and makes no production qualification claim.

## 15. Handoff summary

1. **Formal disposition:** `SUPPORTED WITH QUALIFICATIONS`.
2. **Repository identity:** started at `d8c28a9` / tree `9e7f07c`, three commits ahead of
   `origin/main` `79f7965`; the qualification commit is the single docs-only child of `d8c28a9`.
3. **Root causes:** both independently confirmed. F01 is the abortive-close suppression of an
   in-flight FAILURE with unread pipelined input; F02 is the old test's use of client disposal as
   a proxy for server-side failure accounting.
4. **Terminal FAILURE oracle:** the standalone oracle is byte-exact, requires clean EOF and
   rejects a second message; the pipelined classifier accepts only `ConnectionReset`/
   `ConnectionAborted` and rejects EOF, timeouts, unrelated faults and extra responses.
5. **Multi-client synchronization:** correct; counters update before synchronous events, probes
   subscribe before connections, and exact identity/counters are asserted.
6. **Test helper:** sound for the current tests; the LOGON-ordering filter is necessary and
   sufficient; F03/F04 are bounded, non-blocking observations.
7. **Negative controls:** the combined suite detects all six injected defects; M1/M2 expose the
   accepted pipelined RST-suppression boundary, covered by the standalone oracle.
8. **Independent campaigns:** A 100/100 (10/10), B 100/100 (6/6), C 30/30 (1,227/1,227),
   D 3/3 (41/41), regressions 90/94/119/98/19; zero failures, skips or timeouts.
9. **Builds/formatting:** production 0 warnings/0 errors; solution 0 errors with two pre-existing
   `xUnit2012` advisories; scoped format exit 0; no TS1-attributable warning.
10. **Evidence integrity/portability:** canonical 619/619, excluded 36/36, TS1 12/12 worktree and
    11/12 blob, owner decisions 6/6, zero historical references; F01 documents the mixed
    LF/CRLF checkout dependency and the Q1 package uses blob-byte hashing with `* -text`.
11. **Historical/owner decisions:** all six owner decisions byte-identical and unchanged; no
    historical manifest references a TS1-changed file; historical evidence unchanged.
12. **Findings and qualifications:** F01 LOW (evidence portability), F02 LOW (accepted oracle
    boundary), F03–F05 INFO; N01/N02 justified; no blocking findings. `Q-B5B-03` closed;
    `Q-B5B-04`, `Q-B5B-05`, `Q-B5B-09` and all inherited qualifications open.
13. **Qualification commit:** the docs-only child of `d8c28a9` containing this report and the
    `docs/reviews/evidence/B5-B-TS1-Q1/` package; its exact hash is recorded in the session
    handoff.
14. **Final repository state:** clean, one worktree, no stashes, four commits ahead of
    `origin/main`, nothing pushed.
15. **Recommended next commission:** a separately authorized, bounded `Q-B5B-09` production
    graceful-close/drain investigation (or an owner decision on whether the characterized
    abortive-close delivery limitation is acceptable for release). Do not start it under this
    qualification, and keep `Q-B5B-04`, `Q-B5B-05` and all inherited qualifications separate.

---

**Verification:** `docs/reviews/evidence/B5-B-TS1-Q1/Verify-Evidence.ps1` verifies every manifest
entry against Git blob bytes at the qualification commit, checks package completeness and scope,
and exits 0 only if all checks pass.
