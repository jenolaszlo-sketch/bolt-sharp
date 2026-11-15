# B5-B-TS1 — Deterministic TCP Terminal-Response and Multi-Client Regression Tests

**Task:** `B5-B-TS1` — bounded test-stability implementation (test-only)
**Protocol:** Bolt 5.8 / PackStream 1.0 (unchanged production scope)
**Target findings:** `B5-B-F01-R1-OWNER-F01`, `B5-B-F01-R1-OWNER-F02`
**Starting HEAD / tree:** `17886a5e7f709ef1e01624e49c9d90b50808566c` / `e7d3be41483ab7aa22b51b27c619d7f211d8520b`
**Implementation commit / tree:** `2e0b9da0b8920a2f6cdd7de9fa968891d779090e` / `3fbdbebf21aacd545fd3384c289c649e8c7ab75b`
**Disposition:** **B5-B-TS1 — IMPLEMENTED, READY FOR INDEPENDENT QUALIFICATION**

## 1. Task identity and scope

This is producer implementation work for two test-stability findings recorded during the
B5-B-F01-R1 owner closure. It changes **tests only**: two affected test classes and one narrow
test-only synchronization helper. Production code, schemas, state semantics, diagnostics
semantics, public APIs, packages and the solution file are unchanged.

The goal was not to make failing assertions pass. It was to make each test assert exactly the
accepted contract at the layer that can observe it, and to synchronize on authoritative lifecycle
transitions instead of incidental timing.

## 2. Starting state and preflight

| Check | Result |
| --- | --- |
| Branch | `main` |
| HEAD | `17886a5e7f709ef1e01624e49c9d90b50808566c` (exact) |
| Tree | `e7d3be41483ab7aa22b51b27c619d7f211d8520b` (exact) |
| Worktrees | exactly one (`D:/projects/bolt-sharp`) |
| Working tree / index | clean |
| Stash | none |
| Prior TS1 implementation | none |
| Owner decisions | all six present, byte-identical to committed blobs (§15) |
| Remote relationship | 1 commit ahead of `origin/main` (owner closure commit local), as expected from the handoff; no fetch and no push performed |
| Production changes present | none beyond the accepted F01-R1 transport correction already at HEAD |

No baseline mismatch; no stop condition applied.

## 3. Accepted owner findings

**`B5-B-F01-R1-OWNER-F01` — pipelined terminal tests are client-delivery-race sensitive.**
`PipelinedUnsupportedCommand_NothingAfterTheTerminalFailureExecutes` and
`PipelinedMalformedMessage_NothingAfterTheTerminalFailureExecutes` sent a terminally refused
message followed by a pipelined tail in one write, then required an immediate client read of the
terminal FAILURE. With unread tail bytes present, the accepted terminal close can be an OS
abortive close (RST), which can discard the in-flight response before the client reads it.
Owner-closure observation: 4/33 full-suite runs and 16/20 filtered runs; server-side behavior
correct in every case.

**`B5-B-F01-R1-OWNER-F02` — multi-client counter synchronization.**
`MultiClientTests.FailureInOneClient_IsolatedFromOthers` asserted
`ConnectionsFailed == 1` without synchronizing on the failing connection's completion; observed
1/33 full-suite runs (`Expected 1, Actual 0`, `MultiClientTests.cs` line 78).

`Q-B5B-09` (release-grade hardening, including the abortive-close delivery limitation) remains
open. TS1 does not close production qualifications.

## 4. Baseline reproduction

Full record and environment: `docs/reviews/evidence/B5-B-TS1/00-baseline-summary.txt`.

- **Terminal pipelined pair, 10 fresh processes: 7 failed.** All seven were the same exception:
  `IOException` wrapping `SocketException` (`SocketError.ConnectionReset`, WSAECONNRESET) raised
  in `BoltMessageReader.ReadPayload` while reading the in-flight FAILURE. No wrong response, no
  unexpected message, no provider execution, no hang.
- **Multi-client counter test, 20 fresh isolated processes: 0 failed.** Consistent with the
  history: the race is load-sensitive (1/33 under full-suite parallelism). It was therefore
  characterized from the production ordering, not manufactured with artificial delays.

## 5. Terminal-response race analysis

Three distinct properties were separated:

| Property | Owner | Observable | Guarantee |
| --- | --- | --- | --- |
| **A — protocol response generation** | B3-B session | direct session result / diagnostic event | exactly one FAILURE with `08N06`, `Neo.ClientError.Request.Invalid`, Defunct |
| **B — server-side response write** | transport | `MessageProcessed` event is reported **after** `WriteResponsesAsync` (write + flush) | the accepted response was handed to the OS before the terminal close begins |
| **C — client-visible delivery** | OS + client | client read | can be suppressed by RST when unread pipelined request bytes remain |

The old tests required A + B + C simultaneously in a scenario specifically designed to leave
unread input in the server's receive buffer. C is exactly the property that the retained
`OWNER-F01` / `Q-B5B-09` limitation says is not universally guaranteed. The tests asserted a
stronger contract than the accepted one.

A completed stream write (B) is not proof of remote application consumption (C); the correction
keeps both assertions but in separately scoped tests.

## 6. Revised terminal-response design

**Standalone deterministic oracle (C required).**
`UnsupportedCommand_FailurePayloadIsByteIdenticalToTheAcceptedSessionEncoding` sends BEGIN alone —
no pipelined tail, therefore no unread request input and an orderly FIN close. It now requires:

1. the complete FAILURE is client-visible;
2. the payload is byte-identical to the locked accepted B3-B encoding;
3. the next read is a clean end of stream (`null`) — **not** a reset;
4. no second message exists;
5. the connection reaches `ConnectionsActive == 0`.

The existing standalone theory (`BEGIN`/`COMMIT`/`ROLLBACK`/`ROUTE`) also reads the decoded
FAILURE and requires terminal close, zero provider operations, zero failed connections.

**Pipelined terminality (A + B authoritative; C characterized).**
The two pipelined tests still send the full tail in one write. They now:

1. read the FAILURE when it is delivered and assert the exact status/code/description;
2. accept **only** a characterized RST (`SocketError.ConnectionReset` or
   `ConnectionAborted`) as the alternative — clean EOF before a response, timeouts and every
   other I/O fault are rethrown and fail the test;
3. await the `MessageProcessed` event filtered to the terminal signature and assert
   `responses=1`, `state=Defunct`;
4. await the `ConnectionCompleted` event and assert `detail="closed"` (terminal refusal is not a
   transport fault);
5. assert exact accounting: `MessagesProcessed == 3` (HELLO, LOGON, terminal message only),
   `PrepareCount == 0`, `ConnectionsAccepted == 1`, `ConnectionsCompleted == 1`,
   `ConnectionsFailed == 0`, `ConnectionsActive == 0`;
6. when the FAILURE was delivered, additionally require close and no further message.

The reset branch is admissible only because the intentionally pipelined unread tail is known,
the server-side terminal refusal is independently established, no later request executes, the
connection is retired, and the exception is exactly the characterized abortive-close signature.

## 7. Pipelined terminality and negative controls

The revised tests still fail on every protocol error the originals detected:

| Error condition | Detected by |
| --- | --- |
| No accepted session refusal for BEGIN | `MessageProcessed` probe timeout (pipelined); standalone read failure |
| Wrong GQL status / Neo4j code / description | `AssertFailure` in the standalone theory and in the delivered pipelined branch |
| FAILURE replaced by SUCCESS | `Assert.IsType<FailureResponse>` in all refusal tests |
| Fabricated RECORD/SUCCESS after terminal refusal | `Assert.Null(TryReadAnyFinalMessage)` when delivered; first-read type otherwise |
| Provider operation opens | `PrepareCount == 0` |
| Pipelined RUN executes after terminality | `MessagesProcessed == 3` and `PrepareCount == 0` |
| RESET revives a DEFUNCT session | event `state=Defunct`, `MessagesProcessed == 3`, `ConnectionsActive == 0` |
| Connection remains active | `ConnectionCompleted` probe + exact counters |
| Unrelated transport exception | completion `detail="closed"` + `ConnectionsFailed == 0`; reset classifier accepts only reset/abort |
| Standalone refusal not client-readable | standalone tests read without catching |
| Incorrect terminal response bytes | locked hex assertion |
| Inconsistent connection accounting | exact accepted/completed/failed/active assertions |

Malformed-input behavior is unchanged by TS1: the existing malformed/unknown-signature tests
still assert the terminal protocol-violation FAILURE, non-recoverability, no provider operation,
and the pipelined malformed test retains the no-tail-execution checks. Framing-level behavior was
not modified.

## 8. Multi-client counter race analysis

The old assertion sequence was:

```csharp
bad.SendRaw(...); bad.Dispose();
Assert.Equal("closed", bad.WaitForClose(5_000));   // observes the client's own disposed socket
good.SendRequest(PULL); Assert.Equal(2, good.ReadResult().Length);
Assert.Equal(1, server.Diagnostics.ConnectionsFailed);   // unsynchronized with the bad task
```

`TestBoltClient.WaitForClose` uses `Socket.Poll` and maps every exception (including
`ObjectDisposedException` after local disposal) to `"closed"`. It therefore returned immediately
after `bad.Dispose()` and provided **no** synchronization with the server.

`ConnectionsFailed` is incremented only in `BoltTcpConnection.RunAsync`'s `finally`, after the
handshake failure propagates and session cleanup completes, on the bad connection's task. Under
full-suite load, the good client's PULL round trip could finish before that accounting, so the
assertion could observe `0` while the bad connection had in fact failed.

## 9. Synchronization correction

The revised test subscribes to the server's `ConnectionFailed` and `ConnectionCompleted`
diagnostic events **before any client connects**. These events are raised synchronously by the
connection task after the counters are updated, so they are the authoritative lifecycle signal.
The test awaits:

1. `ConnectionFailed` with `detail="handshake:BoltHandshakeFormatException"` — the invalid bytes
   are rejected by the B2 handshake, not by framing (the old comment said "framing garbage");
2. exact counters at that point: accepted 2, failed 1, completed 0, active 1;
3. the good client's PULL still completes (2 responses);
4. explicit disposal of the good client, then `ConnectionCompleted` with `detail="closed"`;
5. final exact counters: active 0, completed 1, failed 1, accepted 2, capacity-rejected 0,
   messages 4 (HELLO, LOGON, RUN, PULL).

No `Task.Delay`, no fixed sleeps, no retry loop, no weakened inequality. The bounded wait is
`Task.WaitAsync(10 s)` and reports actual counters on timeout.

The shared helper added to `TransportTestSupport` is deliberately narrow: `ProbeEvent` (one-shot
event subscription with an optional predicate) and `AwaitEventAsync` (finite deadline with
counter diagnostics). It is used only by these two fixes.

## 10. Changed files

| File | Change |
| --- | --- |
| `tests/BoltSharp.Tests/Transport/UnsupportedCommandRefusalTests.cs` | standalone byte oracle strengthened with clean EOF/no second message; pipelined tests separated into authoritative server-side terminality + characterized delivery; strict reset classifier |
| `tests/BoltSharp.Tests/Transport/MultiClientTests.cs` | event-driven lifecycle synchronization and exact counter assertions |
| `tests/BoltSharp.Tests/Transport/TransportTestSupport.cs` | narrow event-probe/wait helper |

No `src/`, project, package, solution, decision, report, probe, historical evidence or fixture
changes. The previously observed unrelated whitespace in `BoltMessageFixtures.cs` was not touched.

## 11. Assertion-strength audit

| Test / assertion | Original | Issue | Revised | Guarantee retained / strengthened |
| --- | --- | --- | --- | --- |
| `UnsupportedCommand_FailurePayload...` | one message read + exact hex | did not assert exactly one response or close | exact hex + `Assert.Null(ReadFramedMessage())` + active=0 | byte oracle + exactly-one + orderly close (stronger) |
| `PipelinedUnsupportedCommand...` | unconditional FAILURE read, WaitForClose, no second message, PrepareCount, messages=3, active=0 | required client delivery under an abortive-close scenario | conditional delivery + strict reset classifier + terminal event (`sig=0x11`, `responses=1`, `state=Defunct`) + completion event + exact counters | protocol result, write, terminality, no-execution and accounting all still asserted; delivery asserted whenever observable and separately required in the standalone test |
| `PipelinedMalformedMessage...` | same shape | same | same, with `sig=0x10` | same |
| `TryReadAnyFinalMessage` | swallowed any `IOException`/`ObjectDisposedException` | could hide unrelated faults/timeouts | accepts only reset/abort; clean EOF returns `null`; everything else propagates | stricter |
| `FailureInOneClient_IsolatedFromOthers` | `WaitForClose` (tautological) + unsynchronized `ConnectionsFailed == 1` | race and inaccurate failure classification comment | server events + exact counters + pinned handshake failure class + final completed/active accounting | stronger and deterministic |

No assertion was removed because its guarantee is false. The one removed assertion
(`bad.WaitForClose`) asserted only the test's own disposal and was replaced by the authoritative
failure event with a pinned classification. All other original assertions remain, several with
tighter expectations.

## 12. Repeated targeted results

Full per-run records: evidence files `01`–`04`.

### A. Terminal-refusal suite — 100 independent fresh processes

Filter `FullyQualifiedName~UnsupportedCommandRefusalTests` (10 tests: standalone BEGIN/COMMIT/
ROLLBACK/ROUTE theory, byte oracle, malformed, unknown signature, two pipelined tests).

**Result: 100/100 clean; 0 failed; 0 skipped; every run reported 10/10.**

### B. Multi-client suite — 100 independent fresh processes

Filter `FullyQualifiedName~MultiClientTests` (6 tests).

**Result: 100/100 clean; 0 failed; 0 skipped; every run reported 6/6.**

### C. Full core suite — 30 fresh processes

**Result: 30/30 complete passes at 1,227/1,227; 0 failed; 0 skipped.**

### D. Official Neo4j driver suite — 3 fresh processes

**Result: 3/3 at 41/41; 0 failed; 0 skipped.**

These counts describe this environment and these runs. They do not mathematically prove the
absence of races, and no universal determinism is claimed.

## 13. Regression results

Freshly executed on the committed test tree:

| Suite | Result |
| --- | --- |
| Core | **1,227/1,227** (and 30/30 repetitions) |
| Transport filter | **90/90** |
| B3-C backend filter | **94/94** |
| B3-B sessions filter | **119/119** |
| B3-A messages filter | **98/98** |
| Official `Neo4j.Driver 6.4.0` | **41/41** (3/3 repetitions) |
| Historical B0-B driver integration | **19/19** |

No previously passing test was removed or disabled. Test counts are unchanged (TS1 adds no
tests; the standalone oracle was strengthened in place).

## 14. Build and formatting

- Production build: **0 warnings, 0 errors**.
- Solution build: **0 errors**, exactly the two pre-existing `xUnit2012` advisories in
  `BoltRequestDecodeTests.cs` (unchanged, historical).
- Scoped `dotnet format --verify-no-changes` on the three modified test files: **exit 0**.
- Working tree clean after all commands (evidence run).

## 15. Historical evidence integrity

Verified read-only; no historical file was rewritten:

- Canonical 17-manifest aggregate: **619/619**, zero mismatches.
- Two excluded B3-B implementation-source manifests (outside the aggregate by design):
  36 entries total — 31 unchanged entries verified against the current tree, 5 changed entries
  verified as byte-exact blobs at their recorded implementation commits
  (`9f224cf4...` and `454ae005...`).
- **No historical manifest references any file changed by TS1**, so the test edits cannot alter
  historical evidence hashes.
- All six accepted owner decisions remain byte-identical to their committed blobs
  (B3-A `ab84ffa9…`, B3-B `211a5d56…`, B3-C `323303bd…`, B5-A `385c820e…`, B5-B `7d23b6bb…`,
  B5-B-F01-R1 `67f2846e…`).
- Credential hygiene: the new evidence package contains no credentials, certificates, keys or raw
  payload dumps; marker scan returned zero findings.

## 16. New findings

**`B5-B-TS1-N01` — diagnostic event observation ordering (test-infrastructure, non-blocking).**
`MessageProcessed` is reported after the response write/flush, but the client can read the
response before the server thread reports the event. During development, a naive "next
`MessageProcessed`" probe captured the preceding LOGON event (`sig=0x6A responses=1 state=Ready`)
and failed a signature assertion. The probes now filter on the terminal message signature; the
contract itself is correct and unchanged. Observed once during implementation; 20/20 combined
runs and 100/100 campaign A clean after the filter. No production impact, no diagnostic API
change.

**`B5-B-TS1-N02` — mislabeled failure class in a test comment (documentation, non-blocking).**
The invalid bytes in `FailureInOneClient_IsolatedFromOthers` are rejected during the B2 handshake
(`handshake:BoltHandshakeFormatException`), not by the framing layer as the old comment implied.
The comment is corrected and the actual classification is now pinned. No runtime impact.

No blocking findings.

## 17. Remaining qualifications

Unchanged and binding:

- `Q-B5B-09` — release-grade hardening, including the abortive-close/client-visible delivery
  limitation; TS1 makes the tests faithful to it, it does not remove it.
- `Q-B5B-04` — generic `50N42` error classification (F02 refinement optional, separately
  authorized).
- `Q-B5B-05` — same-connection RESET recovery after IGNORED not established.
- `Q-B5A-06` — blocked-write backpressure.
- `Q-B5B-06`/`Q-B5A-02`/`Q-B5A-03` — TLS qualifications.
- `Q-B5A-07` — session-options passthrough; `Q-B5A-05` slow-provider behavior; `Q-B5A-10`
  shutdown edges; `Q-B5A-09` no performance claims.
- B3-B failed-retirement DEFUNCT guarantees, single-owner processing, exactly-once disposal,
  bounded PULL, no full RESET jump-ahead.
- B3-C exact-form allowlist, finite reference graph, no writes/general transactions.
- `Q-B5B-03` remains **closed** by the F01-R1 owner decision; TS1 does not reopen or extend it.

TS1 does not change production behavior, so it cannot discharge a production qualification.

## 18. Independent qualification handoff

Recommended separate task: **B5-B-TS1-Q1 — Independent Regression Stability Qualification**, in a
separate execution session with a different model. The reviewer should verify:

1. both owner findings are real and correctly characterized;
2. the standalone test still requires client-visible, byte-exact FAILURE delivery;
3. the pipelined tests do not accept arbitrary resets, EOFs or timeouts;
4. no post-terminal operation can execute and no fabricated response can appear;
5. the multi-client test waits on the authoritative lifecycle event, not local disposal;
6. no sleeps, blanket retries or weakened inequalities were introduced;
7. the change is test-only and scoped to the authorized files;
8. repetition counts are reproducible (or explain any environmental variance);
9. official-driver behavior is unchanged (41/41);
10. historical evidence and all six owner decisions remain intact.

Starting points: this report, `docs/reviews/evidence/B5-B-TS1/` (baseline summary, per-run TSVs,
builds, integrity), and the implementation commit `2e0b9da`.

## 19. Commits and final repository state

| Stage | Commit | Tree |
| --- | --- | --- |
| Starting HEAD | `17886a5e7f709ef1e01624e49c9d90b50808566c` | `e7d3be41483ab7aa22b51b27c619d7f211d8520b` |
| Test stabilization | `2e0b9da0b8920a2f6cdd7de9fa968891d779090e` | `3fbdbebf21aacd545fd3384c289c649e8c7ab75b` |
| Evidence (this report + package) | `docs(boltsharp): record B5-B TS1 stability qualification evidence` | recorded in the final handoff |

Parent of the implementation commit is the starting HEAD. Branch `main`; one worktree; no rebase,
reset, history rewrite or push. Final hashes, changed-path inventory and cleanliness are recorded
in the final handoff.
