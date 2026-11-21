# Q-B5B-09-INV-01 — Production Graceful-Close and Input-Drain Investigation

**ADDITIONAL INVESTIGATION REQUIRED**

2026-11-16 (Asia/Manila). Carry forward candidate B, send-side shutdown followed by a
bounded input drain, to an isolated .NET transport prototype. It improved terminal response
receipt for queued tails. A peer that continued writing and delayed reading still lost the
response on Windows. The TLS close path, .NET cancellation, admission pressure, and
official-driver behavior with the candidate need direct evidence before production implementation.

## 1. Repository identity and accepted scope

| Preflight | Verified |
| --- | --- |
| Canonical checkout and branch | D:/projects/bolt-sharp, main |
| Entry commit | 29d521cea9e65fe2d240421156454444e0d055ea |
| Entry tree | 5063e16258354ecebd3587ac2d7e38bdd75d7e5a |
| Local origin/main | 79f7965a0e8e351baf2dd388218290804c31389a |
| Entry divergence and worktree state | 0 behind, 5 ahead, clean, one worktree, zero stashes |
| Verified parent chain from HEAD | 29d521c → b196db8 → d8c28a9 → 2e0b9da → 17886a5 → 79f7965 |
| Owner decisions | TS1 owner package passed 5/5 blob, parent, inventory, and scope checks. Earlier accepted owner record blobs were compared across the TS1 chain. |

The commission uses the [B5-B owner decision](../decisions/B5-B-OWNER-01-owner-acceptance.md),
[F01-R1 owner closure](../decisions/B5-B-F01-R1-OWNER-01-owner-closure.md),
[TS1 implementation](B5-B-TS1-implementation.md) and
[producer manifest](evidence/B5-B-TS1/manifest.json),
[Q1 qualification](B5-B-TS1-Q1-qualification.md) and
[Q1 evidence](evidence/B5-B-TS1-Q1/README.md), and
[TS1 owner closure](../decisions/B5-B-TS1-OWNER-01-owner-closure.md).
The [repository contract](../boltsharp-spec.md), [compatibility target](../compatibility-target.md),
and [listener shutdown precedent](B0-A-TRACE-R1-listener-shutdown-remediation.md)
govern the proposed scope. The B0-A precedent concerns its probe listener. Production
ownership and shutdown conclusions below come from the current production source.

TS1 and Q-B5B-03 remain **CLOSED**. Q-B5B-04, Q-B5B-05, and Q-B5B-09 remain
**OPEN**. All other inherited qualifications retain their prior dispositions. The commit
contains only this report and new investigation evidence. Production implementation and
release acceptance require separate authorization.

## 2. Current production path and ownership

1. After negotiation, the connection task exclusively owns the Bolt session. The B2 reader
   returns one complete unchunked payload. Session ReceiveBytes owns request decoding and
   classifies the recognized unsupported command.
2. FailTerminal attempts operation retirement, sets Defunct, and returns a single FAILURE
   with ShouldClose. Defunct is assigned before the response can reach the transport write.
   The accepted refusal contains GQL status 08N06, Neo.ClientError.Request.Invalid, the
   accepted unsupported-command description, and an empty diagnostic record. The standalone
   TS1 test pins its full encoded payload byte for byte.
3. WriteResponsesAsync uses the B3-A codec and B2 chunk writer, then writes and flushes the
   active stream under the linked write timeout. The active stream is NetworkStream or
   SslStream. MessageProcessed is reported after the awaited write and flush, with one
   response and Defunct. The terminal branch returns without another frame read or session call.
4. RunAsync holds the accepted TcpClient in a using declaration. Its finally block
   disconnects/disposes the session and reports one completed or failed event. The outer
   using scope then disposes the client and its network stream. The terminal event therefore
   precedes final TcpClient disposal. The TLS wrapper uses leaveInnerStreamOpen=false and is
   explicitly disposed on authentication failure. After a successful TLS handshake the
   current terminal branch has no explicit TLS closure alert or TLS shutdown await.
5. The listener tracks the per-connection task until it settles. StopAsync cancels the
   listener shutdown token, stops accepts and waits on the task snapshot up to the configured
   shutdown timeout. Its removal of tracked entries after the wait is bookkeeping. Socket
   release after timeout depends on the connection task honoring cancellation. A drain must
   stay under this task's ownership and terminate promptly when signaled.

See [session source](../../src/BoltSharp/Sessions/BoltConnectionSession.cs),
[connection source](../../src/BoltSharp/Transport/BoltTcpConnection.cs),
[server source](../../src/BoltSharp/Transport/BoltTcpServer.cs), and
[server options](../../src/BoltSharp/Transport/BoltTcpServerOptions.cs).

The application can prove response generation and serialization. Local write completion
shows acceptance by the stream and, for plaintext, the local socket stack. TCP delivery and
peer application receipt are separate observations. TCP acknowledgment, where available,
establishes delivery to the peer TCP implementation. Only the peer application's read or
an explicit application acknowledgment establishes consumption. The terminal Bolt exchange
has no acknowledgment for this FAILURE. Unread receive data at disposal can trigger a
reset, which can suppress the response or appear after the peer reads it. EOF observed by
an application describes the end of its receive stream.
[RFC 9293 §3.6](https://www.rfc-editor.org/rfc/rfc9293.html#section-3.6).

## 3. Experimental evidence

Historical Bolt reproduction comes from independent Q1's isolated pre-TS1 revision. The
old pair lost the FAILURE before client observation in five of ten runs, with a reset.
The accepted non-pipelined oracle supplied the exact Bolt FAILURE byte check. The driver
control verified its client-visible classification. This commission ran a new protocol-agnostic transport reproduction and a fresh
archived driver baseline. The synthetic harness serves a fixed marker on two endpoints
it creates in-process. Its partial and malformed tails model opaque byte sequences.
The recorded marker hex, terminal flag, and counts are harness observations. Actual Bolt
FAILURE bytes, Defunct and production accounting are supported by the pinned Bolt evidence.
[Q1 baseline](evidence/B5-B-TS1-Q1/09-baseline-reproduction.tsv) ·
[Q1 oracle findings](B5-B-TS1-Q1-qualification.md#6-oracle-integrity) ·
[New experiment and raw evidence](evidence/Q-B5B-09-INV-01/README.md).

The fixed experiment ran 280 groups and 380 connections on each platform (760 total).
Windows 11 build 26200 used CPython 3.14.8. Ubuntu WSL2 used Linux
6.6.114.1-microsoft-standard-WSL2 and CPython 3.14.4. Every group finished with zero
active connections and balanced accepted/finished counts. Measured retention is elapsed time
from the terminal marker to local socket disposal. The per-group deadline, campaign deadline,
scratch bytes, actual read totals, observed response hex, exact-one marker check and peer
EOF/reset outcomes are in the package. No packet capture or kernel memory measurement was used.

| Condition | Windows observations | WSL2 observations |
| --- | --- | --- |
| No unread bytes, five per mode | Exact marker and EOF in every run. | Exact marker and EOF in every run. |
| 32-byte tail, partial opaque tail, malformed opaque tail | Immediate close lost the marker and reset in all 15. B, C and D delivered it with EOF in all 15 each. | Immediate close delivered the marker but reported reset in all 15. B, C and D delivered it with EOF in all 15 each. |
| 24 KiB queued tail | A and C lost the marker and reset (5/5 each). B and D delivered it with EOF (5/5 each). B and C consumed 8 KiB, then closed. | A and C delivered the marker followed by reset (5/5 each). B and D delivered it with EOF (5/5 each). The same byte cap applied. |
| Ongoing writes while reads are delayed | B reached its deadline in every run, with up to 6 KiB drained. The peer observed an abort and no marker in 5/5. A, C and D also lost the marker in 5/5 each. | B hit the byte cap in every run, with exact marker and EOF in 5/5. A, C and D also delivered the marker in 5/5 under this timing. |
| One-byte pause and peer that never sends FIN | B delivered the marker in both cases. The peer that never sent FIN consumed the timer budget (5/5). | Same B outcomes. |
| Early close, explicit peer reset, and peer that never reads | There is no application receipt measurement when the peer closes before reading or stays unread. Explicit peer reset interrupted the server write in all modes. B reached the deadline for all five nonreading peers. | Same observations. The small marker fits the socket buffers, so the test does not establish behavior with send backpressure. |
| Cancel during drain or stop listener during drain | B and C exited via cancellation in all ten trials per mode, then balanced counts. A and D had no drain phase. | Same outcomes. This exercises the harness control, not .NET StopAsync. |
| Six concurrent connections × five groups | B, C, D delivered 30/30 with EOF. A delivered 0/30 and reset. | B, C, D delivered 30/30 with EOF. A delivered 30/30 followed by reset. |

The B lab limits were 75 ms, 8 KiB discarded and 2 KiB of application scratch. Largest
observed B retention was 89.932 ms on Windows and 76.166 ms in WSL2. The deadline is a
cancellation trigger. Scheduler and I/O completion latency add to it. Windows and WSL2
loopback sample different TCP stacks. Native Linux and non-loopback behavior require
separate qualification. [Full per-cell summary](evidence/Q-B5B-09-INV-01/matrix-summary.txt).

## 4. Candidate comparison

| Candidate | Lifecycle and protocol effect | Delivery evidence and tradeoff | Implementation, cancellation and resources |
| --- | --- | --- | --- |
| A — Immediate close | Existing one-write terminal path and immediate disposal. | Earliest resource release. The Windows queued-tail losses and WSL2 post-response resets retain the qualification. | No new slots or scratch. Keep as comparison and fallback when shutdown, transport fault or drain admission prevents the candidate. |
| B — Send shutdown, then bounded drain | After the final write/flush and Defunct, signal send EOF and discard inbound bytes until EOF, cap, deadline, cancellation or failure. No requests or extra Bolt replies. | Strongest combined result on queued, stalled and concurrent cases. Windows continuous-input case still lost the marker. TLS ordering and flow control require a .NET prototype. | Single owner, cancellable async receives, fixed scratch, hard byte cap, absolute timer and non-waiting admission. Retains the socket within that policy and needs explicit teardown instrumentation. |
| C — Bound raw drain, then close | Write final response, discard inbound bytes with same caps, then dispose without explicit send half-close. | Handles cooperative small tails. Large tails left unread data, producing a Windows loss and WSL2 reset in 5/5 each. Delays EOF for peers waiting to finish after seeing it. | Similar retention and processing cost to B with weaker delivery evidence. Do not pursue as lead design. |
| D — Send shutdown, then immediate dispose | One write and outbound EOF with no application drain. | Delivered the queued-tail markers in this lab. Windows lost the marker in all ongoing-write and one-byte delayed-write trials. Full close gives the peer no time to finish sending. | Minimal socket retention and no drain memory. A useful control in the .NET follow-up. |

Winsock documents that SD_SEND keeps reception available and separates shutdown from
handle disposal. SD_RECEIVE/SD_BOTH can cause reset with pending input. Linger modifies
close behavior. Zero linger forces abort and positive linger can block close or end in
abort. Linger alone has no inbound discard policy or application receipt signal. Linux
documents the distinct unread-data abort condition. Use neither positive linger nor an
unbounded wait as the delivery mechanism.
[Winsock shutdown](https://learn.microsoft.com/en-us/windows/win32/api/winsock/nf-winsock-shutdown) ·
[Winsock closesocket](https://learn.microsoft.com/en-us/windows/win32/api/winsock/nf-winsock-closesocket) ·
[Linux TCP counters](https://origin.kernel.org/doc/html/v5.19/networking/snmp_counter.html#tcp-abort).

## 5. Proposed lifecycle, limits and diagnostics

Use the single connection task for all work. For a narrowly identified, authenticated
unsupported-command refusal, finish the existing response write and preserve Defunct.
Use a session-owned internal classification if needed. Avoid a second decode or a public
API change. Retire session ownership before entering transport-only cleanup. Preserve
current behavior for GOODBYE, handshake/framing errors, authentication failure, write
failure and unrelated terminal reasons. Keeping that initial eligibility narrow reduces
resource cost and compatibility scope.

On plaintext, after a successful write and flush, acquire a drain permit without waiting.
With a permit, issue send shutdown, discard inbound bytes asynchronously with one
fixed-size buffer and stop on peer FIN, byte cap, absolute deadline, shutdown/cancellation,
or socket fault. Without a permit, dispose promptly. Dispose the socket and return the
permit on all paths. Keep draining sockets within the existing MaxConnections limit and
preserve exactly one terminal accounting event. Define and test its ordering relative to
actual handle release and task removal. Keep transport cleanup outcome separate from
whether the terminal Bolt response was generated and written. Never parse the tail into
Bolt frames, execute a provider operation, emit another Bolt response or allow RESET recovery.

TLS requires a separate transport path. Deliver the final application record, then examine
SslStream.ShutdownAsync for a TLS closure alert before any TCP send half-close. Ensure that
TLS read/write ownership and any buffered bytes are handled by the TLS layer. Compare a
bounded TLS-layer discard policy to a documented fallback. Count decrypted bytes and
measure TLS record processing and socket retention independently. Qualify a bound on
ciphertext work and internal buffering as well as the application discard cap. Applying a cancellation
timeout only to an await can leave the underlying I/O live. The prototype must demonstrate
that owner-triggered socket closure or a supported cancellation path releases every operation
and that pending operations are observed and joined. Until TLS is qualified, retain the
existing TLS close behavior and report the resulting coverage limit.
[RFC 8446 §6.1](https://www.rfc-editor.org/rfc/rfc8446.html#section-6.1) ·
[.NET SslStream](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-8.0).

Production limits must be chosen from measured supported-client tail sizes and reply/close
latencies, operating admission headroom, shutdown deadlines, and host CPU/memory capacity.
For the next experiment, retain the measured 8 KiB / 75 ms reference point and sweep
4/8/16 KiB with 40/75/150 ms. Use the smallest pair that meets a declared cooperative-client
delivery target while staying within headroom and shutdown budgets. For example, a 2 KiB
scratch size with up to eight experiment permits caps application scratch at 16 KiB for that
trial. Kernel socket buffers and TLS state are additional. Select a production permit
limit only after measuring normal admission headroom. Bound concurrent work by permits,
bytes per permit and total close time. Measure churn and fallback rate as well as peak
concurrency. These values are experiment parameters.

Record an internal drain started/finished event and aggregate counts by outcome
(peer EOF, byte cap, deadline, canceled, fault, skipped capacity). Include elapsed
time, bytes discarded, current and peak draining sockets, and a correlation ID.
Keep response write/flush and actual handle-release timing separately observable.
Exclude raw payloads, credentials and unbounded exception text. A deadline plus byte
cap addresses trickle and continuous input without timer extension. Immediate admission
fallback limits held slots during repeated terminal connections. Fixed scratch and
cancellable I/O bound allocations and thread residency. Shutdown cancellation must take
priority over the drain budget. The current default MaxConnections is 100 with a
five-second listener wait and a separate 30-second write timeout. Capacity checks and
finish accounting must use actual retained sockets, including on cancellation and faults.

## 6. Official driver and next qualification

A fresh Windows run of four existing Neo4j.Driver 6.4.0 tests from the archived entry
revision passed with zero failures or skips. It covered the accepted BEGIN refusal,
server shutdown with an active driver, fresh-connection recovery, and self-signed TLS
query flow. The accepted F01-R1 evidence supplies the BEGIN ProtocolException /
08N06 / Request.Invalid classification, non-retriable behavior, distinct-connection
recovery, and observed live ROUTE refusal. The driver manual describes pooling and
error-driven retry. Direct measurement is required to establish how the driver
classifies candidate close/reset timing and whether the refused socket is discarded.
[Driver baseline TRX](evidence/Q-B5B-09-INV-01/driver-baseline.trx) ·
[F01-R1 driver scope](../decisions/B5-B-F01-R1-OWNER-01-owner-closure.md#12-official-driver-interoperability-after-correction) ·
[Neo4j .NET driver manual](https://neo4j.com/docs/dotnet-manual/current/transactions/).

**Recommended next commission: Q-B5B-09-INV-02**, proposed for separate authorization.
Create an isolated .NET 8 prototype of B on the existing post-write terminal path, with
A and D as comparisons. Use Windows and native Linux, with WSL2 as a supplementary
comparison. Exercise plaintext and TLS and use a controlled non-loopback link or network
emulator to cover delay and genuine send backpressure. Include an independent reader and
writer as well as the delayed-reader case. Measure exact FAILURE receipt, EOF/reset, latency,
actual socket/worker occupancy and shutdown behavior under bounded concurrent admissions.
Demonstrate the cap and timeout paths, cancellation during a pending write/read/TLS
shutdown, and prompt healthy-client progress when permits are exhausted.

Re-run the existing standalone byte oracle, refusal and multi-client suites, transport and
core regressions, and the pinned driver scenarios for BEGIN/ROUTE, pool eviction, fresh socket
use, retry count, cancellation, and resets. Keep official-driver reachability qualifications
for COMMIT/ROLLBACK. Add controls demonstrating detection of wrong/missing/extra responses,
post-terminal parsing or execution, misclassified EOF/reset, cap overruns, timer reset on
traffic, ignored shutdown, leaked permits/sockets, and double or premature cleanup reporting.
Place the tests in separate qualification scope before any canonical test change. Leave
Q-B5B-04 and Q-B5B-05 independent.

Selection gate for production authorization: demonstrate a feasible TLS-safe lifecycle,
substantial cooperative-client improvement across platforms, bounded retention with
noncooperative peers, and no driver regression. The enforceable guarantees are application
terminality, unchanged response bytes, a single write path and bounded resource policies.
Peer receipt remains conditional on TCP and peer behavior. The observed Windows failure with
ongoing input, the small lab sample, lack of native Linux/non-loopback tests and absent
driver execution against B are the open evidence gaps.
