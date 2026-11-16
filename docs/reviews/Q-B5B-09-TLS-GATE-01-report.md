# Q-B5B-09-TLS-GATE-01 — Bounded Real TLS Backpressure Qualification

**TLS GATE UNRESOLVED — OWNER DECISION REQUIRED**

2026-11-16 (Asia/Manila). Adapted Ubuntu WSL2 produced two genuinely pending TLS
terminal FAILURE writes. Deadline expiry interrupted one, and listener cancellation
interrupted the other. The Windows trials ended before the terminal stages. The
WSL2 close-notification writes completed without staying pending. No genuine
pending write demonstrated a cancellation, ownership, or cleanup defect.

The platform budgets are closed. Recommend that the owner consider a separately
authorized plaintext-only graceful-close implementation with existing TLS behavior
retained. Q-B5B-09 remains **OPEN**.

## Repository and exact prototype

| Check | Observed |
| --- | --- |
| Canonical checkout | D:/projects/bolt-sharp, main |
| Entry commit / tree | 44093f65df2b49de380e4debd64b9606f9e11733 / 4137367cf955671e1a31a8890c9ca2e54cc30215 |
| Local origin/main | 79f7965a0e8e351baf2dd388218290804c31389a |
| Working state | Zero behind, eight ahead, clean, one worktree, zero stashes |
| Exact sole-parent chain | 44093f6 → 4beafcd → 23d58da → 29d521c → b196db8 → d8c28a9 → 2e0b9da → 17886a5 → 79f7965 |
| Reviewed and verified | INV-03 12/12, INV-02 13/13 and INV-01 11/11 Git blob hashes; no prior owner decision changed. |

The [INV-01](Q-B5B-09-INV-01-investigation.md),
[INV-02](Q-B5B-09-INV-02-investigation.md) and
[INV-03](Q-B5B-09-INV-03-investigation.md) findings governed eligibility,
timers, resource limits and the distinction between injected and real I/O.
The isolated candidate begins with an archive at commit
23d58da4e0c0e10aa46a31f05ef6a0c31ad3f2fa and tree
bd841c86886587d38276fba2798ef13404ce002d. The archive hash matched
INV-02. The INV-02 prototype patch and both INV-03 refinements reproduced all
five recorded candidate source hashes byte for byte before the gate fixture.
Independent replay also matched the tested Windows files and the separate
typed-linger WSL2 variant. The new isolated patch publishes the owned TcpClient
to an in-process observer so the fixture can set its socket buffer at the
selected stage. It leaves production eligibility, response bytes, deadline,
dispose/await sequencing, and admission logic intact.
[Source identities and replay](evidence/Q-B5B-09-TLS-GATE-01/source-identity.json).

## Controlled link and fixed budget

The harness creates one paired TCP connection on an ephemeral IPv4 loopback
listener, negotiates TLS and Bolt, and authenticates with test-local credentials.
It uses a single finite application result from a fixture provider as preceding
traffic. The peer then submits the terminal request with a benign fixture tail and
keeps its receive direction unread. At the selected encrypted stage, the small
in-process callback sets the owned socket's send buffer and returns immediately.
There is no relay, delayed encrypted-write await, privileged network change, or
external endpoint.

The three ordered profiles were recorded before execution. The evidence
configuration hash is a38a5d1c76bbfc035aec52967e5afc5afe69c3f4913b625b8cdc54319a4b351e.
Each cell could use at most three attempts per platform and stopped at its first
qualifying result. The journal records initiation before any network action, then
the result and cleanup of that attempt. The prior write timeout was 750 ms, the
terminal deadline was 300 ms, and the close sub-budget was 75 ms. The attempt
watchdog was 3.5 s with an independent runner watchdog. Profile settings are
kept in the [predeclared configuration](evidence/Q-B5B-09-TLS-GATE-01/configuration.json).

The INV-03 observation point exposes the exact incomplete task returned by
NetworkStream.WriteAsync for the selected encrypted write. The harness required
it to remain incomplete for 20 ms, while the peer did not read. A false sender
SelectWrite probe and the peer's unread encrypted bytes corroborated both successful
inductions. The experiment did not capture TCP packets or infer an on-wire
zero-window event. The observed incomplete network task is distinct from the
invocation of SslStream, a yield in managed code, and an earlier application write.
[NetworkStream.WriteAsync](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.networkstream.writeasync) ·
[RFC 9293 §3.8.6.1](https://www.rfc-editor.org/rfc/rfc9293.html#section-3.8.6.1).

## Four-cell results and all attempts

P = preceding application write did not complete, so the target was never reached.
Q = target was reached and finished without a persistently pending encrypted write.
I = a real pending write was induced, interrupted by the specified stimulus, and
passed the cleanup oracle. The dash denotes a reserved attempt left unused after
a qualifying result.

| Cell | Target and stimulus | Windows attempts 1/2/3 | Adapted WSL2 attempts 1/2/3 |
| --- | --- | --- | --- |
| T1 | TLS terminal FAILURE, deadline | P / P / P | Q / **I** / — |
| T2 | TLS terminal FAILURE, listener | P / P / P | Q / **I** / — |
| T3 | TLS close_notify, deadline | P / P / P | Q / Q / Q |
| T4 | TLS close_notify, listener | P / P / P | Q / Q / Q |

Windows 11 build 26200 used .NET 8.0.31. All twelve attempts parsed the
preceding PULL and ended with three completed message writes, one accounted
failure, zero active sockets, and no terminal result. No listener cancellation
was triggered for a terminal stage. The earlier write timeout and cleanup are
not T1–T4 success evidence.

Ubuntu WSL2 used Linux 6.6.114.1, .NET 10.0.14 roll-forward and the known
typed-disabled-linger adapter. T1 and T2 completed on their second profiles,
so each used two attempts. T3 and T4 each used all three. In the eight Q
attempts, the indicated write and close alert finished and the terminal operation
later left the drain under its phase timer. A drain deadline after a completed
alert is not alert-write cancellation evidence. Neither launcher had a failed
campaign process or an unmatched start record. The xUnit pass status means the
journal campaign ran, not that the gate passed.
[Windows journal](evidence/Q-B5B-09-TLS-GATE-01/windows.jsonl) ·
[WSL2 journal](evidence/Q-B5B-09-TLS-GATE-01/wsl2.jsonl) ·
[Complete matrix](evidence/Q-B5B-09-TLS-GATE-01/matrix.json).

## Genuine pending writes and ownership

Both confirmed writes occurred in the terminal FAILURE phase, after the earlier
application response completed and the terminal result was generated. Parser
entries were exactly five (HELLO, LOGON, RUN, PULL, terminal request), the session
reported Defunct with exactly one response, and the provider was prepared once.
The queued trailing command did not reach the parser or the provider. Neither
confirmed write reported terminal-write completion or reached TLS shutdown.
The parked peer did not provide an application receipt observation.

| Observation | WSL2 T1, attempt 2 | WSL2 T2, attempt 2 |
| --- | ---: | ---: |
| Actual send / peer receive buffer readback | 4,608 / 2,304 bytes | 4,608 / 2,304 bytes |
| Incomplete task observed after start | 23.15 ms | 21.72 ms |
| Peer unread bytes / sender writable | 576 / false | 576 / false |
| Listener signal after inner write start | — | 21.75 ms, task still pending |
| Real network task faulted after start | 299.38 ms | 21.94 ms |
| Terminal handler finished | 301.44 ms, total deadline | 22.14 ms, server cancelled |
| Accepted / active / completed / failed at finish | 1 / 0 / 0 / 1 | 1 / 0 / 1 / 0 |
| Drain permits acquired / returned | 0 / 0 | 0 / 0 |

The terminal handler awaited the faulted network operation and returned. The owner
socket SafeSocketHandle closed in each case, with no retained drain permit.
T2's task fault followed listener stop by about 0.18 ms, well ahead of the 300 ms
deadline. The outcome label was corroborating evidence. During the completed-alert
WSL2 trials the candidate acquired and released the drain permit once per
connection. Every attempt settled its connection accounting and closed the
captured socket. The fixture monitored unobserved-task exceptions and forced
finalization at the end of each platform run, observing zero events. This supports
cleanup of the recorded operations, without a universal claim about other workloads.

## Owner decision options

1. **Recommended — plaintext-only graceful close.** Consider separate authorization
   for the already characterized plaintext path while retaining existing TLS closing
   behavior. Keep TLS delivery and close under Q-B5B-09. Maintain the exact standalone
   FAILURE oracle and all admission and cancellation bounds. Production qualification
   still requires a separately reviewed portable linger initialization, native Linux,
   workload-shaped resource tests and independent regression evidence.
2. **An alternative TLS transport design.** Scope a design review to a transport
   ownership mechanism that provides an explicit interrupt-and-join contract for both
   the terminal write and close_notify, including a real pending alert and listener
   cancellation. Keep SslStream the sole consumer of encrypted bytes. A changed design
   requires separately authorized qualification of these stages.
3. **Explicitly accept the remaining investigation limitation.** The WSL2 terminal
   tests offer positive I/O evidence on that OS/runtime pair. The Windows terminal
   and real pending-alert behavior remain unresolved. Any decision that uses this
   evidence should retain a written risk of TLS terminal loss or incomplete close
   under pressure and explicit release gates for those remaining lifecycle cases.
   Peer receipt is conditional and a successful transport write alone cannot prove
   it. [RFC 8446 §6.1](https://www.rfc-editor.org/rfc/rfc8446.html#section-6.1).

Adapted WSL2 is not native Linux. Native Linux release execution, independently
triggered encrypted-byte cap, production capacity characterization, and integrated
parser/retention negative controls retain their earlier outstanding status. Windows
plaintext improvement, earlier integrated regressions, fixture-held TLS interruption,
and typed disabled-linger evidence are carried forward without reruns. No automatic
investigation cycle is recommended.

B5-B-TS1 and Q-B5B-03 remain **CLOSED**. Q-B5B-04, Q-B5B-05 and Q-B5B-09 remain
**OPEN**. All other inherited qualifications retain their prior dispositions.
Canonical scope is limited to this report and its evidence.
