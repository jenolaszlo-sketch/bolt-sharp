# Q-B5B-09-PLAIN-IMPL-01 — Plaintext Terminal Graceful-Close Implementation Specification

Authority: [Q-B5B-09-OWNER-01](Q-B5B-09-OWNER-01-production-remediation-scope-decision.md).
This specifies the subsequent production commission. Q-B5B-09 remains open.

## Authorized files

| Change | Repository path | Scope |
| --- | --- | --- |
| Modify | src/BoltSharp/Sessions/BoltConnectionSession.cs | Mark the existing authenticated unsupported-command terminal outcome. Preserve exact FAILURE generation, operation retirement and Defunct transition. |
| Modify | src/BoltSharp/Sessions/BoltSessionResult.cs | Carry eligibility in internal result metadata and preserve the public shape and factories. |
| Modify | src/BoltSharp/Transport/BoltTcpConnection.cs | Gate entry on plaintext, propagate deadlines and cancellation, await one write and preserve accounting. |
| Modify | src/BoltSharp/Transport/BoltTcpServer.cs | Supply per-listener policy and permit ownership, coordinate new phase with admission and shutdown, and separately record the typed linger correction. |
| Add | src/BoltSharp/Transport/BoltPlaintextTerminalClose.cs | Internal bounded lifecycle and validated policy. |
| Add | src/BoltSharp/Transport/BoltPlaintextTerminalCloseDiagnostics.cs | Bounded internal lifecycle observations. |
| Add | tests/BoltSharp.Tests/Transport/PlaintextTerminalCloseTests.cs | Integrated plaintext oracle, deadline, backpressure, capacity and disposal. |
| Modify | tests/BoltSharp.Tests/Transport/TcpListenerTests.cs | Plaintext startup and typed linger readback. |
| Modify | tests/BoltSharp.Tests/Transport/TlsIntegrationTests.cs | TLS startup and new-path exclusion. Preserve existing assertions. |
| Add | tests/BoltSharp.DriverInteropTests/Scenarios/PlaintextTerminalCloseInteropTests.cs | Driver refusal, replacement, TLS neutrality and shutdown. |

Producer records and evidence may be added under docs/reviews and
docs/reviews/evidence using Q-B5B-09-PLAIN-IMPL-01 naming. Reuse existing test
helpers. Keep variant-only probes and negative-control edits isolated. The
table is the persistent source/test allowlist. Preserve existing TS1 test
files, projects, packages and public APIs. BoltTcpServerOptions retains its
public contract. A further persistent file requires a separately recorded
scope decision.

## Transport selection and terminality

Select the connection mode explicitly from listener configuration. Only a
configured plaintext connection with its owned cleartext stream/socket may enter
the new helper. The existing plaintext loopback bind restriction applies.
TLS connections retain the current authentication, encryption, security defaults,
close, stream/socket ownership and disposal. They never enter the plaintext
helper, acquire a drain permit, or use raw socket draining. Keep the already
accepted TLS integration contracts and availability, including fail-closed
behavior with no plaintext downgrade.

Within plaintext, use session-owned eligibility for the existing authenticated
unsupported-command refusal. Current eligible pre-refusal states are Ready,
Streaming and authenticated Failed. The session follows its established
operation-retirement path, enters Defunct and returns exactly one existing
FAILURE before marking that result eligible. Use the existing response codec,
metadata, encoding and framing. Eligibility stays at the session boundary.
Avoid transport-side signature parsing or re-decoding. Preserve behavior for
GOODBYE, handshake/framing and authentication failures, unrelated terminal
results and ordinary transport errors.

## Deadline, ownership and fallback

1. After the classified terminal result and before serialization, establish
   one absolute monotonic deadline. This branch retires the Bolt reader for
   the connection. Use the existing writer to serialize and await one
   terminal write and flush. Clamp its timeout to the smaller of WriteTimeout
   and remaining total time.
2. Link the listener shutdown token and an owning connection cancellation
   signal. Use an internal owner signal where the existing public API lacks a
   per-connection token. Detect reset through the active I/O path. Avoid a
   second receive loop. Use the actual completion of pending I/O to decide
   when it is safe to release ownership.
3. After a successful write and flush, check remaining time and cancellation.
   Try to take one listener drain permit without waiting. If admitted, perform
   send-side shutdown and discard inbound transport bytes with a single
   fixed scratch buffer. Limit each read to the remaining byte budget and
   scratch capacity. Apply the lesser of a finite drain timer and remaining
   total time. Never feed drained bytes to a codec, parser, session, RESET
   path or provider. Traffic and phase changes never renew the timer.
4. The per-connection owner interrupts pending I/O when needed, observes
   its actual task completion, and disposes the socket and stream on all
   paths. Make disposal idempotent across races. Release the drain permit
   exactly once after handle disposal. Retain the existing connection
   admission slot through disposal and task retirement. Record exactly one
   terminal accounting transition, preserving the existing behavior for
   unrelated paths.
5. Listener stop ends admission, signals accepted owners, and observes
   completion within the configured shutdown policy. Verify that a
   concurrently accepted owner cannot escape cleanup or accounting.
   Keep the existing synchronous session operation-retirement contract.
   The new I/O deadline covers serialization through teardown after the
   classified result. A scheduled deadline initiates cancellation, with
   OS and scheduling latency measured separately. An arbitrary synchronous
   provider call remains outside the timer's preemptive control.

| Exit | Required behavior |
| --- | --- |
| Write/flush incomplete or faulted | Interrupt and observe pending work, dispose promptly, and skip drain and additional responses. |
| Per-connection cancellation | Propagate cancellation, join active work, dispose, and release any permit. |
| Drain byte, phase or total time limit | End reads, report the actual exit cause, dispose, and return any permit. |
| Drain capacity unavailable | Skip waiting and further reads, dispose promptly, and report capacity fallback. |
| Peer reset | Classify the transport outcome, join and dispose, and preserve accepted disconnect accounting. |
| Listener shutdown | Signal owner cancellation, join active work, dispose and release any permit. Demonstrate interruption before an independent deadline could have released the write or drain. |

Half-close and local write/flush completion describe transport progress. TCP
resets, external network conditions and the peer determine application receipt.
Peer receipt is outside the implementation guarantee.
[RFC 9293 §3.6](https://www.rfc-editor.org/rfc/rfc9293.html#section-3.6) ·
[.NET Socket.Shutdown](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.shutdown) ·
[.NET NetworkStream.WriteAsync](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.networkstream.writeasync).

## Internal limits and diagnostic contract

Keep policy internal, finite and immutable per listener. Preserve the
existing public WriteTimeout, ShutdownTimeout, MaxConnections and transport
defaults. Use no positive linger, blocking waits, unbounded retries, detached
drains or artificial delay. Keep established connection accounting separate
from internal terminal-phase observations.

| Policy | Qualification starting point and production decision |
| --- | --- |
| Absolute deadline | 300 ms was the prototype trial setting. Compare bounded alternatives as needed and justify the smallest sufficient production proposal against observed queued-tail benefit, completion latency and existing timeout settings. |
| Drain sub-budget | 75 ms was the prototype trial setting. Clamp it to remaining total time. Compare delivery improvement against retained occupancy and fallback. |
| Drain and scratch allocation | Start with 8 KiB total discard and 2 KiB fixed scratch per admitted drain, with reads bounded to remaining bytes. Account separately for response framing and kernel buffers. |
| Concurrent drains | Two slots exercised bounded capacity fallback. For qualification, use up to two per listener clamped to MaxConnections minus one, with zero drains when MaxConnections is one. This keeps the drain budget from consuming every configured slot by itself. |
| Aggregate resources | With two trial slots, discard scratch is at most 4 KiB per listener. Admitted drains remain inside MaxConnections. Measure real handle and permit retention, healthy-client progress, and implications for multiple listeners. |

These are **qualification inputs**. The owner has not approved 300 ms or two
permits as production defaults. The producer must explain proposed operational
values, measured tradeoffs and any residual release limit for independent
review. The prototype's bounded lab holds and admissions support the mechanism,
with representative production traffic characterization still required.
[INV-02 resource limits](../reviews/Q-B5B-09-INV-02-investigation.md#4-resource-limits-and-remaining-tls-questions) ·
[INV-03 measured resource results](../reviews/Q-B5B-09-INV-03-investigation.md#resource-and-cancellation-campaign).

Preserve ConnectionAccepted, MessageProcessed, ConnectionCompleted and
ConnectionFailed semantics. Add bounded internal events for write start and
completion/failure, send shutdown, drain admission/start/end, EOF, byte/time
limit, capacity, reset, owner cancellation, listener stop and actual socket
disposal. Record a connection correlation ID, numeric elapsed time and byte
count, and a bounded reason code. Attribute the signal that actually released
the await. MessageProcessed remains evidence of local write/flush completion.
Exclude payloads, discarded bytes, credentials, query text and sensitive peer
data from diagnostics.

## Separately traceable portable linger prerequisite

Prepare a separately reviewable commit for the listener startup correction
and its related tests. Replace the boolean SO_LINGER call with the typed
disabled setting LingerState = new LingerOption(false, 0). Verify equivalent
intended disabled semantics on listener and accepted sockets, and run
plaintext and TLS startup/stop on Windows and WSL2 production sources.
Record Windows success and the original WSL2 EINVAL 22. Document the typed
change explicitly, including failure behavior and the native Linux release
gate. The corrected setting does not promise FIN or peer receipt with unread
input. [INV-03 portability evidence](../reviews/Q-B5B-09-INV-03-investigation.md#socket-linger-and-native-linux) ·
[.NET LingerState](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.lingerstate).

## Required producer evidence

| Requirement | Completion evidence |
| --- | --- |
| Wire protocol and queued-tail improvement | The unchanged standalone byte-exact TS1 test passes. Archive baseline/candidate comparisons on Windows and WSL2 for cooperative queued tails, plus adverse-peer observations. Assert one terminal FAILURE, Defunct, zero post-terminal parser/provider calls, and no extra response. |
| Genuine plaintext terminal write | Observe actual pending terminal write at the transport, separately from earlier application writes. Verify deadline, owner cancellation and listener stop independently. Require actual await return, timely disposal and balanced accounting. |
| Deadline, bytes and capacity | Verify no renewal across write/flush/drain, bounded elapsed time with reasoned allowance, exact byte cap and fixed scratch, concurrent cap and fallback. Cover slow/stalled/never-closing peers, resets and shutdown during pending write/drain. |
| Socket and permit recovery | Check actual handles, one terminal accounting event per accepted owner, zero leaked slots or permits and healthy-client progress under pressure. |
| TLS exclusion and neutrality | Produce the eligible result over TLS and assert original TLS I/O with zero new plaintext phases, permits or raw reads. Check both startup modes, existing authentication/encryption and teardown, including fail-closed behavior for plaintext on a TLS listener. |
| Integrated regressions | Existing TS1 refusal and multi-client, transport, sessions, messages, backend, full core, TLS and Neo4j.Driver 6.4.0. Confirm refusal classification, physical connection replacement, listener shutdown and preserved contracts. |

Run the integrated controls in disposable copies against the
production-candidate tests. Preserve the unaltered control, every outcome and
verified source restoration between trials.

| Negative control | Required independent detection |
| --- | --- |
| Trailing command execution | Detect actual post-terminal parser or provider entry independently of an extra reply. |
| Socket retention after shutdown | Detect a candidate-owned handle remaining after StopAsync despite any completion counters. |
| Unlinked cancellation | Detect the loss of prompt listener interruption before normal deadline expiry can mask it. |
| Deadline renewal | Detect use of more than the original absolute budget across stages. |
| Drain limit violation | Detect excess bytes or time without parser re-entry. |
| Drain capacity violation | Detect over-admission or permit leakage affecting later admissions. |
| Incorrect transport mode | Detect TLS reachability of the plaintext helper, a TLS drain permit, or any raw TLS-owned read. |

## Independent qualification and release gates

| Gate | Required evidence | Decision boundary |
| --- | --- | --- |
| Fresh reviewer and pinned candidate | Independent session verifies exact implementation/linger commits and trees, reviews contracts, runs its own checks, verifies all historical evidence and qualifies the integrated controls. | Implementation qualification. |
| Windows production code | Independently establish delivery improvement, real plaintext terminal backpressure, deadline/cancellation, ownership recovery, TLS exclusion, both listener startups and regressions. | Implementation qualification. |
| WSL2 production code | Run the candidate with its documented production linger correction. Record runtime and results for both startup modes, plaintext lifecycle, TLS exclusion and driver/transport regressions. | Implementation qualification. |
| Native Linux | Attempt authorized native Linux with the supported runtime. Independently verify typed startup, plaintext lifecycle and cancellation, TLS neutrality and driver behavior. Record separately from WSL2. | Explicit release gate where outstanding. |
| Pinned official driver and TLS neutrality | Compare refusal, connection replacement and shutdown, preserve TLS auth and encrypted behavior, and verify incorrect-mode control. | Implementation qualification. |
| Resource bounds and historical integrity | Independently verify time, byte, permit and handle bounds, failure/negative-control evidence and unchanged historical records. | Implementation qualification. |
| Representative production capacity | Measure actual admission headroom, fallback, memory/kernel/socket retention, CPU and shutdown recovery under sustained representative workloads. Verify healthy clients and justify selected internal defaults. | Explicit release gate where outstanding. |
| Owner disposition | Review independent qualification, capacity/default evidence, native Linux and the scope of any partial acceptance. | Separate owner decision. Q-B5B-09 remains open meanwhile. |

The reviewer handoff pins implementation, linger and evidence commits/trees
and records commands, platforms, budgets, oracle outcomes and residual limits.
Q-B5B-04 and Q-B5B-05 remain independent. Windows TLS terminal backpressure,
pending close_notify, encrypted-byte caps, native Linux TLS behavior and
integrated TLS lifecycle controls remain on the deferred TLS track.
