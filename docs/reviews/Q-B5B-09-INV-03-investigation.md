# Q-B5B-09-INV-03 — Targeted Transport and Resource Qualification

**ADDITIONAL INVESTIGATION REQUIRED**

2026-11-16 (Asia/Manila). The isolated prototype interrupted and joined the actual
SslStream terminal-write and close-notification calls when their encrypted writes
were held by an in-process fixture. Real socket trials did not hold either TLS
terminal phase pending. Production TLS implementation remains gated on proving that
disposing the socket owner interrupts and joins those phases under actual network
backpressure. Q-B5B-09 remains **OPEN**.

## Repository and prototype identity

| Preflight | Verified |
| --- | --- |
| Repository and branch | D:/projects/bolt-sharp, main |
| Entry commit | 4beafcdd4ce48e0c0a4f8a6e05a52db328d7e017 |
| Entry tree | a3a8bc2d651ec81c95e59167a914de65b268745e |
| Local origin/main | 79f7965a0e8e351baf2dd388218290804c31389a |
| Entry state | Clean, seven ahead, zero behind, one worktree, zero stashes |
| Exact sole-parent ancestry | 4beafcd → 23d58da → 29d521c → b196db8 → d8c28a9 → 2e0b9da → 17886a5 → 79f7965 |
| Prior evidence and scope | INV-02 13/13, INV-01 11/11 and TS1 owner 5/5 blob hashes. Accepted decisions unchanged since the TS1 closure. |

Reviewed [INV-01](Q-B5B-09-INV-01-investigation.md),
[INV-02](Q-B5B-09-INV-02-investigation.md), the
[TS1 owner closure](../decisions/B5-B-TS1-OWNER-01-owner-closure.md), and the
[transport](../../src/BoltSharp/Transport/BoltTcpConnection.cs),
[listener](../../src/BoltSharp/Transport/BoltTcpServer.cs), and
[session](../../src/BoltSharp/Sessions/BoltConnectionSession.cs) contracts.

The archive was extracted at 23d58da, tree bd841c86886587d38276fba2798ef13404ce002d.
Its SHA-256 and all four reconstructed INV-02 source hashes matched the prior evidence.
New isolated refinements add phase observation, safe-handle accounting and an optional
in-process encrypted-write stall. They retain the sequence and configured budgets with
the seam unset. All five source files matched the tested bytes on independent patch replay.
The WSL typed-linger adapter remains a separate patch from INV-02.
[Source identity](evidence/Q-B5B-09-INV-03/source-identity.json).

## Outstanding-gate matrix

| Gate | Established | Outstanding |
| --- | --- | --- |
| TLS terminal FAILURE write | Defunct and one response; actual SslStream encrypted-write entry; injected pending operation returned after deadline or listener cancellation and socket disposal, on both tested environments. | Real kernel-pending terminal write cancellation. |
| TLS close notification | Exact FAILURE received first; actual ShutdownAsync issued an encrypted write with no cancellable argument; injected timeout/stop joined it, and peer reset produced a real transport error after the hold was released. | Real kernel-pending close notification cancellation. |
| Listener portability | Independently reproduced numeric linger overload incompatibility on WSL2. Typed disabled linger read back correctly on the listener and accepted socket on both environments. | Exact native Linux .NET 8 and official-driver qualification. |
| Resource and deadline | Two permits enforced, bounded release and zero retained captured handles at the tested occupancy. The shared timer was not renewed between write and shutdown. | Representative non-loopback load, native/kernel TLS buffering, and independently triggered encrypted-byte cap. |
| Negative controls | Parser and retained-socket oracles detected isolated test-owned faults. Source-level cancellation, deadline-renewal and capacity mutations failed. | Live production-path mutation coverage for parser re-entry and leaked socket ownership remains independent qualification work. |

[Structured observations](evidence/Q-B5B-09-INV-03/campaign-summary.json) ·
[Raw logs and TRX](evidence/Q-B5B-09-INV-03/execution-records.zip).

## TLS terminal write and close notification

The held operations use the real Bolt terminal path and SslStream over the real accepted
socket. The fixture holds the encrypted write at the managed inner stream. It checks
that the task is still pending before the stimulus, watches the owned SafeSocketHandle
close, releases the hold by raising an I/O failure, and checks the awaited operation
returned and the connection finished. The terminal application write passed a
cancellable token to the seam; ShutdownAsync passed none. The reset trial dropped the
peer, released the hold, and ran the real inner write. No parallel consumer reads the
raw socket. Gate exit and owner disposal were observed, with no process-wide detector
for unobserved task exceptions and no packet-level TCP state capture.

| Phase and stimulus | Windows ms | Adapted WSL2 ms | Outcome |
| --- | ---: | ---: | --- |
| Terminal write, timer | 306.60 | 302.76 | Total deadline; owned socket closed |
| Terminal write, listener stop | 10.73 | 5.62 | Listener cancellation; owned socket closed |
| Alert write, phase timeout | 84.26 | 80.17 | Phase deadline; exact FAILURE reached the reader first |
| Alert write, listener stop | 5.17 | 2.78 | Listener cancellation; exact FAILURE reached the reader first |
| Alert write, peer reset | 19.87 | 21.61 | Transport failure after resuming the real write |
| Write hold followed by alert hold | 324.47 | 304.02 | Original absolute total deadline in shutdown |

Each timing measures one trial from terminal I/O entry. The scheduled timer can
overrun in elapsed time through scheduling and OS completion latency. The fixture
controls whether the encrypted send is pending, without relying on any assumption
about OS send queue state.

The separate real-socket run used the actual engine with the encrypted-write gate
unset. Four Windows trials stopped in the earlier application response. On WSL2, two
trials completed the terminal write and alert normally and two stopped in the earlier
response. No TLS terminal-write or alert task remained pending at the sample on
either environment. This supplies no native I/O cancellation evidence for those stages.
INV-02 already covered cooperative peers, delay, continued input, absent peer
shutdown, and reset/truncation. Its Windows continued-input cases still lost the
reply. Local write completion and a TLS closure alert cannot prove peer application
receipt. [RFC 9293 §3.6](https://www.rfc-editor.org/rfc/rfc9293.html#section-3.6) ·
[RFC 8446 §6.1](https://www.rfc-editor.org/rfc/rfc8446.html#section-6.1) ·
[.NET SslStream](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-8.0).

## Socket linger and native Linux

The exact baseline calls SetSocketOption(Socket, Linger, false), selecting the bool
overload. Independent socket probes confirmed that it succeeded with disabled
linger on Windows 11 build 26200/.NET 8.0.31. On Ubuntu WSL2 Linux 6.6.114.1/.NET
10.0.14 the same call threw SocketException, SocketError.InvalidArgument, native
22 (EINVAL). Reused INV-02 records show both exact listener variants failed before
accepting any connection on the same WSL2 runtime. The separate typed adapter,
LingerState = new LingerOption(false, 0), read back Enabled=false and LingerTime=0
on listening and accepted sockets in both environments. That verifies policy
configuration. It does not guarantee a FIN close with unread data.
[Socket.LingerState](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.lingerstate?view=net-8.0).

The available Linux distributions were Ubuntu and docker-desktop, both WSL2. The
local Docker contexts were default and desktop-linux. No authorized native host
or committed GitHub Actions, Azure Pipelines, GitLab CI or Jenkins definition was
available for this local commit. Windows SDK 10.0.401 built the net8.0 artifacts.
WSL2 used the installed .NET 10.0.14 through roll-forward and the isolated typed
adapter. The [native Linux runner](evidence/Q-B5B-09-INV-03/native-linux.sh) records
exact source runs and a separate adapter comparison. Native Linux is a release
qualification gate. Its absence alone would not block an implementation decision
after the TLS lifetime blocker was resolved. A portable linger fix is a separate
baseline prerequisite for platform claims.

## Resource and cancellation campaign

Each run admitted 65 authenticated local connections. Twelve waves at concurrency
1, 2 and ten waves of 6 exercised below, at, and above two drain permits. Two more
connections were admitted during listener stop. The configured listener maximum
was 12. Peers withheld close cooperation. Every run counted 23 phase deadlines,
40 capacity fallbacks, two listener cancellations, 65 completions, zero failures
and zero retained captured handles. The campaign retains INV-02's 300/75 ms
timers, 8 KiB plaintext cap, 32 KiB additional TLS wire cap and 2 KiB scratch per
admitted drain.

| Lab measure | Windows TCP | Windows TLS | WSL2 TCP | WSL2 TLS |
| --- | ---: | ---: | ---: | ---: |
| Sampled peak active / open | 6 / 6 | 6 / 6 | 2 / 2 | 6 / 6 |
| Observed peak drains / final retained | 2 / 0 | 2 / 0 | 2 / 0 | 2 / 0 |
| Process allocation (MiB) | 2.17 | 3.22 | 2.17 | 3.31 |
| CPU ms / wall ms | 469 / 1176 | 688 / 1349 | 450 / 975 | 810 / 1009 |
| Peak pool threads / queued items | 11 / 3 | 14 / 2 | 9 / 3 | 14 / 2 |
| p95 / max terminal ms | 86.52 / 86.74 | 87.41 / 95.96 | 75.24 / 75.44 | 77.02 / 77.32 |
| Listener stop ms | 0.22 | 0.78 | 0.28 | 0.22 |

Allocation, CPU, thread pool and heap are process-wide and include peers,
instrumentation and GC timing. Sampling can miss fast fallbacks. Final handle
observations do not measure native TLS allocations or TCP state retained by the
kernel. This waveform tests bounded noncooperative close and repeated arrival in
a small laboratory workload. It establishes neither production-shaped saturation
nor an independent encrypted-byte-cap event. INV-02 supplies plaintext-byte-cap
exits on both transports.

The total timer is armed once before terminal serialization and links to listener
shutdown. The 75 ms phase timer links to the same parent and cannot renew it. The
write-to-alert trial and NC-04 distinguish the absolute timer from a renewed one.
Serialization is a single fixed FAILURE. The timer does not preempt synchronous
CPU work or arbitrary provider disposal before the terminal result exists.
Cleanup after the timer relies on interrupting and observing the actual I/O.
Diagnostics should distinguish the signal that released the wait from a listener
token that happened to be canceled.

## Negative controls and regressions

| Control | Measured result |
| --- | --- |
| NC-01 parser entry | Normal integrated parser count was three. A test-only, benign fixture command was deliberately decoded and recorded as an extra parser entry with no response. The parser-count oracle rejected it. The fixture did not reactivate a live Defunct session. |
| NC-02 retained socket | A deliberately retained, connected fixture handle caused the independent disposal oracle to fail following normal shutdown. Releasing it restored the expected result. This was an oracle control, with production-owner fault injection still outstanding. |
| NC-03 unlinked cancellation | Both listener cases failed the timing assertions, completing at 323.54 and 94.81 ms despite the label saying server-cancelled. The three other cases passed. |
| NC-04 renewed total budget | Targeted case failed. The altered path ran until the phase deadline at 364.94 ms. The unaltered path used the original total deadline. |
| NC-05 increased permit capacity | Both transport cases failed. The altered path admitted three simultaneous drains. |

NC-03–05 ran in a separate isolated copy, with the original candidate file hash
restored after each run. All failure records are included. The new fixture passed
11/11 on Windows and 11/11 in adapted WSL2. Windows passed 29/29 focused existing
cases for byte-exact FAILURE, pipelined refusal, multi-client accounting, TLS, and
connection lifecycle. Three official Neo4j.Driver 6.4.0 cases passed, covering
terminal refusal and pooled recovery/shutdown on TCP and TLS. INV-02 results were
reused for unaffected surfaces. TS1 assertions retain their original strength.

## Production decision and proposed commissions

When authorized, constrain implementation to an internal session refusal
classification, the terminal connection I/O helper, per-listener admission and
accounting, and a separately qualified typed linger prerequisite. Preserve public
APIs, existing failure encoding, and the behavior of ordinary errors and GOODBYE.

Enter Defunct and retire session ownership before the response. Serialize one
unchanged FAILURE. Await write and flush under a monotonic absolute deadline and
linked listener token. On admission, half-close TCP send and discard only
transport bytes. For TLS, send close_notify and discard exclusively through
SslStream, with independent limits on application bytes and additional encrypted
input. Keep socket and admission ownership through the real awaited operation,
dispose before releasing a permit, and publish terminal accounting exactly once.
Skip waiting for permits. On close failure, expiration or cancellation, dispose
and observe any outstanding work. Record phase, actual cancellation cause,
timeout, byte/capacity outcomes, reset and socket disposal without payloads or
credentials.

Use 300 ms total, 75 ms closure, 8/32 KiB application and wire caps, 2 KiB scratch,
and two drain slots as initial **qualification settings**. They are judgment-based
starting points supported by the bounded lab observations. Set production defaults
only after calibrating traffic, admission headroom, shutdown and memory on the
supported platforms. Require a portable startup regression, actual TLS
backpressure, robust cancellation/serialization tests, independent parser and
ownership fault injection, driver reset and replacement, healthy-client progress,
and native/kernel resource recovery under representative load. Neither a socket
shutdown nor an alert guarantees receipt by the remote application.

**Recommended next commission — Q-B5B-09-TLS-GATE-01.** Use one controlled local link
or authorized emulator, with a predeclared grid for the two stages and deadline
versus listener cancellation. Run up to three attempts per cell per available
supported runtime, with an independent watchdog. Require a demonstrably pending
real inner write at the marked stage, persistence before the stimulus, the
actual await returning, handle and permits released, and no additional response or
parser entry. If a stage remains unreachable, stop the campaign and take the
evidence to the owner for a decision on plaintext-only scope or an alternative
interruptible ownership design. Start no production implementation from this
investigation. A later narrowly scoped implementation can be commissioned
separately, with native Linux and representative resource qualification required
for release.

B5-B-TS1 and Q-B5B-03 remain **CLOSED**. Q-B5B-04, Q-B5B-05 and Q-B5B-09 remain
**OPEN**. All other inherited qualifications retain their dispositions. The canonical
commit adds only this report and its evidence package.
