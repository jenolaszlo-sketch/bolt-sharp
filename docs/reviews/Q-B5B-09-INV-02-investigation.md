# Q-B5B-09-INV-02 — Isolated Graceful-Close Prototype and Qualification

**ADDITIONAL INVESTIGATION REQUIRED**

2026-11-16 (Asia/Manila). The integrated candidate improves exact terminal FAILURE
delivery for cooperative Windows loopback peers with queued tails. Windows peers that
kept writing and delayed reading could still lose the response over both TCP and TLS.
Real backpressure, a cancellation-oracle gap, and an existing Linux listener-startup
failure were also observed. Q-B5B-09 remains **OPEN**.

## 1. Repository and reviewed evidence

| Preflight | Result |
| --- | --- |
| Canonical checkout | D:/projects/bolt-sharp, main |
| Source commit and tree | 23d58da4e0c0e10aa46a31f05ef6a0c31ad3f2fa / bd841c86886587d38276fba2798ef13404ce002d |
| Local origin/main | 79f7965a0e8e351baf2dd388218290804c31389a |
| Divergence and worktree | 0 behind, 6 ahead, clean, one worktree, zero stashes |
| Ancestry, newest first | 23d58da → 29d521c → b196db8 → d8c28a9 → 2e0b9da → 17886a5 → 79f7965, each a sole-parent relationship |
| Accepted records | INV-01 payload 11/11 and TS1 owner payload 5/5 verified against Git blobs. Accepted decision paths were unchanged between the TS1 owner closure and the entry commit. |

Reviewed the [INV-01 investigation](Q-B5B-09-INV-01-investigation.md) and
[comparison evidence](evidence/Q-B5B-09-INV-01/README.md), [TS1 owner closure](../decisions/B5-B-TS1-OWNER-01-owner-closure.md),
[repository protocol contract](../boltsharp-spec.md), [transport/session code](../../src/BoltSharp/Transport/BoltTcpConnection.cs),
and [official driver fixtures](../../tests/BoltSharp.DriverInteropTests).
The [source identity](evidence/Q-B5B-09-INV-02/source-identity.json) pins the full archive
and the four replayed C# source files. The report and evidence are the only canonical additions.

## 2. Integrated candidate

The archived source provided separate baseline and candidate copies. Both used the same
new loopback tests against actual BoltTcpServer, framing, session and transport. The baseline
production files were unchanged for Windows. The candidate patch replayed byte for byte from
the archive after normalizing the affected files to LF. The separate WSL adapter was applied
to both WSL comparison copies.

The Bolt session first retires any owned operation and sets Defunct, then returns the
original one-FAILURE result. Doing this before the write preserves the existing session
ownership contract. Eligibility is an internal session marker for the unsupported-command
refusal in Ready, Streaming, or Failed. The real codec and chunk writer serialize the same
result. The candidate branches after ReceiveBytes and never returns to the Bolt reader or
calls the provider for tail bytes.

- **TCP.** Await the existing write and flush, acquire one of two permits without waiting,
  half-close send, and discard inbound stream bytes with fixed limits. Close on peer EOF,
  cap, deadline, cancellation or transport error.
- **TLS.** Await the existing TLS application write and flush. Await ShutdownAsync to send
  the closure notification. Discard only through SslStream. A counting wrapper limits
  additional encrypted input through the inner stream. It never receives independently.
  Each layer has a byte limit. Bytes already buffered by SslStream can be discarded with a
  reported wire count of zero, as seen in the large-tail case. The prototype distinguishes
  a TLS read returning zero with observed inner EOF from TLS-layer EOF without observed
  inner EOF. That distinction is an observation of these streams, with no packet capture.
- **Ownership.** A linked terminal deadline can dispose the owned TcpClient to interrupt
  pending I/O, including ShutdownAsync, which has no CancellationToken parameter. Await the
  real operation before exiting. Dispose the client before releasing a drain permit.
  The connection task retains the listener admission slot through cleanup. The prototype
  keeps existing terminal event ordering for unrelated framing, handshake and GOODBYE paths.
  The instrumentation logs phases, timings, numeric byte counts and classifications.
  Write and flush completion describe local I/O. Neither TLS close notification nor TCP
  acknowledgment establishes peer application receipt.
  [RFC 9293 §3.6](https://www.rfc-editor.org/rfc/rfc9293.html#section-3.6) ·
  [RFC 8446 §6.1](https://www.rfc-editor.org/rfc/rfc8446.html#section-6.1) ·
  [.NET SslStream API](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-8.0).

## 3. Integrated comparison

One final trial per scenario and transport ran on each variant per platform. Each
34-case run comprises 28 terminal scenarios, two three-peer capacity tests and four
backpressure cases. Passing a case requires its applicable protocol, accounting or
resource assertions. Receipt was measured separately. The standalone byte oracle
remains mandatory. Other cases allow a missing response only as a recorded observation.
The full per-case record includes exact observed payload hex in the TRX, elapsed timings,
Defunct, response count, preparation count and cleanup outcomes.

| Scenario | Windows baseline → candidate | WSL2 adapted baseline → candidate |
| --- | --- | --- |
| Standalone, TCP and TLS | Exact FAILURE in both variants. | Exact FAILURE in both variants. |
| Small, large, partial, opaque malformed, delayed-reader tails, TCP | Baseline lost five replies with reset. Candidate received the exact reply and TCP EOF in all five. | Both variants received the exact reply in all five. |
| Same five tail/reader cases, TLS | Both variants received the exact reply. Client observed TLS-stream EOF in both variants. | Both variants received the exact reply. |
| Peer sends EOF after request and tail | Baseline lost both replies. Candidate received the exact reply with TCP/TLS EOF. | Both variants received the exact reply over TCP and TLS. |
| Peer keeps writing and delays reads | Both variants lost the reply over both transports. Candidate reached its phase deadline with 4,096 TCP bytes and 3,072 TLS application bytes discarded. | Both variants received the exact reply. Candidate hit the 8 KiB byte cap in both transports. |
| Stalled peer sends one trailing byte | Baseline lost both replies. Candidate delivered both; cooperative peer close completed the drain. | Both variants received both replies. |
| Peer never sends EOF or never reads | Candidate used its phase deadline. A reading peer received the FAILURE. A peer that never reads has no application receipt observation. | Same candidate outcomes. |
| Peer closes immediately, resets, or truncates TLS by dropping TCP | Interruption was bounded. Outcomes include early EOF, write/shutdown errors, and reset. No peer receipt measurement after the peer closed. | Same outcome classes. |
| Listener stop during active drain | Both candidate cases reached server cancellation in drain. Measured stop interval was about 14–15 ms. | Both reached server cancellation. Measured stop interval was under 6 ms. |
| Three simultaneous terminal connections | On each transport, two candidate drains were admitted and the third recorded capacity fallback. All three completed. | Same result. |

[Per-case comparison](evidence/Q-B5B-09-INV-02/matrix-observations.txt) ·
[Complete execution records](evidence/Q-B5B-09-INV-02/execution-records.zip).

### Real backpressure

A fixture provider returned a legal finite 4 MiB value in the real PULL path. A local
peer used a 4 KiB receive buffer and paused reads. The integrated harness checked that
the connection stayed active without further MessageProcessed events during an observation
window. Candidate instrumentation captured the pending write and its length.

On Windows plaintext, the PULL response entered the local path and the following
**169-byte terminal FAILURE write became pending**. The candidate observed an expired
terminal deadline at 300.24 ms in the terminal phase and a server-cancelled terminal
write in the stop trial at 138.79 ms from terminal phase entry. Whole fixture elapsed
times were 380.84 and 198.63 ms. The baseline write-timeout trial finished in about
640 ms from the fixture start with the preceding PULL processed and no terminal write
completion. Terminal delivery under these conditions was not established.

With TLS on Windows, and both transports in adapted WSL2, the **4,194,451-byte preceding
response** was pending. The sequential transport had yet to consume the terminal request.
Existing write timeout or listener cancellation retired the connection and operation.
The candidate Windows TLS timeout and stop trials took about 633 and 203 ms from
fixture start respectively. This demonstrates real blocked TLS application I/O and
bounded server cleanup in the preceding write. A pending TLS terminal FAILURE or
close_notify write was not isolated by these runs.

## 4. Resource limits and remaining TLS questions

| Resource | Trial policy and evidence |
| --- | --- |
| Terminal I/O lifetime | 300 ms from entry to the terminal I/O helper, including serialization, write, flush and cleanup phase. The handler starts after synchronous session work. A timer schedules cancellation; elapsed time can exceed it through OS or scheduling jitter. |
| Shutdown and drain | 75 ms sub-budget includes half-close or TLS shutdown and drain, linked to the overall deadline. Never-closing peers exercised expiry. The initial point came from INV-01. Windows no-read drain reached about 89 ms elapsed in the final trace. |
| Discarded bytes | 8 KiB application limit observed on both transports. 32 KiB encrypted input budget applies to newly read TLS bytes during drain. The encrypted-only cap was not reached independently of the plaintext/time limits in the recorded matrix. |
| Memory and admission | 2 KiB scratch per admitted drain, at most two drains per listener (4 KiB application discard scratch). The fixture had MaxConnections=4. TLS buffers, session allocations and kernel buffers are separate. Peak TLS/native heap and descriptor retention under load were not measured. |
| Capacity fallback | Write is subject to the complete deadline even when the drain permit is unavailable. Close promptly with outcome capacity, with no waiting or extra application response. Fallback receipt is best effort, especially for TLS without available alert time. |
| Cancel and cleanup | Linked server cancellation disposes the owned socket, and active/completed counters balance in the positive cases. The server exposes listener cancellation and peer disconnect/reset. There is no separate public per-connection cancellation injection in these tests. |

The prototype limits work for trickle and continuing input without restarting the timer.
Production still needs calibrated budgets from representative link and client traces,
admission headroom measurements, CPU attribution for TLS processing, and a global budget
for multiple listeners if that hosting model is supported. The prototype values are lab
parameters. The eligibility marker excludes unauthenticated terminal failure. Proposal for
the next prototype is to emit bounded, non-sensitive events for write, shutdown attempt,
byte/deadline/capacity exit, peer reset, listener cancellation, and socket disposal.
Use separate counters for drains and closed sockets. A generic transport error and
an already-cancelled listener flag are insufficient proof of which signal ended I/O.

## 5. Tests, platforms and controls

| Campaign | Result |
| --- | --- |
| Windows 11 build 26200, .NET 8.0.31, SDK 10.0.401 | Final integrated baseline and candidate 34/34 each. Baseline core 1,227/1,227, candidate core 1,227/1,227. |
| Existing candidate core suites, within the same full run | Transport 90/90, Sessions 119/119, Messages 98/98, Backend 94/94, terminal refusal 10/10, multi-client 6/6, TLS transport 8/8. Existing TS1 assertions were unchanged. |
| Official Neo4j.Driver 6.4.0, Windows | 41/41 existing baseline and 2/2 new baseline. Candidate 43/43, covering the existing 41 and two new TCP/TLS cases. |
| WSL2 Linux 6.6.114.1, .NET 10.0.14 roll-forward | Exact baseline and candidate 0/34 each. The shared listener startup throws EINVAL on the integer SO_LINGER overload before a connection is accepted. The typed disabled LingerOption adapter was applied in both WSL-only copies. Adapted runs were 34/34 each. |
| Native Linux | No authorized native Linux runtime or host was available. The available Linux environment was WSL2. The evidence contains commands for a native Linux .NET 8 campaign and a separate adapter comparison. |

The official driver saw ProtocolException with 08N06 and Neo.ClientError.Request.Invalid
over both transports. It made one observed BEGIN attempt. After refusal, two successful
queries reused a distinct physical connection. Listener stop ended both connections with
zero active or failed. This is observed wire/lifecycle evidence for replacement and pool
health. The tests did not instrument private pool data structures or exhaustively qualify
driver cancellation, retries under reset, or ROUTE under TLS.

In the initial Windows matrix, a backpressure assertion incorrectly assumed the pending
write was always the large PULL. The final assertion distinguishes the preceding write
from the terminal write. The first candidate telemetry observer emitted no measurements,
and premature socket disposal changed accounting observation on one unrelated framing
test. The final prototype corrected the observer and preserved the old ordering for
unrelated paths. Initial failures and sandbox test-host launch failures are retained.

| Isolated control | Evidence |
| --- | --- |
| Unmodified capacity control | 2/2 pass. |
| Missing, wrong, extra terminal response | Detected in 24/28, 18/28, 18/28 cases respectively. The independent byte oracle remained intact. |
| Non-Defunct result | Detected in 24/28 cases. |
| Overlong phase/lifetime, byte cap, permit cap | Detected in 4/28, 2/28, 2/2 respectively. The overlong-duration control was externally bounded. |
| Ignored listener cancellation | Initial outcome-label oracle missed the unlinked token (28/28 passed). A supplemental measured 65 ms trial bound caught both shutdown cases. Unmodified supplemental control 28/28; unlinked 26/28. Timing is a lab assertion, with scheduler qualification outstanding. |
| Accounting leak | Detected in both capacity cases. |
| Trailing execution; retained socket after shutdown | Positive assertions checked provider count, message count, and balanced completion. Source-level controls that force actual tail execution or retain a socket after StopAsync were not run as standalone mutations. |

## 6. Recommendation and separate next commission

Carry forward the narrow post-refusal transport lifecycle and the plaintext result.
Keep candidate TLS opt-in in isolated testing until a pending TLS terminal or close alert
write has been demonstrated and interrupted under real backpressure. Preserve the
session-owned refusal classification and the exact standalone FAILURE oracle. Separate
portable listener startup from the terminal-close design, and qualify it before
cross-platform production claims.

**Recommended next commission — Q-B5B-09-INV-03**, a bounded follow-up using this patch
as the baseline for design refinement. Require an exact native Linux .NET 8 campaign
with separate typed-linger comparison, a non-loopback controlled latency link, a TLS
terminal/close-notification backpressure case, explicit classification of cancellation
and independently exercised ciphertext limits. Repeat attempts across Windows and
Linux and collect actual TLS/kernel memory, admitted sockets and healthy-client
progress under concurrent drain pressure. Test absolute limits at several candidate
values before choosing production defaults. Qualify official-driver cancellation
and reset handling and include actual post-terminal execution and socket-retention
negative controls.

Production scope, if subsequently authorized, is internal transport/session metadata
and teardown after the existing authenticated unsupported-command FAILURE. Existing
unsupported commands, framing policy, package/public API, retry semantics and
nonterminal paths retain their accepted behavior. Enforce one terminal response,
Defunct, bounded work and no trailing Bolt execution. Peer receipt remains a measured
outcome contingent on the network and peer.

B5-B-TS1 and Q-B5B-03 remain **CLOSED**. Q-B5B-04, Q-B5B-05 and Q-B5B-09 remain
**OPEN**. All other qualifications retain their prior dispositions. The canonical
production source, tests, project/package files, historical evidence and owner
decisions are outside the investigation commit.
