# Q-B5B-09-PLAIN-Q1 — Independent production qualification

**REMEDIATION REQUIRED** · 2026-11-17, Asia/Manila

The final candidate improved Windows delivery for queued tails and passed the standard Windows and WSL2 campaigns. An independent probe reproduced incorrect write-timeout accounting on both platforms after the exact terminal FAILURE had reached the peer. The candidate can abort the graceful-close path and count a failed connection when a synchronous diagnostic observer runs after write and flush. The separately scoped typed-linger correction passed its Windows and WSL2 implementation checks. [Findings and disposition](evidence/Q-B5B-09-PLAIN-Q1/findings.md).

## Reviewer and starting state

Reviewer chat `01a1e499-f5f6-7630-9a64-2ef1c2f38664` began with this commission. It did no implementation, linger correction or producer verification. The separate producer chat `01a1dff9-002b-7092-a0d3-55bac1d0f917` records all three roles and its stop at the independence gate. The model difference was unverified. Producer artifacts served as integrity and historical inputs. The results below came from fresh execution and source review in this chat.

The canonical checkout `D:/projects/bolt-sharp` was clean on `main`, with one worktree and zero stashes. Local `origin/main` was `79f7965a0e8e351baf2dd388218290804c31389a`. The starting divergence was 13 ahead and zero behind. Each row is the sole parent of the next.

| Role | Commit | Tree |
| --- | --- | --- |
| Owner baseline | `fac10f6c5ece37dbc3f38c9c831c668d49a3d290` | `d464e85f810b50a00f768a9111dcc2a7f60cd699` |
| Typed linger | `dac52d55ade0a9ae703b801c8a4066e8aef2bd58` | `dcc1687c6c8fe8dd6b55863f7324e5c64c478416` |
| Plaintext implementation | `20fbc461c0257620db28fb78a463218b242455bc` | `116ee015f1ae9a30bf991c2db470996db47996c0` |
| Producer evidence, entry HEAD | `5d27b39c32a2d1e56a3d8ef7ce116a36445427cf` | `074c942a71c2ed480c1f3867adc57aaaed5aacbd` |

## Source, authority and protected evidence

The linger diff changes the listener setter and adds two startup tests. The implementation diff has eight authorized file paths. The evidence diff is nine additions under the producer report and package. Source review found two incidental scope issues within allowlisted files, involving the shared TLS-reachable writer and an encoding regression in the server's public XML comment. The TLS path exercised the existing TLS contracts. Public API signatures, options, package and project files, standalone and pipelined TS1 oracles, and historical evidence remain byte-identical where protected. The entire owner-decision tree was `fceee363ebcc72c99263d2bfe5e210a9c49cea4d` before and after. [Binding decision](../decisions/Q-B5B-09-OWNER-01-production-remediation-scope-decision.md), [binding specification](../decisions/Q-B5B-09-PLAIN-IMPL-01-implementation-specification.md), [findings](evidence/Q-B5B-09-PLAIN-Q1/findings.md).

The producer verifier checked eight Git blobs and 59 archive members using byte counts and SHA-256 over committed bytes. Its owner replay checked seven owner blobs and the archived historical package chain, including the exact TLS gate parent. I also checked the historical paths directly for blob changes and reviewed the TS1 closure and prior refusal/transport decisions, the three investigation reports, the TLS gate disposition and the BoltSharp protocol/compatibility contracts. TS1's earlier raw-worktree manifests have documented CRLF-dependent entries. Their continued byte identity and the commissioned Git-blob verification are separate checks. [Evidence package](evidence/Q-B5B-09-PLAIN-Q1/README.md), [TS1 manifest portability record](evidence/B5-B-TS1-Q1/README.md#hashing-semantics).

## Terminal lifecycle and resource ownership

`BoltConnectionSession.ReceiveBytes` classifies the authenticated unsupported command in Ready, Streaming or authenticated Failed. `FailTerminal` retires the active operation and sets Defunct before the existing response is marked eligible. The transport uses the existing message codec and chunk writer and awaits one write and flush. The reader loop returns from the helper branch. Follow-up bytes go to an opaque discard read, with no codec, parser, RESET, provider or second response. The existing standalone byte oracle and pipeline terminality checks passed. The raw capture pins the full 169-byte framed FAILURE. The status is `08N06`, with code `Neo.ClientError.Request.Invalid`.

After local write/flush, admission uses `Wait(0)`. An admitted owner shuts down the send side, uses one 2 KiB array and slices every read to the smaller of scratch length and bytes remaining under 8 KiB. The inclusive cap closes at 8,192 bytes even if peer EOF is already queued. Reads of 8,191, 8,192 and 8,193-byte tails discarded 8,191, 8,192 and 8,192 bytes respectively. Empty, partial and malformed tails, a reset, stalled and transmitting peers, and timed and capped drains exercised both exit mechanisms. Internal lifecycle payloads contain metadata only. The capacity fallback writes the one FAILURE and disposes promptly with zero drain reads or permit wait. Application receipt is established by the peer's actual reads in the recorded cases. Local message completion and send shutdown establish local progress.

The listener grants one permit at MaxConnections greater than one, and zero at one. One permit leaves connection admission headroom for at least one non-draining owner when the drain alone occupies a slot. The laboratory cases used limits 1, 2, 3 and 4 and exercised simultaneous admission, exhausted permits, a healthy query during pressure, cancellation after admission, a disposed socket fault in send shutdown, recovery on the next connection and stop during a drain. The accepted-during-stop test quiesces the plaintext accept loop before the task snapshot. Captured socket handles closed, terminal counters balanced, admission slots persisted through task retirement and the permit was reusable. Server instances are single-use. This enforcement evidence supplies no throughput or representative capacity conclusion. [Requirement matrix](evidence/Q-B5B-09-PLAIN-Q1/requirement-matrix.md).

## Deadline and cancellation

The implementation takes a Stopwatch timestamp before serialization and installs an overall cancellation timer before entering the writer. Elapsed time clamps the per-stage timers. The executable path has one 250 ms total timer and a 75 ms drain budget; the specification gives 300 ms and two permits as trial inputs and requires a reasoned production proposal. The 250 ms and one-permit choices preserved the observed Windows cooperative-tail benefit while reducing allowed occupancy. Local observations do not establish that these defaults are optimal. Pending I/O is directly awaited. Cancellation cancels the owner token and closes the underlying socket, and registration disposal and task retirement precede the relevant ownership release. Asynchronous cancellation can interrupt I/O. Synchronous serialization, operation retirement before entry, and observers must return on their own. The timer is a cancellation trigger subject to runtime and OS scheduling.

A real terminal write stayed pending after the preceding PULL completed. Each pressure test observed the pending write through an 18 ms window, selected a stimulus, awaited disposal and checked no terminal MessageProcessed, no admission and one terminal accounting transition. Timings below are elapsed from lifecycle entry across three repetitions. The owner and listener cases also passed the signal-to-cleanup bound of 130 ms. Their actual lifecycle elapsed values rule out eventual total deadline expiry as the source of interruption.

| Cause or boundary | Windows | WSL2 |
| --- | ---: | ---: |
| Pending terminal write, total deadline | 252.99–255.88 ms | 251.27–252.00 ms |
| Pending terminal write, owner cancellation | 39.09–50.18 ms | 22.38–23.76 ms |
| Pending terminal write, listener stop | 33.15–49.57 ms | 22.69–24.14 ms |
| Write-to-drain total deadline check | 254.93–262.55 ms | 252.22–253.63 ms |
| Stop while drain was active, signal to return | 0.24–0.79 ms | 0.12–0.16 ms |

The successful pending-write cases establish ordinary cancellation behavior. F01 concerns the shorter write timer surviving completed I/O into an inline diagnostic callback. Windows and WSL2 both reported a failed connection with `write-deadline` in that probe despite exact peer receipt and MessageProcessed. [Findings and reproduction](evidence/Q-B5B-09-PLAIN-Q1/findings.md).

## TLS isolation and portable linger

The server only constructs the plaintext helper for a listener with no TLS configuration. The transport also withholds the helper when TLS is configured. The TLS-negotiated stream handles the Bolt handshake and writes the refusal through SslStream and the established timeout and disposal path. No TLS connection enters the helper, acquires a permit, or reads TLS-owned bytes through a raw socket in the pinned production tree. TLS integration and official-driver cases passed, including authentication, encrypted conversations, fail-closed behavior, refusal classification and replacement, and listener shutdown. NC-07 proved the exclusion oracle detects an incorrect production mode selection with the exact refusal still read over TLS. The gate on pending TLS terminal writes and close alerts was carried forward.

On Windows, the original, linger-only and final revisions had disabled linger with zero time on both listening and accepted sockets in plaintext and TLS modes. On WSL2, the original boolean setter failed at startup in each mode with InvalidArgument / EINVAL 22. The typed setter passed pristine startup and TLS handshake coverage. Both corrected revisions read back disabled/zero on the two socket types in both modes. The readback observer recorded the accepted socket option and left the option and transport flow untouched. The uninstrumented core and driver runs used the committed sources and no linger adapters.

## Paired queued-tail observations

Each profile was tried once per revision and platform in the recorded final paired run. The large tail was 24 KiB. Small and large delayed readers began after 35 ms. The continuous profile attempted 512-byte sends every 3 ms with a bounded loop; its delayed reader began after 100 ms. Exact response bytes and ending codes are in the TRX results.

| Profile | Windows original | Windows linger-only | Windows candidate | WSL2 linger-only and candidate |
| --- | --- | --- | --- | --- |
| Small, immediate | Exact then reset | Exact then reset | Exact and EOF | Exact and EOF |
| Small, delayed | No bytes, reset | No bytes, reset | Exact and EOF | Exact and EOF |
| Large, immediate | Exact then reset | Exact then reset | Exact and EOF | Exact and EOF |
| Large, delayed | No bytes, reset | No bytes, reset | Exact and EOF | Exact and EOF |
| Continuous, immediate | Exact then reset | Exact then reset | Exact and EOF | Exact and EOF |
| Continuous, delayed | No bytes, reset | No bytes, reset | No bytes, connection aborted | Exact and EOF |

The Windows queued-tail improvements first appeared in the final plaintext revision. The Windows continuing writer still lost the response when it delayed reading. WSL2 orderly receipt was already observed at the linger-only baseline in all six profiles. The WSL2 owner baseline could not listen. [Raw evidence and runner commands](evidence/Q-B5B-09-PLAIN-Q1/README.md).

## Campaigns and handoff

Both platforms passed 1,250 core, 43 official-driver and three focused runs of 50 tests with zero skips. The seven independent mutant trials all reached and tripped their intended oracles. The clean 12-test control passed before and after them. Production, solution and driver builds and scoped formatting passed. Exactly two baseline xUnit2012 analyzer warnings remained in the solution. The supplemental startup, delivery and boundary cases and F01 reproductions are accounted for in the package, including unsuccessful WSL2 launcher attempts and the expected owner-baseline EINVAL outcomes. [Campaign and control records](evidence/Q-B5B-09-PLAIN-Q1/README.md).

Native Linux and representative production capacity remain release gates. No authorized native Linux host was identified in local environment discovery. Deferred TLS work retains Windows genuine pending terminal TLS write interruption, genuinely pending close-notification interruption, encrypted-byte cap verification, native Linux TLS and integrated TLS resource and lifecycle controls. Q-B5B-09, Q-B5B-04 and Q-B5B-05 remain OPEN. B5-B-TS1 and Q-B5B-03 remain CLOSED. TLS graceful-close remediation remains DEFERRED. Other inherited qualifications retain their owner dispositions.

Recommended next commission is a separately authorized plaintext remediation for F01 and the associated diagnostics and scope findings, followed by independent requalification. Owner acceptance remains a separate decision.
